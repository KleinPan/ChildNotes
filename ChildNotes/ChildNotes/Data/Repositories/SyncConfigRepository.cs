using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

/// <summary>
/// sync_config 表的访问器。该表只有一行（id=1）。
/// ServerUrl 由用户在同步设置页配置并持久化到此表。
/// 优化：单行配置表加内存缓存，避免 ApiSyncService 单次同步流程 4-6 次 Get() 各开连接 + PRAGMA 往返。
///      任何写操作（Save/UpdateSyncResult/UpdateToken/UpdateDeviceId）自动失效缓存。
/// </summary>
public sealed class SyncConfigRepository : BaseRepository
{
    public SyncConfigRepository(DbConnectionFactory factory) : base(factory) { }

    private const string SelectSql =
        "SELECT id, enabled, server_url, username, password, token, " +
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
        Username = c.Username,
        Password = c.Password,
        Token = c.Token,
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
              (id, enabled, server_url, username, password, token,
               last_sync_at, last_sync_status, last_sync_msg, device_id)
              VALUES (@id, @e, @u, @un, @p, @t, @lsa, @lss, @lsm, @did)",
            cmd =>
            {
                cmd.Add("@id", 1)
                   .Add("@e", cfg.Enabled ? 1 : 0)
                   .AddString("@u", cfg.ServerUrl, emptyAsNull: true)
                   .AddString("@un", cfg.Username, emptyAsNull: true)
                   .AddString("@p", cfg.Password, emptyAsNull: true)
                   .AddString("@t", cfg.Token, emptyAsNull: true)
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

    public void UpdateToken(string token)
    {
        // token 列有 NOT NULL 约束，空 token 必须写空字符串而非 NULL。
        // 401 清 token 时会传 ""，若 emptyAsNull=true 会写成 DBNull 撞约束抛 SqliteException，
        // 导致整个请求链路中断（连自动重新登录重试都走不到）。
        ExecuteNonQuery(
            "UPDATE sync_config SET token=@t WHERE id=1",
            cmd => cmd.AddString("@t", token ?? string.Empty, emptyAsNull: false));
        InvalidateCache();
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
        Username = r.GetString(3),
        Password = r.GetString(4),
        Token = r.GetString(5),
        LastSyncAt = r.IsDBNull(6) ? null : DateTimeExtensions.ParseDb(r.GetString(6)),
        LastSyncStatus = r.IsDBNull(7) ? null : r.GetString(7),
        LastSyncMsg = r.IsDBNull(8) ? null : r.GetString(8),
        DeviceId = r.IsDBNull(9) ? string.Empty : r.GetString(9),
    };
}
