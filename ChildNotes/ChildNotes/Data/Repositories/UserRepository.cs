using Microsoft.Data.Sqlite;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class UserRepository : BaseRepository
{
    public UserRepository(DbConnectionFactory factory) : base(factory) { }

    private const string SelectBase =
        "SELECT id, username, password_hash, nick_name, avatar_url, gender, created_at, updated_at FROM app_user";

    public AppUser? FindByUsername(string username)
    {
        DevLogger.Log("UserRepo", $"FindByUsername: '{username}'");
        var user = QueryFirstOrDefault(SelectBase + " WHERE username = @u",
            cmd => cmd.Add("@u", username), Map);
        DevLogger.Log("UserRepo", user is null ? "FindByUsername: not found" : $"FindByUsername: found id={user.Id}");
        return user;
    }

    public AppUser? FindById(string id)
        => QueryFirstOrDefault(SelectBase + " WHERE id = @i", cmd => cmd.Add("@i", id), Map);

    public string Insert(AppUser user)
    {
        DevLogger.Log("UserRepo", $"Insert: username={user.Username}");
        user.Id = Guid.NewGuid().ToString("N");
        ExecuteNonQuery(
            @"INSERT INTO app_user (id, username, password_hash, nick_name, avatar_url, gender, created_at, updated_at)
              VALUES (@i, @u, @p, @n, @a, @g, @c, @c)",
            cmd => cmd
                .Add("@i", user.Id)
                .Add("@u", user.Username)
                .Add("@p", user.PasswordHash)
                .Add("@n", user.NickName)
                .Add("@a", user.AvatarUrl)
                .Add("@g", user.Gender)
                .AddUtc("@c", DateTime.UtcNow));
        DevLogger.Log("UserRepo", $"Insert done: id={user.Id}");
        return user.Id;
    }

    public void UpdateProfile(AppUser user)
        => ExecuteNonQuery(
            "UPDATE app_user SET nick_name=@n, avatar_url=@a, gender=@g, updated_at=@t WHERE id=@i",
            cmd => cmd
                .Add("@n", user.NickName)
                .Add("@a", user.AvatarUrl)
                .Add("@g", user.Gender)
                .AddUtc("@t", DateTime.UtcNow)
                .Add("@i", user.Id));

    private static AppUser Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Username = r.GetString(1),
        PasswordHash = r.GetString(2),
        NickName = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        AvatarUrl = r.IsDBNull(4) ? string.Empty : r.GetString(4),
        Gender = r.IsDBNull(5) ? 0 : r.GetInt32(5),
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(6)),
        UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(7)),
    };
}
