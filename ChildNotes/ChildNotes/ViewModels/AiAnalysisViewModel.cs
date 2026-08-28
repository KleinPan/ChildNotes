using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;

namespace ChildNotes.ViewModels;

public partial class AiAnalysisViewModel : ViewModelBase
{
    private readonly AiAnalysisService _aiService = ServiceProvider.Instance.AiAnalysisService;
    private readonly PointsService _pointsService = ServiceProvider.Instance.PointsService;
    private readonly PointsApiClient _pointsApi = ServiceProvider.Instance.PointsApiClient;
    private readonly AppState _state = ServiceProvider.Instance.AppState;
    private readonly LocaleManager _locale = LocaleManager.Instance;

    [ObservableProperty] private string _babyName = string.Empty;
    // 使用 DateTime? 而非 DateTimeOffset? —— Avalonia 12 的 CalendarDatePicker.SelectedDate
    // 实际类型是 DateTime?，使用 DateTimeOffset? 绑定时控件会调用 DateTimeOffset.ToString()
    // 生成 "2026/6/22 0:00:00 +08:00" 这样的带偏移字符串，再尝试解析为 DateTime 时
    // 因偏移量导致失败，抛出 "could not convert ... to System.DateTime" 错误。
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _endDate;
    [ObservableProperty] private string _rangeTip = string.Empty;
    [ObservableProperty] private bool _rangeValid;
    [ObservableProperty] private bool _canGenerate = true;
    [ObservableProperty] private bool _generating;
    [ObservableProperty] private string _generateButtonText = string.Empty;
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
    /// <summary>积分行显示文案（"积分 {余额} / 消耗 {成本}"），供 XAML 直接绑定。</summary>
    [ObservableProperty] private string _pointsDisplayText = string.Empty;
    /// <summary>是否已加载更多历史记录（懒加载：首次仅展示最近 5 条）。</summary>
    [ObservableProperty] private bool _allLoaded;
    /// <summary>是否还有更多历史记录可加载。</summary>
    [ObservableProperty] private bool _hasMore;

    /// <summary>是否显示"免费次数用尽"积分抵扣确认弹窗（三选：积分抵扣继续 / 升级会员 / 取消）。</summary>
    [ObservableProperty] private bool _showOverageConfirm;
    /// <summary>积分抵扣确认弹窗消息文案（弹出时按当前分析成本 + 抵扣单价计算合计积分）。</summary>
    [ObservableProperty] private string _overageMsg = string.Empty;

    /// <summary>懒加载分页大小：首次进入页面仅加载最近 5 条记录。</summary>
    private const int InitialPageSize = 5;
    private const int LoadMorePageSize = 10;

    private List<AiAnalysisRecord> _allRecords = new();
    private int _loadedCount;
    private bool _isLoading;

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

    public AiAnalysisViewModel()
    {
        RangeTip = _locale.GetString("AiAnalysis_RangeTipDefault", "请选择连续 7 天作为分析区间");
        GenerateButtonText = _locale.GetString("AiAnalysis_GenerateNew", "生成新的分析");
        _locale.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(AppLanguage lang)
    {
        // 仅刷新非生成态的文案；生成中保持"正在分析..."不变
        if (!Generating)
        {
            UpdateRangeTip();
        }
        // 积分行格式串依赖语言，切换时同步刷新
        RefreshPointsSufficiency();
    }

    /// <summary>
    /// 异步加载：DB 查询放到后台线程，UI 线程仅做集合填充。
    /// 用于弹层"先打开再加载"模式，避免阻塞 UI。
    /// </summary>
    public async Task LoadAsync()
    {
        _isLoading = true;
        var baby = _state.CurrentBaby;
        BabyName = baby?.Name ?? string.Empty;

        var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local);
        StartDate = today.AddDays(-6);
        EndDate = today;

        // 并行加载积分余额、分析成本、历史记录
        // HTTP 调用包到 Task.Run：Android 首次 HttpClient 调用的 DNS/SSL 握手
        // 可能在调用线程同步执行，与 PointsViewModel 保持一致。
        var localPointsTask = Task.Run(() => _pointsService.GetDashboard());
        var costTask = Task.Run(() => _aiService.GetAnalysisCostAsync());
        var serverRecordsTask = Task.Run(() => _aiService.ListRecordsFromServerAsync());
        // GetLlmConfig 首次调用会同步读 SQLite，移到后台线程避免阻塞 UI
        var configTask = Task.Run(() => _aiService.GetLlmConfig());

        // server 模式下从后端实时获取权威积分余额，避免本地 SQLite 与后端 PostgreSQL 不一致
        // （本地签到只写 SQLite 不调后端 API，后端积分可能为 0 或偏低，导致扣分时报"积分不足"）
        var config = await configTask;
        long? serverPoints = null;
        if (config.NoteSource == "server")
        {
            serverPoints = await Task.Run(() => _pointsApi.GetPointsAsync());
        }

        var dashboard = await localPointsTask;
        // 后端积分优先；后端不可用时回退到本地 SQLite
        CurrentPoints = (int)(serverPoints ?? dashboard.Points);

        AnalysisCost = await costTask;
        RefreshPointsSufficiency();

        List<AiAnalysisRecord> records;
        var serverRecords = await serverRecordsTask;
        records = serverRecords ?? await Task.Run(() => _aiService.ListRecords());

        _allRecords = records.OrderByDescending(r => r.RangeStartDate).ToList();
        _loadedCount = 0;
        Records.Clear();
        LoadMoreRecords(InitialPageSize);

        _isLoading = false;
        UpdateRangeTip();
    }

