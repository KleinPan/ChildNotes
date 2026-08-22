namespace ChildNotes.Core.Entities;

/// <summary>
/// 家庭（数据归属的真正主体）。详见 docs/development/family-identity-architecture.md。
/// Owner 唯一真相是 FamilyMember.Role == Owner（部分唯一索引保证单 Owner）；
/// Family 表不设 OwnerUserId，避免双真相漂移。
/// </summary>
public class Family
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "我的家庭";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
