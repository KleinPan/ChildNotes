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

    /// <summary>
    /// 当前绑定的家庭 Id（Family-centric 模型，见 docs/development/family-identity-architecture.md）。
    /// 登录成功后由服务端 AuthResponse.currentFamilyId 写入；Push 时随协议上送（仅路由/日志，
    /// 服务端以 JWT 鉴权为准）。阶段 2 引入换绑（rebind）后此值变化触发 synced_at 清理。
    /// </summary>
    public string CurrentFamilyId { get; set; } = string.Empty;

    /// <summary>
    /// 上次登录的云端用户 Id（v6 遗留字段）。
    /// 旧版本登出时记录、启动时反迁移遗留数据；1C 废弃该补偿机制后仅作诊断参考，
    /// fixup 事务会清空此字段（防版本回滚后旧逻辑误触发反迁移）。
    /// </summary>
    public string LastCloudUserId { get; set; } = string.Empty;

    /// <summary>
    /// 本数据空间最近绑定的家庭 Id（阶段 1C，schema v8）。
    /// 用于换绑检测（设计文档 7.1）：登录时 last_bound == F → 同家庭静默绑定；≠ F → 弹换绑确认框（阶段 2）。
    /// 除"清除本地数据"外永不清空（含 SoftLogout/401 登出路径）。
    /// </summary>
    public string LastBoundFamilyId { get; set; } = string.Empty;

    /// <summary>
    /// 一次性身份 fixup 完成标志（阶段 1C，schema v8）。
    /// 0 = 未执行；1 = 已把存量 user_id（旧版本登录迁移后的 CloudUserId 等）归位到 LocalUserId。
    /// fixup 在单事务内完成数据归位 + 标志置位，崩溃可安全重跑。
    /// </summary>
    public int IdentityFixupDone { get; set; }

    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncStatus { get; set; }
    public string? LastSyncMsg { get; set; }

    /// <summary>设备唯一标识（首次启动生成，用于冲突归因与 device_id 字段）。</summary>
    public string DeviceId { get; set; } = string.Empty;
}