    /// <summary>刷新积分是否充足的判断。</summary>
    private void RefreshPointsSufficiency()
    {
        PointsSufficient = CurrentPoints >= AnalysisCost;
        PointsDisplayText = string.Format(
            _locale.GetString("AiAnalysis_PointsFormat", "积分 {0} / 消耗 {1}"),
            CurrentPoints, AnalysisCost);
        InsufficientTip = PointsSufficient
            ? string.Empty
            : string.Format(_locale.GetString("AiAnalysis_ErrPointsShortFull", "积分不足，需 {0} 积分，当前 {1} 积分（每日签到可获取积分）"), AnalysisCost, CurrentPoints);
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
        if (_isLoading) return;
        ErrorMessage = string.Empty;
        if (StartDate is null || EndDate is null)
        {
            RangeTip = _locale.GetString("AiAnalysis_RangeTipDefault", "请选择连续 7 天作为分析区间");
            RangeValid = false;
            CanGenerate = false;
            return;
        }

        var start = StartDate.Value.Date;
        var end = EndDate.Value.Date;
        var days = (end - start).Days + 1;

        if (days < 7)
        {
            RangeTip = _locale.GetString("AiAnalysis_RangeTooShort", "分析区间不能少于 7 天");
            RangeValid = false;
            CanGenerate = false;
        }
        else if (days > 7)
        {
            RangeTip = _locale.GetString("AiAnalysis_RangeTooLong", "分析区间不能超过 7 天");
            RangeValid = false;
            CanGenerate = false;
        }
        else
        {
            RangeTip = _locale.GetString("AiAnalysis_RangeOk", "将分析该连续 7 天内的记录");
            RangeValid = true;
            CanGenerate = !_aiService.HasRangeAnalysis(start, end);
            GenerateButtonText = CanGenerate
                ? _locale.GetString("AiAnalysis_GenerateNew", "生成新的分析")
                : _locale.GetString("AiAnalysis_AlreadyAnalyzed", "该区间已分析");
        }
    }

    [RelayCommand]
    private Task Generate() => GenerateCoreAsync(usePointsForOverage: false);

