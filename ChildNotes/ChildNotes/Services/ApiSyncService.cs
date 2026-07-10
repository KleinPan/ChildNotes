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
        if (string.IsNullOrWhiteSpace(cfg.Username))
            return new SyncResult { Success = false, Message = "同步配置不完整" };

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

            // 1. 确保有 token，失效则重新登录（带重试）
            var token = await EnsureTokenAsync(cfg, serverUrl, ct);
            if (token is null)
                return Finish(false, "登录失败，请检查用户名/密码", cfg, SyncErrorKind.Auth);

            // 2. Pull：以 last_sync_at 为起点分页拉取远端增量（带重试与切备用地址）
            //    大数据量首次同步时通过分页避免单次响应过大、避免中途失败丢失全部进度。
            //    所有页的 upsert 共享同一 SqliteConnection + Transaction，单次提交，避免每行开连。
            var since = cfg.LastSyncAt ?? DateTime.UnixEpoch;
            int pulledBabies = 0, pulledRecords = 0, pulledMilestones = 0, pulledSignIns = 0, pullPages = 0;
            DateTime? cursor = since;
            const int pageSize = 500;
            const int maxPages = 50; // 安全上限：50 页 * 500 = 25000 条，足够覆盖首次同步
            using (var pullConn = _dbFactory!.Create())
            using (var pullTx = pullConn.BeginTransaction())
            {
                while (cursor is not null && pullPages < maxPages)
                {
                    var pageResp = await PullWithRetryAsync(serverUrl, token, cursor.Value, pageSize, ct);
                    if (pageResp is null)
                    {
                        pullTx.Rollback();
                        return Finish(false, "拉取失败，已自动重试，请稍后再试", cfg, SyncErrorKind.Network);
                    }

                    foreach (var b in pageResp.Babies)
                        if (_babyRepo.UpsertFromSync(MapToBaby(b), pullConn, pullTx)) pulledBabies++;
                    foreach (var r in pageResp.Records)
                        if (_recordRepo.UpsertFromSync(MapToRecord(r), pullConn, pullTx)) pulledRecords++;
                    foreach (var m in pageResp.Milestones)
                        if (_milestoneRepo.UpsertFromSync(MapToMilestone(m), pullConn, pullTx)) pulledMilestones++;
                    foreach (var s in pageResp.SignIns)
                        if (_pointsRepo.UpsertSignInFromSync(MapToSignIn(s), pullConn, pullTx)) pulledSignIns++;

                    // 积分余额：每页都带，以最后一页为准（已存在则 LWW 覆盖）
                    if (pageResp.UserPoints is not null)
                        _pointsRepo.UpsertUserPointsFromSync(MapToUserPoints(pageResp.UserPoints), pullConn, pullTx);

                    pullPages++;
                    DevLogger.Log("Sync",
                        $"Pull page {pullPages}: babies={pageResp.Babies.Count}, records={pageResp.Records.Count}, milestones={pageResp.Milestones.Count}, signIns={pageResp.SignIns.Count}, hasMore={pageResp.HasMore}");

                    // HasMore 为 false 或四类都无数据时终止；游标推进到 NextCursor
                    if (!pageResp.HasMore || (pageResp.Babies.Count == 0 && pageResp.Records.Count == 0 && pageResp.Milestones.Count == 0 && pageResp.SignIns.Count == 0))
                        break;
                    cursor = pageResp.NextCursor ?? cursor.Value;
                }
                pullTx.Commit();
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

            var pushReq = new SyncBatchRequest
            {
                Babies = localBabies.Select(MapToBabyItem).ToList(),
                Records = localRecords.Select(MapToRecordItem).ToList(),
                Milestones = localMilestones.Select(MapToMilestoneItem).ToList(),
                SignIns = localSignIns.Select(MapToSignInItem).ToList(),
            };
            var pushResp = await PushWithRetryAsync(serverUrl, token, pushReq, ct);
            if (pushResp is null)
                return Finish(false, "推送失败，已自动重试，请稍后再试", cfg, SyncErrorKind.Network);

            // 4. 标记已成功上送的数据（更新 synced_at），防止崩溃导致重推
            try
            {
                _babyRepo.MarkSynced(localBabies.Select(b => b.Id), pushResp.ServerTime);
                _recordRepo.MarkSynced(localRecords.Select(r => r.Id), pushResp.ServerTime);
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
            cfg.LastSyncMsg = $"拉取 {pulledBabies}宝/{pulledRecords}条/{pulledMilestones}里程碑/{pulledSignIns}签到；推送 {pushResp.BabiesUpserted}宝/{pushResp.RecordsUpserted}条/{pushResp.MilestonesUpserted}里程碑/{pushResp.SignInsUpserted}签到";
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

    private async Task<string?> EnsureTokenAsync(SyncConfig cfg, string serverUrl, CancellationToken ct)
    {
        // 本地有 token 就直接复用，不做主动 me 探活以减少请求次数；
        // token 失效由后续 Pull 触发的 401 重新登录处理（SyncPolicy 对 Auth 错误重试一次）。
        // 但复用 token 时需主动调用一次 /api/auth/me 获取后端 user.id 并做本地迁移：
        // 修复 v0.5.10 之前本地注册生成的 user.id 与后端不一致，导致 PushAsync
        // 的 `item.UserId != uid` 校验静默跳过所有数据，推送 0 条。
        // 迁移逻辑只在登录时触发，已有 token 缓存的用户不会重新登录，所以这里补一次。
        if (!string.IsNullOrWhiteSpace(cfg.Token))
        {
            await VerifyRemoteUserIdAsync(cfg, serverUrl, ct);
            return cfg.Token;
        }

        try
        {
            return await SyncPolicy.ExecuteAsync(
                (attempt, server) => LoginAsync(cfg, server, ct),
                serverUrl, ct);
        }
        catch (SyncException ex)
        {
            DevLogger.Log("Sync", $"Login failed after retry: {ex.Kind} {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 调用 /api/auth/me 获取后端 user.id，与本地不一致则触发迁移。
    /// 失败静默处理（不阻塞同步主流程，后续 Push 校验失败会在日志中体现）。
    /// </summary>
    private async Task VerifyRemoteUserIdAsync(SyncConfig cfg, string serverUrl, CancellationToken ct)
    {
        try
        {
            var url = serverUrl.TrimEnd('/') + "/api/auth/me";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.Token);
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                DevLogger.Log("Sync", $"VerifyRemoteUserId /api/auth/me HTTP {(int)resp.StatusCode}");
                return;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("id", out var idEl))
            {
                var remoteId = idEl.GetString();
                if (!string.IsNullOrEmpty(remoteId))
                    BaseApiClient.MigrateLocalUserIdIfNeeded(remoteId);
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("Sync", "VerifyRemoteUserId exception: " + ex.Message);
        }
    }

    private async Task<string> LoginAsync(SyncConfig cfg, string serverUrl, CancellationToken ct)
    {
        // 登录无 Bearer token，且需从 data.token 取值，不走 SendWithTokenV2Async。
        var url = serverUrl.TrimEnd('/') + "/api/auth/login";
        var body = Serialize(new { username = cfg.Username, password = cfg.Password });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, ct);
        }
        catch (TaskCanceledException ex)
        {
            if (ct.IsCancellationRequested) throw;
            throw new SyncException(SyncErrorKind.Timeout, "登录超时", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw SyncException.FromHttpRequestException(ex);
        }

        using (resp)
        {
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                DevLogger.Log("Sync", $"Login fail: {(int)resp.StatusCode} {json}");
                throw SyncException.FromHttpStatus((int)resp.StatusCode, "登录失败: " + resp.StatusCode);
            }
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                throw new SyncException(SyncErrorKind.Business, "登录响应缺少 data 字段");
            if (!data.TryGetProperty("token", out var tokenEl) || string.IsNullOrWhiteSpace(tokenEl.GetString()))
                throw new SyncException(SyncErrorKind.Business, "登录响应缺少 token");
            var token = tokenEl.GetString()!;
            _cfgRepo.UpdateToken(token);
            // 读取后端 user.id 并做本地迁移（修复本地注册 id 与后端 id 不一致导致推送 0 条的问题）
            if (data.TryGetProperty("user", out var userEl) && userEl.TryGetProperty("id", out var idEl))
            {
                var remoteUserId = idEl.GetString();
                if (!string.IsNullOrEmpty(remoteUserId))
                    MigrateLocalUserIdIfNeeded(remoteUserId);
            }
            return token;
        }
    }

    private async Task<SyncPullResponse?> PullWithRetryAsync(string serverUrl, string token, DateTime since, int limit, CancellationToken ct)
    {
        try
        {
            return await SyncPolicy.ExecuteAsync(
                async (attempt, server) =>
                {
                    var path = "/api/sync/pull?since=" + Uri.EscapeDataString(since.ToUniversalTime().ToString("O"))
                               + "&limit=" + limit;
                    using var resp = await SendWithTokenV2Async(_cfgRepo, server, token, HttpMethod.Get, path, null, ct);
                    return await ReadDataAsync<SyncPullResponse>(resp, ct)
                        ?? throw new SyncException(SyncErrorKind.Business, "Pull 响应解析失败");
                },
                serverUrl, ct);
        }
        catch (SyncException ex)
        {
            // Auth 错误特殊处理：清 token 后重登一次再 Pull
            if (ex.Kind == SyncErrorKind.Auth && string.IsNullOrWhiteSpace(_cfgRepo.Get().Token))
            {
                var cfg = _cfgRepo.Get();
                var newToken = await LoginAsync(cfg, serverUrl, ct);
                // 递归一次（已无 token，不会再触发 Auth 重试分支）
                return await PullWithRetryAsync(serverUrl, newToken, since, limit, ct);
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
                    using var resp = await SendWithTokenV2Async(_cfgRepo, server, token, HttpMethod.Post, "/api/sync/push", body, ct);
                    return await ReadDataAsync<SyncBatchResponse>(resp, ct)
                        ?? throw new SyncException(SyncErrorKind.Business, "Push 响应解析失败");
                },
                serverUrl, ct);
        }
        catch (SyncException ex)
        {
            DevLogger.Log("Sync", $"Push failed: {ex.Kind} {ex.Message}");
            return null;
        }
    }

    // ===== 映射方法：本地实体 ↔ 共享同步 DTO（ChildNotes.Shared.Sync）=====

    private static Baby MapToBaby(SyncBabyItem i) => new()
    {
        Id = i.Id, UserId = i.UserId, Name = i.Name, Avatar = i.Avatar ?? "",
        Gender = i.Gender ?? "", BirthDate = i.BirthDate,
        // 服务器时间约定为 UTC，转 Local 与本地库读取行为一致；BirthDate 是纯日期原样保留
        CreatedAt = ToLocal(i.CreatedAt), UpdatedAt = ToLocal(i.UpdatedAt),
    };

    private static ChildRecord MapToRecord(SyncRecordItem i) => new()
    {
        Id = i.Id, UserId = i.UserId, BabyId = i.BabyId,
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

    /// <summary>把应用层的本地时间转回 UTC，用于上送服务器。</summary>
    private static DateTime ToUtc(DateTime dt)
        => dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();

    private static SyncBabyItem MapToBabyItem(Baby b) => new()
    {
        Id = b.Id, UserId = b.UserId, Name = b.Name, Avatar = b.Avatar ?? "",
        Gender = b.Gender ?? "", BirthDate = b.BirthDate,
        // 应用层时间已是 Local，上送服务器需转 UTC；BirthDate 是纯日期原样上送
        CreatedAt = ToUtc(b.CreatedAt), UpdatedAt = ToUtc(b.UpdatedAt),
    };

    private static SyncRecordItem MapToRecordItem(ChildRecord r) => new()
    {
        Id = r.Id, UserId = r.UserId, BabyId = r.BabyId,
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

    private static Milestone MapToMilestone(SyncMilestoneItem i) => new()
    {
        Id = i.Id, UserId = i.UserId, BabyId = i.BabyId,
        Title = i.Title, Content = i.Content,
        // RecordDate 是纯日期，原样保留；CreatedAt/UpdatedAt 服务器传 UTC，转 Local
        RecordDate = i.RecordDate,
        PhotosJson = string.IsNullOrEmpty(i.PhotosJson) ? "[]" : i.PhotosJson,
        Deleted = i.Deleted,
        CreatedAt = ToLocal(i.CreatedAt), UpdatedAt = ToLocal(i.UpdatedAt),
    };

    private static SyncMilestoneItem MapToMilestoneItem(Milestone m) => new()
    {
        Id = m.Id, UserId = m.UserId, BabyId = m.BabyId,
        Title = m.Title, Content = m.Content,
        RecordDate = m.RecordDate,
        PhotosJson = m.PhotosJson ?? "[]",
        Deleted = m.Deleted,
        // 应用层 Local 时间上送服务器转 UTC
        CreatedAt = ToUtc(m.CreatedAt), UpdatedAt = ToUtc(m.UpdatedAt),
    };

    private static SignInRecord MapToSignIn(SyncSignInItem i) => new()
    {
        Id = i.Id, UserId = i.UserId,
        SignDate = i.SignDate,
        ContinuousDays = i.ContinuousDays,
        Reward = i.Reward,
        CreatedAt = ToLocal(i.CreatedAt),
    };

    private static SyncSignInItem MapToSignInItem(SignInRecord s) => new()
    {
        Id = s.Id, UserId = s.UserId,
        SignDate = s.SignDate,
        ContinuousDays = s.ContinuousDays,
        Reward = s.Reward,
        CreatedAt = ToUtc(s.CreatedAt),
    };

    private static UserPoints MapToUserPoints(SyncUserPointsItem i) => new()
    {
        Id = i.Id, UserId = i.UserId,
        Points = i.Points, TotalEarned = i.TotalEarned, TotalSpent = i.TotalSpent,
        // user_points 本地无独立 CreatedAt 同步，用 UpdatedAt 近似（仅 LWW 判定用）
        CreatedAt = ToLocal(i.UpdatedAt), UpdatedAt = ToLocal(i.UpdatedAt),
    };
}
