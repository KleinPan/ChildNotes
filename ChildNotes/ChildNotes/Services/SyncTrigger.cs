using System.Threading;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.Services;

/// <summary>
/// 同步触发器：启动后 8 秒首同步；写入后 5 秒防抖再同步；手动触发立即同步；
/// 网络恢复时立即触发；长空闲期每 15 分钟保活同步。
/// 全部串行，重入时跳过。失败不抛异常，下次触发再试。
/// </summary>
public sealed class SyncTrigger : IDisposable
{
    private readonly ApiSyncService _sync;
    private readonly SyncLogRepository? _logRepo;
    private readonly Timer _startupTimer;
    private Timer? _debounceTimer;
    private Timer? _keepaliveTimer;
    private readonly object _debounceLock = new();
    private bool _disposed;
    private DateTime _lastRunAt = DateTime.MinValue;
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(3);

    public event Action<ApiSyncService.SyncResult>? SyncCompleted;

    /// <summary>
    /// 网络状态监测器（可选）。注入后：
    /// - 网络恢复时自动触发一次同步
    /// - 离线时跳过启动/防抖触发，避免无谓请求
    /// </summary>
    public NetworkMonitor? NetworkMonitor { get; set; }

    public SyncTrigger(ApiSyncService sync) : this(sync, null) { }

    /// <summary>带 SyncLogRepository 的构造函数：启用同步日志记录。</summary>
    public SyncTrigger(ApiSyncService sync, SyncLogRepository? logRepo)
    {
        _sync = sync;
        _logRepo = logRepo;
        _startupTimer = new Timer(_ => RunOnce("startup"), null, TimeSpan.FromSeconds(8), Timeout.InfiniteTimeSpan);
        // 长空闲期保活：每 15 分钟一次，防止 LastSyncAt 漂移过大
        _keepaliveTimer = new Timer(_ => RunOnce("keepalive"), null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
        DevLogger.Log("Sync", "SyncTrigger created; startup sync scheduled in 8s; keepalive every 15min");
    }

    /// <summary>业务写入后调用。5 秒内多次调用只触发一次同步。</summary>
    public void NotifyWrite()
    {
        if (_disposed) return;
        // 离线时仍记录写入，但不立即触发（待网络恢复时由 NetworkMonitor 触发）
        if (NetworkMonitor?.Current == NetworkMonitor.State.OfflineLocal)
        {
            DevLogger.Log("Sync", "NotifyWrite skipped (offline local); will sync on network recovery");
            return;
        }
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => RunOnce("debounce"), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>用户手动触发立即同步。</summary>
    public Task<ApiSyncService.SyncResult> RunNowAsync()
    {
        return Task.Run(() => RunOnce("manual"));
    }

    /// <summary>由 NetworkMonitor 在状态变化时调用。网络恢复立即触发同步。</summary>
    internal void OnNetworkStateChanged(NetworkMonitor.State state)
    {
        if (_disposed) return;
        if (state == NetworkMonitor.State.Online)
        {
            DevLogger.Log("Sync", "Network recovered, triggering sync immediately");
            _ = RunOnce("net-recover");
        }
    }

    private async Task<ApiSyncService.SyncResult> RunOnce(string source)
    {
        if (_disposed) return new ApiSyncService.SyncResult { Success = false, Message = "已关闭" };

        // 节流：除手动触发外，3 秒内重复触发跳过
        if (source != "manual")
        {
            var elapsed = DateTime.UtcNow - _lastRunAt;
            if (elapsed < MinInterval)
            {
                DevLogger.Log("Sync", $"Sync throttled ({source}), last run {elapsed.TotalMilliseconds:F0}ms ago");
                return new ApiSyncService.SyncResult { Success = false, Message = "节流跳过" };
            }
        }

        // 节流跳过的同步不记录日志，避免噪音。
        // 注意：后续若 SyncAsync 内部因"未启用/配置不完整"返回失败，
        // 仍会写入 running + failed 两条日志（因为此处已先写入 running 日志）。
        // 仅真正进入同步流程时写入 running 日志
        long logId = 0;
        if (_logRepo is not null)
        {
            try
            {
                logId = _logRepo.Add(new SyncLogEntry
                {
                    DoneAt = DateTime.Now,
                    Status = "running",
                    DataVolume = string.Empty,
                    Message = $"同步中（{source}）",
                });
            }
            catch (Exception ex)
            {
                DevLogger.Log("Sync", "SyncLog add running failed: " + ex.Message);
            }
        }

        try
        {
            _lastRunAt = DateTime.UtcNow;
            DevLogger.Log("Sync", $"Sync triggered by '{source}'");
            var result = await _sync.SyncAsync();
            DevLogger.Log("Sync", $"Sync done ({source}): success={result.Success}, msg={result.Message}");
            RecordFinalLog(logId, result);
            SyncCompleted?.Invoke(result);
            return result;
        }
        catch (Exception ex)
        {
            DevLogger.Log("Sync", ex);
            var failResult = new ApiSyncService.SyncResult { Success = false, Message = ex.Message };
            RecordFinalLog(logId, failResult);
            return failResult;
        }
    }

    /// <summary>把最终同步结果写入日志（running → success/failed）。</summary>
    private void RecordFinalLog(long logId, ApiSyncService.SyncResult result)
    {
        if (_logRepo is null || logId <= 0) return;
        try
        {
            var status = result.Success ? "success" : "failed";
            // 成功时：完整摘要只写在 Message，DataVolume 置空避免 UI 重复显示
            // 失败时：Message 写错误原因，DataVolume 写部分进度（如有）
            var volume = result.Success ? string.Empty : BuildPartialVolume(result);
            _logRepo.UpdateFinal(logId, DateTime.Now, status, volume, result.Message ?? string.Empty);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Sync", "SyncLog update final failed: " + ex.Message);
        }
    }

    /// <summary>失败时的部分数据量描述（仅 PullPages > 0 时有意义）。</summary>
    private static string BuildPartialVolume(ApiSyncService.SyncResult r)
    {
        return r.PullPages > 0 ? $"已拉取 {r.PullPages} 页" : string.Empty;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _startupTimer.Dispose();
        _keepaliveTimer?.Dispose();
        lock (_debounceLock) { _debounceTimer?.Dispose(); _debounceTimer = null; }
    }
}
