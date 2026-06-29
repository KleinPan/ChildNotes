using ChildNotes.Core.Constants;

namespace ChildNotes.Core.Entities;

public class SignInRecord : ICreatedAuditable
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public DateTime SignDate { get; set; }
    public DateTime SignTime { get; set; }
    public int ContinuousDays { get; set; }
    public int CycleDay { get; set; }
    public int RewardPoints { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TaskRecord
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string TaskType { get; set; } = string.Empty;
    public string TaskKey { get; set; } = string.Empty;
    public long? RelatedUserId { get; set; }
    public int Points { get; set; }
    public string Status { get; set; } = StatusConstants.TaskRecord.Completed;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class LotteryActivity
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CoverImage { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime DrawTime { get; set; }
    public int CostPoints { get; set; } = 30;
    public int ParticipantCount { get; set; }
    public int WinnerCount { get; set; } = 1;
    public string Status { get; set; } = StatusConstants.Lottery.Active;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class LotteryParticipation : IAuditable
{
    public long Id { get; set; }
    public long ActivityId { get; set; }
    public long UserId { get; set; }
    public int CostPoints { get; set; }
    public string Status { get; set; } = StatusConstants.LotteryParticipation.Joined;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class LotteryPrize
{
    public long Id { get; set; }
    public long ActivityId { get; set; }
    public string PrizeName { get; set; } = string.Empty;
    public string PrizeIntro { get; set; } = string.Empty;
    public string PrizeImage { get; set; } = string.Empty;
    public int PrizeCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class IpBlacklist
{
    public long Id { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string? TriggerMethod { get; set; }
    public string? TriggerPath { get; set; }
    public string? TriggerEndpoint { get; set; }
    public int? RequestCount { get; set; }
    public DateTime? WindowStartedAt { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AiAnalysisRecord : IAuditable
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long BabyId { get; set; }
    public string BabyName { get; set; } = string.Empty;
    public DateTime RangeStartDate { get; set; }
    public DateTime RangeEndDate { get; set; }
    public string SourceText { get; set; } = string.Empty;
    public string SkillPrompt { get; set; } = string.Empty;
    public string AnalysisText { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
