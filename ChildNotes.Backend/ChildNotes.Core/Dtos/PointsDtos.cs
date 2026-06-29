namespace ChildNotes.Core.Dtos;

public class PointsDashboardResponse
{
    public long Points { get; set; }
    public long TotalEarned { get; set; }
    public long TotalSpent { get; set; }
    public string ShareReferrerId { get; set; } = string.Empty;
    public SignInSummaryDto SignIn { get; set; } = new();
    public LotterySummaryDto? Lottery { get; set; }
    public List<TaskTemplateDto> Tasks { get; set; } = new();
    public List<InviteRecordDto> InviteRecords { get; set; } = new();
}

public class SignInRuleDto
{
    public int CycleDays { get; set; } = 30;
    public int BaseReward { get; set; } = 1;
    public string Description { get; set; } = "每日可签到一次；连续第3/5/7/30天分别奖励3/5/7/30积分，30天后进入下一轮循环。";
    public List<SignInRewardRuleDto> Rewards { get; set; } = new();
}

public class SignInRewardRuleDto
{
    public int Day { get; set; }
    public int Points { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class SignInSummaryDto
{
    public bool TodaySigned { get; set; }
    public int ContinuousDays { get; set; }
    public int TodayRewardPoints { get; set; }
    public int NextRewardPoints { get; set; }
    public SignInRuleDto Rule { get; set; } = new();
    public List<SignInTimelineItemDto> Timeline { get; set; } = new();
}

public class SignInTimelineItemDto
{
    public string Date { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Today { get; set; }
    public bool Signed { get; set; }
    public int RewardPoints { get; set; }
    public string DisplayReward { get; set; } = "-";
}

public class LotterySummaryDto
{
    public long ActivityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DrawTime { get; set; } = string.Empty;
    public int CostPoints { get; set; } = 30;
    public int ParticipantCount { get; set; }
    public int WinnerCount { get; set; } = 1;
    public bool AlreadyJoined { get; set; }
    public string PrizeName { get; set; } = string.Empty;
    public string PrizeIntro { get; set; } = string.Empty;
    public string PrizeImage { get; set; } = string.Empty;
    public List<string> ParticipantAvatars { get; set; } = new();
}

public class LotteryHistoryItemDto
{
    public long ActivityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PrizeName { get; set; } = string.Empty;
    public int CostPoints { get; set; }
    public string Status { get; set; } = "joined";
    public string JoinedAt { get; set; } = string.Empty;
    public string DrawTime { get; set; } = string.Empty;
}

public class InviteRecordDto
{
    public long Id { get; set; }
    public long InvitedUserId { get; set; }
    public string InvitedNickName { get; set; } = string.Empty;
    public string InvitedAvatarUrl { get; set; } = string.Empty;
    public int Points { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public class TaskTemplateDto
{
    public string TaskKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Points { get; set; }
    public string Action { get; set; } = "share";
}
