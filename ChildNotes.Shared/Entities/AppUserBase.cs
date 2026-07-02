namespace ChildNotes.Shared.Entities;

/// <summary>
/// 用户实体的核心字段基类（前后端共享）。
/// 后端子类追加 ReferrerUserId/ReferrerBoundAt 并实现 IAuditable 接口。
/// </summary>
public abstract class AppUserBase
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string NickName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public int Gender { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
