using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Models;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;

namespace ChildNotes.ViewModels.Home;

/// <summary>
/// 首页核心信息 ViewModel：管理宝宝基本信息、今日统计（喂养/尿布/睡眠/生长）。
/// 从 HomeViewModel 拆分，职责单一化。
/// </summary>
public partial class HomeCoreViewModel : ObservableObject
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    [ObservableProperty] private string _babyName = string.Empty;
    [ObservableProperty] private string _babyAvatar = string.Empty;
    [ObservableProperty] private string _babyAgeText = string.Empty;
    [ObservableProperty] private string _growthStage = string.Empty;
    [ObservableProperty] private DayStats? _todayStats;
    [ObservableProperty] private string _dailyTip;

    [ObservableProperty] private string _lastFeedAgoText = "--";
    [ObservableProperty] private string _lastFeedSummary = "--";
    [ObservableProperty] private string _diaperTodayText;
    [ObservableProperty] private string _diaperDetailText;
    [ObservableProperty] private string _sleepTodayText;
    [ObservableProperty] private string _latestHeightText = "--cm";
    [ObservableProperty] private string _latestWeightText = "--kg";

    public HomeCoreViewModel()
    {
        _dailyTip = _locale.GetString("Home_DailyTip_Default", "记录宝宝的每一天，陪伴健康成长");
        _diaperTodayText = _locale.GetString("Home_Diaper_Zero", "0次");
        _diaperDetailText = _locale.GetString("Home_DiaperDetail_Zero", "便0 尿0");
        _sleepTodayText = _locale.GetString("Home_Sleep_Zero", "0小时0分钟");

        _locale.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(AppLanguage lang)
    {
        DailyTip = GetDailyTip(TodayStats);
        DiaperTodayText = _locale.GetString("Home_Diaper_Zero", "0次");
        DiaperDetailText = _locale.GetString("Home_DiaperDetail_Zero", "便0 尿0");
        SleepTodayText = _locale.GetString("Home_Sleep_Zero", "0小时0分钟");
        if (BabyName == _locale.GetString("Home_NoBaby", "未添加宝宝"))
        {
            BabyName = _locale.GetString("Home_NoBaby", "未添加宝宝");
        }
    }

    /// <summary>应用宝宝基本信息（从 RefreshAsync 快照调用）。</summary>
    public void ApplyBabyInfo(Baby baby, string growthStage)
    {
        BabyName = baby.Name;
        BabyAvatar = baby.Avatar;
        GrowthStage = growthStage;
        BabyAgeText = baby.BirthDate.HasValue
            ? FormatAge(baby.BirthDate.Value)
            : string.Empty;
    }

    /// <summary>应用今日统计数据（喂养/尿布/睡眠/生长，从快照派生，不再重复查询 DB）。</summary>
    public void ApplyTodayStats(DayStats? stats, ChildRecord? latestFeed, List<ChildRecord> todayRecords, List<ChildRecord> growthRecords)
    {
        TodayStats = stats;
        DailyTip = GetDailyTip(stats);

        ApplyLastFeed(latestFeed, todayRecords);
        ApplyDiaper(stats);
        ApplySleep(stats);
        ApplyGrowth(growthRecords);
    }

    /// <summary>重置为默认状态（无宝宝时调用）。</summary>
    public void Reset()
    {
        BabyName = _locale.GetString("Home_NoBaby", "未添加宝宝");
        BabyAgeText = string.Empty;
        GrowthStage = string.Empty;
        TodayStats = null;
        LastFeedAgoText = "--";
        LastFeedSummary = "--";
        DiaperTodayText = _locale.GetString("Home_Diaper_Zero", "0次");
        DiaperDetailText = _locale.GetString("Home_DiaperDetail_Zero", "便0 尿0");
        SleepTodayText = _locale.GetString("Home_Sleep_Zero", "0小时0分钟");
        LatestHeightText = "--cm";
        LatestWeightText = "--kg";
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
            LastFeedAgoText = $"{(int)ago.TotalMinutes}{_locale.GetString("Home_Minutes", "分钟")}";
        else if (ago.TotalHours < 24)
            LastFeedAgoText = string.Format(_locale.GetString("Home_HoursMinutes", "{0}小时{1}分钟"), (int)ago.TotalHours, (int)(ago.TotalMinutes % 60));
        else
            LastFeedAgoText = string.Format(_locale.GetString("Home_Days", "{0}天"), (int)ago.TotalDays);

        // 从已查的当日记录中筛选喂养记录，避免重复查询
        var todayFeeds = todayRecords.Where(r => r.RecordType == RecordType.Feed).ToList();
        var feedCount = todayFeeds.Count;
        var totalMl = todayFeeds.Sum(r => r.AmountMl ?? 0);
        LastFeedSummary = totalMl > 0
            ? string.Format(_locale.GetString("Home_FeedCount", "{0}次 {1}ml"), feedCount, totalMl)
            : string.Format(_locale.GetString("Home_FeedCountNoMl", "{0}次"), feedCount);
    }

    private void ApplyDiaper(DayStats? stats)
    {
        if (stats is null)
        {
            DiaperTodayText = _locale.GetString("Home_Diaper_Zero", "0次");
            DiaperDetailText = _locale.GetString("Home_DiaperDetail_Zero", "便0 尿0");
            return;
        }
        DiaperTodayText = string.Format(_locale.GetString("Home_DiaperCount", "{0}次"), stats.DiaperCount);
        DiaperDetailText = string.Format(_locale.GetString("Home_DiaperDetail", "便{0} 尿{1}"), stats.DirtyDiaperCount, stats.WetDiaperCount);
    }

    private void ApplySleep(DayStats? stats)
    {
        if (stats is null || stats.SleepTotalMin <= 0)
        {
            SleepTodayText = _locale.GetString("Home_Sleep_Zero", "0小时0分钟");
            return;
        }
        var hours = stats.SleepTotalMin / 60;
        var mins = stats.SleepTotalMin % 60;
        SleepTodayText = string.Format(_locale.GetString("Home_HoursMinutes", "{0}小时{1}分钟"), hours, mins);
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

    private static string FormatAge(DateTime birth)
    {
        var today = DateTime.Today;
        var days = (today - birth).Days;
        if (days < 0) return "未出生";
        if (days < 30) return $"出生{days}天";

        // 按日历月计算（与主流育儿 App 一致）：月数按"日历月对齐"推进，剩余天数为本月对齐日到今天。
        var months = (today.Year - birth.Year) * 12 + today.Month - birth.Month;
        if (today.Day < birth.Day) months--;
        var anchor = birth.AddMonths(months);
        var remainDays = (today - anchor).Days;

        if (months < 12) return $"{months}个月{remainDays}天";
        var years = months / 12;
        return $"{years}岁{months % 12}个月{remainDays}天";
    }

    private string GetDailyTip(DayStats? stats)
    {
        if (stats is null) return _locale.GetString("Home_DailyTip_Default", "记录宝宝的每一天，陪伴健康成长");
        if (stats.HasFever) return _locale.GetString("Home_DailyTip_Fever", "宝宝正在发热，注意监测体温和补水");
        if (stats.HasDiarrhea) return _locale.GetString("Home_DailyTip_Diarrhea", "宝宝有腹泻症状，注意观察和补水");
        if (stats.FeedCount > 0 || stats.DiaperCount > 0) return _locale.GetString("Home_DailyTip_Active", "今天记录很用心，继续加油！");
        return _locale.GetString("Home_DailyTip_Empty", "今天还没有记录，点击下方快捷操作开始吧");
    }
}
