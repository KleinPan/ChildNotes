using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChildNotes.Data;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Shared.Sync;
using Microsoft.Data.Sqlite;

namespace ChildNotes.Services;

/// <summary>
/// Avalonia 客户端在线同步服务：与后端 /api/sync/* 交互。
/// 策略：本地优先 + 后端同步。仅同步 baby + child_record。
/// 服务器地址由 <see cref="ServerEndpoints"/> 硬编码，用户无需配置。
/// Pull/Push 通过 <see cref="SyncPolicy"/> 重试，瞬时错误自动恢复。
/// 继承 BaseApiClient 复用 HttpClient / JsonOpts / SendWithTokenV2Async / ExtractData。
/// </summary>
public sealed class ApiSyncService : BaseApiClient
{
    private readonly SyncConfigRepository _cfgRepo;
    private readonly BabyRepository _babyRepo;
    private readonly RecordRepository _recordRepo;
    private readonly MilestoneRepository _milestoneRepo;
    private readonly PointsRepository _pointsRepo;
    private readonly Data.DbConnectionFactory? _dbFactory;
    /// <summary>家庭加入申请仓储（Pull-only）。null 兼容旧构造函数。</summary>
    private Data.Repositories.FamilyJoinRequestRepository? _joinRequestRepo;
    /// <summary>应用内消息服务（用于生成审批结果/新申请通知）。null 表示不生成通知。</summary>
    private Services.InAppMessageService? _inAppMessageService;
    /// <summary>当前用户状态（用于判断申请是本人提交还是他人提交）。null 表示不可用。</summary>
    private AppState? _appState;

    /// <summary>本次同步中收集的 join_request 状态变化（事务提交后用于生成通知）。</summary>
    private readonly List<(SyncFamilyJoinRequestItem item, string? oldStatus)> _pendingJoinNotifications = new();

    /// <summary>同步过程依赖的网络监测器（可选，由 ServiceProvider 注入）。</summary>
    public NetworkMonitor? NetworkMonitor { get; set; }

    public ApiSyncService(SyncConfigRepository cfgRepo, BabyRepository babyRepo, RecordRepository recordRepo,
        MilestoneRepository milestoneRepo, PointsRepository pointsRepo)
    {
        _cfgRepo = cfgRepo;
        _babyRepo = babyRepo;
        _recordRepo = recordRepo;
        _milestoneRepo = milestoneRepo;
        _pointsRepo = pointsRepo;
    }

    /// <summary>带 DbConnectionFactory 的构造函数，启用同步前备份能力。</summary>
    public ApiSyncService(SyncConfigRepository cfgRepo, BabyRepository babyRepo, RecordRepository recordRepo,
        MilestoneRepository milestoneRepo, PointsRepository pointsRepo, Data.DbConnectionFactory dbFactory)
        : this(cfgRepo, babyRepo, recordRepo, milestoneRepo, pointsRepo)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>注入家庭加入申请仓储与应用内消息服务（用于审批通知）。由 ServiceProvider 构造后调用。</summary>
    public void SetJoinRequestDeps(Data.Repositories.FamilyJoinRequestRepository joinRequestRepo,
        Services.InAppMessageService inAppMessageService, AppState appState)
    {
        _joinRequestRepo = joinRequestRepo;
        _inAppMessageService = inAppMessageService;
        _appState = appState;
    }

