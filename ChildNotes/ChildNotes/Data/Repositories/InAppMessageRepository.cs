using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

/// <summary>
/// 应用内消息仓储：CRUD + 标记已读 + 未读数查询。
/// </summary>
public sealed class InAppMessageRepository : BaseRepository
{
    public InAppMessageRepository(DbConnectionFactory factory) : base(factory) { }

    /// <summary>查询指定用户的全部消息（按时间倒序）。</summary>
    public List<InAppMessage> GetByUser(string userId, int limit = 100)
    {
        const string sql = @"
SELECT id, user_id, title, body, category, data_json, is_read, created_at, read_at
FROM in_app_message
WHERE user_id = @uid
ORDER BY created_at DESC
LIMIT @limit;";
        return Query(sql,
            cmd => { cmd.Add("uid", userId); cmd.Add("limit", limit); },
            MapEntity);
    }

    /// <summary>统计未读消息数。</summary>
    public int CountUnread(string userId)
    {
        const string sql = "SELECT COUNT(*) FROM in_app_message WHERE user_id = @uid AND is_read = 0;";
        var result = ExecuteScalar(sql, cmd => cmd.Add("uid", userId));
        return result is long n ? (int)n : 0;
    }

    /// <summary>插入新消息（已存在则忽略，幂等）。</summary>
    public void Insert(InAppMessage msg)
    {
        const string sql = @"
INSERT OR IGNORE INTO in_app_message
    (id, user_id, title, body, category, data_json, is_read, created_at, read_at)
VALUES
    (@id, @uid, @title, @body, @cat, @data, @read, @created, @readat);";
        ExecuteNonQuery(sql, cmd =>
        {
            cmd.Add("id", msg.Id);
            cmd.Add("uid", msg.UserId);
            cmd.Add("title", msg.Title);
            cmd.Add("body", msg.Body);
            cmd.Add("cat", msg.Category);
            cmd.Add("data", msg.DataJson);
            cmd.Add("read", msg.IsRead ? 1 : 0);
            cmd.Add("created", msg.CreatedAt);
            cmd.Add("readat", (object?)msg.ReadAt ?? DBNull.Value);
        });
    }

    /// <summary>批量插入消息。</summary>
    public void InsertBatch(IEnumerable<InAppMessage> messages)
    {
        foreach (var msg in messages) Insert(msg);
    }

    /// <summary>标记单条消息为已读。</summary>
    public void MarkAsRead(string messageId)
    {
        const string sql = @"
UPDATE in_app_message
SET is_read = 1, read_at = @readat
WHERE id = @id AND is_read = 0;";
        var nowUtc = DateTime.UtcNow.ToString("O");
        ExecuteNonQuery(sql, cmd =>
        {
            cmd.Add("id", messageId);
            cmd.Add("readat", nowUtc);
        });
    }

    /// <summary>标记用户全部消息为已读。</summary>
    public void MarkAllAsRead(string userId)
    {
        const string sql = @"
UPDATE in_app_message
SET is_read = 1, read_at = @readat
WHERE user_id = @uid AND is_read = 0;";
        var nowUtc = DateTime.UtcNow.ToString("O");
        ExecuteNonQuery(sql, cmd =>
        {
            cmd.Add("uid", userId);
            cmd.Add("readat", nowUtc);
        });
    }

    /// <summary>删除指定消息。</summary>
    public void Delete(string messageId)
    {
        const string sql = "DELETE FROM in_app_message WHERE id = @id;";
        ExecuteNonQuery(sql, cmd => cmd.Add("id", messageId));
    }

    /// <summary>清理指定用户 N 天前的已读消息。</summary>
    public int CleanupOldReadMessages(string userId, int olderThanDays = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays).ToString("O");
        const string sql = @"
DELETE FROM in_app_message
WHERE user_id = @uid AND is_read = 1 AND created_at < @cutoff;";
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("uid", userId);
        cmd.Add("cutoff", cutoff);
        return cmd.ExecuteNonQuery();
    }

    private static InAppMessage MapEntity(SqliteDataReader r)
    {
        return new InAppMessage
        {
            Id = r.GetString(0),
            UserId = r.GetString(1),
            Title = r.GetString(2),
            Body = r.GetString(3),
            Category = r.GetString(4),
            DataJson = r.GetString(5),
            IsRead = r.GetInt32(6) == 1,
            CreatedAt = r.GetString(7),
            ReadAt = r.IsDBNull(8) ? null : r.GetString(8)
        };
    }
}
