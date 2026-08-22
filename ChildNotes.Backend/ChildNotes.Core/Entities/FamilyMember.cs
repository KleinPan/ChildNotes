using ChildNotes.Core.Constants;

namespace ChildNotes.Core.Entities;

/// <summary>
/// 家庭成员（User ↔ Family 关系 + 角色）。
/// Role：Owner（单 Owner，由部分唯一索引保证）/ Member / Readonly。
/// </summary>
public class FamilyMember
{
    public string Id { get; set; } = string.Empty;
    public string FamilyId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = StatusConstants.FamilyMemberRole.Member;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
