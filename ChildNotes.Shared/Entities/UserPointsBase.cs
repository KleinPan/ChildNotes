namespace ChildNotes.Shared.Entities;

/// <summary>
/// 用户积分实体的核心字段基类（前后端共享）。
/// 后端子类实现 IAuditable 接口。
/// </summary>
public abstract class UserPointsBase
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public int Points { get; set; }
    public int TotalEarned { get; set; }
    public int TotalSpent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
