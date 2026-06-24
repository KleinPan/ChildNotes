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
    [ObservableProperty] private DateTimeOffset? _startDate;
    [ObservableProperty] private DateTimeOffset? _endDate;
    [ObservableProperty] private string _rangeTip = "请选择连续 7 天作为分析区间";
    [ObservableProperty] private bool _rangeValid;
    [ObservableProperty] private bool _canGenerate = true;
    [ObservableProperty] private bool _generating;
    [ObservableProperty] private string _generateButtonText = "生成新的分析";
    [ObservableProperty] private bool _showConfigSheet;
    [ObservableProperty] private bool _showDetail;
    [ObservableProperty] private string _detailText = string.Empty;
    [ObservableProperty] private string _detailRangeLabel = string.Empty;
    [ObservableProperty] private string _detailCreatedLabel = string.Empty;
    [ObservableProperty] private string _detailQualityTip = string.Empty;
    [ObservableProperty] private string _toastMessage = string.Empty;
    [ObservableProperty] private bool _showToast;
    [ObservableProperty] private string _errorMessage = string.Empty;

    [ObservableProperty] private string _configApiBaseUrl = string.Empty;
    [ObservableProperty] private string _configApiKey = string.Empty;
    [ObservableProperty] private string _configModelName = string.Empty;
    [ObservableProperty] private double _configTemperature = 0.7;
    [ObservableProperty] private int _configMaxTokens = 2048;

    public ObservableCollection<AiAnalysisRecord> Records { get; } = new();

    public event Action? BackRequested;

    public void Load()
    {
        var baby = _state.CurrentBaby;
        BabyName = baby?.Name ?? string.Empty;

        var today = DateTime.Today;
        StartDate = new DateTimeOffset(today.AddDays(-6));
        EndDate = new DateTimeOffset(today);
        UpdateRangeTip();

        Records.Clear();
        foreach (var r in _aiService.ListRecords()) Records.Add(r);

        LoadConfig();
    }

    private void LoadConfig()
    {
        var config = _aiService.GetLlmConfig();
        ConfigApiBaseUrl = config.ApiBaseUrl;
        ConfigApiKey = config.ApiKey;
        ConfigModelName = config.ModelName;
        ConfigTemperature = config.Temperature;
        ConfigMaxTokens = config.MaxTokens;
    }

    partial void OnStartDateChanged(DateTimeOffset? value) => UpdateRangeTip();
    partial void OnEndDateChanged(DateTimeOffset? value) => UpdateRangeTip();

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
            ShowConfigSheet = true;
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

    [RelayCommand]
    private void OpenConfig()
    {
        LoadConfig();
        ShowConfigSheet = true;
    }

    [RelayCommand]
    private void CloseConfig()
    {
        ShowConfigSheet = false;
    }

    [RelayCommand]
    private void SaveConfig()
    {
        var config = new LlmConfig
        {
            ApiBaseUrl = ConfigApiBaseUrl,
            ApiKey = ConfigApiKey,
            ModelName = ConfigModelName,
            Temperature = ConfigTemperature,
            MaxTokens = ConfigMaxTokens,
            Enabled = true,
        };
        _aiService.SaveLlmConfig(config);
        ShowConfigSheet = false;
        ShowToastMessage("配置已保存");
    }

    public void Back() => BackRequested?.Invoke();

    private async void ShowToastMessage(string msg)
    {
        ToastMessage = msg;
        ShowToast = true;
        await Task.Delay(2500);
        ShowToast = false;
    }
}
