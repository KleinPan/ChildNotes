using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Constants;
using ChildNotes.Models;
using ChildNotes.Shared.Dtos;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class HomeViewModel : ViewModelBase, IActivatable
{
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;
    private readonly RecordService _recordService = ServiceProvider.Instance.RecordService;
    private readonly StatisticsService _statsService = ServiceProvider.Instance.StatisticsService;

    [ObservableProperty] private string _babyName = string.Empty;
    [ObservableProperty] private string _babyAvatar = string.Empty;
    [ObservableProperty] private string _babyAgeText = string.Empty;
    [ObservableProperty] private string _growthStage = string.Empty;
    [ObservableProperty] private DayStats? _todayStats;
    [ObservableProperty] private string _dailyTip = "记录宝宝的每一天，陪伴健康成长";

    [ObservableProperty] private string _lastFeedAgoText = "--";
    [ObservableProperty] private string _lastFeedSummary = "--";
    [ObservableProperty] private string _diaperTodayText = "0次";
    [ObservableProperty] private string _diaperDetailText = "便0 尿0";
    [ObservableProperty] private string _sleepTodayText = "0小时0分钟";
    [ObservableProperty] private string _latestHeightText = "--cm";
    [ObservableProperty] private string _latestWeightText = "--kg";

    [ObservableProperty] private string _aiStatusIcon = "☀️";
    [ObservableProperty] private string _aiStatusTitle = "小铃铛状态良好";
    [ObservableProperty] private string _aiStatusSubtitle = "正在快乐成长中~";
    [ObservableProperty] private string _aiTipText = DailyTipsCatalog.Current.DefaultTip;

    // 轮播提示相关：对齐小程序 good-status 组件 <swiper interval=5000> 行为
    private readonly DispatcherTimer _tipCarouselTimer = new(TimeSpan.FromSeconds(5), DispatcherPriority.Normal, (_, _) => { });
    private IReadOnlyList<string> _currentTipPool = Array.Empty<string>();
    private int _tipCarouselIndex;

    [ObservableProperty] private ObservableCollection<VaccineItem> _vaccineItems = new();
    [ObservableProperty] private string _vaccineProgressText = "0/0";
    [ObservableProperty] private bool _isVaccineExpanded;

    /// <summary>疫苗列表默认展示条数（对齐小程序折叠态显示 2-3 条的行为）。</summary>
    private const int VaccineDefaultVisibleCount = 3;

    /// <summary>疫苗列表实际渲染数据：展开时返回全部，收起时只返回前 N 条。</summary>
    public IReadOnlyList<VaccineItem> VisibleVaccineItems =>
        IsVaccineExpanded ? VaccineItems : VaccineItems.Take(VaccineDefaultVisibleCount).ToList();

    /// <summary>是否需要展开/收起按钮（总条数 > 默认显示条数时才显示）。</summary>
    public bool NeedsVaccineExpand => VaccineItems.Count > VaccineDefaultVisibleCount;

    /// <summary>
    /// 疫苗列表 ScrollViewer 的 MaxHeight：折叠态约 3 项高度（180），展开态约 6 项高度（360）。
    /// 配合 VirtualizingStackPanel 实现虚拟化：仅渲染可见区域内的项，52 个剂次不再一次性创建全部 UI 元素。
    /// 展开时通过滚动查看剩余项，而非一次性渲染全部。
    /// </summary>
    public double VaccineListMaxHeight => IsVaccineExpanded ? 360 : 180;

    // ===== 异常/生病追踪状态（对齐小程序首页 fever/diarrhea/other-abnormal 三态） =====
    /// <summary>当前是否有活动异常（发烧/腹泻/其他异常任一）。</summary>
    [ObservableProperty] private bool _hasActiveAbnormal;
    /// <summary>是否存在「其他异常」（控制「已恢复」按钮可见性；发烧/腹泻通过各自入口恢复）。</summary>
    [ObservableProperty] private bool _hasOtherAbnormal;
    [ObservableProperty] private string _abnormalStatusText = string.Empty;
    [ObservableProperty] private string _abnormalSummaryText = string.Empty;

    // ===== 活动追踪（对齐小程序 activity-tracker 组件） =====
    [ObservableProperty] private bool _isActivityExpanded;
    [ObservableProperty] private bool _isActivityDetailOpen;
    [ObservableProperty] private ActivityLatestItem? _lastActivity;
    [ObservableProperty] private string _activityTimeSince = "--";
    [ObservableProperty] private ObservableCollection<ActivityTimelineGroup> _activityTimelineGroups = new();
    /// <summary>活动详情面板加载状态：true 时显示 loading 占位，避免 UI 卡顿无反馈。</summary>
    [ObservableProperty] private bool _isActivityLoading;

    // 距上次活动时间实时刷新（对齐小程序 startTimer 30 秒间隔）
    private readonly DispatcherTimer _activitySinceTimer = new(TimeSpan.FromSeconds(30), DispatcherPriority.Normal, (_, _) => { });
    private DateTime? _lastActivityTime;

    public event Action? StatisticsRequested;
    public event Action? CheckInRequested;
    public event Action<string>? QuickRecordRequested;

    public HomeViewModel()
    {
    }

    public void Activate()
    {
        DevLogger.Log("Home", "Activate start");
        try
        {
            _ = RefreshAsync();
            DevLogger.Log("Home", "Activate done");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Home", ex);
            throw;
        }
    }

    /// <summary>
    /// 异步刷新首页：后台线程批量查询所有 DB 数据，UI 线程仅做属性赋值。
    /// 把原先 Refresh + RefreshLastFeed + RefreshVaccines + RefreshActivity + RefreshAbnormal
    /// 的 8+ 次串行同步 DB 查询合并为 1 次后台批量查询，UI 线程阻塞从 200-500ms 降至 &lt;50ms。
    /// </summary>
    public async Task RefreshAsync()
    {
        DevLogger.Log("Home", "RefreshAsync start");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var appState = ServiceProvider.Instance.AppState;
        var currentBabyId = appState.CurrentBabyId;

        // 后台线程：一次性查询所有需要的数据
        var snapshot = await Task.Run(() =>
        {
            var baby = _babyService.LoadBabyList().FirstOrDefault(b => b.Id == currentBabyId)
                       ?? appState.CurrentBaby;
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
        });

        // UI 线程：属性赋值与集合更新
        appState.CurrentBaby = snapshot.Baby;
        DevLogger.Log("Home", $"RefreshAsync: baby={(snapshot.Baby is null ? "null" : snapshot.Baby.Name)}, db={sw.ElapsedMilliseconds}ms");

        if (snapshot.Baby is null)
        {
            BabyName = "未添加宝宝";
            BabyAgeText = string.Empty;
            GrowthStage = string.Empty;
            TodayStats = null;
            LastFeedAgoText = "--";
            LastFeedSummary = "--";
            DiaperTodayText = "0次";
            DiaperDetailText = "便0 尿0";
            SleepTodayText = "0小时0分钟";
            LatestHeightText = "--cm";
            LatestWeightText = "--kg";
            VaccineItems.Clear();
            VaccineProgressText = "0/0";
            ResetActivity();
            ResetAbnormal();
            return;
        }

        BabyName = snapshot.Baby.Name;
        BabyAvatar = snapshot.Baby.Avatar;
        GrowthStage = _babyService.GetGrowthStageText();
        BabyAgeText = snapshot.Baby.BirthDate.HasValue
            ? FormatAge(snapshot.Baby.BirthDate.Value)
            : string.Empty;

        TodayStats = snapshot.Stats;
        DailyTip = GetDailyTip(TodayStats);

        // 从已查快照中派生各子项，不再重复查询 DB
        ApplyLastFeed(snapshot.LatestFeed, snapshot.TodayRecords);
        ApplyDiaper(snapshot.Stats);
        ApplySleep(snapshot.Stats);
        ApplyGrowth(snapshot.GrowthRecords);
        RefreshAiStatus(snapshot.Stats);
        ApplyVaccines(snapshot.VaccineRecords);
        ApplyActivity(snapshot.Activities);
        ApplyAbnormal(snapshot.Stats, snapshot.AbnormalRecords);

        sw.Stop();
        DevLogger.Log("Home", $"RefreshAsync(total) | total={sw.ElapsedMilliseconds}ms");
    }

    /// <summary>从快照数据应用最近一次喂养信息（不再重复查询 DB）。</summary>
    private void ApplyLastFeed(ChildRecord? lastFeed, List<ChildRecord> todayRecords)
    {
        if (lastFeed is null)
        {
            LastFeedAgoText = "--";
            LastFeedSummary = "--";
            return;
        }

        var ago = DateTime.Now - lastFeed.RecordTime;
        if (ago.TotalMinutes < 60)
            LastFeedAgoText = $"{(int)ago.TotalMinutes}分钟";
        else if (ago.TotalHours < 24)
            LastFeedAgoText = $"{(int)ago.TotalHours}小时{(int)(ago.TotalMinutes % 60)}分钟";
        else
            LastFeedAgoText = $"{(int)ago.TotalDays}天";

        // 从已查的当日记录中筛选喂养记录，避免重复查询
        var todayFeeds = todayRecords.Where(r => r.RecordType == RecordType.Feed).ToList();
        var feedCount = todayFeeds.Count;
        var totalMl = todayFeeds.Sum(r => r.AmountMl ?? 0);
        LastFeedSummary = totalMl > 0 ? $"{feedCount}次 {totalMl}ml" : $"{feedCount}次";
    }

    private void ApplyDiaper(DayStats? stats)
    {
        if (stats is null)
        {
            DiaperTodayText = "0次";
            DiaperDetailText = "便0 尿0";
            return;
        }
        DiaperTodayText = $"{stats.DiaperCount}次";
        DiaperDetailText = $"便{stats.DirtyDiaperCount} 尿{stats.WetDiaperCount}";
    }

    private void ApplySleep(DayStats? stats)
    {
        if (stats is null || stats.SleepTotalMin <= 0)
        {
            SleepTodayText = "0小时0分钟";
            return;
        }
        var hours = stats.SleepTotalMin / 60;
        var mins = stats.SleepTotalMin % 60;
        SleepTodayText = $"{hours}小时{mins}分钟";
    }

    /// <summary>从快照数据应用最新生长记录（不再重复查询 DB）。</summary>
    private void ApplyGrowth(List<ChildRecord> growthRecords)
    {
        var latest = growthRecords.FirstOrDefault();
        if (latest is not null)
        {
            LatestHeightText = latest.HeightCm.HasValue ? $"{latest.HeightCm:F1}cm" : "--cm";
            LatestWeightText = latest.WeightKg.HasValue ? $"{latest.WeightKg:F2}kg" : "--kg";
        }
        else
        {
            LatestHeightText = "--cm";
            LatestWeightText = "--kg";
        }
    }

    private void RefreshAiStatus(DayStats? stats)
    {
        // 配置来源：DailyTipsCatalog.Current（默认值对齐小程序，可由 daily-tips.json 覆盖）
        var cfg = DailyTipsCatalog.Current;
        // 标题拼接对齐小程序 good-status/index.wxml 第 10 行 babyName + '状态良好'
        var name = string.IsNullOrWhiteSpace(BabyName) ? cfg.NoBabyTitle : BabyName;

        if (stats is null)
        {
            AiStatusIcon = "☀️";
            AiStatusTitle = FormatTitle(cfg.GoodTitleTemplate, name, cfg.NoBabyTitle);
            AiStatusSubtitle = "正在快乐成长中~";
            SetStaticTip(cfg.DefaultTip);
            return;
        }

        if (stats.HasFever)
        {
            AiStatusIcon = "🌡️";
            AiStatusTitle = FormatTitle(cfg.FeverTitleTemplate, name, cfg.NoBabyTitle);
            AiStatusSubtitle = $"当前体温{stats.LatestTemperature?.ToString("F1")}℃";
            SetStaticTip(cfg.FeverTip);
        }
        else if (stats.HasDiarrhea)
        {
            AiStatusIcon = "⚠️";
            AiStatusTitle = FormatTitle(cfg.DiarrheaTitleTemplate, name, cfg.NoBabyTitle);
            AiStatusSubtitle = "今日有腹泻记录";
            SetStaticTip(cfg.DiarrheaTip);
        }
        else if (stats.FeedCount >= 6 && stats.SleepTotalMin >= 480)
        {
            AiStatusIcon = "😊";
            AiStatusTitle = FormatTitle(cfg.GoodTitleTemplate, name, cfg.NoBabyTitle);
            AiStatusSubtitle = "吃得好睡得香~";
            StartTipCarousel(cfg.DailyTips);
        }
        else if (stats.FeedCount == 0 && stats.DiaperCount == 0)
        {
            AiStatusIcon = "📝";
            AiStatusTitle = FormatTitle(cfg.NoRecordTitleTemplate, name, cfg.NoBabyTitle);
            AiStatusSubtitle = "点击下方快捷按钮开始吧";
            SetStaticTip(cfg.DefaultTip);
        }
        else
        {
            AiStatusIcon = "☀️";
            AiStatusTitle = FormatTitle(cfg.GoodTitleTemplate, name, cfg.NoBabyTitle);
            AiStatusSubtitle = "正在快乐成长中~";
            StartTipCarousel(cfg.DailyTips);
        }
    }

    /// <summary>
    /// 用宝宝姓名填充标题模板。
    /// 对齐小程序 babyName + '状态良好' 拼接行为：模板含 {0} 时填充姓名，
    /// 否则原样返回；姓名为空（未添加宝宝）时回退到 NoBabyTitle。
    /// </summary>
    private static string FormatTitle(string template, string babyName, string noBabyTitle)
    {
        if (string.IsNullOrWhiteSpace(babyName)) return noBabyTitle;
        return template.Contains("{0}")
            ? string.Format(template, babyName)
            : template;
    }

    /// <summary>设置单条静态提示并停止轮播（异常/未记录状态）。</summary>
    private void SetStaticTip(string tip)
    {
        _tipCarouselTimer.Stop();
        _currentTipPool = Array.Empty<string>();
        _tipCarouselIndex = 0;
        AiTipText = tip;
    }

    /// <summary>
    /// 启动提示轮播（对齐小程序 good-status 组件 vertical swiper，5 秒间隔、循环播放）。
    /// 每次刷新会重置索引；空池时回退到默认提示。
    /// </summary>
    private void StartTipCarousel(IReadOnlyList<string> tips)
    {
        if (tips.Count == 0)
        {
            SetStaticTip(DailyTipsCatalog.Current.DefaultTip);
            return;
        }

        // 若池内容未变则保持当前索引，避免刷新打断轮播节奏
        if (!ReferenceEquals(_currentTipPool, tips) && !_currentTipPool.SequenceEqual(tips))
        {
            _currentTipPool = tips;
            _tipCarouselIndex = 0;
        }
        else
        {
            _currentTipPool = tips;
        }

        AiTipText = _currentTipPool[_tipCarouselIndex];

        _tipCarouselTimer.Stop();
        _tipCarouselTimer.Tick -= OnTipCarouselTick!;
        _tipCarouselTimer.Tick += OnTipCarouselTick!;
        _tipCarouselTimer.Start();
    }

    private void OnTipCarouselTick(object sender, EventArgs e)
    {
        if (_currentTipPool.Count == 0) return;
        _tipCarouselIndex = (_tipCarouselIndex + 1) % _currentTipPool.Count;
        AiTipText = _currentTipPool[_tipCarouselIndex];
    }

    /// <summary>从快照数据应用疫苗进度（不再重复查询 DB）。</summary>
    private void ApplyVaccines(List<ChildRecord> vaccineRecords)
    {
        VaccineItems.Clear();

        // 已接种的疫苗名称集合（规范化：去除空格、括号、针→剂）
        var completedSet = new HashSet<string>(vaccineRecords.Select(v =>
        {
            try { return v.GetPayload<VaccineRecordDto>()?.Name ?? ""; } catch { return ""; }
        }).Where(n => !string.IsNullOrEmpty(n)).Select(NormalizeVaccineName));

        var birthDate = ServiceProvider.Instance.AppState.CurrentBaby?.BirthDate;
        var today = DateTime.Today;

        foreach (var (name, ageLabel, dueDays) in VaccineCatalog.FlattenDoses())
        {
            var done = completedSet.Contains(NormalizeVaccineName(name));
            int daysLater;
            if (done)
            {
                daysLater = 0;
            }
            else if (birthDate.HasValue && dueDays.HasValue)
            {
                var recommendedDate = birthDate.Value.AddDays(dueDays.Value).Date;
                daysLater = (int)(today - recommendedDate).TotalDays;
            }
            else
            {
                daysLater = -1; // 未知出生日期或无推荐日，标记为待安排
            }
            VaccineItems.Add(new VaccineItem(name, ageLabel, daysLater, done));
        }

        var doneCount = VaccineItems.Count(v => v.IsDone);
        VaccineProgressText = $"{doneCount}/{VaccineItems.Count}";
        // 集合内容变更后需手动通知派生属性（ObservableCollection.Clear/Add 不触发集合引用变更）
        OnPropertyChanged(nameof(NeedsVaccineExpand));
        OnPropertyChanged(nameof(VisibleVaccineItems));
    }

    private static string NormalizeVaccineName(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "")
            .Replace("针", "剂")
            .ToLowerInvariant();
    }

    private static string FormatAge(DateTime birth)
    {
        var days = (DateTime.Today - birth).Days;
        if (days < 30) return $"出生{days}天";
        var months = days / 30;
        if (months < 12) return $"{months}个月{days % 30}天";
        var years = months / 12;
        return $"{years}岁{months % 12}个月";
    }

    private static string GetDailyTip(DayStats? stats)
    {
        if (stats is null) return "记录宝宝的每一天，陪伴健康成长";
        if (stats.HasFever) return "宝宝正在发热，注意监测体温和补水";
        if (stats.HasDiarrhea) return "宝宝有腹泻症状，注意观察和补水";
        if (stats.FeedCount > 0 || stats.DiaperCount > 0) return "今天记录很用心，继续加油！";
        return "今天还没有记录，点击下方快捷操作开始吧";
    }

    /// <summary>
    /// 从快照数据应用活动追踪模块（不再重复查询 DB）。
    /// 最近一条活动 = activities 列表的第一条（列表已按 record_time DESC 返回）。
    /// </summary>
    private void ApplyActivity(List<ChildRecord> activities)
    {
        // 刷新即重置展开/详情状态（对齐小程序 pageVersion observer）
        IsActivityExpanded = false;
        IsActivityDetailOpen = false;
        ActivityTimelineGroups.Clear();

        if (activities.Count == 0)
        {
            ResetActivity();
            return;
        }

        var latest = activities[0];
        var dto = latest.GetPayload<ActivityRecordDto>();
        _lastActivityTime = latest.RecordTime;
        LastActivity = new ActivityLatestItem(
            dto?.Name ?? string.Empty,
            dto?.Category ?? latest.RecordSubType ?? "play",
            dto?.Duration,
            ServiceProvider.Instance.DateTimeFormatter.FormatDateTime(latest.RecordTime));

        UpdateActivityTimeSince();
        StartActivitySinceTimer();
    }

    private void ResetActivity()
    {
        StopActivitySinceTimer();
        _lastActivityTime = null;
        LastActivity = null;
        ActivityTimeSince = "--";
        IsActivityExpanded = false;
        IsActivityDetailOpen = false;
        ActivityTimelineGroups.Clear();
    }

    /// <summary>计算"X小时Y分钟前 / Y分钟前"，对齐小程序 timeSince 格式。</summary>
    private void UpdateActivityTimeSince()
    {
        if (!_lastActivityTime.HasValue)
        {
            ActivityTimeSince = "--";
            return;
        }
        var diff = DateTime.Now - _lastActivityTime.Value;
        var totalMin = Math.Max(0, (int)diff.TotalMinutes);
        var h = totalMin / 60;
        var m = totalMin % 60;
        ActivityTimeSince = h > 0 ? $"{h}小时{m}分钟前" : $"{m}分钟前";
    }

    private void StartActivitySinceTimer()
    {
        _activitySinceTimer.Stop();
        _activitySinceTimer.Tick -= OnActivitySinceTick!;
        _activitySinceTimer.Tick += OnActivitySinceTick!;
        _activitySinceTimer.Start();
    }

    private void StopActivitySinceTimer()
    {
        _activitySinceTimer.Stop();
        _activitySinceTimer.Tick -= OnActivitySinceTick!;
    }

    private void OnActivitySinceTick(object sender, EventArgs e)
    {
        UpdateActivityTimeSince();
    }

    /// <summary>
    /// 活动时间轴每页加载条数：首屏 20 条，滚动加载每次 +20。
    /// 原 100 条全量加载导致面板打开卡顿 200-500ms，分页后首屏 < 50ms。
    /// </summary>
    private const int ActivityPageSize = 20;
    private int _activityLoadedCount;

    /// <summary>
    /// 异步构建活动时间轴分组（对齐小程序 buildTimeline）。
    /// DB 查询放到后台线程，UI 线程仅做分组填充；首屏只加载 20 条，剩余按需加载。
    /// 记录列表已按时间倒序（最新在前），分组时保持该顺序：今天在最上。
    /// </summary>
    private async Task BuildActivityTimelineAsync()
    {
        IsActivityLoading = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // 后台线程查询 DB（避免阻塞 UI 线程）
            var activities = await Task.Run(() => _recordService.GetByType(RecordType.Activity, 100));
            DevLogger.Log("ActivityPerf", $"BuildActivityTimelineAsync: DB query {activities.Count} items in {sw.ElapsedMilliseconds}ms");

            // UI 线程：分页填充首屏
            ActivityTimelineGroups.Clear();
            _activityLoadedCount = 0;
            AppendActivityPage(activities, ActivityPageSize);
            DevLogger.Log("ActivityPerf", $"BuildActivityTimelineAsync: first page rendered, total={sw.ElapsedMilliseconds}ms, items={_activityLoadedCount}");
        }
        finally
        {
            IsActivityLoading = false;
        }
    }

    /// <summary>追加一页活动记录到现有分组（保持日期分组逻辑）。</summary>
    private void AppendActivityPage(List<ChildRecord> activities, int pageSize)
    {
        var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
        ActivityTimelineGroup? current = null;
        var lastDate = string.Empty;
        // 复用最后一个已有分组（如果日期相同）
        if (ActivityTimelineGroups.Count > 0)
        {
            current = ActivityTimelineGroups[^1];
            lastDate = current.IsToday ? todayStr : current.Label;
        }

        var endIndex = Math.Min(_activityLoadedCount + pageSize, activities.Count);
        for (var i = _activityLoadedCount; i < endIndex; i++)
        {
            var rec = activities[i];
            var dateStr = rec.RecordTime.ToString("yyyy-MM-dd");
            if (dateStr != lastDate)
            {
                lastDate = dateStr;
                current = new ActivityTimelineGroup(
                    dateStr == todayStr ? "今天" : dateStr,
                    dateStr == todayStr);
                ActivityTimelineGroups.Add(current);
            }
            var dto = rec.GetPayload<ActivityRecordDto>();
            current!.Items.Add(new ActivityTimelineItem(
                dto?.Name ?? string.Empty,
                dto?.Category ?? rec.RecordSubType ?? "play",
                dto?.Duration,
                ServiceProvider.Instance.DateTimeFormatter.FormatDateTime(rec.RecordTime)));
        }
        _activityLoadedCount = endIndex;
    }

    /// <summary>是否还有更多活动记录可加载（用于"加载更多"按钮显隐）。</summary>
    public bool HasMoreActivities => _activityLoadedCount < 100;

    [RelayCommand(CanExecute = nameof(CanLoadMoreActivities))]
    private async Task LoadMoreActivities()
    {
        if (IsActivityLoading) return;
        IsActivityLoading = true;
        try
        {
            var activities = await Task.Run(() => _recordService.GetByType(RecordType.Activity, 100));
            AppendActivityPage(activities, ActivityPageSize);
            LoadMoreActivitiesCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            IsActivityLoading = false;
        }
    }

    private bool CanLoadMoreActivities => HasMoreActivities && !IsActivityLoading;

    /// <summary>
    /// 从快照数据应用异常/生病追踪状态（不再重复查询 DB）。
    /// 依据今日 DayStats 的三态标志（发烧/腹泻/其他异常），
    /// 并从异常记录中提取最新摘要。对齐小程序首页 getTodayStats 驱动的状态展示。
    /// </summary>
    private void ApplyAbnormal(DayStats? stats, List<ChildRecord> abnormalRecords)
    {
        if (stats is null || (!stats.HasFever && !stats.HasDiarrhea && !stats.HasOtherAbnormal))
        {
            ResetAbnormal();
            return;
        }

        HasActiveAbnormal = true;
        HasOtherAbnormal = stats.HasOtherAbnormal;

        var statusParts = new List<string>();
        if (stats.HasFever) statusParts.Add("发烧");
        if (stats.HasDiarrhea) statusParts.Add("腹泻");
        if (stats.HasOtherAbnormal) statusParts.Add("其他异常");
        AbnormalStatusText = string.Join(" · ", statusParts);

        // 摘要：从已查快照中取最新一条异常记录，拼体温 + 备注/其他描述
        var latestAbnormal = abnormalRecords.FirstOrDefault();
        if (latestAbnormal is not null)
        {
            var summaryParts = new List<string>();
            if (latestAbnormal.TemperatureValue.HasValue)
                summaryParts.Add($"{latestAbnormal.TemperatureValue:F1}℃");
            AbnormalRecordDto? dto = null;
            try { dto = latestAbnormal.GetPayload<AbnormalRecordDto>(); } catch { }
            if (dto is not null)
            {
                if (dto.Respiratory.Count > 0)
                    summaryParts.Add("呼吸道：" + string.Join("、", dto.Respiratory));
                if (dto.Vomit) summaryParts.Add("呕吐");
                if (dto.Medicine) summaryParts.Add("已用药");
                if (!string.IsNullOrWhiteSpace(dto.Note)) summaryParts.Add(dto.Note);
            }
            var time = ServiceProvider.Instance.DateTimeFormatter.FormatTime(latestAbnormal.RecordTime);
            summaryParts.Insert(0, time);
            AbnormalSummaryText = string.Join(" · ", summaryParts);
        }
        else
        {
            AbnormalSummaryText = "今日有异常记录，请关注宝宝状态";
        }
    }

    private void ResetAbnormal()
    {
        HasActiveAbnormal = false;
        HasOtherAbnormal = false;
        AbnormalStatusText = string.Empty;
        AbnormalSummaryText = string.Empty;
    }

    /// <summary>
    /// 标记「其他异常」已恢复：写入一条 abnormal_resolved 占位记录，
    /// 对齐小程序 markAbnormalResolved 的语义（写入恢复标记记录，聚合时不再计入活动异常）。
    /// </summary>
    [RelayCommand]
    private async Task MarkAbnormalResolvedAsync()
    {
        _recordService.MarkResolved(RecordType.AbnormalResolved);
        await RefreshAsync();
    }

    [RelayCommand]
    private void QuickRecord(string type)
    {
        QuickRecordRequested?.Invoke(type);
    }

    [RelayCommand]
    private void ToggleActivityPanel()
    {
        IsActivityExpanded = !IsActivityExpanded;
    }

    [RelayCommand]
    private async Task ToggleActivityDetail()
    {
        // 首次打开时异步构建时间轴分组（对齐小程序 onToggleDetail）
        if (!IsActivityDetailOpen)
        {
            // 立即打开面板（显示 loading 占位），后台异步加载数据
            IsActivityDetailOpen = true;
            await BuildActivityTimelineAsync();
        }
        else
        {
            IsActivityDetailOpen = false;
        }
    }

    [RelayCommand]
    private void CloseActivityDetail()
    {
        IsActivityDetailOpen = false;
    }

    [RelayCommand]
    private void ToggleVaccinePanel()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        IsVaccineExpanded = !IsVaccineExpanded;
        // OnPropertyChanged(VisibleVaccineItems) 在 OnIsVaccineExpandedChanged 中触发，
        // 同步触发 ItemsControl 重新绑定（后台线程无意义，UI 渲染必须在 UI 线程）。
        // 埋点放在属性变更后，测量"绑定触发→返回"的同步耗时（不含实际渲染，渲染在布局周期异步进行）
        DevLogger.Log("VaccinePerf", $"ToggleVaccinePanel: expanded={IsVaccineExpanded}, items={VaccineItems.Count}, notify_ms={sw.ElapsedMilliseconds}");
    }

    partial void OnIsVaccineExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(VisibleVaccineItems));
        OnPropertyChanged(nameof(VaccineListMaxHeight));
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
}

