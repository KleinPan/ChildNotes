using ChildNotes.Core.Constants;

namespace ChildNotes.Core.Dtos;

public class AdminLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AdminLoginResponse
{
    public string AdminId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string TokenExpireAt { get; set; } = string.Empty;
}

public class AdminOverviewResponse
{
    public long TotalUsers { get; set; }
    public long TodayUsers { get; set; }
    public long TotalBabies { get; set; }
    public long TodayBabies { get; set; }
    public long DraftLotteryCount { get; set; }
    public long PublishedLotteryCount { get; set; }
}

public class AdminPageResponse<T>
{
    public long Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<T> Records { get; set; } = new();
}

public class AdminUserListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string NickName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public int Gender { get; set; }
    public string? ReferrerUserId { get; set; }
    public int BabyCount { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public class AdminBabyListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public int AgeDays { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string OwnerNickName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public class AdminLotteryPrizeDto
{
    public string Id { get; set; } = string.Empty;
    public string PrizeName { get; set; } = string.Empty;
    public string PrizeIntro { get; set; } = string.Empty;
    public string PrizeImage { get; set; } = string.Empty;
    public int PrizeCount { get; set; } = 1;
}

public class AdminLotteryRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CoverImage { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime DrawTime { get; set; }
    public int CostPoints { get; set; }
    public int WinnerCount { get; set; } = 1;
    public string Status { get; set; } = StatusConstants.AdminLottery.Draft;
    public List<AdminLotteryPrizeDto> Prizes { get; set; } = new();
}

public class AdminLotteryDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CoverImage { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime DrawTime { get; set; }
    public int CostPoints { get; set; }
    public int WinnerCount { get; set; } = 1;
    public string Status { get; set; } = StatusConstants.AdminLottery.Draft;
    public DateTime? PublishTime { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<AdminLotteryPrizeDto> Prizes { get; set; } = new();
}
