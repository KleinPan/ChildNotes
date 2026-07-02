using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class PointsRepository : BaseRepository
{
    public PointsRepository(DbConnectionFactory factory) : base(factory) { }

    public UserPoints GetOrCreate(string userId)
    {
        var existing = QueryFirstOrDefault(
            "SELECT id, user_id, points, total_earned, total_spent, created_at, updated_at FROM user_points WHERE user_id=@uid",
            cmd => cmd.Add("@uid", userId),
            r => new UserPoints
            {
                Id = r.GetString(0),
                UserId = r.GetString(1),
                Points = r.GetInt32(2),
                TotalEarned = r.GetInt32(3),
                TotalSpent = r.GetInt32(4),
                CreatedAt = DateTimeExtensions.ParseDb(r.GetString(5)),
                UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(6)),
            });
        if (existing is not null) return existing;

        var points = new UserPoints { Id = Guid.NewGuid().ToString("N"), UserId = userId, Points = 0, TotalEarned = 0, TotalSpent = 0 };
        ExecuteNonQuery(
            "INSERT INTO user_points (id, user_id, points, total_earned, total_spent, created_at, updated_at) VALUES (@id,@uid,0,0,0,@c,@c)",
            cmd => cmd.Add("@id", points.Id).Add("@uid", userId).AddUtc("@c", DateTime.UtcNow));
        return points;
    }

    public void AddPoints(string userId, int delta)
    {
        // 先确保记录存在
        GetOrCreate(userId);

        // 原子操作：避免先读后写造成的并发积分丢失
        var sql = delta > 0
            ? "UPDATE user_points SET points = points + @d, total_earned = total_earned + @d, updated_at=@t WHERE user_id=@uid"
            : "UPDATE user_points SET points = points + @d, total_spent = total_spent + @neg, updated_at=@t WHERE user_id=@uid";
        ExecuteNonQuery(sql,
            cmd => cmd
                .Add("@d", delta)
                .Add("@neg", -delta)
                .AddUtc("@t", DateTime.UtcNow)
                .Add("@uid", userId));
    }

    public SignInRecord? GetSignIn(string userId, DateTime date)
        => QueryFirstOrDefault(
            "SELECT id, user_id, sign_date, continuous_days, reward, created_at FROM sign_in_record WHERE user_id=@uid AND sign_date=@d",
            cmd => cmd.Add("@uid", userId).AddDate("@d", date),
            MapSignIn);

    public List<SignInRecord> GetRecentSignIns(string userId, int days)
        => Query(
            "SELECT id, user_id, sign_date, continuous_days, reward, created_at FROM sign_in_record WHERE user_id=@uid AND sign_date>=@s ORDER BY sign_date ASC",
            cmd => cmd.Add("@uid", userId).AddDate("@s", DateTime.Today.AddDays(-days)),
            MapSignIn);

    public void InsertSignIn(SignInRecord rec)
    {
        rec.Id = Guid.NewGuid().ToString("N");
        ExecuteNonQuery(
            "INSERT INTO sign_in_record (id, user_id, sign_date, continuous_days, reward, created_at) VALUES (@id,@uid,@d,@c,@r,@t)",
            cmd => cmd
                .Add("@id", rec.Id)
                .Add("@uid", rec.UserId)
                .AddDate("@d", rec.SignDate)
                .Add("@c", rec.ContinuousDays)
                .Add("@r", rec.Reward)
                .AddUtc("@t", DateTime.UtcNow));
    }

    private static SignInRecord MapSignIn(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        UserId = r.GetString(1),
        SignDate = DateTimeExtensions.ParseDb(r.GetString(2)),
        ContinuousDays = r.GetInt32(3),
        Reward = r.GetInt32(4),
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(5)),
    };
}
