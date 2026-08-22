using Microsoft.Data.Sqlite;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

/// <summary>
/// sync_config 表的访问器。该表只有一行（id=1）。
/// ServerUrl 由用户在同步设置页配置并持久化到此表。
/// 优化：单行配置表加内存缓存，避免 ApiSyncService 单次同步流程 4-6 次 Get() 各开连接 + PRAGMA 往返。
///      任何写操作（Save/UpdateSyncResult/UpdateCloudUserId/UpdateDeviceId）自动失效缓存。
/// v5 schema 重构：移除 username/password/token，新增 cloud_user_id/local_user_id。
///   AccessToken/RefreshToken 走 ISecureStorage（Android Keystore / Windows DPAPI）。
/// </summary>
public sealed class SyncConfigRepository : BaseRepository
{
    public SyncConfigRepository(DbConnectionFactory factory) : base(factory) { }

    private const string SelectSql =
        "SELECT id, enabled, server_url, cloud_user_id, local_user_id, last_cloud_user_id, " +
        "current_family_id, last_bound_family_id, identity_fixup_done, " +
        "last_sync_at, last_sync_status, last_sync_msg, device_id FROM sync_config WHERE id=1";

    /// <summary>内存缓存：单行配置表极少变化，仅在写操作后失效。</summary>
    private SyncConfig? _cached;
    private readonly object _cacheLock = new();

    public SyncConfig Get()
    {
        lock (_cacheLock)
        {
            if (_cached is not null) return Clone(_cached);
        }
        var cfg = QueryFirstOrDefault(SelectSql, _ => { }, Map) ?? new SyncConfig();
        lock (_cacheLock) { _cached = cfg; }
        return Clone(cfg);
    }

    /// <summary>
    /// 返回配置对象的浅拷贝。调用方拿到的是独立实例，
    /// 修改其字段不会污染内存缓存，避免引用共享导致的隐性状态错乱。
    /// </summary>
    private static SyncConfig Clone(SyncConfig c) => new()
    {
        Id = c.Id,
        Enabled = c.Enabled,
        ServerUrl = c.ServerUrl,
        CloudUserId = c.CloudUserId,
        LocalUserId = c.LocalUserId,
        CurrentFamilyId = c.CurrentFamilyId,
        LastBoundFamilyId = c.LastBoundFamilyId,
        IdentityFixupDone = c.IdentityFixupDone,
        LastCloudUserId = c.LastCloudUserId,
        LastSyncAt = c.LastSyncAt,
        LastSyncStatus = c.LastSyncStatus,
        LastSyncMsg = c.LastSyncMsg,
        DeviceId = c.DeviceId,
    };

    /// <summary>
    /// 将 DateTime 统一为 UTC 并输出 "O" round-trip 格式字符串（带 "Z" 后缀）。
    /// SQLite TEXT 字典序比较不感知时区，必须保证所有时间字符串格式一致，
    /// 否则 "09:38:25Z" 与 "17:37:29+08:00" 比较会得出错误结果（前者被判定更小）。
    /// </summary>
    private static string ToUtcO(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return utc.ToString("O");
    }

    public void Save(SyncConfig cfg)
    {
        ExecuteNonQuery(
            @"INSERT OR REPLACE INTO sync_config
              (id, enabled, server_url, cloud_user_id, local_user_id, last_cloud_user_id,
               current_family_id, last_bound_family_id, identity_fixup_done,
               last_sync_at, last_sync_status, last_sync_msg, device_id)
              VALUES (@id, @e, @u, @cuid, @luid, @lcuid, @cfid, @lbfid, @fx, @lsa, @lss, @lsm, @did)",
            cmd =>
            {
                cmd.Add("@id", 1)
                   .Add("@e", cfg.Enabled ? 1 : 0)
                   .AddString("@u", cfg.ServerUrl, emptyAsNull: true)
                   .AddString("@cuid", cfg.CloudUserId, emptyAsNull: true)
                   .AddString("@luid", cfg.LocalUserId, emptyAsNull: true)
                   .AddString("@lcuid", cfg.LastCloudUserId, emptyAsNull: true)
                   .AddString("@cfid", cfg.CurrentFamilyId, emptyAsNull: true)
                   .AddString("@lbfid", cfg.LastBoundFamilyId, emptyAsNull: true)
                   .Add("@fx", cfg.IdentityFixupDone)
                   .Add("@lsa", cfg.LastSyncAt is null ? DBNull.Value : (object)ToUtcO(cfg.LastSyncAt.Value))
                   .AddString("@lss", cfg.LastSyncStatus, emptyAsNull: true)
                   .AddString("@lsm", cfg.LastSyncMsg, emptyAsNull: true)
                   .AddString("@did", cfg.DeviceId, emptyAsNull: true);
            });
        InvalidateCache();
    }

