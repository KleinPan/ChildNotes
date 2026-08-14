using Microsoft.Data.Sqlite;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

/// <summary>
/// app_user 表的访问器。
/// v5 schema 重构后：app_user 表仅保存登录用户的 profile 缓存（email/nick_name 等），
///   不再保存密码相关字段。离线模式（未登录）的用户使用 sync_config.local_user_id 作为
///   业务数据的 user_id，不会在 app_user 表中创建行。
/// </summary>
public sealed class UserRepository : BaseRepository
{
    public UserRepository(DbConnectionFactory factory) : base(factory) { }

    private const string SelectBase =
        "SELECT id, email, email_verified_at, nick_name, avatar_url, gender, membership_expire_at, created_at, updated_at FROM app_user";

    public AppUser? FindById(string id)
        => QueryFirstOrDefault(SelectBase + " WHERE id = @i", cmd => cmd.Add("@i", id), Map);

    public AppUser? FindByEmail(string email)
    {
        DevLogger.Log("UserRepo", $"FindByEmail: '{email}'");
        var user = QueryFirstOrDefault(SelectBase + " WHERE email = @e",
            cmd => cmd.Add("@e", email), Map);
        DevLogger.Log("UserRepo", user is null ? "FindByEmail: not found" : $"FindByEmail: found id={user.Id}");
        return user;
    }

    /// <summary>
    /// 插入或覆盖本地 user profile 缓存（登录成功后调用，profile 来源于云端 /api/auth/me）。
    /// </summary>
    public void Upsert(AppUser user)
    {
        DevLogger.Log("UserRepo", $"Upsert: id={user.Id}, email={user.Email}");
        ExecuteNonQuery(
            @"INSERT INTO app_user (id, email, email_verified_at, nick_name, avatar_url, gender, membership_expire_at, created_at, updated_at)
              VALUES (@i, @e, @ev, @n, @a, @g, @m, @c, @u)
              ON CONFLICT(id) DO UPDATE SET
                email = excluded.email,
                email_verified_at = excluded.email_verified_at,
                nick_name = excluded.nick_name,
                avatar_url = excluded.avatar_url,
                gender = excluded.gender,
                membership_expire_at = excluded.membership_expire_at,
                updated_at = excluded.updated_at",
            cmd => cmd
                .Add("@i", user.Id)
                .AddString("@e", user.Email, emptyAsNull: false)
                .Add("@ev", (object?)user.EmailVerifiedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value)
                .AddString("@n", user.NickName, emptyAsNull: true)
                .AddString("@a", user.AvatarUrl, emptyAsNull: true)
                .Add("@g", user.Gender)
                .Add("@m", (object?)user.MembershipExpireAt?.ToUniversalTime().ToString("O") ?? DBNull.Value)
                .AddUtc("@c", user.CreatedAt == DateTime.MinValue ? DateTime.UtcNow : user.CreatedAt)
                .AddUtc("@u", DateTime.UtcNow));
        DevLogger.Log("UserRepo", $"Upsert done: id={user.Id}");
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

    /// <summary>更新会员到期时间（从后端 /api/membership/status 拉取后写入本地缓存）。</summary>
    public void UpdateMembershipExpireAt(string userId, DateTime? expireAt)
        => ExecuteNonQuery(
            "UPDATE app_user SET membership_expire_at=@m, updated_at=@t WHERE id=@i",
            cmd => cmd
                .Add("@m", (object?)(expireAt?.ToUniversalTime().ToString("O")) ?? DBNull.Value)
                .AddUtc("@t", DateTime.UtcNow)
                .Add("@i", userId));

    private static AppUser Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Email = r.IsDBNull(1) ? string.Empty : r.GetString(1),
        EmailVerifiedAt = r.IsDBNull(2) ? null : DateTimeExtensions.ParseDb(r.GetString(2)),
        NickName = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        AvatarUrl = r.IsDBNull(4) ? string.Empty : r.GetString(4),
        Gender = r.IsDBNull(5) ? 0 : r.GetInt32(5),
        MembershipExpireAt = r.IsDBNull(6) ? null : DateTimeExtensions.ParseDb(r.GetString(6)),
        // created_at / updated_at 以 UTC 存储，读入应用层统一转 Local（与其他 Repository 保持一致）
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(7)).ToLocalTime(),
        UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(8)).ToLocalTime(),
    };
}
