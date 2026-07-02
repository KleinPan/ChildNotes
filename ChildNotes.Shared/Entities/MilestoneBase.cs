namespace ChildNotes.Shared.Entities;

/// <summary>
/// 成长时刻（里程碑）实体的核心字段基类（前后端共享）。
/// 前端子类追加 DeviceId/SyncedAt；后端子类实现 IAuditable 接口。
/// PhotosJson 存储图片 URL/路径的 JSON 数组字符串。
/// </summary>
public abstract class MilestoneBase
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long? BabyId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public DateTime RecordDate { get; set; }
    /// <summary>图片 URL/本地路径的 JSON 数组字符串，如 ["url1","url2"]。空数组长字符串为 "[]"。</summary>
    public string PhotosJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
