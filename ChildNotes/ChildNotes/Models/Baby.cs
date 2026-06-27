namespace ChildNotes.Models;

public sealed class Baby
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int AgeInDays => BirthDate.HasValue ? (int)(DateTime.Today - BirthDate.Value).TotalDays : 0;

    // 仅用于 UI 展示
    public string GenderEmoji => Gender == "girl" ? "👧" : "👦";
    public string GenderText => Gender == "girl" ? "女宝" : "男宝";
    public string BirthDateText => BirthDate?.ToString("yyyy-MM-dd") ?? "未设置生日";
}
