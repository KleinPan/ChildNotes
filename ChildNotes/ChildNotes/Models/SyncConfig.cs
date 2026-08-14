namespace ChildNotes.Models;

/// <summary>
/// 同步配置（单行，id=1）。
/// 重构后（v5 schema）：
///   - 移除 Username/Password/Token（不再使用用户名密码登录，Token 不存 SQLite 明文）
///   - 新增 CloudUserId（登录后云端用户 Id，唯一身份权威来源）
///   - 新增 LocalUserId（离线模式本地用户 Id，首次启动生成）
///   - AccessToken/RefreshToken 走 ISecureStorage（Android Keystore / Windows DPAPI）
/// 未登录时 CloudUserId 为空，LocalUserId 非空，App 可永久离线使用本地 SQLite。
/// 登录后 CloudUserId 非空，开启云同步；同步用 Token 从 SecureStorage 读取。
/// </summary>
public sealed class SyncConfig
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }

    /// <summary>服务器地址（用户可在数据同步页配置，为空回退到 ServerEndpoints.DefaultPrimary）。</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>登录后的云端用户 Id。null/空表示未登录（离线模式）。</summary>
    public string CloudUserId { get; set; } = string.Empty;

    /// <summary>本地用户 Id（首次启动生成，永久不变）。未登录时作为本地业务数据的 user_id。</summary>
    public string LocalUserId { get; set; } = string.Empty;

    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncStatus { get; set; }
    public string? LastSyncMsg { get; set; }

    /// <summary>设备唯一标识（首次启动生成，用于冲突归因与 device_id 字段）。</summary>
    public string DeviceId { get; set; } = string.Empty;
}
