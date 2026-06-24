using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Models.Dtos;
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
    [ObservableProperty] private string _aiTipText = "洗澡水温37~38℃最适合，用手肘试温";

    [ObservableProperty] private ObservableCollection<VaccineItem> _vaccineItems = new();
    [ObservableProperty] private string _vaccineProgressText = "0/57";
    [ObservableProperty] private bool _isVaccineExpanded;

    [ObservableProperty] private bool _isQuickRecordPanelOpen;

    public ObservableCollection<QuickActionItem> QuickActions { get; } = new();

    public event Action? StatisticsRequested;
    public event Action? CheckInRequested;

    public HomeViewModel()
    {
        QuickActions.Add(new QuickActionItem("🍼", "喂奶", RecordType.Feed));
        QuickActions.Add(new QuickActionItem("💩", "换尿布", RecordType.Diaper));
        QuickActions.Add(new QuickActionItem("😴", "睡眠", RecordType.Sleep));
        QuickActions.Add(new QuickActionItem("🌡️", "体温", RecordType.Temperature));
        QuickActions.Add(new QuickActionItem("💊", "补药用药", RecordType.Supplement));
        QuickActions.Add(new QuickActionItem("🥛", "吸奶", RecordType.Pump));
        QuickActions.Add(new QuickActionItem("🥣", "辅食", RecordType.Complementary));
        QuickActions.Add(new QuickActionItem("🍱", "妈妈饮食", RecordType.MaternalFood));
        QuickActions.Add(new QuickActionItem("📏", "成长", RecordType.Growth));
    }

    public void Activate()
    {
        Refresh();
    }

    public void Refresh()
    {
        var baby = _babyService.LoadBabyList().FirstOrDefault(b => b.Id == ServiceProvider.Instance.AppState.CurrentBabyId)
                   ?? ServiceProvider.Instance.AppState.CurrentBaby;
        ServiceProvider.Instance.AppState.CurrentBaby = baby;

        if (baby is null)
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
            VaccineProgressText = "0/57";
            return;
        }

        BabyName = baby.Name;
        BabyAvatar = baby.Avatar;
        GrowthStage = _babyService.GetGrowthStageText();
        BabyAgeText = baby.BirthDate.HasValue
            ? FormatAge(baby.BirthDate.Value)
            : string.Empty;

        TodayStats = _statsService.GetDayStats(DateTime.Today);
        DailyTip = GetDailyTip(TodayStats);

        RefreshLastFeed();
        RefreshDiaper();
        RefreshSleep();
        RefreshGrowth();
        RefreshAiStatus(TodayStats);
        RefreshVaccines();
    }

    private void RefreshLastFeed()
    {
        var lastFeed = _recordService.GetLatest(RecordType.Feed);
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

        var todayFeeds = _recordService.GetByDate(DateTime.Today).Where(r => r.RecordType == RecordType.Feed).ToList();
        var feedCount = todayFeeds.Count;
        var totalMl = todayFeeds.Sum(r => r.AmountMl ?? 0);
        LastFeedSummary = totalMl > 0 ? $"{feedCount}次 {totalMl}ml" : $"{feedCount}次";
    }

    private void RefreshDiaper()
    {
        var stats = TodayStats;
        if (stats is null)
        {
            DiaperTodayText = "0次";
            DiaperDetailText = "便0 尿0";
            return;
        }
        DiaperTodayText = $"{stats.DiaperCount}次";
        DiaperDetailText = $"便{stats.DirtyDiaperCount} 尿{stats.WetDiaperCount}";
    }

    private void RefreshSleep()
    {
        var stats = TodayStats;
        if (stats is null || stats.SleepTotalMin <= 0)
        {
            SleepTodayText = "0小时0分钟";
            return;
        }
        var hours = stats.SleepTotalMin / 60;
        var mins = stats.SleepTotalMin % 60;
        SleepTodayText = $"{hours}小时{mins}分钟";
    }

    private void RefreshGrowth()
    {
        var growthRecords = _recordService.GetByType(RecordType.Growth, 1);
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
        if (stats is null)
        {
            AiStatusIcon = "☀️";
            AiStatusTitle = "小铃铛状态良好";
            AiStatusSubtitle = "正在快乐成长中~";
            AiTipText = "洗澡水温37~38℃最适合，用手肘试温";
            return;
        }

        if (stats.HasFever)
        {
            AiStatusIcon = "🌡️";
            AiStatusTitle = "体温偏高需关注";
            AiStatusSubtitle = $"当前体温{stats.LatestTemperature?.ToString("F1")}℃";
            AiTipText = "多喂温水，物理降温，持续发热请及时就医";
        }
        else if (stats.HasDiarrhea)
        {
            AiStatusIcon = "⚠️";
            AiStatusTitle = "肠胃需要呵护";
            AiStatusSubtitle = "今日有腹泻记录";
            AiTipText = "注意补充水分和电解质，清淡饮食为主";
        }
        else if (stats.FeedCount >= 6 && stats.SleepTotalMin >= 480)
        {
            AiStatusIcon = "😊";
            AiStatusTitle = "小铃铛状态良好";
            AiStatusSubtitle = "吃得好睡得香~";
            AiTipText = "继续保持规律作息，户外活动有助于维生素D合成";
        }
        else if (stats.FeedCount == 0 && stats.DiaperCount == 0)
        {
            AiStatusIcon = "📝";
            AiStatusTitle = "今天还没开始记录哦";
            AiStatusSubtitle = "点击下方快捷按钮开始吧";
            AiTipText = "坚持记录能更了解宝宝成长变化";
        }
        else
        {
            AiStatusIcon = "☀️";
            AiStatusTitle = "小铃铛状态良好";
            AiStatusSubtitle = "正在快乐成长中~";
            AiTipText = "洗澡水温37~38℃最适合，用手肘试温";
        }
    }

    private void RefreshVaccines()
    {
        VaccineItems.Clear();
        var vaccines = _recordService.GetByType(RecordType.Vaccine, 20);

        var vaccineNames = new[]
        {
            ("乙肝疫苗（第1针）", "出生"),
            ("卡介苗", "出生"),
            ("乙肝疫苗（第2针）", "1月龄"),
            ("脊灰疫苗（第1针）", "2月龄"),
            ("百白破（第1针）", "3月龄"),
            ("肺炎13价（第1针）", "2月龄"),
            ("轮状病毒（第1剂）", "2月龄"),
            ("五联/百白破（第2针）", "4月龄"),
            ("脊灰疫苗（第2针）", "3月龄"),
        };

        var completedSet = new HashSet<string>(vaccines.Select(v =>
        {
            try { return v.GetPayload<VaccineRecordDto>()?.Name ?? ""; } catch { return ""; }
        }).Where(n => !string.IsNullOrEmpty(n)));

        foreach (var (name, category) in vaccineNames)
        {
            var done = completedSet.Contains(name);
            VaccineItems.Add(new VaccineItem(name, category, done ? 0 : -1, done));
        }

        var doneCount = VaccineItems.Count(v => v.IsDone);
        VaccineProgressText = $"{doneCount}/{VaccineItems.Count}";
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

    [RelayCommand]
    private void QuickRecord(string type)
    {
    }

    [RelayCommand]
    private void ToggleQuickRecordPanel()
    {
        IsQuickRecordPanelOpen = !IsQuickRecordPanelOpen;
    }

    [RelayCommand]
    private void ToggleVaccinePanel()
    {
        IsVaccineExpanded = !IsVaccineExpanded;
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
    public QuickActionItem(string icon, string label, string type)
    {
        Icon = icon; Label = label; Type = type;
    }
}

public sealed class VaccineItem
{
    public string Name { get; }
    public string Category { get; }
    public int DaysLater { get; }
    public bool IsDone { get; }
    public string DueText => IsDone ? "已完成" : (DaysLater >= 0 ? $"逾期{DaysLater}天" : Category);
    public VaccineItem(string name, string category, int daysLater, bool isDone)
    {
        Name = name; Category = category; DaysLater = daysLater; IsDone = isDone;
    }
}
