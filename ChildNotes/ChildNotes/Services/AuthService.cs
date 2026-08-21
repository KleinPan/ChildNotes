using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services.Storage;

namespace ChildNotes.Services;

/// <summary>
/// 邮箱验证码认证服务（客户端）。
///
/// 重构后（v5 schema）：
///   - 移除 PBKDF2 密码哈希、用户名密码登录、本地 user_session 表
///   - 统一走邮箱验证码流程：SendCodeAsync → VerifyCodeAsync（注册+登录合一）
///   - AccessToken / RefreshToken 走 ISecureStorage（Android Keystore / Windows DPAPI）
///   - 身份权威来源：sync_config.cloud_user_id（空 = 未登录离线模式）
///   - 未登录可永久离线使用本地 SQLite，user_id 使用 sync_config.local_user_id
///   - 登录失败不删除业务数据，仅清空 SecureStorage 与 CloudUserId
///
/// 后端契约：
///   - POST /api/auth/send-code  → ApiResponse&lt;SendCodeResponse&gt;{Sent:true}
///   - POST /api/auth/verify-code → ApiResponse&lt;AuthResponse&gt;{AccessToken,RefreshToken,ExpiresIn,User,NewUser}
///   - POST /api/auth/refresh    → ApiResponse&lt;AuthResponse&gt;（RefreshToken Rotation）
///   - GET  /api/auth/me        → ApiResponse&lt;LoginUserDto&gt;（Bearer 鉴权）
///   - 后端通过 ApiResponseWrapperFilter 统一包装为 {state,msg,data} 信封
/// </summary>
public sealed class AuthService
{
    private readonly UserRepository _users;
    private readonly AppState _state;
    private readonly SyncConfigRepository _cfgRepo;
    private readonly ISecureStorage _secureStorage;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        // 后端 camelCase（accessToken/refreshToken 等），前端 DTO 用 PascalCase
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>当前登录用户（本地缓存）。未登录时为 null。</summary>
    public AppUser? CurrentUser { get; private set; }

    /// <summary>是否已登录（CloudUserId 非空）。</summary>
    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(_cfgRepo.Get().CloudUserId);

    public AuthService(
        UserRepository users,
        AppState state,
        SyncConfigRepository cfgRepo,
        ISecureStorage secureStorage)
    {
        _users = users;
        _state = state;
        _cfgRepo = cfgRepo;
        _secureStorage = secureStorage;
    }

    // ===== 邮箱验证码流程 =====

    /// <summary>
    /// 发送邮箱验证码。
/// </summary>
    /// <param name="email">目标邮箱</param>
    /// <param name="ct"></param>
    /// <returns>成功返回 true；失败返回 false 并在 Message 中携带原因。</returns>
    public async Task<SendCodeResult> SendCodeAsync(string email, CancellationToken ct = default)
    {
        var trimmed = email.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || !trimmed.Contains('@'))
            return new SendCodeResult(false, "请输入有效的邮箱");

        var cfg = _cfgRepo.Get();
        var serverUrl = ResolveServerUrl(cfg.ServerUrl);
        if (serverUrl is null)
            return new SendCodeResult(false, "服务器地址未配置");

