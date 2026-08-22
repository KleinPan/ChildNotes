using System.Threading;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.Services;

/// <summary>
/// 同步触发器：启动后 8 秒首同步；写入后 5 秒防抖再同步；手动触发立即同步；
/// 网络恢复时立即触发；长空闲期每 15 分钟保活同步。
/// Single-flight + Pending：任何时刻最多一个 SyncAsync 在执行；
/// 同步期间到达的新请求只标记 pending=true，当前同步完成后追加一次，不会并发。
/// 失败不抛异常，下次触发再试。
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

    // Single-flight：保证同一时刻只有一个同步在执行，期间到达的请求合并为一次 pending
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _syncPending;

    /// <summary>暂停标志（rebind 确认期间禁止新同步触发；设计文档 6.4）。</summary>
    private volatile bool _paused;

    public event Action<ApiSyncService.SyncResult>? SyncCompleted;

    /// <summary>
    /// 暂停同步触发（Family-centric 阶段 2，设计文档 6.4）：换绑确认框弹出前调用，
    /// 拒绝启动/防抖/保活/网络恢复等一切新触发，防止 rebind 事务期间并发读写 sync_config。
    /// </summary>
    public void Pause()
    {
        _paused = true;
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
        DevLogger.Log("Sync", "SyncTrigger paused (rebind in progress)");
    }

    /// <summary>恢复同步触发（rebind 事务完成后调用）。</summary>
    public void Resume()
    {
        _paused = false;
        DevLogger.Log("Sync", "SyncTrigger resumed");
    }

    /// <summary>
    /// 独占执行 rebind 事务（设计文档 6.4）：暂停新触发 → await _syncLock
    /// （等正在执行的同步完成，复用现有锁，禁止另造）→ 执行事务 → 恢复。
    /// 事务本体由 action 携带（SyncConfigRepository.ExecuteRebind）。
    /// </summary>
    public async Task ExecuteExclusiveDuringRebindAsync(Func<Task> action)
    {
        Pause();
        try
        {
            await _syncLock.WaitAsync();
            try { await action(); }
            finally { _syncLock.Release(); }
        }
        finally
        {
            Resume();
        }
    }

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
        if (_paused)
        {
            DevLogger.Log("Sync", $"Sync trigger skipped ({source}): paused for rebind");
            return new ApiSyncService.SyncResult { Success = false, Message = "换绑处理中，同步已暂停" };
        }

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

        // Single-flight：若已有同步在执行，只标记 pending，不启动第二个。
        // 当前同步完成后会检查 pending 并追加一次，合并短时间内的多次触发。
        if (!await _syncLock.WaitAsync(0))
        {
            _syncPending = true;
            DevLogger.Log("Sync", $"Sync already running, {source} coalesced as pending");
            return new ApiSyncService.SyncResult { Success = false, Message = "同步进行中，已合并" };
        }

        ApiSyncService.SyncResult result;
        try
        {
            result = await ExecuteSyncAsync(source);

            // 当前同步完成后，若期间有新请求到达，追加一次同步（coalescing）
            while (_syncPending && !_disposed)
            {
                _syncPending = false;
                DevLogger.Log("Sync", "Running pending sync (coalesced from earlier requests)");
                result = await ExecuteSyncAsync(source + "+pending");
            }
        }
        finally
        {
            _syncLock.Release();
        }
        return result;
    }

    /// <summary>真正执行一次 SyncAsync（含日志记录）。由 RunOnce 在持有 _syncLock 时调用。</summary>
    private async Task<ApiSyncService.SyncResult> ExecuteSyncAsync(string source)
    {
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
        _syncLock.Dispose();
    }
}
