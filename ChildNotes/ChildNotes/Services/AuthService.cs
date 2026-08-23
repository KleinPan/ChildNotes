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
    // 非 readonly：Android/iOS 平台启动时通过 UpdateSecureStorage 热替换（见 OverrideSecureStorage 注释）
    private ISecureStorage _secureStorage;

    // Refresh 串行化锁：多个调用方（同步/上传/推送/BaseApiClient）并发发现 AccessToken 过期时，
    // 若同时发 refresh，后到的请求会拿已被 Rotation 撤销的旧 RefreshToken → 服务端 401 → 误软登出。
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

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

    /// <summary>
    /// 运行时热替换安全存储实现（Android Keystore / iOS 注入时调用）。
    /// ★ 不重建 AuthService 实例：Android 启动时序中 App.OnFrameworkInitializationCompleted
    /// （含 TryRestoreSessionAsync，恢复 CurrentUser）在进程级 Application.OnCreate 中执行，
    /// 早于 MainActivity.OnCreate 的平台注入。若注入时重建 AuthService，已恢复的 CurrentUser
    /// 会随旧实例一起丢弃，导致"我的"页显示"已登录"占位但不显示账号信息
    /// （CurrentUser=null + CloudUserId 非空）。热替换保持实例稳定，任何时序下状态不丢。
    /// </summary>
    public void UpdateSecureStorage(ISecureStorage implementation)
    {
        _secureStorage = implementation;
        DevLogger.Log("DI", $"AuthService SecureStorage updated: {implementation.GetType().Name}");
    }

    // ===== 邮箱验证码流程 =====

    /// <summary>
    /// 待确认换绑的登录上下文（阶段 2，设计文档 7.1）：
    /// VerifyCodeAsync 检测到 last_bound_family ≠ 新家庭时暂存，等用户在确认框做出选择。
    /// CancelRebind 清空；ConfirmRebindAsync 消费。
    /// </summary>
    private AuthResponseDto? _pendingRebindAuth;

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

            // ===== 换绑（rebind）状态机（阶段 2，设计文档 7.1）=====
            // last_bound_family 非空 ≠ 新家庭 → 暂存 pending，返回 NeedsRebindConfirmation 由 UI 弹确认框；
            // 空（首绑/清数据后）或 == 新家庭（同家庭重登，最高频）→ 静默绑定，直接完成登录。
            var newFamilyId = auth.CurrentFamilyId ?? string.Empty;
            var lastBound = _cfgRepo.Get().LastBoundFamilyId;
            if (!string.IsNullOrEmpty(lastBound) && !string.IsNullOrEmpty(newFamilyId) &&
                !string.Equals(lastBound, newFamilyId, StringComparison.Ordinal))
            {
                _pendingRebindAuth = auth;
                DevLogger.Log("Auth", $"Rebind confirmation required: lastBound={lastBound}, newFamily={newFamilyId}");
                return new VerifyCodeResult(false, "需要换绑确认")
                {
                    NeedsRebindConfirmation = true,
                    PreviousFamilyId = lastBound,
                    FamilyId = newFamilyId,
                };
            }

            // Token 写入 SecureStorage（非明文）
            await _secureStorage.SetAsync(SecureStorageKeys.AccessToken, auth.AccessToken, ct);
            await _secureStorage.SetAsync(SecureStorageKeys.RefreshToken, auth.RefreshToken, ct);

            // 绑定身份 + 个人数据归并（同家庭重登/首绑：无 synced_at 清理，走常规增量）
            CompleteLogin(auth, rebind: false);

            DevLogger.Log("Auth", $"VerifyCode ok: email={trimmedEmail}, userId={auth.User?.Id}, newUser={auth.NewUser}");
            return new VerifyCodeResult(true, "登录成功", ToAppUser(auth.User!), auth.NewUser);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", "VerifyCode exception: " + ex.Message);
            return new VerifyCodeResult(false, "网络异常，请稍后重试");
        }
    }

    /// <summary>
    /// 用户在换绑确认框点"确认"后执行（阶段 2，设计文档 6.4）：
    /// 1. SyncTrigger 独占（暂停触发 + 等正在执行的同步完成）
    /// 2. rebind 事务：sync_config 四字段 + baby/child_record/milestone.synced_at 全清
    /// 3. Token 写入 + 个人数据归并 + CurrentUser 设置
    /// 之后由 UI 触发 RunNowAsync → Full Pull Only（LastSyncAt=NULL）。
    /// </summary>
    public async Task<VerifyCodeResult> ConfirmRebindAsync(CancellationToken ct = default)
    {
        var auth = _pendingRebindAuth;
        if (auth is null || string.IsNullOrEmpty(auth.User?.Id))
        {
            return new VerifyCodeResult(false, "没有待确认的换绑请求");
        }
        _pendingRebindAuth = null;

        try
        {
            var userId = auth.User.Id;
            var familyId = auth.CurrentFamilyId ?? string.Empty;

            // Token 先写（Keystore 非 SQLite，无法与 rebind 事务同事务；失败即中止，本地状态零改动）
            await _secureStorage.SetAsync(SecureStorageKeys.AccessToken, auth.AccessToken, ct);
            await _secureStorage.SetAsync(SecureStorageKeys.RefreshToken, auth.RefreshToken, ct);

            var syncTrigger = Infrastructure.ServiceProvider.Instance.SyncTrigger;
            await syncTrigger.ExecuteExclusiveDuringRebindAsync(() =>
                Task.Run(() => _cfgRepo.ExecuteRebind(userId, familyId)));

            // 个人数据归并（换账号遗留行清理 + 离线个人数据 → 账号名下，设计文档 6.5）
            CompleteLogin(auth, rebind: true);

            DevLogger.Log("Auth", $"ConfirmRebind ok: userId={userId}, family={familyId}");
            return new VerifyCodeResult(true, "换绑完成", ToAppUser(auth.User), auth.NewUser);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", "ConfirmRebind exception: " + ex.Message);
            return new VerifyCodeResult(false, "换绑失败，请重试");
        }
    }

    /// <summary>
    /// 用户在换绑确认框点"取消"：清空 pending，本地零改动
    /// （token 未写入、sync_config 未动、家庭业务数据可见性不变），回到登录页初始状态。
    /// </summary>
    public void CancelRebind()
    {
        if (_pendingRebindAuth is not null)
        {
            DevLogger.Log("Auth", "Rebind cancelled by user");
            _pendingRebindAuth = null;
        }
    }

    /// <summary>
    /// 登录完成后的公共落地（VerifyCodeAsync 静默路径 + ConfirmRebindAsync 共用）：
    /// 个人数据归并 + app_user 缓存 + CurrentUser/AppState 设置。
    /// rebind=true 时身份字段已由 ExecuteRebind 事务写入，此处只做归并与缓存。
    /// </summary>
    private void CompleteLogin(AuthResponseDto auth, bool rebind)
    {
        if (auth.User is null || string.IsNullOrEmpty(auth.User.Id)) return;

        if (!rebind)
        {
            // 静默路径：身份字段逐项写入（rebind 路径已在事务内写入）
            _cfgRepo.UpdateCloudUserId(auth.User.Id);
            if (!string.IsNullOrEmpty(auth.CurrentFamilyId))
            {
                _cfgRepo.UpdateCurrentFamilyId(auth.CurrentFamilyId);
                // last_bound_family_id 记录本数据空间最近绑定的家庭（换绑检测，7.1）
                _cfgRepo.UpdateLastBoundFamilyId(auth.CurrentFamilyId);
            }
        }

        // 个人数据归并（设计文档 6.5）：清理换账号遗留行 + 离线个人数据迁到 CloudUserId。
        // 失败不阻塞登录：家庭业务数据不受影响（恒为 LocalDataSpaceId 天然可见），下次登录重试。
        try
        {
            int affected = _cfgRepo.AdoptPersonalDataOnLogin(_cfgRepo.Get().LocalUserId, auth.User.Id);
            if (affected > 0)
                DevLogger.Log("Auth", $"Personal data adopted by cloud user: affected={affected}");
        }
        catch (Exception adoptEx)
        {
            DevLogger.Log("Auth", "AdoptPersonalDataOnLogin failed (non-fatal): " + adoptEx.Message);
        }

        // app_user 表缓存 profile（Upsert），供 UI 展示昵称/头像等
        var user = ToAppUser(auth.User);
        _users.Upsert(user);

        // 设置 CurrentUser 与 AppState
        CurrentUser = user;
        _state.User = user;
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

        // 确保 LocalUserId 已生成（本机数据空间 Id，首次启动）
        EnsureLocalUserId(cfg);

        // 一次性身份 fixup（阶段 1C）：把旧版本（User-centric 双向迁移时代）遗留的 user_id
        // 归位到 Family-centric 语义（家庭业务表恒为 LocalDataSpaceId）。
        // 幂等：identity_fixup_done 标志与数据同事务，崩溃可安全重跑；
        // 失败不阻塞启动（下次重试），家庭业务数据在新语义下按 LocalDataSpaceId 查询。
        if (cfg.IdentityFixupDone == 0)
        {
            try
            {
                int affected = _cfgRepo.RunIdentityFixup(
                    cfg.LocalUserId, cfg.CloudUserId, cfg.LastCloudUserId, cfg.CurrentFamilyId);
                DevLogger.Log("Auth", $"IdentityFixup executed: affected={affected}");
            }
            catch (Exception fixupEx)
            {
                DevLogger.Log("Auth", $"IdentityFixup failed (non-fatal, retry next launch): {fixupEx.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(cfg.CloudUserId))
        {
            DevLogger.Log("Auth", "RestoreSession: cloud_user_id 为空，离线模式");
            return false;
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
            // app_user 表无缓存（首次登录后 DB 重建等场景）：登录态以 sync_config.cloud_user_id
            // 为准（AppState.GetCloudUserId 兜底读取），profile 待下次登录/刷新时重建
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
    /// Family-centric（阶段 1C）：不迁移任何数据——家庭业务表 user_id 恒为
    /// LocalDataSpaceId（离线立即可见）；个人表 C 行原地保留，下次登录归并处理；
    /// last_bound_family_id 保留（换绑检测用，除清数据外永不清空）。
    /// </summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
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
    ///
    /// 并发安全：通过 _refreshLock 串行化，同一时刻只有一个 refresh 请求在飞。
    /// 并发调用方中第一个完成 Rotation；后续请求进锁后重读 RefreshToken 发现已变化，
    /// 直接复用新 AccessToken，不再发请求（旧 Token 已被撤销，重发必 401 → 误软登出）。
    /// </summary>
    public async Task<string?> RefreshAccessTokenAsync(CancellationToken ct = default)
    {
        var cfg = _cfgRepo.Get();
        var serverUrl = ResolveServerUrl(cfg.ServerUrl);
        if (serverUrl is null) return null;

        var refreshTokenOnEntry = await _secureStorage.GetAsync(SecureStorageKeys.RefreshToken, ct);
        if (string.IsNullOrEmpty(refreshTokenOnEntry))
        {
            DevLogger.Log("Auth", "Refresh: RefreshToken 缺失");
            return null;
        }

        try
        {
            await _refreshLock.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return null; // 等锁期间调用方取消，与"请求失败返回 null"语义一致
        }
        try
        {
            // 进锁后重读 RefreshToken：等待期间可能已被其他请求 Rotation 替换
            var refreshToken = await _secureStorage.GetAsync(SecureStorageKeys.RefreshToken, ct);
            if (string.IsNullOrEmpty(refreshToken))
            {
                DevLogger.Log("Auth", "Refresh: RefreshToken 缺失（等待锁期间被清除）");
                return null;
            }
            if (!string.Equals(refreshToken, refreshTokenOnEntry, StringComparison.Ordinal))
            {
                var existing = await _secureStorage.GetAsync(SecureStorageKeys.AccessToken, ct);
                if (!string.IsNullOrEmpty(existing))
                {
                    DevLogger.Log("Auth", "Refresh: 并发请求已完成 Rotation，复用现有 AccessToken");
                    return existing;
                }
                // RefreshToken 已换新但 AccessToken 缺失（异常中间态）：改用新 RefreshToken 继续刷新
                DevLogger.Log("Auth", "Refresh: RefreshToken 已变化但 AccessToken 缺失，改用新 Token 刷新");
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
                    // 401/403：服务端明确拒绝该 RefreshToken（已撤销/过期），
                    // 软登出避免 UI 卡在"已登录但同步失败"状态，引导用户重新登录。
                    if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    {
                        // 二次确认（防御层）：若存储中的 RefreshToken 已不是本次发送的值，
                        // 说明请求飞行期间被其他请求替换（锁路径下不该发生，防御绕过锁的调用方），
                        // 不软登出，复用新 AccessToken。
                        var latestRefresh = await _secureStorage.GetAsync(SecureStorageKeys.RefreshToken, ct);
                        if (!string.Equals(latestRefresh, refreshToken, StringComparison.Ordinal))
                        {
                            var existingAccess = await _secureStorage.GetAsync(SecureStorageKeys.AccessToken, ct);
                            if (!string.IsNullOrEmpty(existingAccess))
                            {
                                DevLogger.Log("Auth", "Refresh: 401 但 RefreshToken 已被并发替换，复用现有 AccessToken");
                                return existingAccess;
                            }
                        }
                        await SoftLogoutAsync(ct);
                    }
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
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// 标记 AccessToken 已失效（401 触发）：删除 SecureStorage 中的 AccessToken。
    /// 不删除 RefreshToken（仍可尝试 Refresh）；若 Refresh 也失败，由 SoftLogoutAsync 处理。
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

    /// <summary>
    /// 软登出：RefreshToken 被服务端拒绝（401/403）时调用。
    /// 清空 Token + CloudUserId，让 UI 显示"未登录"，引导用户重新登录。
    /// Family-centric（阶段 1C）：与 LogoutAsync 一致不迁移数据——家庭业务表 user_id
    /// 恒为 LocalDataSpaceId（离线立即可见），个人表 C 行原地保留待下次登录归并。
    /// 不取消本地提醒（下次登录大概率同账号，与 LogoutAsync 区别）。
    /// </summary>
    private async Task SoftLogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await _secureStorage.DeleteAsync(SecureStorageKeys.AccessToken, ct);
            await _secureStorage.DeleteAsync(SecureStorageKeys.RefreshToken, ct);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", "SoftLogout: 清空 SecureStorage 失败（继续）: " + ex.Message);
        }
        _cfgRepo.UpdateCloudUserId(string.Empty);
        CurrentUser = null;
        _state.Clear();
        DevLogger.Log("Auth", "SoftLogout ok: RefreshToken 被服务端拒绝，需重新登录");
    }

    // ===== 离线用户 Id =====

    /// <summary>
    /// 确保 sync_config.local_user_id 已生成（首次启动）。
    /// 未登录时作为本地业务数据的 user_id，永久不变（写入后冻结）。
    /// Family-centric（阶段 2）：ANDROID_ID 可用时派生（SHA256，同设备重装后数据归属连续），否则 GUID。
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

        var localId = DeviceIdentityDerivation.DeriveLocalDataSpaceId(
            DeviceIdentityProvider.Current?.GetAndroidId());
        // 直接写库（无 UpdateLocalUserId 方法，用 Save 全量更新）
        cfg.LocalUserId = localId;
        _cfgRepo.Save(cfg);
        DevLogger.Log("Auth", $"local_user_id generated: {localId} (derived={DeviceIdentityProvider.Current is not null})");
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
        /// <summary>当前绑定家庭 Id（Family-centric，服务端 FamilyService 解析；空/缺失兼容旧后端）。</summary>
        public string? CurrentFamilyId { get; set; }
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

    /// <summary>需要换绑确认（阶段 2，设计文档 7.1）：last_bound_family ≠ 登录账号当前家庭。
    /// true 时 UI 弹换绑确认框；用户确认 → AuthService.ConfirmRebindAsync，取消 → CancelRebind。</summary>
    public bool NeedsRebindConfirmation { get; init; }

    /// <summary>换绑场景：本数据空间原绑定家庭 Id（确认框文案用）。</summary>
    public string? PreviousFamilyId { get; init; }

    /// <summary>换绑场景：登录账号当前家庭 Id（确认框文案用）。</summary>
    public string? FamilyId { get; init; }

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
