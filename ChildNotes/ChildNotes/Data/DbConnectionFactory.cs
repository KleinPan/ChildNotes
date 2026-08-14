using ChildNotes.Infrastructure;
using Microsoft.Data.Sqlite;

namespace ChildNotes.Data;

public sealed class DbConnectionFactory
{
    private readonly string _connectionString;
    private readonly string _dbPath;

    public DbConnectionFactory(string dbPath)
    {
        _dbPath = dbPath;
        // 启用 WAL 模式提升并发读性能（数据库级持久化属性，构造期设置一次即可）
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate";
        DevLogger.Log("DB", $"DbConnectionFactory ctor: path={dbPath}, dir exists={System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(dbPath))}");
        // WAL 是数据库级持久化属性，只需在首次构造时设置一次；
        // foreign_keys 与 busy_timeout 是连接级属性，必须在每个新连接的 Create() 中重新启用
        using var initConn = new SqliteConnection(_connectionString);
        initConn.Open();
        using (var pragma = initConn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }
        DevLogger.Log("DB", "DbConnectionFactory ctor done (WAL persisted)");
    }

    public SqliteConnection Create()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // 每个连接都必须重新启用 foreign_keys 和 busy_timeout（连接级属性，不跨连接持久化）
        // busy_timeout=10000：同步 Pull 事务期间 UpdateToken 等写操作需等待事务释放写锁
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=10000; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    /// <summary>
    /// 使用 VACUUM INTO 将当前数据库快照备份到指定路径。
    /// VACUUM INTO 是 SQLite 3.27+ 的特性，生成一个干净的、独立的备份文件，
    /// 不影响原数据库读写，适合在同步前做防极端损坏的快照。
    /// 注意：VACUUM INTO 不覆盖已存在文件，需先删除旧备份。
    /// </summary>
    /// <param name="backupPath">备份文件路径。若已存在会先删除再备份。</param>
    public void BackupTo(string backupPath)
    {
        // VACUUM INTO 不覆盖已存在文件，需先删除旧备份
        if (System.IO.File.Exists(backupPath))
        {
            try { System.IO.File.Delete(backupPath); }
            catch { /* 旧文件被占用等情况忽略，VACUUM INTO 会报错但不阻塞同步 */ }
        }
        // VACUUM INTO 不支持参数化路径，但路径来自代码内部常量，无注入风险
        using var conn = Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"VACUUM INTO '{backupPath.Replace("'", "''")}';";
        cmd.ExecuteNonQuery();
    }

    /// <summary>数据库文件路径（供备份路径推导等使用）。</summary>
    public string DbPath => _dbPath;

    /// <summary>
    /// 执行 WAL checkpoint（TRUNCATE 模式），把 -wal 里的活跃事务合并到主文件并清空 -wal。
    /// 用于：导出数据库前确保主文件包含完整数据，避免 adb pull 主文件只拿到 1KB 空壳的问题。
    /// 实现要点：使用独立短连接而非复用 Create()，避免占用长连接阻塞其他读写。
    /// </summary>
    public void Checkpoint()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        pragma.ExecuteNonQuery();
        DevLogger.Log("DB", "Checkpoint(TRUNCATE) done");
    }
}
