namespace ChildNotes.Shared.Entities;

/// <summary>
/// 宝宝实体的核心字段基类（前后端共享）。
/// 前端子类追加 DeviceId/SyncedAt/UI 计算属性；后端子类实现 IAuditable 接口。
/// </summary>
public abstract class BabyBase
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    /// <summary>软删除标记：true 表示已删除，同步通道需传递此字段以实现多设备软删一致性。</summary>
    public bool Deleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
