using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services.Sync;

namespace ChildNotes.ViewModels;

/// <summary>
/// 同步设置页 ViewModel：配置 WebDAV 服务器、测试连接、手动同步。
/// </summary>
public partial class SyncSettingsViewModel : ViewModelBase, IActivatable
{
    private readonly WebDavConfigRepository _configRepo = ServiceProvider.Instance.WebDavConfigRepository;
    private readonly SyncService _syncService = ServiceProvider.Instance.SyncService;
    private readonly SyncTrigger _syncTrigger = ServiceProvider.Instance.SyncTrigger;

    [ObservableProperty] private string _serverUrl = "https://dav.jianguoyun.com/dav/";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _remotePath = "/ChildNotes/";
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _autoSync = true;
    [ObservableProperty] private string _lastSyncText = "从未同步";
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private bool _isSyncing;

    public event Action? BackRequested;

    public void Activate()
    {
        var cfg = _configRepo.GetOrCreate();
        ServerUrl = cfg.ServerUrl;
        Username = cfg.Username;
        Password = cfg.Password;
        RemotePath = cfg.RemotePath;
        Enabled = cfg.Enabled;
        AutoSync = cfg.AutoSync;
        UpdateLastSyncText(cfg);
        StatusText = string.Empty;
    }

    [RelayCommand]
    private void Back()
    {
        BackRequested?.Invoke();
    }

    /// <summary>保存配置（不立即测试连接）。</summary>
    [RelayCommand]
    private void Save()
    {
        var cfg = new WebDavConfig
        {
            Id = 1,
            ServerUrl = ServerUrl.Trim(),
            Username = Username.Trim(),
            Password = Password,
            RemotePath = string.IsNullOrEmpty(RemotePath.Trim()) ? "/ChildNotes/" : RemotePath.Trim(),
            Enabled = Enabled,
            AutoSync = AutoSync,
            LastSyncAt = _configRepo.GetOrCreate().LastSyncAt,
            LastSyncStatus = _configRepo.GetOrCreate().LastSyncStatus,
            UpdatedAt = DateTime.UtcNow
        };
        _configRepo.Save(cfg);
        StatusText = "配置已保存";
    }

    /// <summary>测试连接：尝试创建远程目录。</summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrEmpty(ServerUrl) || string.IsNullOrEmpty(Username))
        {
            StatusText = "请填写服务器地址和账号";
            return;
        }

        IsTesting = true;
        StatusText = "测试中...";
        try
        {
            // 先保存配置
            Save();

            using var client = new WebDavClient(ServerUrl, Username, Password, RemotePath);
            await client.EnsureFolderAsync("");
            await client.EnsureFolderAsync("images/");

            StatusText = "连接成功，远程目录已就绪";
        }
        catch (Exception ex)
        {
            StatusText = "连接失败：" + ex.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }

    /// <summary>立即同步。</summary>
    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (!Enabled)
        {
            StatusText = "请先启用同步";
            return;
        }

        IsSyncing = true;
        StatusText = "同步中...";
        _syncService.ProgressChanged += OnProgressChanged;
        try
        {
            var result = await _syncService.SyncAsync();
            StatusText = result.Success ? $"同步完成：{result.Detail}" : $"同步失败：{result.Detail}";
            UpdateLastSyncText(_configRepo.GetOrCreate());
        }
        catch (Exception ex)
        {
            StatusText = "同步异常：" + ex.Message;
        }
        finally
        {
            _syncService.ProgressChanged -= OnProgressChanged;
            IsSyncing = false;
        }
    }

    private void OnProgressChanged(SyncProgress p)
    {
        // 直接更新状态文本，让用户看到实时进度
        StatusText = p.Stage;
    }

    private void UpdateLastSyncText(WebDavConfig cfg)
    {
        if (cfg.LastSyncAt == null)
        {
            LastSyncText = "从未同步";
            return;
        }
        var localTime = cfg.LastSyncAt.Value.ToLocalTime();
        var status = string.IsNullOrEmpty(cfg.LastSyncStatus) ? "" : $"（{cfg.LastSyncStatus}）";
        LastSyncText = $"上次同步：{localTime:yyyy-MM-dd HH:mm:ss}{status}";
    }
}
