using Microsoft.Data.Sqlite;

namespace ChildNotes.Data;

public sealed class DbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(string dbPath)
    {
        // 启用 WAL 模式提升并发读性能；设置 BusyTimeout 避免写入冲突时立即失败
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate";
        // 初始化 PRAGMA（每次新连接都会执行）
        using var initConn = new SqliteConnection(_connectionString);
        initConn.Open();
        using (var pragma = initConn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }
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
