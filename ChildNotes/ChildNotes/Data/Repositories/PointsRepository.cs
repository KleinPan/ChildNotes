using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class PointsRepository
{
    private readonly DbConnectionFactory _factory;

    public PointsRepository(DbConnectionFactory factory) => _factory = factory;

    public UserPoints GetOrCreate(long userId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, points, total_earned, total_spent, created_at, updated_at FROM user_points WHERE user_id=@uid";
        cmd.Parameters.AddWithValue("@uid", userId);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            return new UserPoints
            {
                Id = r.GetInt64(0),
                UserId = r.GetInt64(1),
                Points = r.GetInt32(2),
                TotalEarned = r.GetInt32(3),
                TotalSpent = r.GetInt32(4),
                CreatedAt = DateTimeExtensions.ParseDb(r.GetString(5)),
                UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(6)),
            };
        }
        r.Close();

        using var ins = conn.CreateCommand();
        ins.CommandText = "INSERT INTO user_points (user_id, points, total_earned, total_spent, created_at, updated_at) VALUES (@uid,0,0,0,@c,@c); SELECT last_insert_rowid();";
        ins.Parameters.AddWithValue("@uid", userId);
        ins.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("O"));
        var id = (long)ins.ExecuteScalar()!;
        return new UserPoints { Id = id, UserId = userId, Points = 0, TotalEarned = 0, TotalSpent = 0 };
    }

    public void AddPoints(long userId, int delta)
    {
        // 先确保记录存在
        GetOrCreate(userId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // 原子操作：避免先读后写造成的并发积分丢失
        if (delta > 0)
        {
            cmd.CommandText = "UPDATE user_points SET points = points + @d, total_earned = total_earned + @d, updated_at=@t WHERE user_id=@uid";
        }
        else
        {
            cmd.CommandText = "UPDATE user_points SET points = points + @d, total_spent = total_spent + @neg, updated_at=@t WHERE user_id=@uid";
        }
        cmd.Parameters.AddWithValue("@d", delta);
        cmd.Parameters.AddWithValue("@neg", -delta);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.ExecuteNonQuery();
    }

    public SignInRecord? GetSignIn(long userId, DateTime date)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, sign_date, continuous_days, reward, created_at FROM sign_in_record WHERE user_id=@uid AND sign_date=@d";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@d", date.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new SignInRecord
        {
            Id = r.GetInt64(0),
            UserId = r.GetInt64(1),
            SignDate = DateTimeExtensions.ParseDb(r.GetString(2)),
            ContinuousDays = r.GetInt32(3),
            Reward = r.GetInt32(4),
            CreatedAt = DateTimeExtensions.ParseDb(r.GetString(5)),
        };
    }

    public List<SignInRecord> GetRecentSignIns(long userId, int days)
    {
        var list = new List<SignInRecord>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, sign_date, continuous_days, reward, created_at FROM sign_in_record WHERE user_id=@uid AND sign_date>=@s ORDER BY sign_date ASC";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@s", DateTime.Today.AddDays(-days).ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new SignInRecord
            {
                Id = r.GetInt64(0),
                UserId = r.GetInt64(1),
                SignDate = DateTimeExtensions.ParseDb(r.GetString(2)),
                ContinuousDays = r.GetInt32(3),
                Reward = r.GetInt32(4),
                CreatedAt = DateTimeExtensions.ParseDb(r.GetString(5)),
            });
        }
        return list;
    }

    public void InsertSignIn(SignInRecord rec)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO sign_in_record (user_id, sign_date, continuous_days, reward, created_at) VALUES (@uid,@d,@c,@r,@t)";
        cmd.Parameters.AddWithValue("@uid", rec.UserId);
        cmd.Parameters.AddWithValue("@d", rec.SignDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@c", rec.ContinuousDays);
        cmd.Parameters.AddWithValue("@r", rec.Reward);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }
}
