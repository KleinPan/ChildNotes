using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

/// <summary>
/// 用户自定义补充剂/用药项的仓储层。
/// 对应 user_supplement_item 表（schema 已在 DbInitializer 中定义）。
/// 支持按 type（supplement / medicine）分组查询、新增、删除。
/// </summary>
public sealed class SupplementItemRepository : BaseRepository
{
    public SupplementItemRepository(DbConnectionFactory factory) : base(factory) { }

    /// <summary>
    /// 获取指定用户+类型的全部自定义项，按创建时间倒序（最近添加的排前面）。
    /// </summary>
    public List<SupplementItem> GetByUser(string userId, string type)
        => Query(
            "SELECT id, user_id, type, name, created_at FROM user_supplement_item WHERE user_id=@uid AND type=@t ORDER BY created_at DESC",
            cmd => cmd.Add("@uid", userId).Add("@t", type),
            Map);

    /// <summary>
    /// 新增自定义项。若 (user_id, type, name) 已存在（UNIQUE 约束），忽略不报错。
    /// </summary>
    public void Insert(string userId, string type, string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        ExecuteNonQuery(
            "INSERT OR IGNORE INTO user_supplement_item (id, user_id, type, name, created_at) VALUES (@id,@uid,@t,@n,@c)",
            cmd => cmd
                .Add("@id", Guid.NewGuid().ToString("N"))
                .Add("@uid", userId)
                .Add("@t", type)
                .Add("@n", trimmed)
                .AddUtc("@c", DateTime.UtcNow));
    }

    /// <summary>
    /// 删除指定自定义项。不影响历史记录中已保存的 name 字段。
    /// </summary>
    public void Delete(string userId, string type, string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        ExecuteNonQuery(
            "DELETE FROM user_supplement_item WHERE user_id=@uid AND type=@t AND name=@n",
            cmd => cmd.Add("@uid", userId).Add("@t", type).Add("@n", trimmed));
    }

    /// <summary>
    /// 判断指定 (user_id, type, name) 是否已存在（防重复添加）。
    /// </summary>
    public bool Exists(string userId, string type, string name)
    {
        var result = ExecuteScalar(
            "SELECT COUNT(1) FROM user_supplement_item WHERE user_id=@uid AND type=@t AND name=@n",
            cmd => cmd.Add("@uid", userId).Add("@t", type).Add("@n", name.Trim()));
        return result is long count && count > 0;
    }

    private static SupplementItem Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        UserId = r.GetString(1),
        Type = r.GetString(2),
        Name = r.GetString(3),
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(4)),
    };
}
