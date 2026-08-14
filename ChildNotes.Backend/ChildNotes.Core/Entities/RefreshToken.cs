namespace ChildNotes.Core.Entities;

/// <summary>
/// Refresh Token 记录。只存储 Hash，不存储明文。
/// 支持 Rotation：每次 refresh 生成新 token 并撤销旧 token。
/// </summary>
public class RefreshToken : ICreatedAuditable
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;

    /// <summary>Token PBKDF2 Hash（iterations:salt:hash 格式）。</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>过期时间（UTC）。</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>撤销时间（UTC）。null 表示活跃。Rotation 时回填。</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>设备标识（可选，用于多设备管理）。</summary>
    public string? DeviceId { get; set; }

    public DateTime CreatedAt { get; set; }
}
