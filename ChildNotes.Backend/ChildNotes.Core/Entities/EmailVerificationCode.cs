namespace ChildNotes.Core.Entities;

/// <summary>
/// 邮箱验证码记录。只存储 Hash，不存储明文。
/// </summary>
public class EmailVerificationCode : ICreatedAuditable
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>验证码 PBKDF2 Hash（iterations:salt:hash 格式）。</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>过期时间（UTC）。</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>消费时间（UTC）。null 表示未消费。验证成功或重发旧码时回填。</summary>
    public DateTime? ConsumedAt { get; set; }

    /// <summary>错误尝试次数。达到 5 次后拒绝。</summary>
    public int AttemptCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
