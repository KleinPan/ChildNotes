using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

/// <summary>
/// sync_log 表的访问器：记录每次同步的结果，仅保留最近 10 条。
/// 表结构：id / done_at / status / data_volume / message。
/// 写入后自动裁剪超出上限的旧记录，保证表大小可控。
/// </summary>
public sealed class SyncLogRepository : BaseRepository
{
    /// <summary>最多保留的日志条数（与 UI 展示数量一致）。</summary>
    private const int MaxEntries = 10;

    public SyncLogRepository(DbConnectionFactory factory) : base(factory) { }

    /// <summary>按时间倒序获取最近 10 条日志（最新的在最前）。</summary>
    public List<SyncLogEntry> GetRecent()
    {
        // MaxEntries 是 const int，但 string + int 在 C# 中不构成编译时常量表达式，
        // 不能赋给 const string，故改为只读局部变量。
        string sql =
            "SELECT id, done_at, status, data_volume, message FROM sync_log " +
            "ORDER BY id DESC LIMIT " + MaxEntries;
        return Query(sql, _ => { }, Map);
    }

    /// <summary>插入一条日志，并裁剪超出上限的旧记录。返回自增 id。</summary>
    public long Add(SyncLogEntry entry)
    {
        ExecuteNonQuery(
            @"INSERT INTO sync_log (done_at, status, data_volume, message)
              VALUES (@t, @s, @v, @m)",
            cmd => cmd
                .AddUtc("@t", entry.DoneAt)
                .AddString("@s", entry.Status, emptyAsNull: false)
                .AddString("@v", entry.DataVolume, emptyAsNull: false)
                .AddString("@m", entry.Message, emptyAsNull: false));

        // 裁剪：保留最近 MaxEntries 条（按 id 倒序），删除更早的
        ExecuteNonQuery(
            "DELETE FROM sync_log WHERE id NOT IN (SELECT id FROM sync_log ORDER BY id DESC LIMIT " + MaxEntries + ")",
            _ => { });

        // 返回最新插入的 id（供 running → 终态更新使用）
        return (long)(ExecuteScalar("SELECT last_insert_rowid()", _ => { }) ?? 0L);
    }

    /// <summary>根据 id 更新某条日志的最终状态（用于 running → success/failed）。</summary>
    public void UpdateFinal(long id, DateTime doneAt, string status, string dataVolume, string message)
    {
        if (id <= 0) return;
        ExecuteNonQuery(
            "UPDATE sync_log SET done_at=@t, status=@s, data_volume=@v, message=@m WHERE id=@id",
            cmd => cmd
                .AddUtc("@t", doneAt)
                .AddString("@s", status, emptyAsNull: false)
                .AddString("@v", dataVolume, emptyAsNull: false)
                .AddString("@m", message, emptyAsNull: false)
                .Add("@id", id));
    }

    private static SyncLogEntry Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        DoneAt = DateTimeExtensions.ParseDb(r.GetString(1)).ToLocalTime(),
        Status = r.GetString(2),
        DataVolume = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        Message = r.IsDBNull(4) ? string.Empty : r.GetString(4),
    };
}
