using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

/// <summary>
/// WebDAV 配置仓储（单行模式，id=1）。
/// </summary>
public sealed class WebDavConfigRepository
{
    private readonly DbConnectionFactory _factory;

    public WebDavConfigRepository(DbConnectionFactory factory) => _factory = factory;

    public WebDavConfig GetOrCreate()
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, server_url, username, password, remote_path,
            enabled, auto_sync, last_sync_at, last_sync_status, updated_at
            FROM webdav_config WHERE id=1;";
        using var r = cmd.ExecuteReader();
        if (r.Read()) return Map(r);

        // 不存在则插入默认空行
        var now = DateTime.UtcNow.ToString("O");
        using var ins = conn.CreateCommand();
        ins.CommandText = @"INSERT INTO webdav_config
            (id, server_url, username, password, remote_path, enabled, auto_sync, updated_at)
            VALUES (1, '', '', '', '/ChildNotes/', 0, 1, @t);";
        ins.Parameters.AddWithValue("@t", now);
        ins.ExecuteNonQuery();

        return new WebDavConfig
        {
            Id = 1,
            RemotePath = "/ChildNotes/",
            AutoSync = true,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Save(WebDavConfig cfg)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO webdav_config
            (id, server_url, username, password, remote_path, enabled, auto_sync, last_sync_at, last_sync_status, updated_at)
            VALUES (1, @url, @u, @p, @rp, @en, @as, @lsa, @lss, @t)
            ON CONFLICT(id) DO UPDATE SET
            server_url=@url, username=@u, password=@p, remote_path=@rp,
            enabled=@en, auto_sync=@as, last_sync_at=@lsa, last_sync_status=@lss, updated_at=@t;";
        cmd.Parameters.AddWithValue("@url", cfg.ServerUrl);
        cmd.Parameters.AddWithValue("@u", cfg.Username);
        cmd.Parameters.AddWithValue("@p", cfg.Password);
        cmd.Parameters.AddWithValue("@rp", string.IsNullOrEmpty(cfg.RemotePath) ? "/ChildNotes/" : cfg.RemotePath);
        cmd.Parameters.AddWithValue("@en", cfg.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@as", cfg.AutoSync ? 1 : 0);
        cmd.Parameters.AddWithValue("@lsa", (object?)cfg.LastSyncAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lss", (object?)cfg.LastSyncStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void UpdateSyncResult(string status, DateTime syncAt)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE webdav_config SET last_sync_at=@t, last_sync_status=@s, updated_at=@t WHERE id=1;";
        cmd.Parameters.AddWithValue("@t", syncAt.ToString("O"));
        cmd.Parameters.AddWithValue("@s", status);
        cmd.ExecuteNonQuery();
    }

    private static WebDavConfig Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        ServerUrl = r.GetString(1),
        Username = r.GetString(2),
        Password = r.GetString(3),
        RemotePath = r.GetString(4),
        Enabled = r.GetInt32(5) == 1,
        AutoSync = r.GetInt32(6) == 1,
        LastSyncAt = r.IsDBNull(7) ? null : DateTime.Parse(r.GetString(7)),
        LastSyncStatus = r.IsDBNull(8) ? null : r.GetString(8),
        UpdatedAt = DateTime.Parse(r.GetString(9))
    };
}
