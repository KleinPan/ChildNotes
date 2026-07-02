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
            if (_cached is not null) return _cached;
        }
        var cfg = QueryFirstOrDefault(SelectSql, _ => { }, Map) ?? new SyncConfig();
        lock (_cacheLock) { _cached = cfg; }
        return cfg;
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
                   .Add("@lsa", (object?)cfg.LastSyncAt?.ToString("O") ?? DBNull.Value)
                   .AddString("@lss", cfg.LastSyncStatus, emptyAsNull: true)
                   .AddString("@lsm", cfg.LastSyncMsg, emptyAsNull: true)
                   .AddString("@did", cfg.DeviceId, emptyAsNull: true);
            });
        InvalidateCache();
    }

    public void UpdateSyncResult(DateTime syncAt, string status, string msg)
    {
        ExecuteNonQuery(
            "UPDATE sync_config SET last_sync_at=@t, last_sync_status=@s, last_sync_msg=@m WHERE id=1",
            cmd => cmd.AddUtc("@t", syncAt).AddString("@s", status, emptyAsNull: true).AddString("@m", msg, emptyAsNull: true));
        InvalidateCache();
    }

    public void UpdateToken(string token)
    {
        ExecuteNonQuery(
            "UPDATE sync_config SET token=@t WHERE id=1",
            cmd => cmd.AddString("@t", token, emptyAsNull: true));
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