    public void UpdateSyncResult(DateTime syncAt, string status, string msg)
    {
        // 失败时不更新 last_sync_at：避免下次同步的 since 比本地数据的 updated_at 还晚，
        // 导致本地待 Push 的数据被 GetByUpdatedAt(since) 过滤掉永远无法上送。
        // 只更新状态和消息，last_sync_at 保留上次成功同步的时间。
        if (status == "ok")
        {
            ExecuteNonQuery(
                "UPDATE sync_config SET last_sync_at=@t, last_sync_status=@s, last_sync_msg=@m WHERE id=1",
                cmd => cmd.AddUtc("@t", syncAt).AddString("@s", status, emptyAsNull: true).AddString("@m", msg, emptyAsNull: true));
        }
        else
        {
            ExecuteNonQuery(
                "UPDATE sync_config SET last_sync_status=@s, last_sync_msg=@m WHERE id=1",
                cmd => cmd.AddString("@s", status, emptyAsNull: true).AddString("@m", msg, emptyAsNull: true));
        }
        InvalidateCache();
    }

    /// <summary>
    /// 重置 last_sync_at 为 NULL，使下次同步做一次全量 Pull。
    /// 加入家庭后调用：新成员需要拉取所有可见的 baby / baby_member 记录，
    /// 而后端 join 虽然更新了 baby.UpdatedAt，但若本成员账号此前同步过，
    /// since 已晚于主人其他宝宝的 updated_at，全量同步确保不漏。
    /// </summary>
    public void ResetLastSyncAt()
    {
        ExecuteNonQuery(
            "UPDATE sync_config SET last_sync_at=NULL, last_sync_status=NULL, last_sync_msg=NULL WHERE id=1",
            _ => { });
        InvalidateCache();
    }

    /// <summary>更新云端用户 Id（登录成功后写入；退出登录传空串）。</summary>
    public void UpdateCloudUserId(string cloudUserId)
    {
        ExecuteNonQuery(
            "UPDATE sync_config SET cloud_user_id=@c WHERE id=1",
            cmd => cmd.AddString("@c", cloudUserId ?? string.Empty, emptyAsNull: false));
        InvalidateCache();
    }

    /// <summary>
    /// 更新当前绑定家庭 Id（登录成功后由 AuthResponse.currentFamilyId 写入）。
    /// 阶段 2 引入换绑（rebind）后，此方法在换绑事务中变更并联动清理 synced_at。
    /// </summary>
    public void UpdateCurrentFamilyId(string familyId)
    {
        ExecuteNonQuery(
            "UPDATE sync_config SET current_family_id=@f WHERE id=1",
            cmd => cmd.AddString("@f", familyId ?? string.Empty, emptyAsNull: false));
        InvalidateCache();
    }

    /// <summary>
    /// 更新最近绑定家庭 Id（登录绑定家庭时写入；换绑检测用，见设计文档 7.1）。
    /// 除"清除本地数据"外永不清空。
    /// </summary>
    public void UpdateLastBoundFamilyId(string familyId)
    {
        ExecuteNonQuery(
            "UPDATE sync_config SET last_bound_family_id=@f WHERE id=1",
            cmd => cmd.AddString("@f", familyId ?? string.Empty, emptyAsNull: false));
        InvalidateCache();
    }

