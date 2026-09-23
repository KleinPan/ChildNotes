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
    private readonly Data.DbConnectionFactory _dbFactory;
    /// <summary>家庭加入申请仓储（Pull-only）。null 兼容旧构造函数。</summary>
    private Data.Repositories.FamilyJoinRequestRepository? _joinRequestRepo;
    /// <summary>应用内消息服务（用于生成审批结果/新申请通知）。null 表示不生成通知。</summary>
    private Services.InAppMessageService? _inAppMessageService;
    /// <summary>当前用户状态（用于判断申请是本人提交还是他人提交）。null 表示不可用。</summary>
    private AppState? _appState;

    /// <summary>本次同步中收集的 join_request 状态变化（事务提交后用于生成通知）。</summary>
    private readonly List<(SyncFamilyJoinRequestItem item, string? oldStatus)> _pendingJoinNotifications = new();

    /// <summary>
    /// 本次同步中是否发生过"申请通过 → ResetLastSyncAt"事件（毒丸修复 #6）。
    /// ResetLastSyncAt 在 Pull 后立即置 DB 的 last_sync_at=NULL，但 Push 成功后第 5 步
    /// Save(cfg) 会用同步开始时的内存快照（LastSyncAt=pushServerTime）把 NULL 覆盖回去，
    /// 全量拉取失效、新成员漏拉加入前历史。因此记录标记，第 5 步 Save 之后再断言一次 NULL。
    /// </summary>
    private bool _joinApprovedFullPullReset;

    /// <summary>同步过程依赖的网络监测器（可选，由 ServiceProvider 注入）。</summary>
    public NetworkMonitor? NetworkMonitor { get; set; }

    /// <summary>
    /// 带 DbConnectionFactory 的构造函数（启用同步前备份能力）。
    /// 原无 dbFactory 的五参构造函数已无调用方且 _dbFactory 为 null 会在 Pull 处触发 NRE，删除。
    /// </summary>
    public ApiSyncService(SyncConfigRepository cfgRepo, BabyRepository babyRepo, RecordRepository recordRepo,
        MilestoneRepository milestoneRepo, PointsRepository pointsRepo, Data.DbConnectionFactory dbFactory)
    {
        _cfgRepo = cfgRepo;
        _babyRepo = babyRepo;
        _recordRepo = recordRepo;
        _milestoneRepo = milestoneRepo;
        _pointsRepo = pointsRepo;
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
        _joinApprovedFullPullReset = false; // 每轮同步重置，防上一轮异常残留导致多余全量拉取
        try
        {
            // 0. 同步前数据库快照备份（防极端损坏，如 Pull 把数据洗坏时可回滚）
            //    降频：仅"首次同步（LastSyncAt 为空，含首次登录全量 Pull）"或"当日首次"时执行，
            //    避免每次同步都全库 VACUUM INTO（高频同步场景 I/O 开销过高）
            //    失败不阻塞同步：备份是保险措施，不应影响主流程
            if (ShouldBackupToday(cfg))
            {
                try
                {
                    var backupPath = _dbFactory.DbPath + ".bak";
                    _dbFactory.BackupTo(backupPath);
                    _cfgRepo.UpdateBackupDate(DateTime.Today);
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
            using (var pullConn = _dbFactory.Create())
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
                        if (_babyRepo.UpsertFromSync(SyncMappers.MapToBaby(b, pullLocalId), pullConn, pullTx)) pulledBabies++;
                    foreach (var r in pageResp.Records)
                        if (_recordRepo.UpsertFromSync(SyncMappers.MapToRecord(r, pullLocalId), pullConn, pullTx)) pulledRecords++;
                    foreach (var m in pageResp.Milestones)
                        if (_milestoneRepo.UpsertFromSync(SyncMappers.MapToMilestone(m, pullLocalId), pullConn, pullTx)) pulledMilestones++;
                    foreach (var s in pageResp.SignIns)
                        if (_pointsRepo.UpsertSignInFromSync(SyncMappers.MapToSignIn(s, pullCloudId), pullConn, pullTx)) pulledSignIns++;
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
                        _pointsRepo.UpsertUserPointsFromSync(SyncMappers.MapToUserPoints(pageResp.UserPoints, pullCloudId), pullConn, pullTx);

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
            //     分批上送：每类实体按 500 条/批循环（长期离线积累数千条时避免单次 POST 巨包）。
            //     服务端 PushAsync 对空数组类型安全（?? new() 空迭代），故每批只带一类增量，
            //     按 babies → records → milestones → signIns 顺序：服务端需先落库 baby 才能校验
            //     records/milestones 的 BabyId 归属（与原四类同批时的服务端处理顺序一致）。
            //     全部批次成功后再 MarkSynced/更新 LastSyncAt；任一批失败即中止整次同步
            //     （下次同步整批重来，服务端 LWW 幂等已支持）。
            //     注：使用最后一批的 ServerTime 作为新的 last_sync_at 基准，
            //     避免本地时钟与服务器不一致导致漏推/重推。
            //     积分余额不上送（Pull-only，服务端为准）；签到记录按 CreatedAt 增量上送。
            var pushSince = since;
            DevLogger.Log("Sync", $"Push start (batched, pushSince={pushSince:O})");

            // Family-centric（阶段 1B）：身份注入点 —— 协议项的 UserId/FamilyId 一律来自登录态
            // （CloudUserId / sync_config.current_family_id），禁止读本地业务表的 user_id
            // （该列语义已降级为 LocalDataSpaceId，服务端以 JWT 鉴权为准，payload 仅作路由/日志）。
            var cloudUid = cfg.CloudUserId;
            var familyId = cfg.CurrentFamilyId;
            const int pushBatchSize = 500;
            // 以 Pull 阶段最后一页的 ServerTime 为基准（服务器时钟），而非本地 DateTime.UtcNow：
            // 四类均无增量时若落本地时钟，本机时钟偏快（Android 离线自动对时漂移常见）会使
            // LastSyncAt 超前服务器，下次 Pull 的 since 跳过时间差内他机变更（漏拉）。
            // 有推送时会被各批次的 resp.ServerTime 覆盖，行为不变。
            DateTime pushServerTime = pullServerTime;

            // 各类累计统计（跨批次累加，语义与原单批版一致）
            int totalBabies = 0, totalRecords = 0, totalMilestones = 0, totalSignIns = 0;
            int pushedBabies = 0, pushedRecords = 0, pushedMilestones = 0, pushedSignIns = 0;
            var foreignBabyIds = new List<string>();
            var foreignRecordIds = new List<string>();
            var foreignMilestoneIds = new List<string>();

            // Push 遍历基于"待推送 id 快照"：一次性取回 id 列表后按块取整行上送。
            // 原 LIMIT/OFFSET 分页在同步期间有本地写入（UI 编辑）时会插页错位，跳过/重复读行。
            // 快照固定遍历集合；同步期间的编辑由 MarkSynced 的版本条件兜底（变更行清 synced_at 重推）。
            // babies 先推（服务端先落库 baby 权限集合，后续 records/milestones 才能通过归属校验）
            var babyIds = _babyRepo.GetPendingIds(pushSince);
            var babyVersions = new List<(string Id, DateTime UpdatedAt)>(babyIds.Count);
            for (int offset = 0; offset < babyIds.Count; offset += pushBatchSize)
            {
                var page = _babyRepo.GetByIds(babyIds.Skip(offset).Take(pushBatchSize).ToList());
                if (page.Count == 0) break;
                totalBabies += page.Count;
                babyVersions.AddRange(page.Select(b => (b.Id, b.UpdatedAt)));
                var req = new SyncBatchRequest
                {
                    Babies = page.Select(b => SyncMappers.MapToBabyItem(b, cloudUid, familyId)).ToList(),
                };
                var resp = await PushWithRetryAsync(serverUrl, token, req, ct);
                if (resp is null)
                    return Finish(false, "推送失败，已自动重试，请稍后再试", cfg, SyncErrorKind.Network);
                pushedBabies += resp.BabiesUpserted;
                foreignBabyIds.AddRange(resp.SkippedForeignBabyIds);
                pushServerTime = resp.ServerTime;
            }

            // records
            var recordIds = _recordRepo.GetPendingIds(pushSince);
            var recordVersions = new List<(string Id, DateTime UpdatedAt)>(recordIds.Count);
            for (int offset = 0; offset < recordIds.Count; offset += pushBatchSize)
            {
                var page = _recordRepo.GetByIds(recordIds.Skip(offset).Take(pushBatchSize).ToList());
                if (page.Count == 0) break;
                totalRecords += page.Count;
                recordVersions.AddRange(page.Select(r => (r.Id, r.UpdatedAt)));
                var req = new SyncBatchRequest
                {
                    Records = page.Select(r => SyncMappers.MapToRecordItem(r, cloudUid, familyId)).ToList(),
                };
                var resp = await PushWithRetryAsync(serverUrl, token, req, ct);
                if (resp is null)
                    return Finish(false, "推送失败，已自动重试，请稍后再试", cfg, SyncErrorKind.Network);
                pushedRecords += resp.RecordsUpserted;
                foreignRecordIds.AddRange(resp.SkippedForeignRecordIds);
                pushServerTime = resp.ServerTime;
            }

            // milestones
            var milestoneIds = _milestoneRepo.GetPendingIds(pushSince);
            var milestoneVersions = new List<(string Id, DateTime UpdatedAt)>(milestoneIds.Count);
            for (int offset = 0; offset < milestoneIds.Count; offset += pushBatchSize)
            {
                var page = _milestoneRepo.GetByIds(milestoneIds.Skip(offset).Take(pushBatchSize).ToList());
                if (page.Count == 0) break;
                totalMilestones += page.Count;
                milestoneVersions.AddRange(page.Select(m => (m.Id, m.UpdatedAt)));
                var req = new SyncBatchRequest
                {
                    Milestones = page.Select(m => SyncMappers.MapToMilestoneItem(m, cloudUid, familyId)).ToList(),
                };
                var resp = await PushWithRetryAsync(serverUrl, token, req, ct);
                if (resp is null)
                    return Finish(false, "推送失败，已自动重试，请稍后再试", cfg, SyncErrorKind.Network);
                pushedMilestones += resp.MilestonesUpserted;
                foreignMilestoneIds.AddRange(resp.SkippedForeignMilestoneIds);
                pushServerTime = resp.ServerTime;
            }

            // signIns（个人数据，按 CreatedAt 增量）
            for (int offset = 0; ; offset += pushBatchSize)
            {
                var page = _pointsRepo.GetSignInsByCreatedAt(pushSince, pushBatchSize, offset);
                if (page.Count == 0) break;
                totalSignIns += page.Count;
                var req = new SyncBatchRequest
                {
                    SignIns = page.Select(s => SyncMappers.MapToSignInItem(s, cloudUid)).ToList(),
                };
                var resp = await PushWithRetryAsync(serverUrl, token, req, ct);
                if (resp is null)
                    return Finish(false, "推送失败，已自动重试，请稍后再试", cfg, SyncErrorKind.Network);
                pushedSignIns += resp.SignInsUpserted;
                pushServerTime = resp.ServerTime;
                if (page.Count < pushBatchSize) break;
            }
            DevLogger.Log("Sync",
                $"Push done: babies={pushedBabies}/{totalBabies}, records={pushedRecords}/{totalRecords}, milestones={pushedMilestones}/{totalMilestones}, signIns={pushedSignIns}/{totalSignIns}");

            // 4. 标记已成功上送的数据（更新 synced_at），防止崩溃导致重推
            //    仅当 upserted + skippedForeign == count 时才对该类调用 MarkSynced；否则不 MarkSynced，
            //    让下次同步重试（后端 LWW 幂等跳过），避免"假同步"：推送 0 条却标记已同步。
            //    skippedForeign（跨家庭 terminal skip）视为终态：曾同步到其他家庭的数据永久留本机，
            //    记冲突日志后随全批 MarkSynced，防止无限重推（见设计文档 6.3）。
            //    整体仍视为成功（更新 LastSyncAt），但 LastSyncMsg 加"部分丢弃"提示（排除 foreign 行）。
            //    MarkSynced 带 updated_at 版本条件：同步期间被编辑的行（版本已变）会被清 synced_at=NULL，
            //    下次同步经 synced_at IS NULL 分支重新推送，不会误标导致编辑永久丢失。
            var babyForeign = foreignBabyIds.Count;
            var recordForeign = foreignRecordIds.Count;
            var milestoneForeign = foreignMilestoneIds.Count;
            if (babyForeign + recordForeign + milestoneForeign > 0)
            {
                DevLogger.Log("Sync", $"Push foreign-skipped (terminal): babies={babyForeign} [{string.Join(",", foreignBabyIds)}], records={recordForeign} [{string.Join(",", foreignRecordIds)}], milestones={milestoneForeign} [{string.Join(",", foreignMilestoneIds)}]");
            }
            var babyDropped = totalBabies > 0 && pushedBabies + babyForeign < totalBabies;
            var recordDropped = totalRecords > 0 && pushedRecords + recordForeign < totalRecords;
            var milestoneDropped = totalMilestones > 0 && pushedMilestones + milestoneForeign < totalMilestones;
            if (babyDropped || recordDropped || milestoneDropped)
            {
                DevLogger.Log("Sync", $"Push partial drop: babies {pushedBabies}+{babyForeign}f/{totalBabies}, records {pushedRecords}+{recordForeign}f/{totalRecords}, milestones {pushedMilestones}+{milestoneForeign}f/{totalMilestones}");
            }
            try
            {
                if (!babyDropped && babyVersions.Count > 0)
                    _babyRepo.MarkSynced(babyVersions, pushServerTime);
                if (!recordDropped && recordVersions.Count > 0)
                    _recordRepo.MarkSynced(recordVersions, pushServerTime);
                if (!milestoneDropped && milestoneVersions.Count > 0)
                    _milestoneRepo.MarkSynced(milestoneVersions, pushServerTime);
            }
            catch (Exception ex)
            {
                // MarkSynced 失败不影响同步整体成功，最坏情况是下次重推（服务端 LWW 会幂等跳过）
                DevLogger.Log("Sync", "MarkSynced failed (non-fatal): " + ex.Message);
            }

            // 5. 更新本地同步时间戳
            cfg.LastSyncAt = pushServerTime;
            cfg.LastSyncStatus = "ok";
            var partialHint = (babyDropped || recordDropped || milestoneDropped) ? "（部分丢弃，下次重试）" : "";
            // 跨家庭 terminal skip 是既定语义（换绑后历史数据留本机），如实提示但不告警为失败
            var foreignHint = (babyForeign + recordForeign + milestoneForeign) > 0
                ? $"，另有 {babyForeign + recordForeign + milestoneForeign} 条其他家庭的历史数据已保留在本机" : "";
            cfg.LastSyncMsg = $"拉取 {pulledBabies}宝/{pulledRecords}条/{pulledMilestones}里程碑/{pulledSignIns}签到；推送 {pushedBabies}宝/{pushedRecords}条/{pushedMilestones}里程碑/{pushedSignIns}签到{foreignHint}{partialHint}";
            _cfgRepo.Save(cfg);

            // 5.1 毒丸修复 #6：本次同步若发生过"申请通过 → ResetLastSyncAt"（2.1 步），
            // 上面的 Save(cfg) 已用同步开始时的内存快照把 last_sync_at=NULL 覆盖回去。
            // 这里重新断言 NULL，保证下次同步做全量 Pull，新成员能拉到加入前的历史数据。
            // 顺序保证崩溃安全：任何一步之后崩溃，DB 里 last_sync_at 至少有一个状态可回退；
            // 最坏情况（Save 后断言前崩溃）= 回到旧行为（增量拉取），不比修复前差。
            if (_joinApprovedFullPullReset)
            {
                _cfgRepo.ResetLastSyncAt();
                DevLogger.Log("Sync", "JoinRequest approved this sync, re-assert LastSyncAt=NULL after cfg save (full pull next)");
            }

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
                PushedBabies = pushedBabies,
                PushedRecords = pushedRecords,
                PushedMilestones = pushedMilestones,
                PushedSignIns = pushedSignIns,
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
    /// 是否需要同步前备份：首次同步（LastSyncAt 为空，含首次登录全量 Pull）
    /// 或当日尚未备份过。用于把全库 VACUUM INTO 降频到每天一次。
    /// </summary>
    private static bool ShouldBackupToday(SyncConfig cfg)
        => cfg.LastSyncAt is null || cfg.BackupDate?.Date != DateTime.Today;

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
                    // 立即重置 + 记标记：Push 成功后的 Save(cfg) 会用同步开始时的内存快照把
                    // last_sync_at 覆盖回去（毒丸修复 #6），第 5 步 Save 后再断言一次 NULL。
                    _cfgRepo.ResetLastSyncAt();
                    _joinApprovedFullPullReset = true;
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
            // IsJwtExpired 已上移到 BaseApiClient 共享（所有客户端 token 获取统一预检）。
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

    // ===== 映射方法已拆分到 Services/SyncMappers.cs（internal static class，纯函数原样搬移）=====
}
