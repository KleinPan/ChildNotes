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
        "SELECT id, enabled, server_url, cloud_user_id, local_user_id, " +
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
              (id, enabled, server_url, cloud_user_id, local_user_id,
               last_sync_at, last_sync_status, last_sync_msg, device_id)
              VALUES (@id, @e, @u, @cuid, @luid, @lsa, @lss, @lsm, @did)",
            cmd =>
            {
                cmd.Add("@id", 1)
                   .Add("@e", cfg.Enabled ? 1 : 0)
                   .AddString("@u", cfg.ServerUrl, emptyAsNull: true)
                   .AddString("@cuid", cfg.CloudUserId, emptyAsNull: true)
                   .AddString("@luid", cfg.LocalUserId, emptyAsNull: true)
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
    /// 更新上次登录的云端用户 Id（登出时记录，用于下次启动时反迁移遗留数据）。
    /// 启动时若发现此字段非空且 CloudUserId 为空（已登出），执行反迁移后清空此字段。
    /// </summary>
    public void UpdateLastCloudUserId(string lastCloudUserId)
    {
        ExecuteNonQuery(
            "UPDATE sync_config SET last_cloud_user_id=@c WHERE id=1",
            cmd => cmd.AddString("@c", lastCloudUserId ?? string.Empty, emptyAsNull: false));
        InvalidateCache();
    }

    /// <summary>
    /// 把 oldUserId 名下的所有业务数据迁移到 newUserId 名下。
    ///
    /// 背景：v5 重构后 AppState.UserId 未登录返回 LocalUserId，登录返回 CloudUserId。
    /// 用户切换登录态时（登录或登出），若不迁移 user_id，GetByUser(新 id) 查不到原数据，
    /// 导致首页显示"未添加宝宝"。
    ///
    /// 双向使用：
    ///   - 登录时：MigrateUserId(localUserId, cloudUserId)
    ///   - 登出时：MigrateUserId(cloudUserId, localUserId)
    ///
    /// 单事务执行所有 UPDATE，遇 UNIQUE 冲突时合并/去重后保留 newUserId 名下数据。
    /// 幂等：相同 id 或无数据可迁时返回 0，多次调用安全。
    /// </summary>
    /// <param name="oldUserId">迁移源用户 Id（被替换的 user_id 值）。</param>
    /// <param name="newUserId">迁移目标用户 Id（替换后的 user_id 值）。</param>
    /// <returns>受影响的总行数（含 UPDATE 与 DELETE，用于诊断）。</returns>
    public int MigrateUserId(string oldUserId, string newUserId)
    {
        if (string.IsNullOrEmpty(oldUserId) || string.IsNullOrEmpty(newUserId)) return 0;
        if (string.Equals(oldUserId, newUserId, StringComparison.Ordinal)) return 0;

        int totalAffected = 0;
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        try
        {
            // user_points.user_id UNIQUE：若 newUserId 已有积分行，合并积分后删 oldUserId 行
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

            // task_record (user_id, task_code) UNIQUE：删 oldUserId 名下与 newUserId 冲突的行后 UPDATE
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

            // user_supplement_item (user_id, type, name) UNIQUE：同上去重后 UPDATE
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

            // 无 UNIQUE 约束的表：直接 UPDATE
            var simpleTables = new[]
            {
                "baby", "baby_member", "child_record", "milestone",
                "sign_in_record", "user_custom_vaccine",
                "ai_analysis_record", "in_app_message",
            };
            foreach (var table in simpleTables)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE {table} SET user_id = @new WHERE user_id = @old;";
                cmd.Parameters.AddWithValue("@old", oldUserId);
                cmd.Parameters.AddWithValue("@new", newUserId);
                totalAffected += cmd.ExecuteNonQuery();
            }

            tx.Commit();
            InvalidateCache();
            DevLogger.Log("Sync", $"MigrateUserId: {oldUserId} → {newUserId}, affected={totalAffected}");
            return totalAffected;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DevLogger.Log("Sync", $"MigrateUserId failed (rolled back): {ex.Message}");
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
        LastSyncAt = r.IsDBNull(6) ? null : DateTimeExtensions.ParseDb(r.GetString(6)),
        LastSyncStatus = r.IsDBNull(7) ? null : r.GetString(7),
        LastSyncMsg = r.IsDBNull(8) ? null : r.GetString(8),
        DeviceId = r.IsDBNull(9) ? string.Empty : r.GetString(9),
    };
}
