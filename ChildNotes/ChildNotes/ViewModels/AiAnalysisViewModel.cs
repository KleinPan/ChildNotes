using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class AiAnalysisViewModel : ViewModelBase
{
    private readonly AiAnalysisService _aiService = ServiceProvider.Instance.AiAnalysisService;
    private readonly AppState _state = ServiceProvider.Instance.AppState;

    [ObservableProperty] private string _babyName = string.Empty;
    // 使用 DateTime? 而非 DateTimeOffset? —— Avalonia 12 的 CalendarDatePicker.SelectedDate
    // 实际类型是 DateTime?，使用 DateTimeOffset? 绑定时控件会调用 DateTimeOffset.ToString()
    // 生成 "2026/6/22 0:00:00 +08:00" 这样的带偏移字符串，再尝试解析为 DateTime 时
    // 因偏移量导致失败，抛出 "could not convert ... to System.DateTime" 错误。
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _endDate;
    [ObservableProperty] private string _rangeTip = "请选择连续 7 天作为分析区间";
    [ObservableProperty] private bool _rangeValid;
    [ObservableProperty] private bool _canGenerate = true;
    [ObservableProperty] private bool _generating;
    [ObservableProperty] private string _generateButtonText = "生成新的分析";
    [ObservableProperty] private bool _showDetail;
    [ObservableProperty] private string _detailText = string.Empty;
    [ObservableProperty] private string _detailRangeLabel = string.Empty;
    [ObservableProperty] private string _detailCreatedLabel = string.Empty;
    [ObservableProperty] private string _detailQualityTip = string.Empty;

    public ObservableCollection<AiAnalysisRecord> Records { get; } = new();

    /// <summary>沿用历史 2500ms 显示时长。</summary>
    protected override int ToastDurationMs => 2500;

    /// <summary>请求跳转到 AI 分析设置页（由 MainShellViewModel 订阅）。</summary>
    public event Action? ConfigRequired;

    public void Load()
    {
        var baby = _state.CurrentBaby;
        BabyName = baby?.Name ?? string.Empty;

        // 显式指定 Local Kind，避免 CalendarDatePicker 双向绑定回传时
        // 丢失 Kind 信息导致后续与 DateTime.Today 做差值运算抛 DateTimeKind 异常
        var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local);
        StartDate = today.AddDays(-6);
        EndDate = today;
        UpdateRangeTip();

        Records.Clear();
        foreach (var r in _aiService.ListRecords()) Records.Add(r);
    }

    partial void OnStartDateChanged(DateTime? value) => UpdateRangeTip();
    partial void OnEndDateChanged(DateTime? value) => UpdateRangeTip();

    private void UpdateRangeTip()
    {
        ErrorMessage = string.Empty;
        if (StartDate is null || EndDate is null)
        {
            RangeTip = "请选择连续 7 天作为分析区间";
            RangeValid = false;
            CanGenerate = false;
            return;
        }

        var start = StartDate.Value.Date;
        var end = EndDate.Value.Date;
        var days = (end - start).Days + 1;

        if (days < 7)
        {
            RangeTip = "分析区间不能少于 7 天";
            RangeValid = false;
            CanGenerate = false;
        }
        else if (days > 7)
        {
            RangeTip = "分析区间不能超过 7 天";
            RangeValid = false;
            CanGenerate = false;
        }
        else
        {
            RangeTip = "将分析该连续 7 天内的记录";
            RangeValid = true;
            CanGenerate = !_aiService.HasRangeAnalysis(start, end);
            GenerateButtonText = CanGenerate ? "生成新的分析" : "该区间已分析";
        }
    }

    [RelayCommand]
    private async Task Generate()
    {
        if (Generating || !RangeValid || StartDate is null || EndDate is null) return;

        var config = _aiService.GetLlmConfig();
        if (!config.Enabled || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            ShowToastMessage("请先配置大模型 API Key");
            ConfigRequired?.Invoke();
            return;
        }

        Generating = true;
        GenerateButtonText = "正在分析...";
        ErrorMessage = string.Empty;

        try
        {
            var record = await _aiService.GenerateAsync(StartDate.Value.Date, EndDate.Value.Date);
            Records.Clear();
            foreach (var r in _aiService.ListRecords()) Records.Add(r);
            ShowDetail = true;
            DetailText = record.AnalysisText;
            DetailRangeLabel = record.RangeLabel;
            DetailCreatedLabel = record.CreatedAtLabel;
            DetailQualityTip = record.DataQualityTip;
            UpdateRangeTip();
            ShowToastMessage("分析完成");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ShowToastMessage("分析失败：" + ex.Message);
        }
        finally
        {
            Generating = false;
            GenerateButtonText = CanGenerate ? "生成新的分析" : "该区间已分析";
        }
    }

    public void OpenDetail(AiAnalysisRecord record)
    {
        ShowDetail = true;
        DetailText = record.AnalysisText;
        DetailRangeLabel = record.RangeLabel;
        DetailCreatedLabel = record.CreatedAtLabel;
        DetailQualityTip = record.DataQualityTip;
    }

    [RelayCommand]
    private void BackToList()
    {
        ShowDetail = false;
    }

    /// <summary>请求跳转到 AI 分析设置页。</summary>
    [RelayCommand]
    private void OpenConfig()
    {
        ConfigRequired?.Invoke();
    }

    // 历史调用 ShowToastMessage，统一改走基类 DisplayToast（沿用 2500ms 时长由 ToastDurationMs 覆写控制）
    private void ShowToastMessage(string msg) => DisplayToast(msg);
}
