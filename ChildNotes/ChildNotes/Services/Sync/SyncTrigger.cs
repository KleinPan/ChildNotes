using System.Threading;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services.Sync;

/// <summary>
/// 同步触发器：负责"写入后防抖 5 秒"和"启动后延迟同步"。
/// 避免每次写记录都立即上传，防抖期内多次写入只同步一次。
/// </summary>
public sealed class SyncTrigger : IDisposable
{
    private readonly SyncService _syncService;
    private Timer? _debounceTimer;
    private readonly TimeSpan _debounceDelay = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(8);
    private int _isScheduled = 0;
    private bool _disposed;

    public SyncTrigger(SyncService syncService)
    {
        _syncService = syncService;
    }

    /// <summary>App 启动后、用户登录成功后调用。延迟几秒后执行首次同步（拉取远程最新）。</summary>
    public void TriggerStartupSync()
    {
        if (!IsAutoSyncEnabled()) return;
        ScheduleDelayed(_startupDelay);
    }

    /// <summary>记录写入后调用。防抖 5 秒，期内多次写入只触发一次推送。</summary>
    public void TriggerAfterWrite()
    {
        if (!IsAutoSyncEnabled()) return;
        ScheduleDelayed(_debounceDelay);
    }

    /// <summary>立即触发（手动同步按钮使用）。</summary>
    public async Task TriggerNowAsync()
    {
        await _syncService.SyncAsync();
    }

    private bool IsAutoSyncEnabled()
    {
        try
        {
            var cfg = ServiceProvider.Instance.WebDavConfigRepository.GetOrCreate();
            return cfg.Enabled && cfg.AutoSync;
        }
        catch
        {
            return false;
        }
    }

    private void ScheduleDelayed(TimeSpan delay)
    {
        if (Interlocked.Exchange(ref _isScheduled, 1) == 1) return; // 已有调度
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(async _ =>
        {
            Interlocked.Exchange(ref _isScheduled, 0);
            try
            {
                await _syncService.SyncAsync();
            }
            catch (Exception ex)
            {
                DevLogger.Log("Sync", $"Debounced sync failed: {ex.Message}");
            }
        }, null, delay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer?.Dispose();
    }
}
