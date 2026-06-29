using ChildNotes.Infrastructure;

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

    /// <summary>创建该宝宝的设备标识（用于多设备冲突归因）。</summary>
    public string? DeviceId { get; set; }
    /// <summary>最后一次成功上送到服务器的时间；null 表示尚未上送（待发）。</summary>
    public DateTime? SyncedAt { get; set; }

    public int AgeInDays => BirthDate.HasValue ? (int)(DateTime.Today - BirthDate.Value).TotalDays : 0;

    // 仅用于 UI 展示
    public string GenderEmoji => Gender == "girl" ? "👧" : "👦";
    public string GenderText => Gender == "girl" ? "女宝" : "男宝";
    public string BirthDateText => BirthDate.HasValue
        ? ServiceProvider.Instance.DateTimeFormatter.FormatDate(BirthDate.Value)
        : "未设置生日";
}
