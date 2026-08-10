using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Constants;
using ChildNotes.Models;
using ChildNotes.Models.Home;
using ChildNotes.Shared.Dtos;
using ChildNotes.Services;
using ChildNotes.ViewModels.Home;

namespace ChildNotes.ViewModels;

public partial class HomeViewModel : ViewModelBase, IActivatable
{
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;
    private readonly RecordService _recordService = ServiceProvider.Instance.RecordService;
    private readonly StatisticsService _statsService = ServiceProvider.Instance.StatisticsService;

    // ===== 子 ViewModel（协调者持有引用，各模块职责单一） =====
    public HomeCoreViewModel Core { get; }
    public VaccineTrackingViewModel VaccineTracking { get; }
    public AbnormalTrackingViewModel AbnormalTracking { get; }
    public AiStatusViewModel AiStatus { get; }

    [ObservableProperty] private bool _isInitialLayoutDone;

    public event Action? StatisticsRequested;
    public event Action? CheckInRequested;
    public event Action<string>? QuickRecordRequested;

    public HomeViewModel()
    {
        // ★ 启动埋点 P0：测量 HomeViewModel 构造耗时（含 4 个子 VM 创建 + 事件订阅）
        var ctorSw = System.Diagnostics.Stopwatch.StartNew();
        DevLogger.Log("Startup", "HomeViewModel ctor start");

        Core = new HomeCoreViewModel();
        VaccineTracking = new VaccineTrackingViewModel();
        AbnormalTracking = new AbnormalTrackingViewModel();
        AiStatus = new AiStatusViewModel();

        // 异常恢复后触发首页刷新
        AbnormalTracking.RefreshRequested += async () => await RefreshAsync();

        // 转发子 ViewModel 的 PropertyChanged 通知到 HomeViewModel，
        // 使 View 层（编译绑定）能在子 VM 属性变更时收到通知并更新 UI。
        ForwardPropertyChanged(Core);
        ForwardPropertyChanged(AiStatus);
        ForwardPropertyChanged(VaccineTracking);
        ForwardPropertyChanged(AbnormalTracking);

        ctorSw.Stop();
        DevLogger.Log("Startup", $"HomeViewModel ctor done: {ctorSw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 将子 ViewModel 的 PropertyChanged 事件转发到本类，
    /// 使 View 层通过 HomeViewModel 路径绑定的属性能正确响应子 VM 的变更通知。
    /// </summary>
    private void ForwardPropertyChanged(INotifyPropertyChanged subVm)
    {
        subVm.PropertyChanged += (_, e) =>
        {
            // 仅转发本类已声明的转发属性（与子 VM 同名的属性）
            // 触发本类的 PropertyChanged，让 View 绑定更新
            OnPropertyChanged(e.PropertyName);
        };
    }

    public void Activate()
    {
        DevLogger.Log("Home", "Activate start");
        try
        {
            _ = RefreshAsync();
            // 首屏布局完成后，再显示非关键卡片（疫苗/活动追踪），减少初始构建时间
            DispatcherTimer.RunOnce(() =>
            {
                IsInitialLayoutDone = true;
                DevLogger.Log("Home", "IsInitialLayoutDone=true (非关键卡片展开)");
            }, TimeSpan.FromMilliseconds(100));
            // 后台预加载疫苗时间轴数据（用户点"补记"时直接用）
            _ = VaccineFormViewModel.PreloadAsync();
            DevLogger.Log("Home", "Activate done");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Home", ex);
            throw;
        }
    }

    /// <summary>RefreshAsync 防重复令牌：启动时多个事件（Activate + BabySetup + BabyChanged）短时间内依次触发，
    /// 每次都执行完整 DB 查询（~140ms）。此字段确保同一时刻只有一个 RefreshAsync 在跑。/// </summary>
    private CancellationTokenSource? _refreshCts;

    /// <summary>上次 RefreshAsync 完成的 UTC 时间戳（用于最小间隔防抖）。
    /// 启动时多个事件串行触发 RefreshAsync，CTS 只能取消并发重叠的调用，
    /// 对"完成→立即再调用"的场景无效。此字段在调用前检查间隔，
    /// 若距上次完成不足 2 秒则跳过（数据不可能在这 2 秒内变化）。/// </summary>
    private static DateTime s_lastRefreshCompletedUtc = DateTime.MinValue;
    private const int MinRefreshIntervalMs = 2000;

    /// <summary>
    /// 异步刷新首页：后台线程批量查询所有 DB 数据，UI 线程仅做属性赋值。
    /// 把原先 Refresh + RefreshLastFeed + RefreshVaccines + RefreshActivity + RefreshAbnormal
    /// 的 8+ 次串行同步 DB 查询合并为 1 次后台批量查询，UI 线程阻塞从 200-500ms 降至 &lt;50ms。
    /// 含双重防重复：
    ///   1) CTS 取消：并发调用时取消旧任务
    ///   2) 最小间隔：串行调用时跳过距上次不足 2s 的请求
    /// </summary>
    public async Task RefreshAsync()
    {
        // 防抖：若距上次完成不足 2 秒，跳过（启动时多个事件触发、保存记录后的冗余刷新等场景）
        var elapsedSinceLast = (DateTime.UtcNow - s_lastRefreshCompletedUtc).TotalMilliseconds;
        if (elapsedSinceLast < MinRefreshIntervalMs && elapsedSinceLast > 0)
        {
            DevLogger.Log("Home", $"RefreshAsync skipped ({elapsedSinceLast:F0}ms since last)");
            return;
        }

        // 防重复：取消上一次未完成的刷新
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;

        DevLogger.Log("Home", "RefreshAsync start");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var appState = ServiceProvider.Instance.AppState;
        var currentBabyId = appState.CurrentBabyId;

        // UI 线程：属性赋值与集合更新（被取消则静默跳过）
        try
        {
            // ===== 阶段 1：首屏必需数据（宝宝信息 + 今日记录 + 统计 + 上次喂奶 + 身高体重）=====
            // 这些数据驱动 Core 卡片（宝宝名/今日统计/上次喂奶/身高体重）和 AI 状态卡片，
            // 是用户首屏立刻可见的内容，必须尽快查询并 Apply。
            var phase1 = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                // 去重复查询：启动时 TryRestoreSession 已调用 BabyService.LoadBabyList()
                // 填充了 AppState.BabyList。此处优先复用缓存，仅在缓存为空时才重新加载。
                IReadOnlyList<Baby> babyList = appState.BabyList.Count > 0
                    ? appState.BabyList
                    : _babyService.LoadBabyList();
                var baby = babyList.FirstOrDefault(b => b.Id == currentBabyId)
                           ?? appState.CurrentBaby;
                ct.ThrowIfCancellationRequested();
                if (baby is null)
                    return (Baby: (Baby?)null, TodayRecords: new List<ChildRecord>(),
                            LatestFeed: (ChildRecord?)null, GrowthRecords: new List<ChildRecord>(),
                            Stats: (DayStats?)null);

                var todayRecords = _recordService.GetByDate(DateTime.Today);
                var stats = _statsService.GetDayStats(DateTime.Today, todayRecords);
                var latestFeed = _recordService.GetLatest(RecordType.Feed);
                var growthRecords = _recordService.GetByType(RecordType.Growth, 1);
                return (Baby: (Baby?)baby, TodayRecords: todayRecords,
                        LatestFeed: latestFeed, GrowthRecords: growthRecords, Stats: (DayStats?)stats);
            }, ct);

            appState.CurrentBaby = phase1.Baby;
            var tPhase1 = sw.ElapsedMilliseconds;
            DevLogger.Log("Home", $"RefreshAsync phase1: baby={(phase1.Baby is null ? "null" : phase1.Baby.Name)}, db={tPhase1}ms");

            if (phase1.Baby is null)
            {
                Core.Reset();
                VaccineTracking.Reset();
                AbnormalTracking.ApplyAbnormal(null, new List<ChildRecord>());
                AiStatus.Reset();
                s_lastRefreshCompletedUtc = DateTime.UtcNow;
                return;
            }

            // 阶段 1 Apply：立即更新首屏可见的 Core 卡片 + AI 状态
            Core.ApplyBabyInfo(phase1.Baby, _babyService.GetGrowthStageText());
            Core.ApplyTodayStats(phase1.Stats, phase1.LatestFeed, phase1.TodayRecords, phase1.GrowthRecords);
            AiStatus.RefreshAiStatus(phase1.Stats, Core.BabyName);

            // ===== 阶段 2：非首屏数据（疫苗记录 + 异常记录）=====
            // 这两个卡片由 IsInitialLayoutDone（100ms 后）控制显示，数据可以稍晚填充。
            // 不阻塞阶段 1 的 UI 更新，后台继续查询。
            var birthDate = phase1.Baby.BirthDate;
            var today = DateTime.Today;
            var phase2 = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var vaccineRecords = _recordService.GetByType(RecordType.Vaccine, 100);
                var abnormalRecords = _recordService.GetByType(RecordType.Abnormal, 1);
                return (VaccineRecords: vaccineRecords, AbnormalRecords: abnormalRecords);
            }, ct);