    /// <summary>
    /// 个人数据表清单（UserId = CloudUserId；未登录离线态挂 LocalDataSpaceId）。
    /// sign_in_record：按 Id 全局唯一（INSERT OR IGNORE upsert），user_id 为属性列。
    /// </summary>
    private static readonly string[] PersonalTables = { "sign_in_record", "in_app_message" };

    /// <summary>家庭业务表清单（本地 user_id 恒为 LocalDataSpaceId；云端归属 FamilyId）。</summary>
    private static readonly string[] FamilyTables =
        { "baby", "child_record", "milestone", "user_custom_vaccine", "ai_analysis_record" };

    /// <summary>
    /// 把个人数据表中的 oldUserId 行迁移/合并到 newUserId 名下（单事务）。
    /// 仅处理个人表；家庭业务表 user_id 恒为 LocalDataSpaceId，不再参与任何迁移。
    ///
    /// UNIQUE 冲突处理：
    ///   - user_points（user_id UNIQUE）：合并 points/total_earned/total_spent 后删旧行
    ///   - task_record（user_id+task_code UNIQUE）：删冲突行后 UPDATE
    ///   - user_supplement_item（user_id+type+name UNIQUE）：删冲突行后 UPDATE
    ///   - sign_in_record / in_app_message：无 UNIQUE 约束，直接 UPDATE
    /// 幂等：相同 id 或无数据可迁时返回 0，多次调用安全。
    /// </summary>
    /// <returns>受影响的总行数（用于诊断）。</returns>
    private int MigratePersonalData(SqliteConnection conn, SqliteTransaction tx, string oldUserId, string newUserId)
    {
        int totalAffected = 0;

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE user_points
SET points = points + (SELECT points FROM user_points WHERE user_id = @old),
    total_earned = total_earned + (SELECT total_earned FROM user_points WHERE user_id = @old),
    total_spent = total_spent + (SELECT total_spent FROM user_points WHERE user_id = @old)
WHERE user_id = @new
  AND EXISTS (SELECT 1 FROM user_points WHERE user_id = @old);
DELETE FROM user_points
WHERE user_id = @old
  AND EXISTS (SELECT 1 FROM user_points WHERE user_id = @new);
UPDATE user_points SET user_id = @new WHERE user_id = @old;";
            cmd.Parameters.AddWithValue("@old", oldUserId);
            cmd.Parameters.AddWithValue("@new", newUserId);
            totalAffected += cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
DELETE FROM task_record
WHERE user_id = @old
  AND EXISTS (SELECT 1 FROM task_record AS b WHERE b.user_id = @new AND b.task_code = task_record.task_code);
UPDATE task_record SET user_id = @new WHERE user_id = @old;";
            cmd.Parameters.AddWithValue("@old", oldUserId);
            cmd.Parameters.AddWithValue("@new", newUserId);
            totalAffected += cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
DELETE FROM user_supplement_item
WHERE user_id = @old
  AND EXISTS (SELECT 1 FROM user_supplement_item AS b
              WHERE b.user_id = @new AND b.type = user_supplement_item.type AND b.name = user_supplement_item.name);
UPDATE user_supplement_item SET user_id = @new WHERE user_id = @old;";
            cmd.Parameters.AddWithValue("@old", oldUserId);
            cmd.Parameters.AddWithValue("@new", newUserId);
            totalAffected += cmd.ExecuteNonQuery();
        }

