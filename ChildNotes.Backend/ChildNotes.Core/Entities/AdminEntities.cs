using ChildNotes.Core.Constants;

namespace ChildNotes.Core.Entities;

public class AdminAccount : IAuditable
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = StatusConstants.Admin.Active;
    public string? Token { get; set; }
    public DateTime? TokenExpireAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AdminLotteryActivity
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CoverImage { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime DrawTime { get; set; }
    public int CostPoints { get; set; }
    public int WinnerCount { get; set; } = 1;
    public string Status { get; set; } = StatusConstants.AdminLottery.Draft; // draft / published / closed
    public DateTime? PublishTime { get; set; }
    public long? CreatedBy { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AdminLotteryPrize
{
    public long Id { get; set; }
    public long ActivityId { get; set; }
    public string PrizeName { get; set; } = string.Empty;
    public string PrizeIntro { get; set; } = string.Empty;
    public string PrizeImage { get; set; } = string.Empty;
    public int PrizeCount { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
