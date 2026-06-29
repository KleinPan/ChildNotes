using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class RecordRepository : BaseRepository
{
    public RecordRepository(DbConnectionFactory factory) : base(factory) { }

    private const string SelectBase =
        "SELECT id, user_id, baby_id, record_type, record_sub_type, record_date, record_time, " +
        "amount_ml, duration_sec, left_duration_sec, right_duration_sec, abnormal_flag, " +
        "temperature_value, height_cm, weight_kg, payload_json, deleted, created_at, updated_at, " +
        "device_id, synced_at FROM child_record";

    public long Insert(ChildRecord rec)
        => (long)ExecuteScalar(
            @"INSERT INTO child_record
              (user_id, baby_id, record_type, record_sub_type, record_date, record_time,
               amount_ml, duration_sec, left_duration_sec, right_duration_sec, abnormal_flag,
               temperature_value, height_cm, weight_kg, payload_json, deleted, created_at, updated_at)
              VALUES (@uid,@bid,@rt,@rst,@rd,@rtm,@aml,@ds,@lds,@rds,@af,@tv,@hc,@wk,@pj,0,@c,@c);
              SELECT last_insert_rowid();",
            cmd => AddParams(cmd, rec))!;

    public void Update(ChildRecord rec)
        => ExecuteNonQuery(
            @"UPDATE child_record SET
              record_type=@rt, record_sub_type=@rst, record_date=@rd, record_time=@rtm,
              amount_ml=@aml, duration_sec=@ds, left_duration_sec=@lds, right_duration_sec=@rds,
              abnormal_flag=@af, temperature_value=@tv, height_cm=@hc, weight_kg=@wk,
              payload_json=@pj, updated_at=@c WHERE id=@id",
            cmd =>
            {
                AddParams(cmd, rec);
                cmd.Add("@id", rec.Id);
            });

    public void SoftDelete(long id)
        => ExecuteNonQuery(
            "UPDATE child_record SET deleted=1, updated_at=@t WHERE id=@id",
            cmd => cmd.AddUtc("@t", DateTime.UtcNow).Add("@id", id));

    public ChildRecord? FindById(long id)
        => QueryFirstOrDefault(SelectBase + " WHERE id=@id AND deleted=0",
            cmd => cmd.Add("@id", id), Map);

    public List<ChildRecord> GetByDate(long userId, long? babyId, DateTime date)
        => QueryRecords(userId, babyId, "record_date=@d", cmd => cmd.AddDate("@d", date));

    public List<ChildRecord> GetByDateRange(long userId, long? babyId, DateTime start, DateTime end)
        => QueryRecords(userId, babyId, "record_date>=@s AND record_date<=@e",
            cmd => cmd.AddDate("@s", start).AddDate("@e", end));

    public ChildRecord? GetLatest(long userId, long? babyId, string recordType)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = SelectBase + " WHERE user_id=@uid AND record_type=@rt AND deleted=0";
        if (babyId.HasValue) sql += " AND baby_id=@bid";
        sql += " ORDER BY record_time DESC LIMIT 1";
        cmd.CommandText = sql;
        cmd.Add("@uid", userId).Add("@rt", recordType);
        if (babyId.HasValue) cmd.Add("@bid", babyId.Value);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    public List<ChildRecord> GetByType(long userId, long? babyId, string recordType, int limit = 100)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = SelectBase + " WHERE user_id=@uid AND record_type=@rt AND deleted=0";
        if (babyId.HasValue) sql += " AND baby_id=@bid";
        sql += " ORDER BY record_time DESC LIMIT @lim";
        cmd.CommandText = sql;
        cmd.Add("@uid", userId).Add("@rt", recordType).Add("@lim", limit);
        if (babyId.HasValue) cmd.Add("@bid", babyId.Value);
        using var r = cmd.ExecuteReader();
        var list = new List<ChildRecord>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    /// <summary>获取本地指定更新时间之后的所有记录（含已软删，用于增量上送）。</summary>
    public List<ChildRecord> GetByUpdatedAt(DateTime since)
        => Query(SelectBase + " WHERE updated_at > @s ORDER BY updated_at",
            cmd => cmd.AddUtc("@s", since), Map);

    /// <summary>以 LWW（updated_at 比较）合并远端下发的记录。返回是否实际写入。</summary>
    public bool UpsertFromSync(ChildRecord item)
    {
        using var conn = OpenConnection();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT updated_at FROM child_record WHERE id=@i";
        check.Add("@i", item.Id);
        var existing = check.ExecuteScalar() as string;
        if (existing is not null)
        {
            var existingAt = DateTimeExtensions.ParseDb(existing);
            if (item.UpdatedAt <= existingAt) return false; // 本地较新，跳过
            using var upd = conn.CreateCommand();
            upd.CommandText = @"UPDATE child_record SET
                user_id=@uid, baby_id=@bid, record_type=@rt, record_sub_type=@rst,
                record_date=@rd, record_time=@rtm, amount_ml=@aml, duration_sec=@ds,
                left_duration_sec=@lds, right_duration_sec=@rds, abnormal_flag=@af,
                temperature_value=@tv, height_cm=@hc, weight_kg=@wk, payload_json=@pj,
                deleted=@d, updated_at=@t WHERE id=@id";
            AddSyncParams(upd, item);
            upd.ExecuteNonQuery();
            return true;
        }
        using var ins = conn.CreateCommand();
        ins.CommandText = @"INSERT INTO child_record
            (id, user_id, baby_id, record_type, record_sub_type, record_date, record_time,
             amount_ml, duration_sec, left_duration_sec, right_duration_sec, abnormal_flag,
             temperature_value, height_cm, weight_kg, payload_json, deleted, created_at, updated_at)
            VALUES (@id, @uid, @bid, @rt, @rst, @rd, @rtm, @aml, @ds, @lds, @rds, @af, @tv, @hc, @wk, @pj, @d, @c, @t)";
        AddSyncParams(ins, item);
        ins.AddUtc("@c", item.CreatedAt);
        ins.ExecuteNonQuery();
        return true;
    }

    private List<ChildRecord> QueryRecords(long userId, long? babyId, string condition, Action<SqliteCommand> addExtra)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = SelectBase + " WHERE user_id=@uid AND deleted=0 AND " + condition;
        if (babyId.HasValue) sql += " AND baby_id=@bid";
        sql += " ORDER BY record_time ASC";
        cmd.CommandText = sql;
        cmd.Add("@uid", userId);
        if (babyId.HasValue) cmd.Add("@bid", babyId.Value);
        addExtra(cmd);
        using var r = cmd.ExecuteReader();
        var list = new List<ChildRecord>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    private static void AddSyncParams(SqliteCommand cmd, ChildRecord rec)
    {
        cmd.Add("@id", rec.Id)
           .Add("@uid", rec.UserId)
           .Add("@bid", (object?)rec.BabyId ?? DBNull.Value)
           .Add("@rt", rec.RecordType)
           .Add("@rst", (object?)rec.RecordSubType ?? DBNull.Value)
           .AddDate("@rd", rec.RecordDate)
           .AddUtc("@rtm", rec.RecordTime)
           .Add("@aml", (object?)rec.AmountMl ?? DBNull.Value)
           .Add("@ds", (object?)rec.DurationSec ?? DBNull.Value)
           .Add("@lds", (object?)rec.LeftDurationSec ?? DBNull.Value)
           .Add("@rds", (object?)rec.RightDurationSec ?? DBNull.Value)
           .Add("@af", rec.AbnormalFlag.HasValue ? (rec.AbnormalFlag.Value ? 1 : 0) : (object)DBNull.Value)
           .Add("@tv", (object?)rec.TemperatureValue ?? DBNull.Value)
           .Add("@hc", (object?)rec.HeightCm ?? DBNull.Value)
           .Add("@wk", (object?)rec.WeightKg ?? DBNull.Value)
           .Add("@pj", rec.PayloadJson ?? "{}")
           .Add("@d", rec.Deleted ? 1 : 0)
           .AddUtc("@t", rec.UpdatedAt);
    }

    private static void AddParams(SqliteCommand cmd, ChildRecord rec)
    {
        cmd.Add("@uid", rec.UserId)
           .Add("@bid", (object?)rec.BabyId ?? DBNull.Value)
           .Add("@rt", rec.RecordType)
           .Add("@rst", (object?)rec.RecordSubType ?? DBNull.Value)
           .AddDate("@rd", rec.RecordDate)
           .AddUtc("@rtm", rec.RecordTime)
           .Add("@aml", (object?)rec.AmountMl ?? DBNull.Value)
           .Add("@ds", (object?)rec.DurationSec ?? DBNull.Value)
           .Add("@lds", (object?)rec.LeftDurationSec ?? DBNull.Value)
           .Add("@rds", (object?)rec.RightDurationSec ?? DBNull.Value)
           .Add("@af", rec.AbnormalFlag.HasValue ? (rec.AbnormalFlag.Value ? 1 : 0) : (object)DBNull.Value)
           .Add("@tv", (object?)rec.TemperatureValue ?? DBNull.Value)
           .Add("@hc", (object?)rec.HeightCm ?? DBNull.Value)
           .Add("@wk", (object?)rec.WeightKg ?? DBNull.Value)
           .Add("@pj", rec.PayloadJson)
           .AddUtc("@c", DateTime.UtcNow);
    }

    /// <summary>
    /// 批量标记记录为"已上送"（更新 synced_at）。Push 成功后调用，防止崩溃导致重推。
    /// </summary>
    public void MarkSynced(IEnumerable<long> ids, DateTime syncedAt)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE child_record SET synced_at=@t WHERE id=@id";
        cmd.AddUtc("@t", syncedAt);
        var idParam = cmd.Parameters.AddWithValue("@id", (long)0);
        foreach (var id in idList)
        {
            idParam.Value = id;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static ChildRecord Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        UserId = r.GetInt64(1),
        BabyId = r.IsDBNull(2) ? null : r.GetInt64(2),
        RecordType = r.GetString(3),
        RecordSubType = r.IsDBNull(4) ? null : r.GetString(4),
        RecordDate = DateTimeExtensions.ParseDb(r.GetString(5)),
        RecordTime = DateTimeExtensions.ParseDb(r.GetString(6)),
        AmountMl = r.IsDBNull(7) ? null : r.GetInt32(7),
        DurationSec = r.IsDBNull(8) ? null : r.GetInt32(8),
        LeftDurationSec = r.IsDBNull(9) ? null : r.GetInt32(9),
        RightDurationSec = r.IsDBNull(10) ? null : r.GetInt32(10),
        AbnormalFlag = r.IsDBNull(11) ? null : r.GetInt32(11) == 1,
        TemperatureValue = r.IsDBNull(12) ? null : r.GetDecimal(12),
        HeightCm = r.IsDBNull(13) ? null : r.GetDecimal(13),
        WeightKg = r.IsDBNull(14) ? null : r.GetDecimal(14),
        PayloadJson = r.GetString(15),
        Deleted = r.IsDBNull(16) ? false : r.GetInt32(16) == 1,
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(17)),
        UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(18)),
        DeviceId = r.IsDBNull(19) ? null : r.GetString(19),
        SyncedAt = r.IsDBNull(20) ? null : DateTimeExtensions.ParseDb(r.GetString(20)),
    };
}
