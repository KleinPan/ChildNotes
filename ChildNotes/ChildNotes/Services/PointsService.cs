using ChildNotes.Data.Repositories;
using ChildNotes.Models;
using ChildNotes.Shared.Constants;

namespace ChildNotes.Services;

public sealed class PointsService
{
    private readonly PointsRepository _repo;
    private readonly RecordService _recordService;
    private readonly AppState _state;

    private static readonly int[] SignInRewards = { 1, 2, 3, 5, 7, 10, 15 };

    public PointsService(PointsRepository repo, RecordService recordService, AppState state)
    {
        _repo = repo;
        _recordService = recordService;
        _state = state;
    }

    public PointsDashboard GetDashboard()
    {
        var points = _repo.GetOrCreate(_state.UserId);
        var signIns = _repo.GetRecentSignIns(_state.UserId, 7);
        var todaySigned = signIns.Any(s => s.SignDate.Date == DateTime.Today);
        var continuousDays = CalculateContinuousDays(signIns);

        var timeline = new List<SignInTimelineItem>();
        for (var i = 6; i >= 0; i--)
        {
            var date = DateTime.Today.AddDays(-i);
            var signed = signIns.Any(s => s.SignDate.Date == date.Date);
            var dayIndex = (continuousDays - i) % 7;
            if (dayIndex < 0) dayIndex += 7;
            var reward = SignInRewards[Math.Min(dayIndex, 6)];
            timeline.Add(new SignInTimelineItem
            {
                Date = date,
                Label = i == 0 ? "今天" : date.ToString("M/d"),
                Signed = signed,
                Today = i == 0,
                DisplayReward = $"+{reward}",
                Reward = reward,
            });
        }

        var todayReward = todaySigned
            ? timeline.First(t => t.Today).Reward
            : SignInRewards[Math.Min(continuousDays % 7, 6)];

        return new PointsDashboard
        {
            Points = points.Points,
            TotalEarned = points.TotalEarned,
            TotalSpent = points.TotalSpent,
            TodaySigned = todaySigned,
            ContinuousDays = continuousDays,
            TodayRewardPoints = todayReward,
            Timeline = timeline,
            Tasks = GetTasks(),
        };
    }

    public PointsDashboard SignIn()
    {
        var existing = _repo.GetSignIn(_state.UserId, DateTime.Today);
        if (existing is not null) return GetDashboard();

        var recent = _repo.GetRecentSignIns(_state.UserId, 2);
        var yesterday = DateTime.Today.AddDays(-1);
        var continuous = recent.Any(s => s.SignDate.Date == yesterday)
            ? recent.First(s => s.SignDate.Date == yesterday).ContinuousDays + 1
            : 1;

        var dayIndex = (continuous - 1) % 7;
        var reward = SignInRewards[Math.Min(dayIndex, 6)];

        _repo.InsertSignIn(new SignInRecord
        {
            UserId = _state.UserId,
            SignDate = DateTime.Today,
            ContinuousDays = continuous,
            Reward = reward,
        });
        _repo.AddPoints(_state.UserId, reward);

        return GetDashboard();
    }

    private List<TaskItem> GetTasks()
    {
        var tasks = new List<TaskItem>
        {
            new("daily_record", "每日记录", "记录一条宝宝数据", 5),
            new("daily_feed", "喂奶打卡", "记录一次喂奶", 3),
            new("daily_diaper", "换尿布打卡", "记录一次换尿布", 2),
            new("weekly_growth", "每周成长", "记录一次身高体重", 20),
        };

        // 查询今日记录，判断任务完成状态
        var todayRecords = _state.CurrentBabyId.HasValue
            ? _recordService.GetByDate(DateTime.Today)
            : new List<ChildRecord>();

        var hasRecord = todayRecords.Count > 0;
        var hasFeed = todayRecords.Any(r => r.RecordType == RecordType.Feed);
        var hasDiaper = todayRecords.Any(r => r.RecordType == RecordType.Diaper);

        // 每周成长：查询最近 7 天是否有成长记录
        var weekAgo = DateTime.Today.AddDays(-6);
        var growthRecords = _recordService.GetByDateRange(weekAgo, DateTime.Today);
        var hasGrowthThisWeek = growthRecords.Any(r => r.RecordType == RecordType.Growth);

        foreach (var t in tasks)
        {
            t.IsCompleted = t.Code switch
            {
                "daily_record" => hasRecord,
                "daily_feed" => hasFeed,
                "daily_diaper" => hasDiaper,
                "weekly_growth" => hasGrowthThisWeek,
                _ => false,
            };
        }
        return tasks;
    }

    private static int CalculateContinuousDays(List<SignInRecord> signIns)
    {
        if (signIns.Count == 0) return 0;
        var sorted = signIns.OrderByDescending(s => s.SignDate).ToList();
        var today = DateTime.Today;
        // 今天已签到：直接返回连续天数
        if (sorted[0].SignDate.Date == today)
        {
            return sorted[0].ContinuousDays;
        }
        // 昨天签到但今天未签：连续已断，返回 0
        // （保留昨天记录的 ContinuousDays 仅作历史参考，当前连续天数应为 0）
        return 0;
    }
}

public sealed class PointsDashboard
{
    public int Points { get; set; }
    public int TotalEarned { get; set; }
    public int TotalSpent { get; set; }
    public bool TodaySigned { get; set; }
    public int ContinuousDays { get; set; }
    public int TodayRewardPoints { get; set; }
    public List<SignInTimelineItem> Timeline { get; set; } = new();
    public List<TaskItem> Tasks { get; set; } = new();
}

public sealed class SignInTimelineItem
{
    public DateTime Date { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool Signed { get; set; }
    public bool Today { get; set; }
    public string DisplayReward { get; set; } = string.Empty;
    public int Reward { get; set; }
}

public sealed class TaskItem
{
    public string Code { get; }
    public string Name { get; }
    public string Desc { get; }
    public int Reward { get; }
    public bool IsCompleted { get; set; }

    public TaskItem(string code, string name, string desc, int reward)
    {
        Code = code; Name = name; Desc = desc; Reward = reward;
    }
}
