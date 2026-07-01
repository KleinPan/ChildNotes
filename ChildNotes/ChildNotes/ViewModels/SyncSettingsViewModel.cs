using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 同步设置页 ViewModel：管理 sync_config 的 enabled 开关 + 手动触发同步。
/// 服务器地址由 <see cref="ServerEndpoints"/> 硬编码，登录凭据由登录流程自动写入，
/// UI 不再提供账号/密码/服务器编辑入口。
/// </summary>
/// <remarks>
/// 修复：原实现订阅了外部单例（NetworkMonitor / SyncTrigger）事件但从未退订，
/// 若 VM 被多次创建会叠加订阅。现实现 IDisposable 在 Dispose 中退订。
/// 同时移除与基类 ViewModelBase 冲突的 _toast / _showToast 字段（基类已有 _toastMessage / _showToast），
/// 并删除 ShowToastMsg 包装方法（直接调用基类 DisplayToast）。
/// </remarks>
public partial class SyncSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly SyncConfigRepository _cfgRepo = ServiceProvider.Instance.SyncConfigRepository;
    private readonly SyncTrigger _trigger = ServiceProvider.Instance.SyncTrigger;
    private readonly NetworkMonitor _networkMonitor = ServiceProvider.Instance.NetworkMonitor;
    private bool _disposed;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _lastSyncText = "尚未同步";
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isSyncing;

    /// <summary>网络状态文案（绑定到 UI 徽标）。</summary>
    [ObservableProperty] private string _networkStateText = "检测中…";
    /// <summary>是否在线（用于徽标颜色绑定）。</summary>
    [ObservableProperty] private bool _isOnline;

    /// <summary>服务器地址只读展示。</summary>
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
        UpdateLastSyncText(cfg);
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
        });
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        if (IsSyncing) return;
        IsSyncing = true;
        StatusText = "同步中…";
        try
        {
            var result = await _trigger.RunNowAsync();
            StatusText = result.Success ? "同步成功" : "同步失败";
            UpdateLastSyncText(_cfgRepo.Get());
            DisplayToast(result.Success ? result.Message : ("失败：" + result.Message));
        }
        finally
        {
            IsSyncing = false;
        }
    }
}
