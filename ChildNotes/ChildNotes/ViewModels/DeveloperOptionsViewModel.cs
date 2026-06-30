using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 开发者选项页 ViewModel：管理日志悬浮层等开发工具的开关。
/// </summary>
public partial class DeveloperOptionsViewModel : ViewModelBase
{
    [ObservableProperty] private bool _showDevLogOverlay;

    public DeveloperOptionsViewModel()
    {
        Title = "开发者选项";
        Load();
    }

    private bool _loading = true;

    private void Load()
    {
        var cfg = DeveloperPreferences.Load();
        ShowDevLogOverlay = cfg.ShowDevLogOverlay;
        _loading = false;
    }

    /// <summary>开关切换时立即持久化，跳过首次加载触发。</summary>
    partial void OnShowDevLogOverlayChanged(bool value)
    {
        if (_loading) return;
        var cfg = DeveloperPreferences.Load();
        if (cfg.ShowDevLogOverlay == value) return;
        cfg.ShowDevLogOverlay = value;
        DeveloperPreferences.Save(cfg);
        // 通知 MainShellViewModel 更新悬浮层可见性（通过事件或直接引用）
        DevLogOverlayVisibilityChanged?.Invoke(value);
    }

    /// <summary>悬浮层可见性变更事件，供 MainShellViewModel 订阅以实时更新 UI。</summary>
    public event Action<bool>? DevLogOverlayVisibilityChanged;
}
