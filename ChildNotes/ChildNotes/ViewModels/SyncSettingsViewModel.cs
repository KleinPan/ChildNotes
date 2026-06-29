using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 同步设置页 ViewModel：管理 sync_config 配置 + 手动触发同步。
/// 服务器地址由 <see cref="ServerEndpoints"/> 硬编码，UI 不再提供编辑入口。
/// </summary>
public partial class SyncSettingsViewModel : ViewModelBase
{
    private readonly SyncConfigRepository _cfgRepo = ServiceProvider.Instance.SyncConfigRepository;
    private readonly SyncTrigger _trigger = ServiceProvider.Instance.SyncTrigger;
    private readonly NetworkMonitor _networkMonitor = ServiceProvider.Instance.NetworkMonitor;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _lastSyncText = "尚未同步";
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private string _toast = string.Empty;
    [ObservableProperty] private bool _showToast;

    /// <summary>网络状态文案（绑定到 UI 徽标）。</summary>
    [ObservableProperty] private string _networkStateText = "检测中…";
    /// <summary>是否在线（用于徽标颜色绑定）。</summary>
    [ObservableProperty] private bool _isOnline;

    /// <summary>服务器地址只读展示。</summary>
    public string ServerUrlDisplay => ServerEndpoints.Primary;

    public string SyncButtonText => IsSyncing ? "同步中…" : "立即同步";

    partial void OnIsSyncingChanged(bool value) => OnPropertyChanged(nameof(SyncButtonText));

    public SyncSettingsViewModel()
    {
        Title = "数据同步";
        Load();
        UpdateNetworkState(_networkMonitor.Current);
        _networkMonitor.StateChanged += OnNetworkStateChanged;
        _trigger.SyncCompleted += OnSyncCompleted;
    }

    private void Load()
    {
        var cfg = _cfgRepo.Get();
        Enabled = cfg.Enabled;
        Username = cfg.Username;
        Password = cfg.Password;
        UpdateLastSyncText(cfg);
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
    private void Save()
    {
        var cfg = _cfgRepo.Get();
        cfg.Enabled = Enabled;
        cfg.Username = Username.Trim();
        if (!string.IsNullOrWhiteSpace(Password))
            cfg.Password = Password; // 仅在用户输入新密码时覆盖
        // ServerUrl 不再由 UI 配置，保留硬编码值
        _cfgRepo.Save(cfg);
        ShowToastMsg("已保存");
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        if (IsSyncing) return;
        // 先保存当前编辑值，确保同步使用最新配置
        Save();
        IsSyncing = true;
        StatusText = "同步中…";
        try
        {
            var result = await _trigger.RunNowAsync();
            StatusText = result.Success ? "同步成功" : "同步失败";
            UpdateLastSyncText(_cfgRepo.Get());
            ShowToastMsg(result.Success ? result.Message : ("失败：" + result.Message));
        }
        finally
        {
            IsSyncing = false;
        }
    }

    // 历史调用 ShowToastMsg，统一改走基类 DisplayToast
    private void ShowToastMsg(string msg) => DisplayToast(msg);
}
