using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class UserRepository
{
    private readonly DbConnectionFactory _factory;

    public UserRepository(DbConnectionFactory factory) => _factory = factory;

    public AppUser? FindByUsername(string username)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, password_hash, nick_name, avatar_url, gender, created_at, updated_at FROM app_user WHERE username = @u";
        cmd.Parameters.AddWithValue("@u", username);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapUser(r) : null;
    }

    public AppUser? FindById(long id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, password_hash, nick_name, avatar_url, gender, created_at, updated_at FROM app_user WHERE id = @i";
        cmd.Parameters.AddWithValue("@i", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapUser(r) : null;
    }

    public long Insert(AppUser user)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO app_user (username, password_hash, nick_name, avatar_url, gender, created_at, updated_at)
            VALUES (@u, @p, @n, @a, @g, @c, @c); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@u", user.Username);
        cmd.Parameters.AddWithValue("@p", user.PasswordHash);
        cmd.Parameters.AddWithValue("@n", user.NickName);
        cmd.Parameters.AddWithValue("@a", user.AvatarUrl);
        cmd.Parameters.AddWithValue("@g", user.Gender);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("O"));
        return (long)cmd.ExecuteScalar()!;
    }

    public void UpdateProfile(AppUser user)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE app_user SET nick_name=@n, avatar_url=@a, gender=@g, updated_at=@t WHERE id=@i";
        cmd.Parameters.AddWithValue("@n", user.NickName);
        cmd.Parameters.AddWithValue("@a", user.AvatarUrl);
        cmd.Parameters.AddWithValue("@g", user.Gender);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@i", user.Id);
        cmd.ExecuteNonQuery();
    }

    private static AppUser MapUser(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Username = r.GetString(1),
        PasswordHash = r.GetString(2),
        NickName = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        AvatarUrl = r.IsDBNull(4) ? string.Empty : r.GetString(4),
        Gender = r.IsDBNull(5) ? 0 : r.GetInt32(5),
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(6)),
        UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(7)),
    };
}
