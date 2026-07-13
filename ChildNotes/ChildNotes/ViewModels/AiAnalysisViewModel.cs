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
    private readonly PointsService _pointsService = ServiceProvider.Instance.PointsService;
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

    /// <summary>当前用户积分余额（进入页面和生成后刷新）。</summary>
    [ObservableProperty] private int _currentPoints;
    /// <summary>本次 AI 分析需消耗的积分数量（从后端实时获取）。</summary>
    [ObservableProperty] private int _analysisCost = PointsConstants.AiAnalysisDefaultCost;
    /// <summary>积分是否充足：用于控制生成按钮文案和充值入口显示。</summary>
    [ObservableProperty] private bool _pointsSufficient = true;
    /// <summary>积分不足提示文案。</summary>
    [ObservableProperty] private string _insufficientTip = string.Empty;
    /// <summary>是否已加载更多历史记录（懒加载：首次仅展示最近 5 条）。</summary>
    [ObservableProperty] private bool _allLoaded;
    /// <summary>是否还有更多历史记录可加载。</summary>
    [ObservableProperty] private bool _hasMore;

    /// <summary>懒加载分页大小：首次进入页面仅加载最近 5 条记录。</summary>
    private const int InitialPageSize = 5;
    private const int LoadMorePageSize = 10;

    private List<AiAnalysisRecord> _allRecords = new();
    private int _loadedCount;

    public ObservableCollection<AiAnalysisRecord> Records { get; } = new();

    // AI 分析取消令牌：用户点击"取消分析"时取消正在进行的 LLM 请求
    private CancellationTokenSource? _generateCts;

    /// <summary>沿用历史 2500ms 显示时长。</summary>
    protected override int ToastDurationMs => 2500;

    /// <summary>请求跳转到 AI 分析设置页（由 MainShellViewModel 订阅）。</summary>
    public event Action? ConfigRequired;

    /// <summary>请求跳转到积分页（充值入口，由 MainShellViewModel 订阅）。</summary>
    public event Action? PointsRequired;

    /// <summary>请求跳转到会员中心（AI 次数用尽时，由 MainShellViewModel 订阅）。</summary>
    public event Action? MembershipRequired;

    /// <summary>
    /// 异步加载：DB 查询放到后台线程，UI 线程仅做集合填充。
    /// 用于弹层"先打开再加载"模式，避免阻塞 UI。
    /// </summary>
    public async Task LoadAsync()
    {
        var baby = _state.CurrentBaby;
        BabyName = baby?.Name ?? string.Empty;

        var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local);
        StartDate = today.AddDays(-6);
        EndDate = today;
        UpdateRangeTip();

        // 并行加载积分余额、分析成本、历史记录
        var pointsTask = Task.Run(() => _pointsService.GetDashboard());
        var costTask = _aiService.GetAnalysisCostAsync();
        var serverRecordsTask = _aiService.ListRecordsFromServerAsync();

        var dashboard = await pointsTask;
        CurrentPoints = dashboard.Points;

        AnalysisCost = await costTask;
        RefreshPointsSufficiency();

        List<AiAnalysisRecord> records;
        var serverRecords = await serverRecordsTask;
        records = serverRecords ?? await Task.Run(() => _aiService.ListRecords());

        _allRecords = records.OrderByDescending(r => r.RangeStartDate).ToList();
        _loadedCount = 0;
        Records.Clear();
        LoadMoreRecords(InitialPageSize);
    }

    /// <summary>刷新积分是否充足的判断。</summary>
    private void RefreshPointsSufficiency()
    {
        PointsSufficient = CurrentPoints >= AnalysisCost;
        InsufficientTip = PointsSufficient
            ? string.Empty
            : $"积分不足，需 {AnalysisCost} 积分，当前 {CurrentPoints} 积分（每日签到可获取积分）";
    }

    /// <summary>从全量记录中加载下一批到 ObservableCollection（懒加载）。</summary>
    private void LoadMoreRecords(int count)
    {
        var remaining = _allRecords.Count - _loadedCount;
        if (remaining <= 0)
        {
            AllLoaded = true;
            HasMore = false;
            return;
        }
        var take = Math.Min(count, remaining);
        for (var i = 0; i < take; i++)
            Records.Add(_allRecords[_loadedCount + i]);
        _loadedCount += take;
        AllLoaded = _loadedCount >= _allRecords.Count;
        HasMore = !AllLoaded;
    }

    /// <summary>加载更多历史记录（懒加载：每次追加 10 条）。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private void LoadMore()
    {
        LoadMoreRecords(LoadMorePageSize);
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    private bool CanLoadMore => HasMore && !Generating;

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
        if (!config.Enabled)
        {
            DisplayToast("请先在设置中启用大模型");
            ConfigRequired?.Invoke();
            return;
        }

        // 积分不足提示 + 提供充值入口（server 模式下才校验，local 模式不消耗积分）
        if (config.NoteSource == "server" && !PointsSufficient)
        {
            ErrorMessage = InsufficientTip;
            DisplayToast($"积分不足，需 {AnalysisCost} 积分，当前 {CurrentPoints} 积分");
            return;
        }

        // 取消上一次未完成的请求（防御性，正常情况下 finally 已清理）
        _generateCts?.Cancel();
        _generateCts?.Dispose();
        _generateCts = new CancellationTokenSource();

        Generating = true;
        GenerateButtonText = "正在分析...";
        ErrorMessage = string.Empty;

        try
        {
            var record = await _aiService.GenerateAsync(StartDate.Value.Date, EndDate.Value.Date, _generateCts.Token);
            // 生成后刷新积分余额（server 模式扣了积分）
            if (config.NoteSource == "server")
            {
                var dashboard = await Task.Run(() => _pointsService.GetDashboard());
                CurrentPoints = dashboard.Points;
                RefreshPointsSufficiency();
            }
            // 生成后刷新记录列表：server 模式从后端拉取，local 模式从本地 DB 读取
            var serverRecords = await _aiService.ListRecordsFromServerAsync();
            var records = serverRecords ?? _aiService.ListRecords();
            _allRecords = records.OrderByDescending(r => r.RangeStartDate).ToList();
            _loadedCount = 0;
            Records.Clear();
            LoadMoreRecords(InitialPageSize);
            ShowDetail = true;
            DetailText = record.AnalysisText;
            DetailRangeLabel = record.RangeLabel;
            DetailCreatedLabel = record.CreatedAtLabel;
            DetailQualityTip = record.DataQualityTip;
            UpdateRangeTip();
            DisplayToast("分析完成");
        }
        catch (OperationCanceledException)
        {
            DisplayToast("已取消分析");
        }
        catch (AiAnalysisApiException ex)
        {
            // 积分不足：刷新余额并提示充值
            if (ex.IsInsufficientPoints)
            {
                var dashboard = await Task.Run(() => _pointsService.GetDashboard());
                CurrentPoints = dashboard.Points;
                RefreshPointsSufficiency();
                ErrorMessage = $"积分不足，本次分析需 {AnalysisCost} 积分，当前余额 {CurrentPoints} 积分";
                DisplayToast("积分不足，请每日签到获取积分");
            }
            // AI 分析次数用尽（本周）：提示并跳转会员中心
            else if (ex.IsAiLimitExceeded)
            {
                ErrorMessage = "本周 AI 分析次数已用完，升级会员可享 10 次/周";
                DisplayToast("本周次数已达上限，升级会员解锁更多次数");
                MembershipRequired?.Invoke();
            }
            else
            {
                ErrorMessage = ex.Message;
                DisplayToast("分析失败：" + ex.Message);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            DisplayToast("分析失败：" + ex.Message);
        }
        finally
        {
            Generating = false;
            GenerateButtonText = CanGenerate ? "生成新的分析" : "该区间已分析";
            _generateCts?.Dispose();
            _generateCts = null;
        }
    }

    /// <summary>取消正在进行的 AI 分析请求。</summary>
    [RelayCommand(CanExecute = nameof(CanCancelGenerate))]
    private void CancelGenerate()
    {
        _generateCts?.Cancel();
    }

    private bool CanCancelGenerate => Generating;

    /// <summary>Generating 状态变化时刷新取消按钮可用性。</summary>
    partial void OnGeneratingChanged(bool value) => CancelGenerateCommand.NotifyCanExecuteChanged();

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

    /// <summary>请求跳转到积分页（充值入口）。</summary>
    [RelayCommand]
    private void OpenPoints()
    {
        PointsRequired?.Invoke();
    }
}
