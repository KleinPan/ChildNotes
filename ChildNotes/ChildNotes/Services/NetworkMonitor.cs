using System.Net.Http;
using System.Net.NetworkInformation;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services;

/// <summary>
/// 网络状态监测器：三层探测 + 事件驱动。
/// L1: NetworkChange 系统事件（作为触发器：网卡通断时立即触发 ProbeAsync 探测，桌面端可靠，移动端需平台桥接）。
/// L2: NetworkInterface.GetIsNetworkAvailable（ProbeAsync 内部第一层状态判断，判断本地网卡是否在线）。
/// L3: HTTP HEAD /api/health 探活（ProbeAsync 内部第二层判断，区分"有网但服务不可用"与"完全离线"）。
/// 状态变化时触发 <see cref="StateChanged"/>，供 SyncTrigger 在网络恢复时立即触发同步。
/// </summary>
public sealed class NetworkMonitor : IDisposable
{
    /// <summary>网络连接状态。</summary>
    public enum State
    {
        /// <summary>在线：本地有网且服务器可达。</summary>
        Online,
        /// <summary>本地无网（网卡断开 / GetIsNetworkAvailable=false）。</summary>
        OfflineLocal,
        /// <summary>本地有网但服务器不可达（DNS 失败 / 5xx / 超时）。</summary>
        OfflineServer,
    }

    /// <summary>当前状态。初始假定为 OfflineLocal，首次 Probe 后更新。</summary>
    public State Current { get; private set; } = State.OfflineLocal;

    /// <summary>状态变化事件。回调可能来自线程池，UI 更新需 Dispatcher.Post。</summary>
    public event Action<State>? StateChanged;

    private readonly Timer _probeTimer;
    private readonly HttpClient _healthHttp;
    private readonly object _stateLock = new();
    private bool _disposed;
    private DateTime _lastSuccessTime = DateTime.MinValue;

    // 失败后探活频率提高，成功后降低以省电省流量。
    private static readonly TimeSpan ProbeIntervalOnline = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProbeIntervalOffline = TimeSpan.FromSeconds(30);

    public NetworkMonitor()
    {
        _healthHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        // 启动后立即探活一次，随后按周期轮询
        _probeTimer = new Timer(_ => _ = ProbeAsync(), null, TimeSpan.Zero, ProbeIntervalOffline);
        try
        {
            NetworkChange.NetworkAvailabilityChanged += OnNetChanged;
            NetworkChange.NetworkAddressChanged += OnNetChanged;
        }
        catch (Exception ex)
        {
            // 部分平台（如某些 Android 版本）可能不支持，降级到纯定时轮询
            DevLogger.Log("NetMonitor", "NetworkChange unavailable: " + ex.Message);
        }
        DevLogger.Log("NetMonitor", "NetworkMonitor created");
    }

    /// <summary>立即触发一次探活（不等待下一个周期）。</summary>
    public void ProbeNow()
    {
        if (_disposed) return;
        _ = ProbeAsync();
    }

    private async Task ProbeAsync()
    {
        if (_disposed) return;
        try
        {
            // L2：本地网卡层
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                Set(State.OfflineLocal);
                return;
            }

            // L3：服务端探活
            var url = ServerEndpoints.Primary + ServerEndpoints.HealthPath;
            try
            {
                using var resp = await _healthHttp.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, url));
                if (resp.IsSuccessStatusCode)
                {
                    _lastSuccessTime = DateTime.UtcNow;
                    Set(State.Online);
                    return;
                }
                // 4xx 通常意味着服务器在线但接口未实现/鉴权问题，仍视为 Online
                if ((int)resp.StatusCode >= 400 && (int)resp.StatusCode < 500)
                {
                    _lastSuccessTime = DateTime.UtcNow;
                    Set(State.Online);
                    return;
                }
                Set(State.OfflineServer);
            }
            catch (TaskCanceledException)
            {
                // 超时
                Set(State.OfflineServer);
            }
            catch (HttpRequestException)
            {
                Set(State.OfflineServer);
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("NetMonitor", "ProbeAsync exception: " + ex.Message);
            Set(State.OfflineServer);
        }
    }

    private void OnNetChanged(object? sender, EventArgs e)
    {
        // 网卡状态变化时立即探活，避免等待周期
        DevLogger.Log("NetMonitor", "NetworkChange event, probing...");
        _ = ProbeAsync();
    }

    private void Set(State s)
    {
        lock (_stateLock)
        {
            if (s == Current) return;
            var prev = Current;
            Current = s;
            // 根据状态调整轮询周期
            var nextInterval = s == State.Online ? ProbeIntervalOnline : ProbeIntervalOffline;
            try { _probeTimer.Change(nextInterval, nextInterval); } catch { /* 已 Dispose */ }
            DevLogger.Log("NetMonitor", $"State {prev} -> {s}");
        }
        try { StateChanged?.Invoke(Current); }
        catch (Exception ex) { DevLogger.Log("NetMonitor", "StateChanged handler exception: " + ex.Message); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            NetworkChange.NetworkAvailabilityChanged -= OnNetChanged;
            NetworkChange.NetworkAddressChanged -= OnNetChanged;
        }
        catch { /* 见构造函数注释 */ }
        _probeTimer.Dispose();
        _healthHttp.Dispose();
    }
}
