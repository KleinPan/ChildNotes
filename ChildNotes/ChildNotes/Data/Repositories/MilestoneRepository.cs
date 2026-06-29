using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class MilestoneRepository : BaseRepository
{
    public MilestoneRepository(DbConnectionFactory factory) : base(factory) { }

    private const string SelectBase =
        "SELECT id, user_id, baby_id, title, content, record_date, photos_json, created_at, updated_at FROM milestone";

    public List<Milestone> GetAll(long userId, long? babyId)
    {
        var sql = SelectBase + " WHERE user_id=@uid";
        if (babyId.HasValue) sql += " AND baby_id=@bid";
        sql += " ORDER BY record_date DESC, id DESC";
        return Query(sql,
            cmd =>
            {
                cmd.Add("@uid", userId);
                if (babyId.HasValue) cmd.Add("@bid", babyId.Value);
            },
            Map);
    }

    public long Insert(Milestone m)
        => (long)ExecuteScalar(
            @"INSERT INTO milestone (user_id, baby_id, title, content, record_date, photos_json, created_at, updated_at)
              VALUES (@uid,@bid,@t,@c,@d,@p,@n,@n); SELECT last_insert_rowid();",
            cmd => cmd
                .Add("@uid", m.UserId)
                .Add("@bid", (object?)m.BabyId ?? DBNull.Value)
                .Add("@t", m.Title)
                .Add("@c", (object?)m.Content ?? DBNull.Value)
                .AddDate("@d", m.RecordDate)
                .Add("@p", m.PhotosJson)
                .AddUtc("@n", DateTime.UtcNow))!;

    public void Update(Milestone m)
        => ExecuteNonQuery(
            "UPDATE milestone SET title=@t, content=@c, record_date=@d, photos_json=@p, updated_at=@n WHERE id=@id",
            cmd => cmd
                .Add("@t", m.Title)
                .Add("@c", (object?)m.Content ?? DBNull.Value)
                .AddDate("@d", m.RecordDate)
                .Add("@p", m.PhotosJson)
                .AddUtc("@n", DateTime.UtcNow)
                .Add("@id", m.Id));

    public void Delete(long id)
        => ExecuteNonQuery("DELETE FROM milestone WHERE id=@id", cmd => cmd.Add("@id", id));

    private static Milestone Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        UserId = r.GetInt64(1),
        BabyId = r.IsDBNull(2) ? null : r.GetInt64(2),
        Title = r.GetString(3),
        Content = r.IsDBNull(4) ? null : r.GetString(4),
        RecordDate = DateTimeExtensions.ParseDb(r.GetString(5)),
        PhotosJson = r.IsDBNull(6) ? "[]" : r.GetString(6),
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(7)),
        UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(8)),
    };
}