        var url = serverUrl.TrimEnd('/') + "/api/auth/send-code";
        var body = Serialize(new { email = trimmed });
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            using var resp = await Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                var msg = ExtractMessage(json);
                DevLogger.Log("Auth", $"SendCode fail: {(int)resp.StatusCode} {msg}");
                return new SendCodeResult(false, msg ?? $"发送失败（HTTP {(int)resp.StatusCode}）");
            }
            // 后端包装为 {state,msg,data}；data.Sent=true 表示已发送
            var sent = ExtractData<SendCodeDto>(json)?.Sent ?? false;
            DevLogger.Log("Auth", $"SendCode ok: email={trimmed}, sent={sent}");
            return new SendCodeResult(true, "验证码已发送");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", "SendCode exception: " + ex.Message);
            return new SendCodeResult(false, "网络异常，请稍后重试");
        }
    }

    /// <summary>
    /// 验证邮箱验证码并完成登录/自动注册。
    /// 成功条件：后端返回 AuthResponse（含 AccessToken/RefreshToken/User）。
    /// 成功后：
    ///   1) AccessToken / RefreshToken 写入 ISecureStorage
    ///   2) CloudUserId 写入 sync_config
    ///   3) app_user 表缓存用户 profile（Upsert）
    ///   4) CurrentUser 与 AppState.User 同步设置
    /// </summary>
    public async Task<VerifyCodeResult> VerifyCodeAsync(string email, string code, CancellationToken ct = default)
    {
        var trimmedEmail = email.Trim();
        var trimmedCode = code.Trim();
        if (string.IsNullOrWhiteSpace(trimmedEmail) || !trimmedEmail.Contains('@'))
            return new VerifyCodeResult(false, "请输入有效的邮箱");
        if (string.IsNullOrWhiteSpace(trimmedCode))
            return new VerifyCodeResult(false, "请输入验证码");

        var cfg = _cfgRepo.Get();
        var serverUrl = ResolveServerUrl(cfg.ServerUrl);
        if (serverUrl is null)
            return new VerifyCodeResult(false, "服务器地址未配置");

        var url = serverUrl.TrimEnd('/') + "/api/auth/verify-code";
        var body = Serialize(new { email = trimmedEmail, code = trimmedCode });
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            using var resp = await Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                var msg = ExtractMessage(json);
                DevLogger.Log("Auth", $"VerifyCode fail: {(int)resp.StatusCode} {msg}");
                return new VerifyCodeResult(false, msg ?? $"验证失败（HTTP {(int)resp.StatusCode}）");
            }

            var auth = ExtractData<AuthResponseDto>(json);
            if (auth is null || string.IsNullOrEmpty(auth.AccessToken))
            {
                DevLogger.Log("Auth", "VerifyCode fail: 响应缺少 data.accessToken");
                return new VerifyCodeResult(false, "登录失败：响应数据不完整");
            }

            // 账号切换保护：MVP 不支持同一 SQLite 切换不同云端账号
            var currentCloudUserId = _cfgRepo.Get().CloudUserId;
            if (!string.IsNullOrEmpty(currentCloudUserId) &&
                !string.IsNullOrEmpty(auth.User?.Id) &&
                !string.Equals(currentCloudUserId, auth.User.Id, StringComparison.Ordinal))
            {
                // 不清理 token，提示用户必须清除本地数据后重新登录
                DevLogger.Log("Auth", $"Account switch blocked: current={currentCloudUserId}, new={auth.User?.Id}");
                return new VerifyCodeResult(false, "当前已绑定其他账号，请先清除本地数据后再登录");
            }

            // 1) Token 写入 SecureStorage（非明文）
            await _secureStorage.SetAsync(SecureStorageKeys.AccessToken, auth.AccessToken, ct);
            await _secureStorage.SetAsync(SecureStorageKeys.RefreshToken, auth.RefreshToken, ct);

            // 2) CloudUserId 写入 sync_config（唯一身份权威来源）
            //    同时把 LocalUserId 名下的本地业务数据迁移到 CloudUserId 名下：
            //    用户离线时按 LocalUserId 创建的 baby/record/points 等数据，
            //    若不迁移，登录后 GetByUser(CloudUserId) 查不到，首页会显示"未添加宝宝"。
            if (auth.User is not null && !string.IsNullOrEmpty(auth.User.Id))
            {
                var localUserIdBeforeLogin = _cfgRepo.Get().LocalUserId;
                _cfgRepo.UpdateCloudUserId(auth.User.Id);
                try
                {
                    int affected = _cfgRepo.MigrateUserId(localUserIdBeforeLogin ?? string.Empty, auth.User.Id);
                    if (affected > 0)
                        DevLogger.Log("Auth", $"Local data migrated to cloud user: affected={affected}");
                }
                catch (Exception migrateEx)
                {
                    // 迁移失败不阻塞登录：保留 CloudUserId 与 Token，让用户登录后再手动处理
                    DevLogger.Log("Auth", "MigrateUserId(local→cloud) failed (non-fatal): " + migrateEx.Message);
                }
            }

            // 3) app_user 表缓存 profile（Upsert），供 UI 展示昵称/头像等
            var user = ToAppUser(auth.User!);
            _users.Upsert(user);

            // 4) 设置 CurrentUser 与 AppState
            CurrentUser = user;
            _state.User = user;

            DevLogger.Log("Auth", $"VerifyCode ok: email={trimmedEmail}, userId={auth.User?.Id}, newUser={auth.NewUser}");
            return new VerifyCodeResult(true, "登录成功", user, auth.NewUser);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", "VerifyCode exception: " + ex.Message);
            return new VerifyCodeResult(false, "网络异常，请稍后重试");
        }
    }

    // ===== 启动恢复 =====

    /// <summary>
    /// 启动时尝试恢复登录态（离线优先）。
    /// 逻辑：
    ///   1) 读取 sync_config.cloud_user_id；空 → 未登录，直接返回 false（离线模式可用）
    ///   2) 读取 SecureStorage.AccessToken；空 → 已登录但 token 丢失，仍恢复 CurrentUser
    ///   3) 从 app_user 表读取 profile 缓存设置 CurrentUser 与 AppState.User
    /// 失败时不删除业务数据，仅返回 false。
    /// </summary>
    public async Task<bool> TryRestoreSessionAsync(CancellationToken ct = default)
    {
        var cfg = _cfgRepo.Get();
        if (string.IsNullOrWhiteSpace(cfg.CloudUserId))
        {
            // 离线模式补救迁移：检查 last_cloud_user_id 是否非空，
            // 有则把上次 CloudUserId 名下遗留数据反迁移到 LocalUserId 名下。
            // 场景：旧版本（v0.7.19 及之前）登出时未反迁移，数据留在旧 CloudUserId 名下，
            // 重启 App 走离线模式（CloudUserId 空，AppState.UserId = LocalUserId）查不到。
            // 此分支在 v0.7.21 引入，能修复所有用户从旧版本升级后的遗留数据。
            // 幂等：反迁移成功后清空 last_cloud_user_id，下次启动不再重复；
            //      反迁移失败时保留 last_cloud_user_id，下次启动重试。
            EnsureLocalUserId(cfg);
            if (!string.IsNullOrEmpty(cfg.LastCloudUserId) &&
                !string.IsNullOrEmpty(cfg.LocalUserId) &&
                !string.Equals(cfg.LastCloudUserId, cfg.LocalUserId, StringComparison.Ordinal))
            {
                try
                {
                    int affected = _cfgRepo.MigrateUserId(cfg.LastCloudUserId, cfg.LocalUserId);
                    DevLogger.Log("Auth", $"Offline migration: last_cloud={cfg.LastCloudUserId} → local={cfg.LocalUserId}, affected={affected}");
                    _cfgRepo.UpdateLastCloudUserId(string.Empty);
                }
                catch (Exception migrateEx)
                {
                    DevLogger.Log("Auth", $"Offline migration failed (non-fatal): {migrateEx.Message}");
                }
            }
            DevLogger.Log("Auth", "RestoreSession: cloud_user_id 为空，离线模式");
            return false;
        }

        // 确保 LocalUserId 已生成（离线模式的业务数据需要）
        EnsureLocalUserId(cfg);

        // 启动时补救迁移：将 LocalUserId 名下未迁移的业务数据迁移到 CloudUserId 名下。
        // 修复"先离线记录后登录"用户在更新到此版本前已写入 CloudUserId 但未触发迁移的场景：
        // 重启 App 走 TryRestoreSessionAsync 路径不会调用 VerifyCodeAsync，
        // 若不补救迁移，原本地宝宝数据仍按 LocalUserId 存储，首页继续显示"未添加宝宝"。
        // 幂等：相同 id 或无数据可迁时返回 0，每次启动重复调用安全。
        try
        {
            int affected = _cfgRepo.MigrateUserId(cfg.LocalUserId, cfg.CloudUserId);
            if (affected > 0)
                DevLogger.Log("Auth", $"RestoreSession migration: local={cfg.LocalUserId} → cloud={cfg.CloudUserId}, affected={affected}");
        }
        catch (Exception migrateEx)
        {
            DevLogger.Log("Auth", "RestoreSession migration failed (non-fatal): " + migrateEx.Message);
        }

        var user = _users.FindById(cfg.CloudUserId);
        if (user is not null)
        {
            CurrentUser = user;
            _state.User = user;
            DevLogger.Log("Auth", $"RestoreSession success: cloud_user_id={cfg.CloudUserId}, email={user.Email}");
        }
        else
        {
            // app_user 表无缓存（首次登录后 DB 重建等场景），仅设置 AppState.UserId
            // 通过 AppState.UserId 计算（cloud_user_id 优先，否则 local_user_id）兜底
            DevLogger.Log("Auth", $"RestoreSession: cloud_user_id={cfg.CloudUserId} 但 app_user 表无缓存，仍视为已登录");
        }

        // 检查 SecureStorage 是否有 AccessToken（不影响登录态判断）
        try
        {
            var token = await _secureStorage.GetAsync(SecureStorageKeys.AccessToken, ct);
            if (string.IsNullOrEmpty(token))
            {
                DevLogger.Log("Auth", "RestoreSession: AccessToken 缺失，下次同步需 RefreshToken 续期或重新登录");
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", "RestoreSession: 读取 SecureStorage 失败: " + ex.Message);
        }

        return true;
    }

    // ===== 登出 =====

    /// <summary>
    /// 登出：清空 SecureStorage 的 Token + sync_config 的 CloudUserId。
    /// 不删除业务数据（Baby/Record/Milestone 等），用户可继续离线使用。
    /// 切换账号前需先调用此方法。
    /// </summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var cfg = _cfgRepo.Get();
        var cloudUserIdBeforeLogout = cfg.CloudUserId;
        var localUserId = cfg.LocalUserId;

        // 反迁移：把 CloudUserId 名下的业务数据迁移到 LocalUserId 名下，
        // 让用户登出后继续离线使用本地数据（否则 AppState.UserId 切回 LocalUserId 后查不到，
        // 首页会显示"未添加宝宝"）。与登录时的正向迁移对称，方法内部幂等。
        if (!string.IsNullOrEmpty(cloudUserIdBeforeLogout) &&
            !string.IsNullOrEmpty(localUserId) &&
            !string.Equals(cloudUserIdBeforeLogout, localUserId, StringComparison.Ordinal))
        {
            try
            {
                int affected = _cfgRepo.MigrateUserId(cloudUserIdBeforeLogout, localUserId);
                DevLogger.Log("Auth", $"Logout migration: cloud={cloudUserIdBeforeLogout} → local={localUserId}, affected={affected}");
            }
            catch (Exception migrateEx)
            {
                DevLogger.Log("Auth", "Logout migration failed (non-fatal): " + migrateEx.Message);
            }
        }

        // 记录上次 CloudUserId：兜底机制，下次启动 TryRestoreSessionAsync 若发现此字段非空，
        // 且 CloudUserId 仍空（离线模式），会再次尝试反迁移（处理本次反迁移失败/跳过的场景）。
        // 反迁移成功的情况下此字段下次启动时也会被清空（避免重复迁移）。
        if (!string.IsNullOrEmpty(cloudUserIdBeforeLogout))
        {
            _cfgRepo.UpdateLastCloudUserId(cloudUserIdBeforeLogout);
        }

        try
        {
            await _secureStorage.DeleteAsync(SecureStorageKeys.AccessToken, ct);
            await _secureStorage.DeleteAsync(SecureStorageKeys.RefreshToken, ct);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", "Logout: 清空 SecureStorage 失败（继续）: " + ex.Message);
        }

        _cfgRepo.UpdateCloudUserId(string.Empty);
        CurrentUser = null;
        _state.Clear();

        // 登出时取消所有本地提醒，避免切换账号后仍收到旧账号的喂奶/睡眠提醒
        try
        {
            var localNoti = ServiceProvider.Instance.LocalNotification;
            if (localNoti.IsSupported)
            {
                _ = localNoti.CancelAllAsync();
            }
        }
        catch { /* 提醒取消失败不影响登出 */ }

        DevLogger.Log("Auth", "Logout ok: token + cloud_user_id cleared");
    }

    // ===== Token 访问（供 BaseApiClient / ApiSyncService 使用） =====

    /// <summary>从 SecureStorage 读取 AccessToken（可能为空）。</summary>
    public Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
        => _secureStorage.GetAsync(SecureStorageKeys.AccessToken, ct);

    /// <summary>从 SecureStorage 读取 RefreshToken（可能为空）。</summary>
    public Task<string?> GetRefreshTokenAsync(CancellationToken ct = default)
        => _secureStorage.GetAsync(SecureStorageKeys.RefreshToken, ct);

    /// <summary>
    /// 用 RefreshToken 换取新的 AccessToken + RefreshToken（Rotation）。
    /// 旧 RefreshToken 在服务端已撤销；新 Token 写入 SecureStorage。
    /// 失败返回 null，调用方应提示用户重新邮箱登录（不删除业务数据）。
    /// </summary>
    public async Task<string?> RefreshAccessTokenAsync(CancellationToken ct = default)
    {
        var cfg = _cfgRepo.Get();
        var serverUrl = ResolveServerUrl(cfg.ServerUrl);
        if (serverUrl is null) return null;

        var refreshToken = await _secureStorage.GetAsync(SecureStorageKeys.RefreshToken, ct);
        if (string.IsNullOrEmpty(refreshToken))
        {
            DevLogger.Log("Auth", "Refresh: RefreshToken 缺失");
            return null;
        }

        var url = serverUrl.TrimEnd('/') + "/api/auth/refresh";
        var body = Serialize(new { refreshToken });
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            using var resp = await Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                DevLogger.Log("Auth", $"Refresh fail: {(int)resp.StatusCode}");
                return null;
            }
            var auth = ExtractData<AuthResponseDto>(json);
            if (auth is null || string.IsNullOrEmpty(auth.AccessToken))
            {
                DevLogger.Log("Auth", "Refresh fail: 响应缺少 data.accessToken");
                return null;
            }

            // Rotation：写入新的 Token 对
            await _secureStorage.SetAsync(SecureStorageKeys.AccessToken, auth.AccessToken, ct);
            await _secureStorage.SetAsync(SecureStorageKeys.RefreshToken, auth.RefreshToken, ct);

            // 若返回了新的 User（profile 更新），更新本地缓存
            if (auth.User is not null && !string.IsNullOrEmpty(auth.User.Id))
            {
                var user = ToAppUser(auth.User);
                _users.Upsert(user);
                CurrentUser = user;
                _state.User = user;
            }

            DevLogger.Log("Auth", "Refresh ok: new AccessToken + RefreshToken saved");
            return auth.AccessToken;
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", "Refresh exception: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 标记 AccessToken 已失效（401 触发）：删除 SecureStorage 中的 AccessToken。
    /// 不删除 RefreshToken（仍可尝试 Refresh）；若 Refresh 也失败，调用 LogoutAsync 不删业务数据。
    /// </summary>
    public async Task InvalidateAccessTokenAsync(CancellationToken ct = default)
    {
        try
        {
            await _secureStorage.DeleteAsync(SecureStorageKeys.AccessToken, ct);
            DevLogger.Log("Auth", "AccessToken invalidated (401)");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", "InvalidateAccessToken failed: " + ex.Message);
        }
    }

    // ===== 离线用户 Id =====

    /// <summary>
    /// 确保 sync_config.local_user_id 已生成（首次启动）。
    /// 未登录时作为本地业务数据的 user_id，永久不变。
    /// </summary>
    public string EnsureLocalUserId()
    {
        var cfg = _cfgRepo.Get();
        return EnsureLocalUserId(cfg);
    }

    private string EnsureLocalUserId(SyncConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.LocalUserId))
            return cfg.LocalUserId;

        var localId = Guid.NewGuid().ToString("N");
        // 直接写库（无 UpdateLocalUserId 方法，用 Save 全量更新）
        cfg.LocalUserId = localId;
        _cfgRepo.Save(cfg);
        DevLogger.Log("Auth", $"local_user_id generated: {localId}");
        return localId;
    }

    // ===== Profile 更新 =====

    /// <summary>更新本地 profile 缓存（昵称/头像/性别）。仅本地，不主动同步到后端。</summary>
    public void UpdateProfile(string nickName, string avatarUrl, int gender)
    {
        if (CurrentUser is null) return;
        CurrentUser.NickName = nickName;
        CurrentUser.AvatarUrl = avatarUrl;
        CurrentUser.Gender = gender;
        _users.Upsert(CurrentUser);
    }

    // ===== 辅助方法 =====

    private static string? ResolveServerUrl(string configured)
        => string.IsNullOrWhiteSpace(configured) ? ServerEndpoints.Primary : configured;

    private static string Serialize<T>(T obj) => JsonSerializer.Serialize(obj, JsonOpts);

    private static T? ExtractData<T>(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return default;
            return JsonSerializer.Deserialize<T>(data.GetRawText(), JsonOpts);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", "ExtractData parse fail: " + ex.Message);
            return default;
        }
    }

    private static string? ExtractMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("msg", out var msg) &&
                msg.ValueKind == JsonValueKind.String)
                return msg.GetString();
        }
        catch { }
        return null;
    }

    private static AppUser ToAppUser(LoginUserDto dto) => new()
    {
        Id = dto.Id,
        Email = dto.Email,
        NickName = dto.NickName,
        AvatarUrl = dto.AvatarUrl,
        Gender = dto.Gender,
        MembershipExpireAt = ParseIsoDate(dto.MembershipExpireAt),
        UpdatedAt = DateTime.UtcNow,
    };

    private static DateTime? ParseIsoDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt;
        return null;
    }

    // ===== DTO（与后端 ChildNotes.Core.Dtos 对齐，前端独立定义避免依赖后端程序集） =====

    private sealed class SendCodeDto
    {
        public bool Sent { get; set; }
    }

    private sealed class AuthResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public LoginUserDto? User { get; set; }
        public bool NewUser { get; set; }
    }
}

/// <summary>发送验证码结果。</summary>
public sealed class SendCodeResult
{
    public bool Success { get; }
    public string Message { get; }
    public SendCodeResult(bool success, string message) { Success = success; Message = message; }
}

/// <summary>验证验证码结果。</summary>
public sealed class VerifyCodeResult
{
    public bool Success { get; }
    public string Message { get; }
    public AppUser? User { get; }
    public bool NewUser { get; }
    public VerifyCodeResult(bool success, string message, AppUser? user = null, bool newUser = false)
    {
        Success = success; Message = message; User = user; NewUser = newUser;
    }
}

/// <summary>与后端 LoginUserDto 对齐的前端 DTO（仅用于反序列化 AuthResponse.User）。</summary>
public sealed class LoginUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NickName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public int Gender { get; set; }
    public string? MembershipExpireAt { get; set; }
    public bool IsMember { get; set; }
}
