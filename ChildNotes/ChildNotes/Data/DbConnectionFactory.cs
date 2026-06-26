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
        // 启用 WAL 模式提升并发读性能；设置 BusyTimeout 避免写入冲突时立即失败
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate";
        DevLogger.Log("DB", $"DbConnectionFactory ctor: path={dbPath}, dir exists={System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(dbPath))}");
        // 初始化 PRAGMA（每次新连接都会执行）
        using var initConn = new SqliteConnection(_connectionString);
        initConn.Open();
        using (var pragma = initConn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }
        DevLogger.Log("DB", "DbConnectionFactory ctor done (initial PRAGMA ok)");
    }

    public SqliteConnection Create()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // 每个连接都启用 WAL 和 busy_timeout，确保多连接并发安全
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }
}
