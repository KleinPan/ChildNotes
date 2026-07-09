using System.Collections.ObjectModel;
using Avalonia.Threading;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildNotes.ViewModels;

/// <summary>
/// "程序日志"页 ViewModel：展示 DevLogger 内存环形缓冲区的实时日志。
/// 功能：
/// - 实时展示日志（订阅 Logged 事件）
/// - 按级别筛选（全部/Info/Warn/Error）
/// - 按关键字搜索（Tag 或 Message 模糊匹配）
/// - 导出当前筛选结果为 .txt 文件
/// </summary>
public partial class AppLogViewModel : ViewModelBase, IActivatable
{
    private readonly ObservableCollection<DevLogger.LogEntry> _allEntries = new();
    private readonly ObservableCollection<DevLogger.LogEntry> _filtered = new();
    public ObservableCollection<DevLogger.LogEntry> Filtered => _filtered;

    /// <summary>搜索关键字（为空时不过滤）。</summary>
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>当前选中的级别筛选：0=全部, 1=Info, 2=Warn, 3=Error, 4=AI。</summary>
    [ObservableProperty] private int _levelFilter = 0;

    /// <summary>是否暂停自动滚动（用户手动滚动后可暂停，避免新日志把列表顶下去）。</summary>
    [ObservableProperty] private bool _autoScroll = true;

    /// <summary>是否正在导出。</summary>
    [ObservableProperty] private bool _isExporting;

    public bool IsAllLevel => LevelFilter == 0;
    public bool IsInfoLevel => LevelFilter == 1;
    public bool IsWarnLevel => LevelFilter == 2;
    public bool IsErrorLevel => LevelFilter == 3;
    public bool IsAiLevel => LevelFilter == 4;

    public AppLogViewModel()
    {
        Title = "程序日志";
    }

    public void Activate()
    {
        // 订阅实时日志
        DevLogger.Logged -= OnLogged;
        DevLogger.Logged += OnLogged;

        // 加载已有日志（倒序：最新在上）
        _allEntries.Clear();
        foreach (var e in DevLogger.Entries)
            _allEntries.Insert(0, e);
        ApplyFilter();
    }

    /// <summary>页面关闭时取消订阅（由 MainShell 关闭逻辑间接触发，无需显式调用）。</summary>
    public void Deactivate()
    {
        DevLogger.Logged -= OnLogged;
    }

    private void OnLogged(DevLogger.LogEntry entry)
    {
        // 新日志插入到最前面（倒序展示，最新在上）
        if (Dispatcher.UIThread.CheckAccess())
        {
            _allEntries.Insert(0, entry);
            if (MatchesFilter(entry))
                _filtered.Insert(0, entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                _allEntries.Insert(0, entry);
                if (MatchesFilter(entry))
                    _filtered.Insert(0, entry);
            });
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnLevelFilterChanged(int value)
    {
        OnPropertyChanged(nameof(IsAllLevel));
        OnPropertyChanged(nameof(IsInfoLevel));
        OnPropertyChanged(nameof(IsWarnLevel));
        OnPropertyChanged(nameof(IsErrorLevel));
        OnPropertyChanged(nameof(IsAiLevel));
        ApplyFilter();
    }

    /// <summary>判断单条日志是否匹配当前搜索关键字与级别筛选。</summary>
    private bool MatchesFilter(DevLogger.LogEntry e)
    {
        // 级别筛选
        if (LevelFilter == 1 && e.Level != DevLogger.Level.Info) return false;
        if (LevelFilter == 2 && e.Level != DevLogger.Level.Warn) return false;
        if (LevelFilter == 3 && e.Level != DevLogger.Level.Error) return false;
        // AI 筛选：匹配 AiNote / LlmClient / QuickInput 等 AI 相关 Tag，
        // 或消息含 [AI-LOG] 标记，或 LogLlmCall 生成的 "LLM [" 前缀
        if (LevelFilter == 4 && !IsAiRelated(e)) return false;

        // 关键字搜索（Tag 或 Message）
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var kw = SearchText.Trim();
            if (!e.Tag.Contains(kw, StringComparison.OrdinalIgnoreCase)
                && !e.Message.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 判断日志是否为 AI 相关：用于"AI"快捷筛选按钮。
    /// 匹配规则（任一命中即视为 AI 相关）：
    /// - Tag 以 AiNote / LlmClient / QuickInput 开头（含 LlmClient/Test 子标签）
    /// - Message 含 "[AI-LOG]" 标记（AiNoteParseService / QuickInputViewModel 显式标记）
    /// - Message 以 "LLM [" 开头（DevLogger.LogLlmCall 生成的格式化消息）
    /// </summary>
    private static bool IsAiRelated(DevLogger.LogEntry e)
    {
        return e.Tag.StartsWith("AiNote", StringComparison.OrdinalIgnoreCase)
            || e.Tag.StartsWith("LlmClient", StringComparison.OrdinalIgnoreCase)
            || e.Tag.StartsWith("QuickInput", StringComparison.OrdinalIgnoreCase)
            || e.Message.Contains("[AI-LOG]", StringComparison.OrdinalIgnoreCase)
            || e.Message.StartsWith("LLM [", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>按当前筛选条件刷新 Filtered 集合。</summary>
    private void ApplyFilter()
    {
        _filtered.Clear();
        foreach (var e in _allEntries)
        {
            if (MatchesFilter(e))
                _filtered.Add(e);
        }
    }

    [RelayCommand] private void SelectAllLevel() => LevelFilter = 0;
    [RelayCommand] private void SelectInfoLevel() => LevelFilter = 1;
    [RelayCommand] private void SelectWarnLevel() => LevelFilter = 2;
    [RelayCommand] private void SelectErrorLevel() => LevelFilter = 3;
    [RelayCommand] private void SelectAiLevel() => LevelFilter = 4;

    /// <summary>清空内存日志缓冲区（不影响已写入文件的日志）。</summary>
    [RelayCommand]
    private void ClearLogs()
    {
        DevLogger.Clear();
        _allEntries.Clear();
        _filtered.Clear();
    }

    /// <summary>导出当前筛选后的日志为 .txt 文件。</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (IsExporting) return;
        IsExporting = true;
        try
        {
            var result = await AppLogExportService.ExportAsync(_filtered);
            if (result.Success)
            {
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

    private bool CanExport() => !IsExporting && _filtered.Count > 0;

    partial void OnIsExportingChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();

    /// <summary>导出提示需要更长时间让用户看清文件路径。</summary>
    protected override int ToastDurationMs => 5000;
}
