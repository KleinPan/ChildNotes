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
    public ActivityTrackingViewModel ActivityTracking { get; }
    public AbnormalTrackingViewModel AbnormalTracking { get; }
    public AiStatusViewModel AiStatus { get; }

    [ObservableProperty] private bool _isInitialLayoutDone;

    public event Action? StatisticsRequested;
    public event Action? CheckInRequested;
    public event Action<string>? QuickRecordRequested;

    public HomeViewModel()
    {
        Core = new HomeCoreViewModel();
        VaccineTracking = new VaccineTrackingViewModel();
        ActivityTracking = new ActivityTrackingViewModel();
        AbnormalTracking = new AbnormalTrackingViewModel();
        AiStatus = new AiStatusViewModel();

        // 异常恢复后触发首页刷新
        AbnormalTracking.RefreshRequested += async () => await RefreshAsync();

        // 转发子 ViewModel 的 PropertyChanged 通知到 HomeViewModel，
        // 使 View 层（编译绑定）能在子 VM 属性变更时收到通知并更新 UI。
        ForwardPropertyChanged(Core);
        ForwardPropertyChanged(AiStatus);
        ForwardPropertyChanged(VaccineTracking);
        ForwardPropertyChanged(ActivityTracking);
        ForwardPropertyChanged(AbnormalTracking);
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

    /// <summary>RefreshAsync 防重复令牌：启动时多个事件（Activate + BabySetup + BabyChanged）在 5 秒内依次触发，
    /// 每次都执行完整 DB 查询（~140ms），浪费 ~280ms。此字段确保同一时刻只有一个 RefreshAsync 在跑。/// </summary>
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

        // 失效活动时间轴缓存：保存记录后 RefreshAsync 会触发，下次打开活动面板需重查 DB 取最新数据
        ActivityTracking.InvalidateCache();

        // 后台线程：一次性查询所有需要的数据（可被后续 RefreshAsync 取消）
        var snapshot = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var baby = _babyService.LoadBabyList().FirstOrDefault(b => b.Id == currentBabyId)
                       ?? appState.CurrentBaby;
            ct.ThrowIfCancellationRequested();
            if (baby is null)
                return (Baby: (Baby?)null, TodayRecords: new List<ChildRecord>(),
                        LatestFeed: (ChildRecord?)null, VaccineRecords: new List<ChildRecord>(),
                        Activities: new List<ChildRecord>(), GrowthRecords: new List<ChildRecord>(),
                        AbnormalRecords: new List<ChildRecord>(), Stats: (DayStats?)null);

            var todayRecords = _recordService.GetByDate(DateTime.Today);
            var stats = _statsService.GetDayStats(DateTime.Today, todayRecords);
            var latestFeed = _recordService.GetLatest(RecordType.Feed);
            var vaccineRecords = _recordService.GetByType(RecordType.Vaccine, 100);
            var activities = _recordService.GetByType(RecordType.Activity, 100);
            var growthRecords = _recordService.GetByType(RecordType.Growth, 1);
            var abnormalRecords = _recordService.GetByType(RecordType.Abnormal, 1);
            return (Baby: (Baby?)baby, TodayRecords: todayRecords,
                    LatestFeed: latestFeed, VaccineRecords: vaccineRecords,
                    Activities: activities, GrowthRecords: growthRecords,
                    AbnormalRecords: abnormalRecords, Stats: (DayStats?)stats);
        }, ct);

        // UI 线程：属性赋值与集合更新（被取消则静默跳过）
        try
        {
            appState.CurrentBaby = snapshot.Baby;
            DevLogger.Log("Home", $"RefreshAsync: baby={(snapshot.Baby is null ? "null" : snapshot.Baby.Name)}, db={sw.ElapsedMilliseconds}ms");

            if (snapshot.Baby is null)
            {
                Core.Reset();
                VaccineTracking.Reset();
                ActivityTracking.ApplyActivity(new List<ChildRecord>());
                AbnormalTracking.ApplyAbnormal(null, new List<ChildRecord>());
                AiStatus.Reset();
                return;
            }

            Core.ApplyBabyInfo(snapshot.Baby, _babyService.GetGrowthStageText());
            Core.ApplyTodayStats(snapshot.Stats, snapshot.LatestFeed, snapshot.TodayRecords, snapshot.GrowthRecords);
            AiStatus.RefreshAiStatus(snapshot.Stats, Core.BabyName);

            var tVac = sw.ElapsedMilliseconds;
            var birthDate = snapshot.Baby.BirthDate;
            var today = DateTime.Today;
            VaccineTracking.ApplyVaccines(snapshot.VaccineRecords, birthDate, today);
            var tAct = sw.ElapsedMilliseconds;
            ActivityTracking.ApplyActivity(snapshot.Activities);
            var tAbn = sw.ElapsedMilliseconds;
            AbnormalTracking.ApplyAbnormal(snapshot.Stats, snapshot.AbnormalRecords);

            sw.Stop();
            s_lastRefreshCompletedUtc = DateTime.UtcNow; // 记录完成时间，用于最小间隔防抖
            DevLogger.Log("Home", $"RefreshAsync(total) | total={sw.ElapsedMilliseconds}ms | vaccines={tAct - tVac}ms activity={tAbn - tAct}ms abnormal={sw.ElapsedMilliseconds - tAbn}ms");
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

    // 活动追踪
    public bool IsActivityExpanded { get => ActivityTracking.IsActivityExpanded; set => ActivityTracking.IsActivityExpanded = value; }
    public bool IsActivityDetailOpen { get => ActivityTracking.IsActivityDetailOpen; set => ActivityTracking.IsActivityDetailOpen = value; }
    public ActivityLatestItem? LastActivity { get => ActivityTracking.LastActivity; set => ActivityTracking.LastActivity = value; }
    public string ActivityTimeSince { get => ActivityTracking.ActivityTimeSince; set => ActivityTracking.ActivityTimeSince = value; }
    public ObservableCollection<ActivityTimelineGroup> ActivityTimelineGroups => ActivityTracking.ActivityTimelineGroups;
    public bool IsActivityLoading { get => ActivityTracking.IsActivityLoading; set => ActivityTracking.IsActivityLoading = value; }
    public bool HasMoreActivities => ActivityTracking.HasMoreActivities;
    public ICommand ToggleActivityPanelCommand => ActivityTracking.ToggleActivityPanelCommand;
    public ICommand ToggleActivityDetailCommand => ActivityTracking.ToggleActivityDetailCommand;
    public ICommand CloseActivityDetailCommand => ActivityTracking.CloseActivityDetailCommand;
    public ICommand LoadMoreActivitiesCommand => ActivityTracking.LoadMoreActivitiesCommand;

    // 异常追踪
    public bool HasActiveAbnormal { get => AbnormalTracking.HasActiveAbnormal; set => AbnormalTracking.HasActiveAbnormal = value; }
    public bool HasOtherAbnormal { get => AbnormalTracking.HasOtherAbnormal; set => AbnormalTracking.HasOtherAbnormal = value; }
    public string AbnormalStatusText { get => AbnormalTracking.AbnormalStatusText; set => AbnormalTracking.AbnormalStatusText = value; }
    public string AbnormalSummaryText { get => AbnormalTracking.AbnormalSummaryText; set => AbnormalTracking.AbnormalSummaryText = value; }
    public ICommand MarkAbnormalResolvedCommand => AbnormalTracking.MarkAbnormalResolvedCommand;
}
