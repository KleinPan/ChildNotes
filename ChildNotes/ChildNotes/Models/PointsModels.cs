namespace ChildNotes.Models;

public sealed class UserPoints
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public int Points { get; set; }
    public int TotalEarned { get; set; }
    public int TotalSpent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class SignInRecord
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public DateTime SignDate { get; set; }
    public int ContinuousDays { get; set; }
    public int Reward { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class TaskRecord
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string TaskCode { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public int Reward { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class Milestone
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long? BabyId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public DateTime RecordDate { get; set; }
    public string PhotosJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
