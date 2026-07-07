using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class MilestoneRepository : BaseRepository
{
    public MilestoneRepository(DbConnectionFactory factory) : base(factory) { }

    private const string SelectBase =
        "SELECT id, user_id, baby_id, title, content, record_date, photos_json, created_at, updated_at, " +
        "is_deleted, device_id, synced_at FROM milestone";

    /// <summary>查询当前用户+宝宝下的未删除里程碑（按日期倒序）。</summary>
    public List<Milestone> GetAll(string userId, string? babyId)
    {
        var sql = SelectBase + " WHERE user_id=@uid AND is_deleted=0";
        if (babyId is not null) sql += " AND baby_id=@bid";
        sql += " ORDER BY record_date DESC, id DESC";
        return Query(sql,
            cmd =>
            {
                cmd.Add("@uid", userId);
                if (babyId is not null) cmd.Add("@bid", babyId);
            },
            Map);
    }

    public Milestone? FindById(string id)
        => QueryFirstOrDefault(SelectBase + " WHERE id=@i", cmd => cmd.Add("@i", id), Map);

    public string Insert(Milestone m)
    {
        m.Id = Guid.NewGuid().ToString("N");
        ExecuteNonQuery(
            @"INSERT INTO milestone (id, user_id, baby_id, title, content, record_date, photos_json, is_deleted, device_id, created_at, updated_at)
              VALUES (@id,@uid,@bid,@t,@c,@d,@p,@del,@dev,@n,@n)",
            cmd => cmd
                .Add("@id", m.Id)
                .Add("@uid", m.UserId)
                .Add("@bid", (object?)m.BabyId ?? DBNull.Value)
                .Add("@t", m.Title)
                .Add("@c", (object?)m.Content ?? DBNull.Value)
                .AddDate("@d", m.RecordDate)
                .Add("@p", m.PhotosJson)
                .Add("@del", m.Deleted ? 1 : 0)
                .Add("@dev", (object?)m.DeviceId ?? DBNull.Value)
                .AddUtc("@n", DateTime.UtcNow));
        return m.Id;
    }

    public void Update(Milestone m)
        => ExecuteNonQuery(
            "UPDATE milestone SET title=@t, content=@c, record_date=@d, photos_json=@p, baby_id=@bid, is_deleted=@del, updated_at=@n WHERE id=@id",
            cmd => cmd
                .Add("@t", m.Title)
                .Add("@c", (object?)m.Content ?? DBNull.Value)
                .AddDate("@d", m.RecordDate)
                .Add("@p", m.PhotosJson)
                .Add("@bid", (object?)m.BabyId ?? DBNull.Value)
                .Add("@del", m.Deleted ? 1 : 0)
                .AddUtc("@n", DateTime.UtcNow)
                .Add("@id", m.Id));

    /// <summary>软删里程碑（is_deleted=1），便于同步通道传递删除事件。</summary>
    public void Delete(string id)
        => ExecuteNonQuery(
            "UPDATE milestone SET is_deleted=1, updated_at=@n WHERE id=@id",
            cmd => cmd.AddUtc("@n", DateTime.UtcNow).Add("@id", id));

    /// <summary>获取本地指定更新时间之后的所有里程碑（含已软删，用于增量上送）。</summary>
    public List<Milestone> GetByUpdatedAt(DateTime since)
        => Query(SelectBase + " WHERE updated_at > @s ORDER BY updated_at",
            cmd => cmd.AddUtc("@s", since), Map);

    /// <summary>以 LWW（updated_at 比较）合并远端下发的里程碑。返回是否实际写入。</summary>
    public bool UpsertFromSync(Milestone item)
    {
        using var conn = OpenConnection();
        return UpsertFromSync(item, conn, null);
    }

    /// <summary>在指定连接/事务上执行 LWW 合并。Pull 循环共享同一事务。</summary>
    public bool UpsertFromSync(Milestone item, SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO milestone (id, user_id, baby_id, title, content, record_date, photos_json, is_deleted, device_id, created_at, updated_at)
            VALUES (@i, @u, @bid, @t, @c, @d, @p, @del, @dev, @crt, @t2)
            ON CONFLICT(id) DO UPDATE SET
                user_id = excluded.user_id,
                baby_id = excluded.baby_id,
                title = excluded.title,
                content = excluded.content,
                record_date = excluded.record_date,
                photos_json = excluded.photos_json,
                is_deleted = excluded.is_deleted,
                updated_at = excluded.updated_at
            WHERE excluded.updated_at > milestone.updated_at";
        cmd.Add("@i", item.Id)
           .Add("@u", item.UserId)
           .Add("@bid", (object?)item.BabyId ?? DBNull.Value)
           .Add("@t", item.Title)
           .Add("@c", (object?)item.Content ?? DBNull.Value)
           .AddDate("@d", item.RecordDate)
           .Add("@p", item.PhotosJson)
           .Add("@del", item.Deleted ? 1 : 0)
           .Add("@dev", (object?)item.DeviceId ?? DBNull.Value)
           .AddUtc("@crt", item.CreatedAt)
           .AddUtc("@t2", item.UpdatedAt);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>批量标记里程碑为"已上送"（更新 synced_at）。</summary>
    public void MarkSynced(IEnumerable<string> ids, DateTime syncedAt)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        const int BatchSize = 500;
        for (var i = 0; i < idList.Count; i += BatchSize)
        {
            var batch = idList.Skip(i).Take(BatchSize).ToList();
            var paramNames = Enumerable.Range(0, batch.Count).Select(k => "@id" + k).ToList();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE milestone SET synced_at=@t WHERE id IN ({string.Join(",", paramNames)})";
            cmd.AddUtc("@t", syncedAt);
            for (var j = 0; j < batch.Count; j++)
                cmd.Parameters.AddWithValue(paramNames[j], batch[j]);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static Milestone Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        UserId = r.GetString(1),
        BabyId = r.IsDBNull(2) ? null : r.GetString(2),
        Title = r.GetString(3),
        Content = r.IsDBNull(4) ? null : r.GetString(4),
        // record_date 以 "yyyy-MM-dd" 存储（纯日期无时区），Unspecified 即可
        RecordDate = DateTimeExtensions.ParseDb(r.GetString(5)),
        PhotosJson = r.IsDBNull(6) ? "[]" : r.GetString(6),
        // created_at / updated_at / synced_at 以 UTC 存储，读入应用层统一转 Local
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(7)).ToLocalTime(),
        UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(8)).ToLocalTime(),
        Deleted = r.IsDBNull(9) ? false : r.GetInt64(9) != 0,
        DeviceId = r.IsDBNull(10) ? null : r.GetString(10),
        SyncedAt = r.IsDBNull(11) ? null : DateTimeExtensions.ParseDb(r.GetString(11)).ToLocalTime(),
    };
}
