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
}
