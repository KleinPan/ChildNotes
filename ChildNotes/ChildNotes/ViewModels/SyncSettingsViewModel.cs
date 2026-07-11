using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 同步设置页 ViewModel：管理 sync_config 的 enabled 开关 + 服务器地址配置 + 手动触发同步。
/// 服务器地址可编辑可保存（持久化到 sync_config），后期可通过隐藏 UI 编辑入口改为只读展示。
/// 实现 IDisposable 在 Dispose 中退订 NetworkMonitor / SyncTrigger 事件，避免 VM 多次创建叠加订阅。
/// </summary>
public partial class SyncSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly SyncConfigRepository _cfgRepo = ServiceProvider.Instance.SyncConfigRepository;
    private readonly SyncLogRepository _logRepo = ServiceProvider.Instance.SyncLogRepository;
    private readonly SyncTrigger _trigger = ServiceProvider.Instance.SyncTrigger;
    private readonly NetworkMonitor _networkMonitor = ServiceProvider.Instance.NetworkMonitor;
    private bool _disposed;

    /// <summary>最近同步日志（按时间倒序，最新在最上方）。供 UI 底部日志区绑定。</summary>
    public ObservableCollection<SyncLogEntry> SyncLogs { get; } = new();

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _lastSyncText = "尚未同步";
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isSyncing;

    /// <summary>网络状态文案（绑定到 UI 徽标）。</summary>
    [ObservableProperty] private string _networkStateText = "检测中…";
    /// <summary>是否在线（用于徽标颜色绑定）。</summary>
    [ObservableProperty] private bool _isOnline;

    /// <summary>可编辑的服务器地址（双向绑定 TextBox）。</summary>
    [ObservableProperty] private string _serverUrlInput = string.Empty;

    /// <summary>服务器地址是否已修改（控制"保存"按钮可见性）。</summary>
    [ObservableProperty] private bool _isServerUrlDirty;

    /// <summary>
    /// 服务器地址是否可编辑。仅开发版可编辑，正式版改为只读展示当前生效地址。
    /// 由 <see cref="Infrastructure.BuildConfiguration.IsDevelopmentBuild"/> 编译时决定。
    /// </summary>
    public bool IsServerUrlEditable => Infrastructure.BuildConfiguration.IsDevelopmentBuild;

    partial void OnServerUrlInputChanged(string value)
    {
        if (_loading) return;
        // 比较与已保存值是否不同，标记 dirty
        var saved = _cfgRepo.Get().ServerUrl ?? string.Empty;
        IsServerUrlDirty = !string.Equals(value?.Trim(), saved, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>保存服务器地址到 sync_config。</summary>
    [RelayCommand]
    private void SaveServerUrl()
    {
        var url = (ServerUrlInput ?? string.Empty).Trim();
        // 简单校验：空值=回退默认，非空需以 http:// 或 https:// 开头
        if (!string.IsNullOrEmpty(url) &&
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            DisplayToast("地址需以 http:// 或 https:// 开头");
            return;
        }
        var cfg = _cfgRepo.Get();
        cfg.ServerUrl = url;
        _cfgRepo.Save(cfg);
        IsServerUrlDirty = false;
        DisplayToast(url.Length > 0 ? "服务器地址已保存" : "已恢复默认服务器地址");
    }

    /// <summary>服务器地址只读展示（当前生效值，含回退逻辑）。</summary>
    public string ServerUrlDisplay => ServerEndpoints.Primary;

    public string SyncButtonText => IsSyncing ? "同步中…" : "立即同步";

    partial void OnIsSyncingChanged(bool value) => OnPropertyChanged(nameof(SyncButtonText));

    /// <summary>开关切换时自动保存到 sync_config（无需单独保存按钮）。</summary>
    partial void OnEnabledChanged(bool value) => PersistEnabled();

    public SyncSettingsViewModel()
    {
        Title = "数据同步";
        Load();
        UpdateNetworkState(_networkMonitor.Current);
        _networkMonitor.StateChanged += OnNetworkStateChanged;
        _trigger.SyncCompleted += OnSyncCompleted;
    }

    /// <summary>从数据库重新加载最近 10 条同步日志到 SyncLogs 集合。</summary>
    private void ReloadSyncLogs()
    {
        try
        {
            var logs = _logRepo.GetRecent();
            SyncLogs.Clear();
            foreach (var log in logs)
                SyncLogs.Add(log);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Sync", "ReloadSyncLogs failed: " + ex.Message);
        }
    }

    /// <summary>
    /// 退订外部单例事件，避免内存泄漏。
    /// 调用时机：MainShell 关闭 SyncSettings overlay 时（或应用退出时）。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _networkMonitor.StateChanged -= OnNetworkStateChanged;
        _trigger.SyncCompleted -= OnSyncCompleted;
        _disposed = true;
    }

    private bool _loading = true;

    private void Load()
    {
        var cfg = _cfgRepo.Get();
        Enabled = cfg.Enabled;
        ServerUrlInput = cfg.ServerUrl ?? string.Empty;
        UpdateLastSyncText(cfg);
        ReloadSyncLogs();
        _loading = false;
    }

    /// <summary>开关切换时持久化 enabled 字段，跳过首次加载触发。</summary>
    private void PersistEnabled()
    {
        if (_loading) return;
        var cfg = _cfgRepo.Get();
        if (cfg.Enabled == Enabled) return;
        cfg.Enabled = Enabled;
        _cfgRepo.Save(cfg);
        DisplayToast(Enabled ? "已启用云同步" : "已关闭云同步");
    }

    private void UpdateLastSyncText(SyncConfig cfg)
    {
        if (cfg.LastSyncAt.HasValue)
            LastSyncText = $"上次同步：{cfg.LastSyncAt.Value:yyyy-MM-dd HH:mm:ss}（{(cfg.LastSyncStatus == "ok" ? "成功" : "失败")}）";
        else
            LastSyncText = "尚未同步";
    }

    private void OnNetworkStateChanged(NetworkMonitor.State state)
    {
        // 回调可能来自线程池，UI 更新需切到主线程
        Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateNetworkState(state));
    }

    private void UpdateNetworkState(NetworkMonitor.State state)
    {
        IsOnline = state == NetworkMonitor.State.Online;
        NetworkStateText = state switch
        {
            NetworkMonitor.State.Online => "在线",
            NetworkMonitor.State.OfflineLocal => "离线（无网络）",
            NetworkMonitor.State.OfflineServer => "离线（服务不可用）",
            _ => "未知",
        };
    }

    private void OnSyncCompleted(ApiSyncService.SyncResult result)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateLastSyncText(_cfgRepo.Get());
            StatusText = result.Success ? "同步成功" : "同步失败";
            ReloadSyncLogs();
        });
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        if (IsSyncing) return;
        IsSyncing = true;
        StatusText = "同步中…";
        // 立即插入一条 running 日志并刷新 UI（SyncTrigger 内部也会写日志，
        // 但 RunNowAsync 完成前 UI 不会刷新，这里先刷新让用户看到"进行中"状态）
        ReloadSyncLogs();
        try
        {
            var result = await _trigger.RunNowAsync();
            StatusText = result.Success ? "同步成功" : "同步失败";
            UpdateLastSyncText(_cfgRepo.Get());
            ReloadSyncLogs();
            DisplayToast(result.Success ? result.Message : ("失败：" + result.Message));
        }
        finally
        {
            IsSyncing = false;
        }
    }
}
