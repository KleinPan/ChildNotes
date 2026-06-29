using Microsoft.Data.Sqlite;

namespace ChildNotes.Data.Repositories;

/// <summary>
/// 仓储基类：封装 Sqlite 连接创建、参数化查询执行、实体映射等公共逻辑，
/// 消除各仓储中重复的 <c>using var conn = _factory.Create();</c> 等样板代码。
/// </summary>
public abstract class BaseRepository
{
    /// <summary>数据库连接工厂（由派生类通过构造函数注入）。</summary>
    protected readonly DbConnectionFactory _factory;

    protected BaseRepository(DbConnectionFactory factory) => _factory = factory;

    /// <summary>创建并打开一个新的 Sqlite 连接（调用方负责释放）。</summary>
    protected SqliteConnection OpenConnection() => _factory.Create();

    /// <summary>
    /// 执行无返回值的参数化命令（INSERT/UPDATE/DELETE）。
    /// </summary>
    /// <param name="sql">SQL 文本。</param>
    /// <param name="addParams">向命令添加参数的回调。</param>
    protected void ExecuteNonQuery(string sql, Action<SqliteCommand> addParams)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        addParams(cmd);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 执行标量查询（如 <c>SELECT last_insert_rowid()</c>）。
    /// </summary>
    protected object? ExecuteScalar(string sql, Action<SqliteCommand> addParams)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        addParams(cmd);
        return cmd.ExecuteScalar();
    }

    /// <summary>
    /// 执行查询并映射为实体列表。
    /// </summary>
    /// <typeparam name="T">实体类型。</typeparam>
    /// <param name="sql">SQL 文本（应已包含 WHERE/ORDER BY 等）。</param>
    /// <param name="addParams">向命令添加参数的回调。</param>
    /// <param name="map">数据读取器到实体的映射函数。</param>
    protected List<T> Query<T>(string sql, Action<SqliteCommand> addParams, Func<SqliteDataReader, T> map)
    {
        var list = new List<T>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        addParams(cmd);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    /// <summary>
    /// 执行查询并映射为单个实体（或 null）。
    /// </summary>
    protected T? QueryFirstOrDefault<T>(string sql, Action<SqliteCommand> addParams, Func<SqliteDataReader, T> map)
        where T : class
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        addParams(cmd);
        using var r = cmd.ExecuteReader();
        return r.Read() ? map(r) : null;
    }
}

/// <summary>
/// SQLite 参数添加的便捷扩展，统一处理 null/DBNull 转换。
/// </summary>
public static class DbParam
{
    /// <summary>添加参数，null 自动转为 DBNull。</summary>
    public static SqliteCommand Add(this SqliteCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    /// <summary>添加可能为 null 的可空值类型参数。</summary>
    public static SqliteCommand Add<T>(this SqliteCommand cmd, string name, T? value) where T : struct
    {
        cmd.Parameters.AddWithValue(name, value.HasValue ? (object)value.Value : DBNull.Value);
        return cmd;
    }

    /// <summary>添加字符串参数，null 或空字符串按指定策略处理。</summary>
    public static SqliteCommand AddString(this SqliteCommand cmd, string name, string? value, bool emptyAsNull = false)
    {
        object? v = value;
        if (emptyAsNull && string.IsNullOrEmpty(value)) v = DBNull.Value;
        else if (value is null) v = DBNull.Value;
        cmd.Parameters.AddWithValue(name, v);
        return cmd;
    }

    /// <summary>添加 UTC 时间参数（"O" round-trip 格式）。</summary>
    public static SqliteCommand AddUtc(this SqliteCommand cmd, string name, DateTime value)
    {
        cmd.Parameters.AddWithValue(name, value.ToString("O"));
        return cmd;
    }

    /// <summary>添加日期参数（"yyyy-MM-dd" 格式）。</summary>
    public static SqliteCommand AddDate(this SqliteCommand cmd, string name, DateTime value)
    {
        cmd.Parameters.AddWithValue(name, value.ToString("yyyy-MM-dd"));
        return cmd;
    }
}
