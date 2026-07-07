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

    public void UpdatePassword(string userId, string passwordHash)
        => ExecuteNonQuery(
            "UPDATE app_user SET password_hash=@p, updated_at=@t WHERE id=@i",
            cmd => cmd
                .Add("@p", passwordHash)
                .AddUtc("@t", DateTime.UtcNow)
                .Add("@i", userId));

    /// <summary>
    /// 将本地旧 user_id 全量替换为后端 user_id（含 app_user 主键 + 所有关联表）。
    /// 用于首次同步登录后发现本地注册生成的 id 与后端 id 不一致的场景。
    /// 返回是否发生了替换。
    /// </summary>
    public bool UpdateIdIfDifferent(string oldLocalId, string remoteId)
    {
        if (string.IsNullOrEmpty(oldLocalId) || string.IsNullOrEmpty(remoteId) || oldLocalId == remoteId)
            return false;

        DevLogger.Log("UserRepo", $"UpdateIdIfDifferent: {oldLocalId} -> {remoteId}");

        // 显式关闭外键约束检查（本项目 DbConnectionFactory.Create() 会强制 PRAGMA foreign_keys=ON，
        // 此处虽然 schema 中未声明 FOREIGN KEY，但显式关闭为防御性写法，避免未来加外键时出错）
        ExecuteNonQuery("PRAGMA foreign_keys = OFF", _ => { });
        try
        {
            // 主键更新
            ExecuteNonQuery("UPDATE app_user SET id=@new WHERE id=@old",
                cmd => cmd.Add("@new", remoteId).Add("@old", oldLocalId));
            // 所有关联表 user_id 替换
            ExecuteNonQuery("UPDATE baby SET user_id=@new WHERE user_id=@old",
                cmd => cmd.Add("@new", remoteId).Add("@old", oldLocalId));
            ExecuteNonQuery("UPDATE child_record SET user_id=@new WHERE user_id=@old",
                cmd => cmd.Add("@new", remoteId).Add("@old", oldLocalId));
            ExecuteNonQuery("UPDATE milestone SET user_id=@new WHERE user_id=@old",
                cmd => cmd.Add("@new", remoteId).Add("@old", oldLocalId));
            ExecuteNonQuery("UPDATE ai_analysis_record SET user_id=@new WHERE user_id=@old",
                cmd => cmd.Add("@new", remoteId).Add("@old", oldLocalId));
        }
        finally
        {
            ExecuteNonQuery("PRAGMA foreign_keys = ON", _ => { });
        }

        return true;
    }

    private static AppUser Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Username = r.GetString(1),
        PasswordHash = r.GetString(2),
        NickName = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        AvatarUrl = r.IsDBNull(4) ? string.Empty : r.GetString(4),
        Gender = r.IsDBNull(5) ? 0 : r.GetInt32(5),
        // created_at / updated_at 以 UTC 存储，读入应用层统一转 Local（与其他 Repository 保持一致）
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(6)).ToLocalTime(),
        UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(7)).ToLocalTime(),
    };
}
