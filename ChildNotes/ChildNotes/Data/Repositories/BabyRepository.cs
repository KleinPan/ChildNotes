using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class BabyRepository : BaseRepository
{
    public BabyRepository(DbConnectionFactory factory) : base(factory) { }

    private const string SelectBase =
        "SELECT id, user_id, name, avatar, gender, birth_date, created_at, updated_at, " +
        "device_id, synced_at FROM baby";

    public List<Baby> GetByUser(long userId)
        => Query(SelectBase + " WHERE user_id = @u ORDER BY id",
            cmd => cmd.Add("@u", userId), Map);

    public Baby? FindById(long id)
        => QueryFirstOrDefault(SelectBase + " WHERE id = @i", cmd => cmd.Add("@i", id), Map);

    public long Insert(Baby baby)
        => (long)ExecuteScalar(
            @"INSERT INTO baby (user_id, name, avatar, gender, birth_date, created_at, updated_at)
              VALUES (@u, @n, @a, @g, @b, @c, @c); SELECT last_insert_rowid();",
            cmd => cmd
                .Add("@u", baby.UserId)
                .Add("@n", baby.Name)
                .Add("@a", (object?)baby.Avatar ?? DBNull.Value)
                .Add("@g", (object?)baby.Gender ?? DBNull.Value)
                .Add("@b", (object?)(baby.BirthDate?.ToString("yyyy-MM-dd")) ?? DBNull.Value)
                .AddUtc("@c", DateTime.UtcNow))!;

    public void Update(Baby baby)
        => ExecuteNonQuery(
            "UPDATE baby SET name=@n, avatar=@a, gender=@g, birth_date=@b, updated_at=@t WHERE id=@i",
            cmd => cmd
                .Add("@n", baby.Name)
                .Add("@a", (object?)baby.Avatar ?? DBNull.Value)
                .Add("@g", (object?)baby.Gender ?? DBNull.Value)
                .Add("@b", (object?)(baby.BirthDate?.ToString("yyyy-MM-dd")) ?? DBNull.Value)
                .AddUtc("@t", DateTime.UtcNow)
                .Add("@i", baby.Id));

    public void Delete(long id)
        => ExecuteNonQuery("DELETE FROM baby WHERE id=@i", cmd => cmd.Add("@i", id));

    /// <summary>获取本地指定更新时间之后的所有宝宝（含已软删，用于增量上送）。</summary>
    public List<Baby> GetByUpdatedAt(DateTime since)
        => Query(SelectBase + " WHERE updated_at > @s ORDER BY updated_at",
            cmd => cmd.AddUtc("@s", since), Map);

    /// <summary>
    /// 以 LWW（updated_at 比较）合并远端下发的 baby。返回是否实际写入。
    /// 优化：原实现 SELECT + UPDATE/INSERT 两次往返，改用单条 INSERT ON CONFLICT 一次完成。
    /// SQLite 的 ON CONFLICT DO UPDATE WHERE 支持在冲突时按条件执行，LWW 逻辑由 WHERE 表达。
    /// </summary>
    public bool UpsertFromSync(Baby item)
    {
        using var conn = OpenConnection();
        return UpsertFromSync(item, conn, null);
    }

    /// <summary>
    /// 在指定连接/事务上执行 LWW 合并。Pull 循环通过此重载共享同一事务，避免每行重新开连。
    /// </summary>
    public bool UpsertFromSync(Baby item, SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // INSERT ... ON CONFLICT(id) DO UPDATE：仅在 excluded.updated_at > baby.updated_at 时更新
        cmd.CommandText = @"
            INSERT INTO baby (id, user_id, name, avatar, gender, birth_date, created_at, updated_at)
            VALUES (@i, @u, @n, @a, @g, @b, @c, @t)
            ON CONFLICT(id) DO UPDATE SET
                user_id = excluded.user_id,
                name = excluded.name,
                avatar = excluded.avatar,
                gender = excluded.gender,
                birth_date = excluded.birth_date,
                updated_at = excluded.updated_at
            WHERE excluded.updated_at > baby.updated_at";
        cmd.Add("@i", item.Id)
           .Add("@u", item.UserId)
           .Add("@n", item.Name)
           .Add("@a", (object?)item.Avatar ?? DBNull.Value)
           .Add("@g", (object?)item.Gender ?? DBNull.Value)
           .Add("@b", (object?)(item.BirthDate?.ToString("yyyy-MM-dd")) ?? DBNull.Value)
           .AddUtc("@c", item.CreatedAt)
           .AddUtc("@t", item.UpdatedAt);
        // 返回受影响行数：1 表示写入（INSERT 或 UPDATE），0 表示因 LWW 跳过
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 批量标记宝宝为"已上送"（更新 synced_at）。Push 成功后调用，防止崩溃导致重推。
    /// 优化：原实现逐条 UPDATE，500 条 = 500 次往返。改为按 500 个 id 一批的 IN 子句批量 UPDATE。
    /// </summary>
    public void MarkSynced(IEnumerable<long> ids, DateTime syncedAt)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        // SQLite 默认参数上限 999，单批最多 500 个 id 安全
        const int BatchSize = 500;
        for (var i = 0; i < idList.Count; i += BatchSize)
        {
            var batch = idList.Skip(i).Take(BatchSize).ToList();
            var paramNames = Enumerable.Range(0, batch.Count).Select(k => "@id" + k).ToList();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE baby SET synced_at=@t WHERE id IN ({string.Join(",", paramNames)})";
            cmd.AddUtc("@t", syncedAt);
            for (var j = 0; j < batch.Count; j++)
                cmd.Parameters.AddWithValue(paramNames[j], batch[j]);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static Baby Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        UserId = r.GetInt64(1),
        Name = r.GetString(2),
        Avatar = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        Gender = r.IsDBNull(4) ? string.Empty : r.GetString(4),
        BirthDate = r.IsDBNull(5) ? null : DateTimeExtensions.ParseDb(r.GetString(5)),
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(6)),
        UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(7)),
        DeviceId = r.IsDBNull(8) ? null : r.GetString(8),
        SyncedAt = r.IsDBNull(9) ? null : DateTimeExtensions.ParseDb(r.GetString(9)),
    };
}