    /// <summary>指示当前是否正在同步中（避免重入）。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>同步结果。供 UI 展示。</summary>
    public sealed class SyncResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public int PulledBabies { get; init; }
        public int PulledRecords { get; init; }
        public int PulledMilestones { get; init; }
        public int PulledSignIns { get; init; }
        public int PushedBabies { get; init; }
        public int PushedRecords { get; init; }
        public int PushedMilestones { get; init; }
        public int PushedSignIns { get; init; }
        public DateTime DoneAt { get; init; }
        /// <summary>错误分类（失败时填充），供 UI 决定是否显示重试按钮。</summary>
        public SyncErrorKind? ErrorKind { get; init; }
        /// <summary>Pull 共拉取多少页（用于诊断大数据量同步）。</summary>
        public int PullPages { get; init; }
    }

    /// <summary>执行一次完整的双向同步：先 Pull 后 Push。</summary>
    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        if (IsRunning) return new SyncResult { Success = false, Message = "同步进行中，请稍候" };
        var cfg = _cfgRepo.Get();
        if (!cfg.Enabled) return new SyncResult { Success = false, Message = "同步未启用" };
        // v5：未登录（CloudUserId 为空）直接跳过同步，不强制登录。
        // 离线模式可永久使用本地 SQLite，登录的作用是开启云同步。
        if (string.IsNullOrWhiteSpace(cfg.CloudUserId))
            return new SyncResult { Success = false, Message = "未登录，请先邮箱验证码登录" };

        // 服务器地址从 sync_config 读取（用户可在数据同步页配置），为空时回退到默认地址
        var serverUrl = ServerEndpoints.Primary;

        // 网络监测器判定为本地无网时直接跳过，避免无谓请求
        if (NetworkMonitor?.Current == NetworkMonitor.State.OfflineLocal)
            return new SyncResult { Success = false, Message = "当前无网络连接，已自动切换至离线模式", ErrorKind = SyncErrorKind.Network };

        IsRunning = true;
        try
        {
            // 0. 同步前数据库快照备份（防极端损坏，如 Pull 把数据洗坏时可回滚）
            //    失败不阻塞同步：备份是保险措施，不应影响主流程
            if (_dbFactory is not null)
            {
                try
                {
                    var backupPath = _dbFactory.DbPath + ".bak";
                    _dbFactory.BackupTo(backupPath);
                    DevLogger.Log("Sync", $"DB backup created: {backupPath}");
                }
                catch (Exception ex)
                {
                    DevLogger.Log("Sync", "DB backup failed (non-fatal): " + ex.Message);
                }
            }

            // 1. 确保有可用 AccessToken，缺失则尝试 RefreshToken 续期（v5：不再用户名密码登录）
            var token = await EnsureTokenAsync(cfg, serverUrl, ct);
            // 诊断日志：记录 token 前 8 字符，方便排查"登录失效"问题（仅前缀，无安全风险；ReleaseLogger 自动脱敏）
            DevLogger.Log("Sync", $"Token acquired: {token?.Substring(0, Math.Min(8, token?.Length ?? 0))}...");
            if (token is null)
                return Finish(false, "登录已失效，请重新邮箱验证码登录", cfg, SyncErrorKind.Auth);

            // 2. Pull：以 last_sync_at 为起点分页拉取远端增量（带重试与切备用地址）
            //    大数据量首次同步时通过分页避免单次响应过大、避免中途失败丢失全部进度。
            //    所有页的 upsert 共享同一 SqliteConnection + Transaction，单次提交，避免每行开连。
            var since = cfg.LastSyncAt ?? DateTime.UnixEpoch;
            var isFirstLogin = cfg.LastSyncAt is null; // v5 规则(6)：首次登录以云端为准，只 Full Pull 不 Push
            DevLogger.Log("Sync", $"Pull since={since:O} (LastSyncAt={(cfg.LastSyncAt?.ToString("O") ?? "null")}, isFirstLogin={isFirstLogin})");
            // Family-centric（阶段 1C）Pull 身份注入（设计文档 6.2）：
            //   家庭业务表 → 本地 user_id 一律写 LocalDataSpaceId（幂等，登录态无关）
            //   个人表（积分/签到）→ 写当前 CloudUserId（Pull 仅在登录后执行，非空）
            var pullLocalId = cfg.LocalUserId;
            var pullCloudId = cfg.CloudUserId;
            int pulledBabies = 0, pulledRecords = 0, pulledMilestones = 0, pulledSignIns = 0, pullPages = 0;
            DateTime pullServerTime = DateTime.UtcNow; // 最后一页的 ServerTime，用于 Full Pull Only 的 LastSyncAt 基准
            SyncCursor? cursor = null; // null 表示第一页，用 since 过滤
            const int pageSize = 500;
            const int maxPages = 50; // 安全上限：50 页 * 500 = 25000 条，足够覆盖首次同步
            using (var pullConn = _dbFactory!.Create())
            using (var pullTx = pullConn.BeginTransaction())
            {
                while (pullPages < maxPages)
                {
                    var pageResp = await PullWithRetryAsync(serverUrl, token, since, pageSize, cursor, ct);
                    if (pageResp is null)
                    {
                        pullTx.Rollback();
                        return Finish(false, "拉取失败，已自动重试，请稍后再试", cfg, SyncErrorKind.Network);
                    }

                    foreach (var b in pageResp.Babies)
                        if (_babyRepo.UpsertFromSync(MapToBaby(b, pullLocalId), pullConn, pullTx)) pulledBabies++;
                    foreach (var r in pageResp.Records)
                        if (_recordRepo.UpsertFromSync(MapToRecord(r, pullLocalId), pullConn, pullTx)) pulledRecords++;
                    foreach (var m in pageResp.Milestones)
                        if (_milestoneRepo.UpsertFromSync(MapToMilestone(m, pullLocalId), pullConn, pullTx)) pulledMilestones++;
                    foreach (var s in pageResp.SignIns)
                        if (_pointsRepo.UpsertSignInFromSync(MapToSignIn(s, pullCloudId), pullConn, pullTx)) pulledSignIns++;
                    foreach (var bm in pageResp.BabyMembers)
                        _babyRepo.UpsertMemberFromSync(bm, pullConn, pullTx);

                    // 加入申请：写入前先记录旧状态（用于状态变化生成通知），再 LWW 合并
                    if (_joinRequestRepo is not null && pageResp.FamilyJoinRequests.Count > 0)
                    {
                        foreach (var jr in pageResp.FamilyJoinRequests)
                        {
                            string? oldStatus = _joinRequestRepo.FindById(jr.Id)?.Status;
                            _joinRequestRepo.UpsertFromSync(jr, pullConn, pullTx);
                            _pendingJoinNotifications.Add((jr, oldStatus));
                        }
                    }

                    // 积分余额：每页都带，以最后一页为准（已存在则 LWW 覆盖）
                    if (pageResp.UserPoints is not null)
                        _pointsRepo.UpsertUserPointsFromSync(MapToUserPoints(pageResp.UserPoints, pullCloudId), pullConn, pullTx);

                    pullServerTime = pageResp.ServerTime; // 每页都更新，最终为最后一页的 ServerTime
                    pullPages++;
                    DevLogger.Log("Sync",
                        $"Pull page {pullPages}: babies={pageResp.Babies.Count}, records={pageResp.Records.Count}, milestones={pageResp.Milestones.Count}, signIns={pageResp.SignIns.Count}, babyMembers={pageResp.BabyMembers.Count}, joinRequests={pageResp.FamilyJoinRequests.Count}, hasMore={pageResp.HasMore}");

                    // HasMore 为 false 或六类都无数据时终止；游标推进到 NextCursor
                    if (!pageResp.HasMore || (pageResp.Babies.Count == 0 && pageResp.Records.Count == 0 && pageResp.Milestones.Count == 0 && pageResp.SignIns.Count == 0 && pageResp.BabyMembers.Count == 0 && pageResp.FamilyJoinRequests.Count == 0))
                        break;
                    cursor = pageResp.NextCursor;
                    if (cursor is null) break; // 无游标但 HasMore=true 的防御性退出
                }
                pullTx.Commit();
            }

            // 2.1 处理 join_request 状态变化，生成本地 InAppMessage 通知
            //     事务已提交，本地仓储可读到最新状态；通知仅生成一次
            ProcessJoinRequestNotifications();

            // v5 规则(6)：首次正式登录以云端为准，只 Full Pull 不 Push。
            //   首次登录时本地 SQLite 为空（新装 App），Push 无意义且可能引入不必要行为。
            //   LastSyncAt 用 Pull 最后一页的 ServerTime 作为基准，后续正常同步走 Pull→Merge→Push。
            if (isFirstLogin)
            {
                cfg.LastSyncAt = pullServerTime;
                cfg.LastSyncStatus = "ok";
                cfg.LastSyncMsg = $"首次同步：拉取 {pulledBabies}宝/{pulledRecords}条/{pulledMilestones}里程碑/{pulledSignIns}签到（Full Pull Only）";
                _cfgRepo.Save(cfg);
                NetworkMonitor?.ProbeNow();
                DevLogger.Log("Sync", $"First login full pull done: LastSyncAt={pullServerTime:O}");
                return new SyncResult
                {
                    Success = true,
                    Message = cfg.LastSyncMsg!,
                    PulledBabies = pulledBabies,
                    PulledRecords = pulledRecords,
                    PulledMilestones = pulledMilestones,
                    PulledSignIns = pulledSignIns,
                };
            }

            // 3. Push：把本地 updated_at > since 的数据上送（带重试与切备用地址）
            //     注：使用 pushResp.ServerTime 作为新的 last_sync_at 基准，
            //     避免本地时钟与服务器不一致导致漏推/重推。
            //     积分余额不上送（Pull-only，服务端为准）；签到记录按 CreatedAt 增量上送。
            var pushSince = since;
            var localBabies = _babyRepo.GetByUpdatedAt(pushSince);
            var localRecords = _recordRepo.GetByUpdatedAt(pushSince);
            var localMilestones = _milestoneRepo.GetByUpdatedAt(pushSince);
            var localSignIns = _pointsRepo.GetSignInsByCreatedAt(pushSince);
            DevLogger.Log("Sync",
                $"Push prepare: babies={localBabies.Count}, records={localRecords.Count}, milestones={localMilestones.Count}, signIns={localSignIns.Count} (pushSince={pushSince:O})");

            // Family-centric（阶段 1B）：身份注入点 —— 协议项的 UserId/FamilyId 一律来自登录态
            // （CloudUserId / sync_config.current_family_id），禁止读本地业务表的 user_id
            // （该列语义已降级为 LocalDataSpaceId，服务端以 JWT 鉴权为准，payload 仅作路由/日志）。
            var cloudUid = cfg.CloudUserId;
            var familyId = cfg.CurrentFamilyId;
            var pushReq = new SyncBatchRequest
            {
                Babies = localBabies.Select(b => MapToBabyItem(b, cloudUid, familyId)).ToList(),
                Records = localRecords.Select(r => MapToRecordItem(r, cloudUid, familyId)).ToList(),
                Milestones = localMilestones.Select(m => MapToMilestoneItem(m, cloudUid, familyId)).ToList(),
                SignIns = localSignIns.Select(s => MapToSignInItem(s, cloudUid)).ToList(),
            };
            var pushResp = await PushWithRetryAsync(serverUrl, token, pushReq, ct);
            if (pushResp is null)
                return Finish(false, "推送失败，已自动重试，请稍后再试", cfg, SyncErrorKind.Network);

            // 4. 标记已成功上送的数据（更新 synced_at），防止崩溃导致重推
            //    仅当 upserted + skippedForeign == count 时才对该类调用 MarkSynced；否则不 MarkSynced，
            //    让下次同步重试（后端 LWW 幂等跳过），避免"假同步"：推送 0 条却标记已同步。
            //    skippedForeign（跨家庭 terminal skip）视为终态：曾同步到其他家庭的数据永久留本机，
            //    记冲突日志后随全批 MarkSynced，防止无限重推（见设计文档 6.3）。
            //    整体仍视为成功（更新 LastSyncAt），但 LastSyncMsg 加"部分丢弃"提示（排除 foreign 行）。
            var babyForeign = pushResp.SkippedForeignBabyIds.Count;
            var recordForeign = pushResp.SkippedForeignRecordIds.Count;
            var milestoneForeign = pushResp.SkippedForeignMilestoneIds.Count;
            if (babyForeign + recordForeign + milestoneForeign > 0)
            {
                DevLogger.Log("Sync", $"Push foreign-skipped (terminal): babies={babyForeign} [{string.Join(",", pushResp.SkippedForeignBabyIds)}], records={recordForeign} [{string.Join(",", pushResp.SkippedForeignRecordIds)}], milestones={milestoneForeign} [{string.Join(",", pushResp.SkippedForeignMilestoneIds)}]");
            }
            var babyDropped = localBabies.Count > 0 && pushResp.BabiesUpserted + babyForeign < localBabies.Count;
            var recordDropped = localRecords.Count > 0 && pushResp.RecordsUpserted + recordForeign < localRecords.Count;
            var milestoneDropped = localMilestones.Count > 0 && pushResp.MilestonesUpserted + milestoneForeign < localMilestones.Count;
            if (babyDropped || recordDropped || milestoneDropped)
            {
                DevLogger.Log("Sync", $"Push partial drop: babies {pushResp.BabiesUpserted}+{babyForeign}f/{localBabies.Count}, records {pushResp.RecordsUpserted}+{recordForeign}f/{localRecords.Count}, milestones {pushResp.MilestonesUpserted}+{milestoneForeign}f/{localMilestones.Count}");
            }
            try
            {
                if (!babyDropped)
                    _babyRepo.MarkSynced(localBabies.Select(b => b.Id), pushResp.ServerTime);
                if (!recordDropped)
                    _recordRepo.MarkSynced(localRecords.Select(r => r.Id), pushResp.ServerTime);
                if (!milestoneDropped)
                    _milestoneRepo.MarkSynced(localMilestones.Select(m => m.Id), pushResp.ServerTime);
            }
            catch (Exception ex)
            {
                // MarkSynced 失败不影响同步整体成功，最坏情况是下次重推（服务端 LWW 会幂等跳过）
                DevLogger.Log("Sync", "MarkSynced failed (non-fatal): " + ex.Message);
            }

            // 5. 更新本地同步时间戳
            cfg.LastSyncAt = pushResp.ServerTime;
            cfg.LastSyncStatus = "ok";
            var partialHint = (babyDropped || recordDropped || milestoneDropped) ? "（部分丢弃，下次重试）" : "";
            // 跨家庭 terminal skip 是既定语义（换绑后历史数据留本机），如实提示但不告警为失败
            var foreignHint = (babyForeign + recordForeign + milestoneForeign) > 0
                ? $"，另有 {babyForeign + recordForeign + milestoneForeign} 条其他家庭的历史数据已保留在本机" : "";
            cfg.LastSyncMsg = $"拉取 {pulledBabies}宝/{pulledRecords}条/{pulledMilestones}里程碑/{pulledSignIns}签到；推送 {pushResp.BabiesUpserted}宝/{pushResp.RecordsUpserted}条/{pushResp.MilestonesUpserted}里程碑/{pushResp.SignInsUpserted}签到{foreignHint}{partialHint}";
            _cfgRepo.Save(cfg);

            // 6. 通知网络监测器本次成功，加速从 OfflineServer 恢复
            NetworkMonitor?.ProbeNow();

            return new SyncResult
            {
                Success = true,
                Message = cfg.LastSyncMsg!,
                PulledBabies = pulledBabies,
                PulledRecords = pulledRecords,
                PulledMilestones = pulledMilestones,
                PulledSignIns = pulledSignIns,
                PushedBabies = pushResp.BabiesUpserted,
                PushedRecords = pushResp.RecordsUpserted,
                PushedMilestones = pushResp.MilestonesUpserted,
                PushedSignIns = pushResp.SignInsUpserted,
                DoneAt = DateTime.Now,
                PullPages = pullPages,
            };
        }
        catch (OperationCanceledException)
        {
            return Finish(false, "同步已取消", _cfgRepo.Get(), null);
        }
        catch (SyncException ex)
        {
            // 重试用尽仍失败：通知监测器探活，加速状态判定
            NetworkMonitor?.ProbeNow();
            ReleaseLogger.Warn("Sync", ex, "Sync failed (retries exhausted)");
            return Finish(false, "同步失败：" + ex.Message, _cfgRepo.Get(), ex.Kind);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Sync", ex);
            ReleaseLogger.Error("Sync", ex, "Sync unexpected error");
            return Finish(false, "同步异常：" + ex.Message, _cfgRepo.Get(), SyncErrorKind.Unknown);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private SyncResult Finish(bool ok, string msg, SyncConfig cfg, SyncErrorKind? errKind, DateTime? syncAt = null)
    {
        _cfgRepo.UpdateSyncResult(syncAt ?? DateTime.Now, ok ? "ok" : "fail", msg);
        return new SyncResult { Success = ok, Message = msg, DoneAt = DateTime.Now, ErrorKind = errKind };
    }

    /// <summary>
    /// 处理本次同步中收集的 join_request 状态变化，生成对应的本地 InAppMessage 通知。
    /// 必须在 Pull 事务提交后调用，避免事务回滚导致通知与本地状态不一致。
    /// 通知规则：
    /// - 新申请（old=null/new=pending）：通知宝宝 owner（当 owner 是当前用户）
    /// - 申请通过（old=pending/new=approved）：通知申请人（当申请人是当前用户）
    /// - 申请被拒（old=pending/new=rejected）：通知申请人（当申请人是当前用户）
    /// 注：同步协议项只含 BabyId/ApplicantUserId 等关键字段，不含名称；
    /// 通知 Body 显示 BabyId 简写（前 8 位），用户可点开家人管理页查看详情。
    /// </summary>
    private void ProcessJoinRequestNotifications()
    {
        // Family-centric（阶段 1C）：云端 uid 匹配用 CloudUserId（云端成员身份），与本地数据空间无关
        if (_inAppMessageService is null || _appState?.GetCloudUserId() is not string myUid)
        {
            _pendingJoinNotifications.Clear();
            return;
        }

        try
        {
            foreach (var (item, oldStatus) in _pendingJoinNotifications)
            {
                var newStatus = item.Status;
                var isApplicant = item.ApplicantUserId == myUid;
                var babyIdShort = item.BabyId.Length > 8 ? item.BabyId.Substring(0, 8) : item.BabyId;

                // 规则1：新申请通知 owner（当前用户不是申请人时，可能是该宝宝 owner）
                if (oldStatus is null && newStatus == "pending" && !isApplicant)
                {
                    _inAppMessageService.Insert(new InAppMessage
                    {
                        UserId = myUid,
                        Title = "新的家庭加入申请",
                        Body = $"有新用户申请加入宝宝（ID: {babyIdShort}…）",
                        Category = "family_join_request_new",
                        DataJson = $"{{\"requestId\":\"{item.Id}\",\"babyId\":\"{item.BabyId}\"}}",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow.ToString("O"),
                    });
                }
                // 规则2：申请通过通知申请人；同时重置 LastSyncAt 强制下次全量同步，
                // 让新成员能拉到加入家庭前的历史记录（baby/records/milestones）。
                // 根因：新成员在加入前可能已同步过，LastSyncAt 晚于历史记录的 updated_at，
                // 增量同步的 since > updated_at 过滤条件会把历史数据全过滤掉。
                else if (oldStatus == "pending" && newStatus == "approved" && isApplicant)
                {
                    _cfgRepo.ResetLastSyncAt();
                    DevLogger.Log("Sync", $"JoinRequest approved, reset LastSyncAt for full pull (baby={babyIdShort})");
                    _inAppMessageService.Insert(new InAppMessage
                    {
                        UserId = myUid,
                        Title = "加入申请已通过",
                        Body = $"你加入宝宝（ID: {babyIdShort}…）的申请已被通过",
                        Category = "family_join_request_approved",
                        DataJson = $"{{\"requestId\":\"{item.Id}\",\"babyId\":\"{item.BabyId}\"}}",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow.ToString("O"),
                    });
                }
                // 规则3：申请被拒通知申请人
                else if (oldStatus == "pending" && newStatus == "rejected" && isApplicant)
                {
                    _inAppMessageService.Insert(new InAppMessage
                    {
                        UserId = myUid,
                        Title = "加入申请被拒绝",
                        Body = $"你加入宝宝（ID: {babyIdShort}…）的申请被拒绝",
                        Category = "family_join_request_rejected",
                        DataJson = $"{{\"requestId\":\"{item.Id}\",\"babyId\":\"{item.BabyId}\"}}",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow.ToString("O"),
                    });
                }
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("Sync", "ProcessJoinRequestNotifications failed (non-fatal): " + ex.Message);
        }
        finally
        {
            _pendingJoinNotifications.Clear();
        }
    }

    private async Task<string?> EnsureTokenAsync(SyncConfig cfg, string serverUrl, CancellationToken ct)
    {
        // v5 重构：登录态由 CloudUserId 标识，Token 从 SecureStorage 读取。
        // SyncAsync 入口已检查 CloudUserId 非空，这里只负责获取可用 AccessToken。
        var auth = ServiceProvider.Instance.AuthService;
        var token = await auth.GetAccessTokenAsync(ct);
        if (!string.IsNullOrWhiteSpace(token))
        {
            // 检查 JWT exp，过期则主动 refresh（避免一次无谓的 401 往返）。
            // 解码失败（非 JWT 格式）不拦截，让 401 反推处理。
            if (!IsJwtExpired(token))
            {
                return token;
            }
            DevLogger.Log("Sync", "EnsureToken: AccessToken JWT exp 已过期，主动 refresh");
        }

        // AccessToken 缺失或过期：尝试用 RefreshToken 续期（Rotation）
        var refreshed = await auth.RefreshAccessTokenAsync(ct);
        if (!string.IsNullOrEmpty(refreshed))
        {
            DevLogger.Log("Sync", "EnsureToken: AccessToken refreshed");
            return refreshed;
        }

        // RefreshToken 也失效：停止同步，但保留 CloudUserId 和所有 SQLite 业务数据。
        // 用户需在 UI 上重新邮箱登录（不删除业务数据，登录后可继续同步）。
        DevLogger.Log("Sync", "EnsureToken: AccessToken 和 RefreshToken 均失效，需重新登录");
        return null;
    }

    /// <summary>
    /// 轻量 JWT exp 解码：手写 Base64Url 解码 + 字符串查找 exp claim。
    /// 不引入 JWT 库，兼容 AOT/Trimming。解析失败返回 false（不拦截，让 401 处理）。
    /// </summary>
    private static bool IsJwtExpired(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return false;
            var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            // 简单查找 "exp":1234567890（Unix 秒）
            var key = "\"exp\"";
            var idx = payload.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return false;
            idx += key.Length;
            while (idx < payload.Length && (payload[idx] == ':' || payload[idx] == ' ')) idx++;
            var start = idx;
            while (idx < payload.Length && char.IsDigit(payload[idx])) idx++;
            if (idx <= start) return false;
            var exp = long.Parse(payload.AsSpan(start, idx - start));
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return now >= exp;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var sb = new StringBuilder(s);
        sb.Replace('-', '+').Replace('_', '/');
        switch (sb.Length % 4)
        {
            case 2: sb.Append("=="); break;
            case 3: sb.Append("="); break;
        }
        return Convert.FromBase64String(sb.ToString());
    }

    private async Task<SyncPullResponse?> PullWithRetryAsync(string serverUrl, string token, DateTime since, int limit, SyncCursor? cursor, CancellationToken ct)
    {
        try
        {
            return await SyncPolicy.ExecuteAsync(
                async (attempt, server) =>
                {
                    var path = "/api/sync/pull?since=" + Uri.EscapeDataString(since.ToUniversalTime().ToString("O"))
                               + "&limit=" + limit;
                    if (cursor is not null)
                        path += "&cursorTime=" + Uri.EscapeDataString(cursor.Timestamp.ToUniversalTime().ToString("O"))
                             + "&cursorId=" + Uri.EscapeDataString(cursor.Id);
                    using var resp = await SendWithTokenV2Async(server, token, HttpMethod.Get, path, null, ct);
                    return await ReadDataAsync<SyncPullResponse>(resp, ct)
                        ?? throw new SyncException(SyncErrorKind.Business, "Pull 响应解析失败");
                },
                serverUrl, ct);
        }
        catch (SyncException ex)
        {
            // v5：Auth 错误（401）已由 SendWithTokenV2Async 清空 AccessToken；
            // 尝试用 RefreshToken 续期后重试一次，仍失败则停止同步（保留业务数据）。
            if (ex.Kind == SyncErrorKind.Auth)
            {
                var auth = ServiceProvider.Instance.AuthService;
                var newToken = await auth.RefreshAccessTokenAsync(ct);
                if (!string.IsNullOrEmpty(newToken))
                {
                    // 递归一次（新 token 已写入 SecureStorage，不会再触发 Auth 重试分支）
                    return await PullWithRetryAsync(serverUrl, newToken, since, limit, cursor, ct);
                }
                DevLogger.Log("Sync", "Pull Auth 失败且 Refresh 失败，停止同步");
                return null;
            }
            DevLogger.Log("Sync", $"Pull failed: {ex.Kind} {ex.Message}");
            return null;
        }
    }

    private async Task<SyncBatchResponse?> PushWithRetryAsync(string serverUrl, string token, SyncBatchRequest req, CancellationToken ct)
    {
        try
        {
            return await SyncPolicy.ExecuteAsync(
                async (attempt, server) =>
                {
                    var body = Serialize(req);
                    using var resp = await SendWithTokenV2Async(server, token, HttpMethod.Post, "/api/sync/push", body, ct);
                    return await ReadDataAsync<SyncBatchResponse>(resp, ct)
                        ?? throw new SyncException(SyncErrorKind.Business, "Push 响应解析失败");
                },
                serverUrl, ct);
        }
        catch (SyncException ex)
        {
            // v5：与 Pull 对称，Auth 错误（401）尝试 RefreshToken 续期后重试一次；
            // 旧实现直接吞掉异常导致"登录失效"被误报为"推送失败"，且无法自愈。
            if (ex.Kind == SyncErrorKind.Auth)
            {
                DevLogger.Log("Sync", "Push 401, refreshing token...");
                var auth = ServiceProvider.Instance.AuthService;
                var newToken = await auth.RefreshAccessTokenAsync(ct);
                if (!string.IsNullOrEmpty(newToken))
                {
                    // 递归一次（新 token 已写入 SecureStorage，不会再触发 Auth 重试分支）
                    return await PushWithRetryAsync(serverUrl, newToken, req, ct);
                }
                DevLogger.Log("Sync", "Push Auth 失败且 Refresh 失败，停止同步");
                return null;
            }
            DevLogger.Log("Sync", $"Push failed: {ex.Kind} {ex.Message}");
            return null;
        }
    }

    // ===== 映射方法：本地实体 ↔ 共享同步 DTO（ChildNotes.Shared.Sync）=====

    /// <summary>
    /// Pull 映射（家庭业务表）：本地 UserId 一律写 LocalDataSpaceId（设计文档 6.2，幂等），
    /// 禁止透传云端 UserId（家庭数据本地可见性与登录态无关）。
    /// </summary>
    private static Baby MapToBaby(SyncBabyItem i, string localDataSpaceId) => new()
    {
        Id = i.Id, UserId = localDataSpaceId, Name = i.Name, Avatar = i.Avatar ?? "",
        Gender = i.Gender ?? "", BirthDate = i.BirthDate,
        // 服务器时间约定为 UTC，转 Local 与本地库读取行为一致；BirthDate 是纯日期原样保留
        CreatedAt = ToLocal(i.CreatedAt), UpdatedAt = ToLocal(i.UpdatedAt),
    };

    /// <summary>Pull 映射（家庭业务表），本地 UserId 写 LocalDataSpaceId（规则同 MapToBaby）。</summary>
    private static ChildRecord MapToRecord(SyncRecordItem i, string localDataSpaceId) => new()
    {
        Id = i.Id, UserId = localDataSpaceId, BabyId = i.BabyId,
        RecordType = i.RecordType, RecordSubType = i.RecordSubType,
        // 服务器传来的时间约定为 UTC（后端 SyncService 用 SpecifyKind(..., Utc) 标记）。
        // 但 DTO 用 DateTime 传输、JSON 反序列化后 Kind=Unspecified。这里显式转 Local，
        // 与 RecordRepository.Map 读本地库的行为一致，使应用层统一感知本地时间。
        // 写库时 AddUtc 会再次转回 UTC（幂等）。
        RecordDate = i.RecordDate,
        RecordTime = ToLocal(i.RecordTime),
        AmountMl = i.AmountMl, DurationSec = i.DurationSec,
        LeftDurationSec = i.LeftDurationSec, RightDurationSec = i.RightDurationSec,
        AbnormalFlag = i.AbnormalFlag, TemperatureValue = i.TemperatureValue,
        HeightCm = i.HeightCm, WeightKg = i.WeightKg,
        PayloadJson = i.PayloadJson ?? "{}", Deleted = i.Deleted,
        CreatedAt = ToLocal(i.CreatedAt), UpdatedAt = ToLocal(i.UpdatedAt),
    };

    /// <summary>
    /// 把同步 DTO 反序列化后的 DateTime 视为 UTC 并转 Local。
    /// 反序列化时 Kind 通常为 Unspecified（JSON 无时区信息时）或 Utc（带 Z 时），
    /// 二者都先 SpecifyKind(Utc) 再 ToLocal，保证应用层始终拿到本地时间。
    /// </summary>
    private static DateTime ToLocal(DateTime dt)
        => (dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToLocalTime();

    /// <summary>把应用层的本地时间转回 UTC，用于上送服务器。
    /// 同时截断到微秒精度：存量数据（本次修复前写入）仍带 100ns 余数，
    /// 若原样上送，服务端 PostgreSQL 截断后与存储值相等，LWW 严格大于判断失败被跳过。</summary>
    private static DateTime ToUtc(DateTime dt)
        => (dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime()).TruncateToMicroseconds();

    /// <summary>
    /// Push 映射（家庭业务表）：UserId 注入当前 CloudUserId（创建者归因），FamilyId 注入当前绑定家庭。
    /// 禁止读本地实体 user_id（语义已降级为 LocalDataSpaceId）；服务端以 JWT 鉴权为准，payload 仅路由/日志。
    /// </summary>
    private static SyncBabyItem MapToBabyItem(Baby b, string cloudUserId, string familyId) => new()
    {
        Id = b.Id, UserId = cloudUserId, FamilyId = familyId, Name = b.Name, Avatar = b.Avatar ?? "",
        Gender = b.Gender ?? "", BirthDate = b.BirthDate,
        // 应用层时间已是 Local，上送服务器需转 UTC；BirthDate 是纯日期原样上送
        CreatedAt = ToUtc(b.CreatedAt), UpdatedAt = ToUtc(b.UpdatedAt),
    };

    /// <summary>Push 映射（家庭业务表），身份注入规则同 <see cref="MapToBabyItem"/>。</summary>
    private static SyncRecordItem MapToRecordItem(ChildRecord r, string cloudUserId, string familyId) => new()
    {
        Id = r.Id, UserId = cloudUserId, FamilyId = familyId, BabyId = r.BabyId,
        RecordType = r.RecordType, RecordSubType = r.RecordSubType,
        // 应用层 RecordTime/CreatedAt/UpdatedAt 已是 Local（RecordRepository.Map 转换过）。
        // 服务器期望 UTC，这里显式转回。RecordDate 是纯日期无时区，原样上送。
        RecordDate = r.RecordDate,
        RecordTime = ToUtc(r.RecordTime),
        AmountMl = r.AmountMl, DurationSec = r.DurationSec,
        LeftDurationSec = r.LeftDurationSec, RightDurationSec = r.RightDurationSec,
        AbnormalFlag = r.AbnormalFlag, TemperatureValue = r.TemperatureValue,
        HeightCm = r.HeightCm, WeightKg = r.WeightKg,
        PayloadJson = r.PayloadJson ?? "{}", Deleted = r.Deleted,
        CreatedAt = ToUtc(r.CreatedAt), UpdatedAt = ToUtc(r.UpdatedAt),
    };

    /// <summary>Pull 映射（家庭业务表），本地 UserId 写 LocalDataSpaceId（规则同 MapToBaby）。</summary>
    private static Milestone MapToMilestone(SyncMilestoneItem i, string localDataSpaceId) => new()
    {
        Id = i.Id, UserId = localDataSpaceId, BabyId = i.BabyId,
        Title = i.Title, Content = i.Content,
        // RecordDate 是纯日期，原样保留；CreatedAt/UpdatedAt 服务器传 UTC，转 Local
        RecordDate = i.RecordDate,
        PhotosJson = string.IsNullOrEmpty(i.PhotosJson) ? "[]" : i.PhotosJson,
        Deleted = i.Deleted,
        CreatedAt = ToLocal(i.CreatedAt), UpdatedAt = ToLocal(i.UpdatedAt),
    };

    /// <summary>Push 映射（家庭业务表）：UserId 注入当前 CloudUserId（创建者透传），FamilyId 注入当前绑定家庭。</summary>
    private static SyncMilestoneItem MapToMilestoneItem(Milestone m, string cloudUserId, string familyId) => new()
    {
        Id = m.Id, UserId = cloudUserId, FamilyId = familyId, BabyId = m.BabyId,
        Title = m.Title, Content = m.Content,
        RecordDate = m.RecordDate,
        PhotosJson = m.PhotosJson ?? "[]",
        Deleted = m.Deleted,
        // 应用层 Local 时间上送服务器转 UTC
        CreatedAt = ToUtc(m.CreatedAt), UpdatedAt = ToUtc(m.UpdatedAt),
    };

    /// <summary>Pull 映射（个人数据）：签到 UserId 写当前 CloudUserId（设计文档 6.2）。</summary>
    private static SignInRecord MapToSignIn(SyncSignInItem i, string cloudUserId) => new()
    {
        Id = i.Id, UserId = cloudUserId,
        SignDate = i.SignDate,
        ContinuousDays = i.ContinuousDays,
        Reward = i.Reward,
        CreatedAt = ToLocal(i.CreatedAt),
    };

    /// <summary>
    /// Push 映射（个人数据）：签到 UserId 注入当前 CloudUserId（不随家庭切换）。
    /// 离线期间以 LocalUserId 创建的签到，登录后按此归因到账号（服务端校验 item.UserId == JWT uid）。
    /// </summary>
    private static SyncSignInItem MapToSignInItem(SignInRecord s, string cloudUserId) => new()
    {
        Id = s.Id, UserId = cloudUserId,
        SignDate = s.SignDate,
        ContinuousDays = s.ContinuousDays,
        Reward = s.Reward,
        CreatedAt = ToUtc(s.CreatedAt),
    };

    /// <summary>Pull 映射（个人数据）：积分余额 UserId 写当前 CloudUserId（服务端为准覆盖本地）。</summary>
    private static UserPoints MapToUserPoints(SyncUserPointsItem i, string cloudUserId) => new()
    {
        Id = i.Id, UserId = cloudUserId,
        Points = i.Points, TotalEarned = i.TotalEarned, TotalSpent = i.TotalSpent,
        // user_points 本地无独立 CreatedAt 同步，用 UpdatedAt 近似（仅 LWW 判定用）
        CreatedAt = ToLocal(i.UpdatedAt), UpdatedAt = ToLocal(i.UpdatedAt),
    };
}