    /// <summary>
    /// 生成分析核心逻辑。
    /// <paramref name="usePointsForOverage"/> 为 true 时表示免费次数用尽后用户选择积分抵扣，
    /// 携带 UsePointsForOverage=true 重新请求同一接口（后端额外扣积分放行本次超限请求）。
    /// </summary>
    private async Task GenerateCoreAsync(bool usePointsForOverage)
    {
        if (Generating || !RangeValid || StartDate is null || EndDate is null) return;

        var config = _aiService.GetLlmConfig();
        if (!config.Enabled)
        {
            DisplayToast(_locale.GetString("AiAnalysis_ErrEnableAi", "请先在设置中启用大模型"));
            ConfigRequired?.Invoke();
            return;
        }

        // 积分不足提示 + 提供充值入口（server 模式下才校验，local 模式不消耗积分）
        if (config.NoteSource == "server" && !PointsSufficient && !usePointsForOverage)
        {
            ErrorMessage = InsufficientTip;
            DisplayToast(string.Format(_locale.GetString("AiAnalysis_ErrPointsShort", "积分不足，需 {0} 积分，当前 {1} 积分"), AnalysisCost, CurrentPoints));
            return;
        }

        // 取消上一次未完成的请求（防御性，正常情况下 finally 已清理）
        _generateCts?.Cancel();
        _generateCts?.Dispose();
        _generateCts = new CancellationTokenSource();

        Generating = true;
        GenerateButtonText = _locale.GetString("AiAnalysis_Analyzing", "正在分析...");
        ErrorMessage = string.Empty;

        try
        {
            var record = await _aiService.GenerateAsync(StartDate.Value.Date, EndDate.Value.Date, _generateCts.Token, usePointsForOverage);
            // 生成后刷新积分余额（server 模式扣了积分）
            if (config.NoteSource == "server")
            {
                // 优先从后端拉取最新积分（权威值），失败回退到本地 SQLite
                var serverPoints = await _pointsApi.GetPointsAsync();
                if (serverPoints.HasValue)
                {
                    CurrentPoints = (int)serverPoints.Value;
                }
                else
                {
                    var dashboard = await Task.Run(() => _pointsService.GetDashboard());
                    CurrentPoints = dashboard.Points;
                }
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
            DisplayToast(_locale.GetString("AiAnalysis_Done", "分析完成"));
        }
        catch (OperationCanceledException)
        {
            DisplayToast(_locale.GetString("AiAnalysis_Canceled", "已取消分析"));
        }
        catch (AiAnalysisApiException ex)
        {
            // 积分不足：刷新余额并提示充值
            if (ex.IsInsufficientPoints)
            {
                // 优先从后端拉取最新积分（后端扣分失败但返回 INSUFFICIENT_POINTS，说明本地显示的积分与后端不一致）
                var serverPoints = await _pointsApi.GetPointsAsync();
                if (serverPoints.HasValue)
                {
                    CurrentPoints = (int)serverPoints.Value;
                }
                else
                {
                    var dashboard = await Task.Run(() => _pointsService.GetDashboard());
                    CurrentPoints = dashboard.Points;
                }
                RefreshPointsSufficiency();
                // 积分抵扣模式下积分不足（免费次数已用尽 + 积分余额不够抵扣）：
                // 提示并引导去积分任务页（签到获取积分）
                if (usePointsForOverage)
                {
                    ErrorMessage = string.Format(_locale.GetString("AiAnalysis_ErrPointsShortFinal", "积分不足，本次分析需 {0} 积分，当前余额 {1} 积分"), AnalysisCost + MembershipConstants.AiAnalysisOveragePointsCost, CurrentPoints);
                    DisplayToast(_locale.GetString("AiAnalysis_ErrPointsDaily", "积分不足，请每日签到获取积分"));
                    PointsRequired?.Invoke();
                }
                else
                {
                    ErrorMessage = string.Format(_locale.GetString("AiAnalysis_ErrPointsShortFinal", "积分不足，本次分析需 {0} 积分，当前余额 {1} 积分"), AnalysisCost, CurrentPoints);
                    DisplayToast(_locale.GetString("AiAnalysis_ErrPointsDaily", "积分不足，请每日签到获取积分"));
                }
            }
            // AI 分析免费次数用尽（本周）：弹积分抵扣三选弹窗（积分抵扣继续 / 升级会员 / 取消）
            else if (ex.IsAiLimitExceeded && !usePointsForOverage)
            {
                // 合计积分 = 正常分析消耗 + 超限抵扣单价（如 10 + 20 = 30，更透明）
                OverageMsg = string.Format(
                    _locale.GetString("AiAnalysis_OverageMsg", "可用积分抵扣继续（本次共消耗 {0} 积分）"),
                    AnalysisCost + MembershipConstants.AiAnalysisOveragePointsCost);
                ShowOverageConfirm = true;
            }
            // 已带积分抵扣仍返回次数上限（后端不应出现，防御性兜底）
            else if (ex.IsAiLimitExceeded)
            {
                ErrorMessage = _locale.GetString("AiAnalysis_ErrWeeklyLimitMember", "本周 AI 分析次数已用完，升级会员可享 10 次/周");
                DisplayToast(_locale.GetString("AiAnalysis_ErrWeeklyLimitUpgrade", "本周次数已达上限，升级会员解锁更多次数"));
                MembershipRequired?.Invoke();
            }
            else
            {
                ErrorMessage = ex.Message;
                DisplayToast(string.Format(_locale.GetString("AiAnalysis_ErrFailed", "分析失败：{0}"), ex.Message));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            DisplayToast(string.Format(_locale.GetString("AiAnalysis_ErrFailed", "分析失败：{0}"), ex.Message));
        }
        finally
        {
            Generating = false;
            GenerateButtonText = CanGenerate
                ? _locale.GetString("AiAnalysis_GenerateNew", "生成新的分析")
                : _locale.GetString("AiAnalysis_AlreadyAnalyzed", "该区间已分析");
            _generateCts?.Dispose();
            _generateCts = null;
        }
    }

    // ===== 免费次数用尽：积分抵扣三选弹窗（积分抵扣继续 / 升级会员 / 取消） =====

    /// <summary>确认积分抵扣：关闭弹窗，携带 usePointsForOverage=true 重新生成。</summary>
    [RelayCommand]
    private async Task ConfirmOverage()
    {
        ShowOverageConfirm = false;
        await GenerateCoreAsync(usePointsForOverage: true);
    }

    /// <summary>取消积分抵扣弹窗（放弃本次生成）。</summary>
    [RelayCommand]
    private void CancelOverage() => ShowOverageConfirm = false;

    /// <summary>升级会员：关闭弹窗并跳转会员中心（复用现有 MembershipRequired 事件链路）。</summary>
    [RelayCommand]
    private void UpgradeForOverage()
    {
        ShowOverageConfirm = false;
        MembershipRequired?.Invoke();
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