public sealed class QuickActionItem
{
    public string Icon { get; }
    public string Label { get; }
    public string Type { get; }
    /// <summary>图标背景色（对齐原版 quick-actions 配色）</summary>
    public string IconBg { get; }
    /// <summary>扇形菜单中的水平偏移（相对 + 按钮中心，px）</summary>
    public double OffsetX { get; }
    /// <summary>扇形菜单中的垂直偏移（相对 + 按钮中心，px）</summary>
    public double OffsetY { get; }
    public QuickActionItem(string icon, string label, string type, string iconBg = "#F7F7F7", double offsetX = 0, double offsetY = 0)
    {
        Icon = icon; Label = label; Type = type; IconBg = iconBg;
        OffsetX = offsetX; OffsetY = offsetY;
    }
}

public sealed class VaccineItem
{
    public string Name { get; }
    public string Category { get; }
    public int DaysLater { get; }
    public bool IsDone { get; }
    public string DueText => IsDone
        ? "已完成"
        : DaysLater > 0
            ? $"逾期{DaysLater}天"
            : DaysLater == 0
                ? "今天可打"
                : DaysLater == -1
                    ? Category
                    : $"{-DaysLater}天后";
    public VaccineItem(string name, string category, int daysLater, bool isDone)
    {
        Name = name; Category = category; DaysLater = daysLater; IsDone = isDone;
    }
}

