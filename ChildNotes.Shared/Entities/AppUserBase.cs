namespace ChildNotes.Shared.Entities;

/// <summary>
/// 用户实体的核心字段基类（前后端共享）。
/// 后端子类追加 ReferrerUserId/ReferrerBoundAt 并实现 IAuditable 接口。
/// 认证方式：邮箱验证码（Email + EmailVerifiedAt）。
/// </summary>
public abstract class AppUserBase
{
    public string Id { get; set; } = string.Empty;

    /// <summary>邮箱（唯一，登录凭证）。</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>邮箱验证时间（UTC）。null 表示未验证。</summary>
    public DateTime? EmailVerifiedAt { get; set; }

    public string NickName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public int Gender { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 会员到期时间（UTC）。null 或早于当前时间表示非会员。
    /// 会员状态判断统一通过 MembershipHelper.IsActive(expireAt) 进行。
    /// </summary>
    public DateTime? MembershipExpireAt { get; set; }
}
