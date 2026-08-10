using Microsoft.Data.Sqlite;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class BabyRepository : BaseRepository
{
    public BabyRepository(DbConnectionFactory factory) : base(factory) { }

    private const string SelectBase =
        "SELECT id, user_id, name, avatar, gender, birth_date, is_deleted, created_at, updated_at, " +
        "device_id, synced_at FROM baby";

    public List<Baby> GetByUser(string userId)
        => Query(SelectBase + " WHERE user_id = @u ORDER BY id",
            cmd => cmd.Add("@u", userId), Map);

    public Baby? FindById(string id)
        => QueryFirstOrDefault(SelectBase + " WHERE id = @i", cmd => cmd.Add("@i", id), Map);

    public string Insert(Baby baby)
    {
        // 宝宝 ID 用户可见（用于加入家庭），截取 GUID 前 8 位缩短显示。
        // 16^8=42 亿组合，用户量远小于此，冲突概率可忽略；后期用户上来再加后端唯一性校验。
        baby.Id = Guid.NewGuid().ToString("N")[..8];
        ExecuteNonQuery(
            @"INSERT INTO baby (id, user_id, name, avatar, gender, birth_date, created_at, updated_at)
              VALUES (@i, @u, @n, @a, @g, @b, @c, @c)",
            cmd => cmd
                .Add("@i", baby.Id)
                .Add("@u", baby.UserId)
                .Add("@n", baby.Name)
                .Add("@a", (object?)baby.Avatar ?? DBNull.Value)
                .Add("@g", (object?)baby.Gender ?? DBNull.Value)
                .Add("@b", (object?)(baby.BirthDate?.ToString("yyyy-MM-dd")) ?? DBNull.Value)
                .AddUtc("@c", DateTime.UtcNow));
        return baby.Id;
    }

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

    public void Delete(string id)
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
            INSERT INTO baby (id, user_id, name, avatar, gender, birth_date, is_deleted, created_at, updated_at)
            VALUES (@i, @u, @n, @a, @g, @b, @d, @c, @t)
            ON CONFLICT(id) DO UPDATE SET
                user_id = excluded.user_id,
                name = excluded.name,
                avatar = excluded.avatar,
                gender = excluded.gender,
                birth_date = excluded.birth_date,
                is_deleted = excluded.is_deleted,
                updated_at = excluded.updated_at
            WHERE excluded.updated_at > baby.updated_at";
        cmd.Add("@i", item.Id)
           .Add("@u", item.UserId)
           .Add("@n", item.Name)
           .Add("@a", (object?)item.Avatar ?? DBNull.Value)
           .Add("@g", (object?)item.Gender ?? DBNull.Value)
           .Add("@b", (object?)(item.BirthDate?.ToString("yyyy-MM-dd")) ?? DBNull.Value)
           .Add("@d", item.Deleted ? 1 : 0)
           .AddUtc("@c", item.CreatedAt)
           .AddUtc("@t", item.UpdatedAt);
        // 返回受影响行数：1 表示写入（INSERT 或 UPDATE），0 表示因 LWW 跳过
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 批量标记宝宝为"已上送"（更新 synced_at）。Push 成功后调用，防止崩溃导致重推。
    /// 优化：原实现逐条 UPDATE，500 条 = 500 次往返。改为按 500 个 id 一批的 IN 子句批量 UPDATE。
    /// </summary>
    public void MarkSynced(IEnumerable<string> ids, DateTime syncedAt)
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
        Id = r.GetString(0),
        UserId = r.GetString(1),
        Name = r.GetString(2),
        Avatar = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        Gender = r.IsDBNull(4) ? string.Empty : r.GetString(4),
        // birth_date 以 "yyyy-MM-dd" 存储（纯日期无时区），Unspecified 即可
        BirthDate = r.IsDBNull(5) ? null : DateTimeExtensions.ParseDb(r.GetString(5)),
        Deleted = r.IsDBNull(6) ? false : r.GetInt64(6) != 0,
        // created_at / updated_at / synced_at 以 UTC 存储，读入应用层统一转 Local（与 RecordRepository.Map 一致）
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(7)).ToLocalTime(),
        UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(8)).ToLocalTime(),
        DeviceId = r.IsDBNull(9) ? null : r.GetString(9),
        SyncedAt = r.IsDBNull(10) ? null : DateTimeExtensions.ParseDb(r.GetString(10)).ToLocalTime(),
    };

    /// <summary>
    /// 一次性迁移：将长度 >8 的 baby.id 截短为前 8 位，并级联更新所有关联表的 baby_id。
    /// 用于把旧的 32 位 GUID 缩短为 8 位短码。迁移幂等：已是 8 位的记录不会被处理。
    /// 注意：必须在同步前执行，否则 ID 不一致会导致同步重复和孤儿记录。
    /// </summary>
    /// <returns>实际迁移的 baby 数量。</returns>
    public int MigrateShortBabyIds()
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();

        // 1. 查出所有需要迁移的 baby（id 长度 >8）
        var toMigrate = new List<(string oldId, string newId)>();
        using (var readCmd = conn.CreateCommand())
        {
            readCmd.Transaction = tx;
            readCmd.CommandText = "SELECT id FROM baby WHERE length(id) > 8";
            using var r = readCmd.ExecuteReader();
            while (r.Read())
            {
                var oldId = r.GetString(0);
                var newId = oldId[..8];
                // 检测截短后是否与已有 ID 冲突（8 位短码已存在且不是当前记录）
                toMigrate.Add((oldId, newId));
            }
        }

        if (toMigrate.Count == 0)
        {
            tx.Commit();
            return 0;
        }

        // 冲突检测：截短后的新 ID 若已存在于 baby 表（且不是源 ID），跳过该条并记日志
        var validMigrations = new List<(string oldId, string newId)>();
        foreach (var (oldId, newId) in toMigrate)
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.Transaction = tx;
            checkCmd.CommandText = "SELECT COUNT(*) FROM baby WHERE id = @new AND id != @old";
            checkCmd.Add("@new", newId).Add("@old", oldId);
            var count = (long)checkCmd.ExecuteScalar()!;
            if (count > 0)
            {
                DevLogger.Log("Migrate", $"MigrateShortBabyIds: 跳过 {oldId} → {newId}（目标 ID 已存在，冲突）");
                continue;
            }
            validMigrations.Add((oldId, newId));
        }

        // 2. 逐条迁移：更新 baby 主键 + 所有关联表的 baby_id
        foreach (var (oldId, newId) in validMigrations)
        {
            DevLogger.Log("Migrate", $"MigrateShortBabyIds: {oldId} → {newId}");
            // baby 主键
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE baby SET id=@new WHERE id=@old";
                cmd.Add("@new", newId).Add("@old", oldId);
                cmd.ExecuteNonQuery();
            }
            // 关联表 baby_id
            foreach (var table in new[] { "baby_member", "child_record", "milestone", "ai_analysis_record" })
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE {table} SET baby_id=@new WHERE baby_id=@old";
                cmd.Add("@new", newId).Add("@old", oldId);
                cmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
        DevLogger.Log("Migrate", $"MigrateShortBabyIds: 迁移完成，共 {validMigrations.Count} 条");
        return validMigrations.Count;
    }
}