        foreach (var table in PersonalTables)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE {table} SET user_id = @new WHERE user_id = @old;";
            cmd.Parameters.AddWithValue("@old", oldUserId);
            cmd.Parameters.AddWithValue("@new", newUserId);
            totalAffected += cmd.ExecuteNonQuery();
        }

        return totalAffected;
    }

    /// <summary>
    /// 删除个人数据表中"既不属于本地数据空间、也不属于当前账号"的遗留行（换云账号场景，
    /// 设计文档 6.5：CloudUserId 变更时清理本地个人表，新账号数据由 Pull 重建）。
    /// </summary>
    private int DeleteForeignPersonalRows(SqliteConnection conn, SqliteTransaction tx, string localId, string cloudId)
    {
        int totalAffected = 0;
        var allTables = new List<string>(PersonalTables) { "user_points", "task_record", "user_supplement_item" };
        foreach (var table in allTables)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table} WHERE user_id != @l AND user_id != @c;";
            cmd.Parameters.AddWithValue("@l", localId);
            cmd.Parameters.AddWithValue("@c", cloudId);
            totalAffected += cmd.ExecuteNonQuery();
        }
        return totalAffected;
    }

    /// <summary>
    /// 登录成功后把本地个人数据归到账号名下（设计文档 6.5，阶段 1C）：
    ///   1. 删除换账号遗留行（user_id 既非 LocalDataSpaceId 也非当前 CloudUserId）
    ///   2. 离线期间挂 LocalDataSpaceId 的个人数据（积分/签到/任务/自定义项/站内信）迁移到 CloudUserId
    /// 单事务执行；家庭业务表不迁移（user_id 恒为 LocalDataSpaceId）。
    /// </summary>
    public int AdoptPersonalDataOnLogin(string localId, string cloudId)
    {
        if (string.IsNullOrEmpty(localId) || string.IsNullOrEmpty(cloudId)) return 0;
        if (string.Equals(localId, cloudId, StringComparison.Ordinal)) return 0;

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        try
        {
            int totalAffected = DeleteForeignPersonalRows(conn, tx, localId, cloudId);
            totalAffected += MigratePersonalData(conn, tx, localId, cloudId);
            tx.Commit();
            InvalidateCache();
            DevLogger.Log("Sync", $"AdoptPersonalDataOnLogin: local={localId} → cloud={cloudId}, affected={totalAffected}");
            return totalAffected;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DevLogger.Log("Sync", $"AdoptPersonalDataOnLogin failed (rolled back): {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 一次性身份 fixup（阶段 1C，设计文档 11 节）：把旧版本（User-centric 双向迁移时代）
    /// 遗留的 user_id 归位到 Family-centric 语义。单事务，崩溃安全可重跑：
    ///
    ///   1. 家庭业务表：所有非 LocalDataSpaceId 的 user_id → LocalDataSpaceId
    ///      （baby_member 不动：其 user_id 是云端成员名单，非本地数据空间概念）
    ///   2. 个人表：
    ///      - 已登录：清理换账号遗留行 + 离线个人数据（L 名下）迁到 CloudUserId
    ///      - 未登录：lastCloudUserId 遗留行迁回 LocalDataSpaceId（旧版登出未反迁移的兜底）
    ///   3. last_bound_family_id = 当前绑定家庭（若已登录）
    ///   4. 清空 last_cloud_user_id（v6 补偿机制废弃；防版本回滚误触发旧反迁移）
    ///   5. identity_fixup_done = 1（幂等标志，与数据同事务）
    /// </summary>
    public int RunIdentityFixup(string localId, string? cloudId, string? lastCloudId, string? currentFamilyId)
    {
        if (string.IsNullOrEmpty(localId)) return 0;

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        try
        {
            int totalAffected = 0;

            // 1. 家庭业务表：user_id 恒为 LocalDataSpaceId（存量非 L 的全部归 L）
            foreach (var table in FamilyTables)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE {table} SET user_id=@l WHERE user_id != @l;";
                cmd.Parameters.AddWithValue("@l", localId);
                totalAffected += cmd.ExecuteNonQuery();
            }

            // 2. 个人表按登录态归位
            if (!string.IsNullOrEmpty(cloudId) && !string.Equals(cloudId, localId, StringComparison.Ordinal))
            {
                totalAffected += DeleteForeignPersonalRows(conn, tx, localId, cloudId);
                totalAffected += MigratePersonalData(conn, tx, localId, cloudId);
            }
            else if (!string.IsNullOrEmpty(lastCloudId) && !string.Equals(lastCloudId, localId, StringComparison.Ordinal))
            {
                totalAffected += MigratePersonalData(conn, tx, lastCloudId, localId);
            }

            // 3-5. sync_config：last_bound_family_id / 清空 last_cloud_user_id / fixup 标志
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
UPDATE sync_config SET
  last_bound_family_id = CASE WHEN @f != '' THEN @f ELSE last_bound_family_id END,
  last_cloud_user_id = '',
  identity_fixup_done = 1
WHERE id = 1;";
                cmd.Parameters.AddWithValue("@f", currentFamilyId ?? string.Empty);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            InvalidateCache();
            DevLogger.Log("Sync", $"RunIdentityFixup done: local={localId}, cloud={cloudId ?? "null"}, lastCloud={lastCloudId ?? "null"}, family={currentFamilyId ?? "null"}, affected={totalAffected}");
            return totalAffected;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DevLogger.Log("Sync", $"RunIdentityFixup failed (rolled back): {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 换绑（rebind）事务（Family-centric 阶段 2，设计文档 6.4）：用户在换绑确认框点"确认"后，
    /// 在 SyncTrigger 独占锁内执行。单事务完成：
    ///
    ///   1. sync_config：cloud_user_id / current_family_id / last_bound_family_id 更新为登录账号与家庭，
    ///      last_sync_at 置 NULL（下次同步 = Full Pull Only）
    ///   2. baby / child_record / milestone 的 synced_at 全部置 NULL
    ///      （本机数据标记为"未上送"，归属变更后重新推送到新家庭；服务端 LWW 幂等）
    ///
    /// 崩溃安全：单事务原子提交，重跑无副作用（UPDATE 幂等）。
    /// </summary>
    /// <returns>受影响行数（sync_config 1 行 + 三张业务表行数）。</returns>
    public int ExecuteRebind(string cloudUserId, string familyId)
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        try
        {
            int totalAffected;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
UPDATE sync_config SET
  cloud_user_id = @c,
  current_family_id = @f,
  last_bound_family_id = @f,
  last_sync_at = NULL,
  last_sync_status = NULL,
  last_sync_msg = NULL
WHERE id = 1;";
                cmd.Parameters.AddWithValue("@c", cloudUserId ?? string.Empty);
                cmd.Parameters.AddWithValue("@f", familyId ?? string.Empty);
                totalAffected = cmd.ExecuteNonQuery();
            }

            foreach (var table in new[] { "baby", "child_record", "milestone" })
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE {table} SET synced_at = NULL;";
                totalAffected += cmd.ExecuteNonQuery();
            }

            tx.Commit();
            InvalidateCache();
            DevLogger.Log("Sync", $"ExecuteRebind done: cloud={cloudUserId}, family={familyId}, affected={totalAffected}");
            return totalAffected;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DevLogger.Log("Sync", $"ExecuteRebind failed (rolled back): {ex.Message}");
            throw;
        }
    }

    /// <summary>更新设备标识。首次启动时由 ServiceProvider 调用。</summary>
    public void UpdateDeviceId(string deviceId)
    {
        ExecuteNonQuery(
            "UPDATE sync_config SET device_id=@d WHERE id=1",
            cmd => cmd.AddString("@d", deviceId, emptyAsNull: true));
        InvalidateCache();
    }

    private void InvalidateCache()
    {
        lock (_cacheLock) { _cached = null; }
    }

    private static SyncConfig Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Enabled = r.GetInt32(1) == 1,
        ServerUrl = r.GetString(2),
        CloudUserId = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        LocalUserId = r.IsDBNull(4) ? string.Empty : r.GetString(4),
        LastCloudUserId = r.IsDBNull(5) ? string.Empty : r.GetString(5),
        CurrentFamilyId = r.IsDBNull(6) ? string.Empty : r.GetString(6),
        LastBoundFamilyId = r.IsDBNull(7) ? string.Empty : r.GetString(7),
        IdentityFixupDone = r.GetInt32(8),
        LastSyncAt = r.IsDBNull(9) ? null : DateTimeExtensions.ParseDb(r.GetString(9)),
        LastSyncStatus = r.IsDBNull(10) ? null : r.GetString(10),
        LastSyncMsg = r.IsDBNull(11) ? null : r.GetString(11),
        DeviceId = r.IsDBNull(12) ? string.Empty : r.GetString(12),
    };
}
