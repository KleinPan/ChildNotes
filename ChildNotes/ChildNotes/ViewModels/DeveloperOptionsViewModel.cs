using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 开发者选项页 ViewModel：管理日志导出等开发工具。
/// </summary>
public partial class DeveloperOptionsViewModel : ViewModelBase
{
    /// <summary>是否正在导出日志（防止重复点击）。</summary>
    [ObservableProperty] private bool _isExporting;

    public DeveloperOptionsViewModel()
    {
        Title = "开发者选项";
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
}
