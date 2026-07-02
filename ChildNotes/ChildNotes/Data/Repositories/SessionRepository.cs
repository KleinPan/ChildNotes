using Microsoft.Data.Sqlite;
using ChildNotes.Infrastructure;

namespace ChildNotes.Data.Repositories;

/// <summary>
/// user_session 表的访问器（单行，id=1）。
/// 用于持久化登录会话，支持关闭应用后自动登录与滑动过期续期。
/// </summary>
public sealed class SessionRepository : BaseRepository
{
    private const int SessionId = 1;

    public SessionRepository(DbConnectionFactory factory) : base(factory) { }

    /// <summary>保存或覆盖会话（INSERT OR REPLACE）。</summary>
    public void Save(string userId, DateTime issuedAt, DateTime expireAt)
        => ExecuteNonQuery(
            @"INSERT OR REPLACE INTO user_session (id, user_id, issued_at, expire_at)
              VALUES (@id, @u, @i, @e)",
            cmd => cmd
                .Add("@id", SessionId)
                .Add("@u", userId)
                .AddUtc("@i", issuedAt)
                .AddUtc("@e", expireAt));

    /// <summary>读取当前会话。返回 null 表示无会话。</summary>
    public SessionRecord? Get()
    {
        const string sql = "SELECT user_id, issued_at, expire_at FROM user_session WHERE id=@id";
        return QueryFirstOrDefault(sql,
            cmd => cmd.Add("@id", SessionId),
            r => new SessionRecord(
                r.GetString(0),
                DateTimeExtensions.ParseDb(r.GetString(1)),
                DateTimeExtensions.ParseDb(r.GetString(2))));
    }

    /// <summary>清除会话（退出登录时调用）。</summary>
    public void Clear()
        => ExecuteNonQuery("DELETE FROM user_session WHERE id=@id",
            cmd => cmd.Add("@id", SessionId));
}

/// <summary>会话记录。</summary>
public sealed record SessionRecord(string UserId, DateTime IssuedAt, DateTime ExpireAt);
