using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 开发者选项页 ViewModel：管理日志导出、动画设置等开发工具。
/// 仅开发版构建可通过 MineView 入口进入；正式版入口已隐藏。
/// </summary>
public partial class DeveloperOptionsViewModel : ViewModelBase
{
    /// <summary>是否正在导出日志（防止重复点击）。</summary>
    [ObservableProperty] private bool _isExporting;

    /// <summary>是否启用动画效果。</summary>
    [ObservableProperty] private bool _enableAnimations = true;

    /// <summary>
    /// 日志导出功能是否可见。仅开发版可见，正式版隐藏导出按钮。
    /// 虽然开发者选项入口本身在正式版已隐藏，此属性作为双重保险，
    /// 确保 AppLogView 等其他入口也能正确隐藏导出功能。
    /// </summary>
    public bool IsLogExportVisible => BuildConfiguration.IsDevelopmentBuild;

    /// <summary>请求打开"程序日志"页（由 MainShellViewModel 订阅）。</summary>
    public event Action? OpenAppLogRequested;

    /// <summary>打开"程序日志"页面。</summary>
    [RelayCommand]
    private void OpenAppLog() => OpenAppLogRequested?.Invoke();

    public DeveloperOptionsViewModel()
    {
        Title = "开发者选项";
        LoadSettings();
    }

    /// <summary>从持久化配置加载设置。</summary>
    private void LoadSettings()
    {
        try
        {
            var config = DeveloperPreferences.Load();
            EnableAnimations = config.EnableAnimations;
        }
        catch
        {
            // 加载失败时使用默认值
            EnableAnimations = true;
        }
    }

    /// <summary>保存当前设置到持久化配置。</summary>
    private void SaveSettings()
    {
        try
        {
            var config = new DeveloperOptionsConfig
            {
                EnableAnimations = EnableAnimations
            };
            DeveloperPreferences.Save(config);

            // 实时应用动画开关
            AnimationService.IsEnabled = EnableAnimations;
        }
        catch (Exception ex)
        {
            DisplayToast("保存设置失败：" + ex.Message);
        }
    }

    /// <summary>导出当前运行日志到 .txt 文件。</summary>
    [RelayCommand(CanExecute = nameof(CanExportLog))]
    private async Task ExportLogAsync()
    {
        if (IsExporting) return;
        IsExporting = true;
        try
        {
            var result = await LogExportService.ExportAsync();
            if (result.Success)
            {
                // Android 端路径为相对展示路径（Download/xxx.txt），桌面端为绝对路径
                var location = OperatingSystem.IsAndroid()
                    ? $"Download/{result.FilePath}"
                    : result.FilePath;
                DisplayToast($"已导出 {result.LineCount} 行日志到：{location}");
            }
            else
            {
                DisplayToast("导出失败：" + result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            DisplayToast("导出失败：" + ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }

    private bool CanExportLog() => !IsExporting;

    /// <summary>导出结果提示需要更长时间让用户看清文件路径。</summary>
    protected override int ToastDurationMs => 5000;

    partial void OnIsExportingChanged(bool value) => ExportLogCommand.NotifyCanExecuteChanged();

    /// <summary>动画开关变化时自动保存并实时生效。</summary>
    partial void OnEnableAnimationsChanged(bool value) => SaveSettings();
}
