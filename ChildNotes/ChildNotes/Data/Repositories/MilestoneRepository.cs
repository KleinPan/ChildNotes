using System.Text.Json;
using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class MilestoneRepository
{
    private readonly DbConnectionFactory _factory;

    public MilestoneRepository(DbConnectionFactory factory) => _factory = factory;

    public List<Milestone> GetAll(long userId, long? babyId)
    {
        var list = new List<Milestone>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sql = "SELECT id, user_id, baby_id, title, content, record_date, photos_json, created_at, updated_at FROM milestone WHERE user_id=@uid";
        if (babyId.HasValue) sql += " AND baby_id=@bid";
        sql += " ORDER BY record_date DESC, id DESC";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@uid", userId);
        if (babyId.HasValue) cmd.Parameters.AddWithValue("@bid", babyId.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public long Insert(Milestone m)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO milestone (user_id, baby_id, title, content, record_date, photos_json, created_at, updated_at)
            VALUES (@uid,@bid,@t,@c,@d,@p,@n,@n); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@uid", m.UserId);
        cmd.Parameters.AddWithValue("@bid", (object?)m.BabyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", m.Title);
        cmd.Parameters.AddWithValue("@c", (object?)m.Content ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@d", m.RecordDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@p", m.PhotosJson);
        cmd.Parameters.AddWithValue("@n", DateTime.UtcNow.ToString("O"));
        return (long)cmd.ExecuteScalar()!;
    }

    public void Update(Milestone m)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE milestone SET title=@t, content=@c, record_date=@d, photos_json=@p, updated_at=@n WHERE id=@id";
        cmd.Parameters.AddWithValue("@t", m.Title);
        cmd.Parameters.AddWithValue("@c", (object?)m.Content ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@d", m.RecordDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@p", m.PhotosJson);
        cmd.Parameters.AddWithValue("@n", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", m.Id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM milestone WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static Milestone Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        UserId = r.GetInt64(1),
        BabyId = r.IsDBNull(2) ? null : r.GetInt64(2),
        Title = r.GetString(3),
        Content = r.IsDBNull(4) ? null : r.GetString(4),
        RecordDate = DateTime.Parse(r.GetString(5)),
        PhotosJson = r.IsDBNull(6) ? "[]" : r.GetString(6),
        CreatedAt = DateTime.Parse(r.GetString(7)),
        UpdatedAt = DateTime.Parse(r.GetString(8)),
    };
}
