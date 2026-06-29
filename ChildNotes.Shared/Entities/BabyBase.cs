namespace ChildNotes.Shared.Entities;

/// <summary>
/// 宝宝实体的核心字段基类（前后端共享）。
/// 前端子类追加 DeviceId/SyncedAt/UI 计算属性；后端子类实现 IAuditable 接口。
/// </summary>
public abstract class BabyBase
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