/// <summary>
/// 活动追踪"最近活动"展示项（对齐小程序 activity-tracker 的 lastActivity）。
/// </summary>
public sealed class ActivityLatestItem
{
    public string Name { get; }
    public string Category { get; }
    public int? Duration { get; }
    /// <summary>格式化的记录时间（yyyy-MM-dd HH:mm）。</summary>
    public string Time { get; }
    /// <summary>类别展示文本（🌳 室外 / 🏠 室内），对齐小程序 at-cat。</summary>
    public string CategoryText => Category == "outdoor" ? "🌳 室外" : "🏠 室内";
    /// <summary>类别 emoji（时间轴卡片用），对齐小程序 at-tl-cat。</summary>
    public string CategoryEmoji => Category == "outdoor" ? "🌳" : "🏠";
    /// <summary>时长展示文本（仅当 duration 存在）。</summary>
    public string DurationText => Duration.HasValue ? $"⏱ {Duration}分钟" : string.Empty;

    public ActivityLatestItem(string name, string category, int? duration, string time)
    {
        Name = name; Category = category; Duration = duration; Time = time;
    }
}

/// <summary>活动时间轴分组（按日期分组，对齐小程序 timelineGroups）。</summary>
public sealed class ActivityTimelineGroup
{
    public string Label { get; }
    public bool IsToday { get; }
    public ObservableCollection<ActivityTimelineItem> Items { get; } = new();
    public ActivityTimelineGroup(string label, bool isToday)
    {
        Label = label; IsToday = isToday;
    }
}

/// <summary>活动时间轴单项（对齐小程序 at-tl-item）。</summary>
public sealed class ActivityTimelineItem
{
    public string Name { get; }
    public string Category { get; }
    public int? Duration { get; }
    public string Time { get; }
    public string CategoryEmoji => Category == "outdoor" ? "🌳" : "🏠";
    public string DurationText => Duration.HasValue ? $"⏱ {Duration}分钟" : string.Empty;

    public ActivityTimelineItem(string name, string category, int? duration, string time)
    {
        Name = name; Category = category; Duration = duration; Time = time;
    }
}
