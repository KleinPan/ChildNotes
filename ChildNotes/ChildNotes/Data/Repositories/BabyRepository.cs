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

    /// <summary>以 LWW（updated_at 比较）合并远端下发的 baby。返回是否实际写入。</summary>
    public bool UpsertFromSync(Baby item)
    {
        // 需要先查后写，无法走通用 ExecuteNonQuery/Query 路径
        using var conn = OpenConnection();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT updated_at FROM baby WHERE id=@i";
        check.Add("@i", item.Id);
        var existing = check.ExecuteScalar() as string;
        if (existing is not null)
        {
            var existingAt = DateTimeExtensions.ParseDb(existing);
            if (item.UpdatedAt <= existingAt) return false; // 本地较新，跳过
            using var upd = conn.CreateCommand();
            upd.CommandText = "UPDATE baby SET user_id=@u, name=@n, avatar=@a, gender=@g, birth_date=@b, updated_at=@t WHERE id=@i";
            upd.Add("@u", item.UserId)
               .Add("@n", item.Name)
               .Add("@a", (object?)item.Avatar ?? DBNull.Value)
               .Add("@g", (object?)item.Gender ?? DBNull.Value)
               .Add("@b", (object?)(item.BirthDate?.ToString("yyyy-MM-dd")) ?? DBNull.Value)
               .AddUtc("@t", item.UpdatedAt)
               .Add("@i", item.Id);
            upd.ExecuteNonQuery();
            return true;
        }
        using var ins = conn.CreateCommand();
        ins.CommandText = @"INSERT INTO baby (id, user_id, name, avatar, gender, birth_date, created_at, updated_at)
            VALUES (@i, @u, @n, @a, @g, @b, @c, @t)";
        ins.Add("@i", item.Id)
           .Add("@u", item.UserId)
           .Add("@n", item.Name)
           .Add("@a", (object?)item.Avatar ?? DBNull.Value)
           .Add("@g", (object?)item.Gender ?? DBNull.Value)
           .Add("@b", (object?)(item.BirthDate?.ToString("yyyy-MM-dd")) ?? DBNull.Value)
           .AddUtc("@c", item.CreatedAt)
           .AddUtc("@t", item.UpdatedAt);
        ins.ExecuteNonQuery();
        return true;
    }

    /// <summary>
    /// 批量标记宝宝为"已上送"（更新 synced_at）。Push 成功后调用，防止崩溃导致重推。
    /// </summary>
    public void MarkSynced(IEnumerable<long> ids, DateTime syncedAt)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE baby SET synced_at=@t WHERE id=@id";
        cmd.AddUtc("@t", syncedAt);
        var idParam = cmd.Parameters.AddWithValue("@id", (long)0);
        foreach (var id in idList)
        {
            idParam.Value = id;
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
