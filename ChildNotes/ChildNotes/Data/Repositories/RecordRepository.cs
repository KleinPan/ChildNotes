using System.Text.Json;
using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class RecordRepository
{
    private readonly DbConnectionFactory _factory;

    public RecordRepository(DbConnectionFactory factory) => _factory = factory;

    public long Insert(ChildRecord rec)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO child_record
            (user_id, baby_id, record_type, record_sub_type, record_date, record_time,
             amount_ml, duration_sec, left_duration_sec, right_duration_sec, abnormal_flag,
             temperature_value, height_cm, weight_kg, payload_json, deleted, created_at, updated_at)
            VALUES (@uid,@bid,@rt,@rst,@rd,@rtm,@aml,@ds,@lds,@rds,@af,@tv,@hc,@wk,@pj,0,@c,@c);
            SELECT last_insert_rowid();";
        AddParams(cmd, rec);
        return (long)cmd.ExecuteScalar()!;
    }

    public void Update(ChildRecord rec)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE child_record SET
            record_type=@rt, record_sub_type=@rst, record_date=@rd, record_time=@rtm,
            amount_ml=@aml, duration_sec=@ds, left_duration_sec=@lds, right_duration_sec=@rds,
            abnormal_flag=@af, temperature_value=@tv, height_cm=@hc, weight_kg=@wk,
            payload_json=@pj, updated_at=@c WHERE id=@id";
        AddParams(cmd, rec);
        cmd.Parameters.AddWithValue("@id", rec.Id);
        cmd.ExecuteNonQuery();
    }

    public void SoftDelete(long id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE child_record SET deleted=1, updated_at=@t WHERE id=@id";
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public ChildRecord? FindById(long id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectBase + " WHERE id=@id AND deleted=0";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    public List<ChildRecord> GetByDate(long userId, long? babyId, DateTime date)
    {
        return Query(userId, babyId, "record_date=@d", ("@d", date.ToString("yyyy-MM-dd")));
    }

    public List<ChildRecord> GetByDateRange(long userId, long? babyId, DateTime start, DateTime end)
    {
        return Query(userId, babyId, "record_date>=@s AND record_date<=@e",
            ("@s", start.ToString("yyyy-MM-dd")), ("@e", end.ToString("yyyy-MM-dd")));
    }

    public ChildRecord? GetLatest(long userId, long? babyId, string recordType)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sql = SelectBase + " WHERE user_id=@uid AND record_type=@rt AND deleted=0";
        if (babyId.HasValue) sql += " AND baby_id=@bid";
        sql += " ORDER BY record_time DESC LIMIT 1";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@rt", recordType);
        if (babyId.HasValue) cmd.Parameters.AddWithValue("@bid", babyId.Value);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    public List<ChildRecord> GetByType(long userId, long? babyId, string recordType, int limit = 100)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sql = SelectBase + " WHERE user_id=@uid AND record_type=@rt AND deleted=0";
        if (babyId.HasValue) sql += " AND baby_id=@bid";
        sql += " ORDER BY record_time DESC LIMIT @lim";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@rt", recordType);
        if (babyId.HasValue) cmd.Parameters.AddWithValue("@bid", babyId.Value);
        cmd.Parameters.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<ChildRecord>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    private const string SelectBase =
        "SELECT id, user_id, baby_id, record_type, record_sub_type, record_date, record_time, " +
        "amount_ml, duration_sec, left_duration_sec, right_duration_sec, abnormal_flag, " +
        "temperature_value, height_cm, weight_kg, payload_json, created_at, updated_at FROM child_record";

    private List<ChildRecord> Query(long userId, long? babyId, string condition, params (string, object)[] extra)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sql = SelectBase + " WHERE user_id=@uid AND deleted=0 AND " + condition;
        if (babyId.HasValue) sql += " AND baby_id=@bid";
        sql += " ORDER BY record_time ASC";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@uid", userId);
        if (babyId.HasValue) cmd.Parameters.AddWithValue("@bid", babyId.Value);
        foreach (var (name, val) in extra) cmd.Parameters.AddWithValue(name, val);
        using var r = cmd.ExecuteReader();
        var list = new List<ChildRecord>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    private static void AddParams(SqliteCommand cmd, ChildRecord rec)
    {
        cmd.Parameters.AddWithValue("@uid", rec.UserId);
        cmd.Parameters.AddWithValue("@bid", (object?)rec.BabyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rt", rec.RecordType);
        cmd.Parameters.AddWithValue("@rst", (object?)rec.RecordSubType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rd", rec.RecordDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@rtm", rec.RecordTime.ToString("O"));
        cmd.Parameters.AddWithValue("@aml", (object?)rec.AmountMl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ds", (object?)rec.DurationSec ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lds", (object?)rec.LeftDurationSec ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rds", (object?)rec.RightDurationSec ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@af", rec.AbnormalFlag.HasValue ? (rec.AbnormalFlag.Value ? 1 : 0) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@tv", (object?)rec.TemperatureValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hc", (object?)rec.HeightCm ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wk", (object?)rec.WeightKg ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pj", rec.PayloadJson);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("O"));
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
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(16)),
        UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(17)),
    };
}