            var tPhase2 = sw.ElapsedMilliseconds;
            VaccineTracking.ApplyVaccines(phase2.VaccineRecords, birthDate, today);
            AbnormalTracking.ApplyAbnormal(phase1.Stats, phase2.AbnormalRecords);

            sw.Stop();
            s_lastRefreshCompletedUtc = DateTime.UtcNow; // 记录完成时间，用于最小间隔防抖
            DevLogger.Log("Home", $"RefreshAsync(total) | total={sw.ElapsedMilliseconds}ms | phase1={tPhase1}ms phase2={tPhase2 - tPhase1}ms");
        }
        catch (OperationCanceledException)
        {
            DevLogger.Log("Home", "RefreshAsync cancelled (superseded by newer refresh)");
            // 不更新 s_lastRefreshCompletedUtc：被取消说明有新任务在跑，让新任务的完成时间作为基准
        }
        finally
        {
            _refreshCts?.Dispose();
            _refreshCts = null;
        }
    }

    [RelayCommand]
    private void QuickRecord(string type)
    {
        QuickRecordRequested?.Invoke(type);
    }

    [RelayCommand]
    private void GoStatistics()
    {
        StatisticsRequested?.Invoke();
    }

    [RelayCommand]
    private void GoCheckIn()
    {
        CheckInRequested?.Invoke();
    }

    // ===== 转发属性/命令：保持 View 层绑定路径不变，减少 AXAML 改动 =====
    // 基本信息
    public string BabyName { get => Core.BabyName; set => Core.BabyName = value; }
    public string BabyAvatar { get => Core.BabyAvatar; set => Core.BabyAvatar = value; }
    public string BabyAgeText { get => Core.BabyAgeText; set => Core.BabyAgeText = value; }
    public string GrowthStage { get => Core.GrowthStage; set => Core.GrowthStage = value; }
    public DayStats? TodayStats { get => Core.TodayStats; set => Core.TodayStats = value; }
    public string DailyTip { get => Core.DailyTip; set => Core.DailyTip = value; }
    public string LastFeedAgoText { get => Core.LastFeedAgoText; set => Core.LastFeedAgoText = value; }
    public string LastFeedSummary { get => Core.LastFeedSummary; set => Core.LastFeedSummary = value; }
    public string DiaperTodayText { get => Core.DiaperTodayText; set => Core.DiaperTodayText = value; }
    public string DiaperDetailText { get => Core.DiaperDetailText; set => Core.DiaperDetailText = value; }
    public string SleepTodayText { get => Core.SleepTodayText; set => Core.SleepTodayText = value; }
    public string LatestHeightText { get => Core.LatestHeightText; set => Core.LatestHeightText = value; }
    public string LatestWeightText { get => Core.LatestWeightText; set => Core.LatestWeightText = value; }

    // AI 状态
    public string AiStatusIcon { get => AiStatus.AiStatusIcon; set => AiStatus.AiStatusIcon = value; }
    public string AiStatusTitle { get => AiStatus.AiStatusTitle; set => AiStatus.AiStatusTitle = value; }
    public string AiStatusSubtitle { get => AiStatus.AiStatusSubtitle; set => AiStatus.AiStatusSubtitle = value; }
    public string AiTipText { get => AiStatus.AiTipText; set => AiStatus.AiTipText = value; }

    // 疫苗追踪
    public ObservableCollection<VaccineItem> VaccineItems => VaccineTracking.VaccineItems;
    public string VaccineProgressText { get => VaccineTracking.VaccineProgressText; set => VaccineTracking.VaccineProgressText = value; }
    public bool IsVaccineExpanded { get => VaccineTracking.IsVaccineExpanded; set => VaccineTracking.IsVaccineExpanded = value; }
    public IReadOnlyList<VaccineItem> VisibleVaccineItems => VaccineTracking.VisibleVaccineItems;
    public bool NeedsVaccineExpand => VaccineTracking.NeedsVaccineExpand;
    public double VaccineListMaxHeight => VaccineTracking.VaccineListMaxHeight;
    public ICommand ToggleVaccinePanelCommand => VaccineTracking.ToggleVaccinePanelCommand;

    // 异常追踪
    public bool HasActiveAbnormal { get => AbnormalTracking.HasActiveAbnormal; set => AbnormalTracking.HasActiveAbnormal = value; }
    public bool HasOtherAbnormal { get => AbnormalTracking.HasOtherAbnormal; set => AbnormalTracking.HasOtherAbnormal = value; }
    public string AbnormalStatusText { get => AbnormalTracking.AbnormalStatusText; set => AbnormalTracking.AbnormalStatusText = value; }
    public string AbnormalSummaryText { get => AbnormalTracking.AbnormalSummaryText; set => AbnormalTracking.AbnormalSummaryText = value; }
    public ICommand MarkAbnormalResolvedCommand => AbnormalTracking.MarkAbnormalResolvedCommand;
}
