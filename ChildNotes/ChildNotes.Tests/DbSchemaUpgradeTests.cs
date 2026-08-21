using ChildNotes.Data;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace ChildNotes.Tests;

/// <summary>
/// 复现 v0.7.21~v0.7.23 启动闪退：v5 老库（user_version=5，sync_config 无 last_cloud_user_id 列）
/// 升级到 CurrentSchemaVersion 后，SyncConfigRepository.Get() 查询该列不再抛 "no such column"。
/// 根因：v0.7.21 引入 last_cloud_user_id 列时 CurrentSchemaVersion 未递增（仍为 5），
/// 老库 5 >= 5 跳过 DDL，AddColumnIfNotExists 永远不执行。
/// </summary>
public class DbSchemaUpgradeTests : IDisposable
{
    private readonly string _dbPath;

    public DbSchemaUpgradeTests()
    {
        Batteries_V2.Init();
        _dbPath = Path.Combine(Path.GetTempPath(), $"cn_schema_upgrade_{Guid.NewGuid():N}.db");
    }

    /// <summary>构造 v5 老库：完整跑一遍当前 DDL 后把 user_version 回写为 5，并删掉 last_cloud_user_id 列，模拟 v0.7.20 时代的库。</summary>
    private DbConnectionFactory CreateV5Database()
    {
        var factory = new DbConnectionFactory(_dbPath);
        DbInitializer.Initialize(factory);

        // 模拟 v0.7.20 的库：版本回退到 5，且没有 v6 新增的列
        using (var conn = factory.Create())
        {
            Exec(conn, "ALTER TABLE sync_config DROP COLUMN last_cloud_user_id;");
            Exec(conn, "PRAGMA user_version = 5;");
        }
        return factory;
    }

    [Fact]
    public void V5Database_Should_Be_Upgraded_And_Get_Should_Not_Throw()
    {
        var factory = CreateV5Database();

        // 升级路径：user_version=5 < CurrentSchemaVersion → 跑完整 DDL → 补列 → 写版本号
        DbInitializer.Initialize(factory);

        // 验证 1：版本号已升级
        using (var conn = factory.Create())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            Assert.Equal(DbInitializer.CurrentSchemaVersion, Convert.ToInt32(cmd.ExecuteScalar()));
        }

        // 验证 2：SyncConfigRepository.Get()（SelectSql 含 last_cloud_user_id）不再抛异常
        var repo = new SyncConfigRepository(factory);
        var cfg = repo.Get();
        Assert.NotNull(cfg);
        Assert.Equal(string.Empty, cfg.LastCloudUserId);
    }

    [Fact]
    public void V5Database_With_SyncData_Should_Preserve_Config_After_Upgrade()
    {
        var factory = CreateV5Database();

        // 老库写入正常同步数据（不含 last_cloud_user_id 列）
        using (var conn = factory.Create())
        {
            Exec(conn, @"
UPDATE sync_config SET enabled=1, server_url='https://api.example.com', cloud_user_id='cloud-1', local_user_id='local-1'
WHERE id=1;");
        }

        DbInitializer.Initialize(factory);

        // 升级后原有配置保留，新列默认空串
        var repo = new SyncConfigRepository(factory);
        var cfg = repo.Get();
        Assert.True(cfg.Enabled);
        Assert.Equal("https://api.example.com", cfg.ServerUrl);
        Assert.Equal("cloud-1", cfg.CloudUserId);
        Assert.Equal("local-1", cfg.LocalUserId);
        Assert.Equal(string.Empty, cfg.LastCloudUserId);
    }

    [Fact]
    public void UpgradedDatabase_Save_And_Reload_LastCloudUserId_Should_Work()
    {
        var factory = CreateV5Database();
        DbInitializer.Initialize(factory);

        // 升级后写入/读回 last_cloud_user_id（登出反迁移路径依赖）
        var repo = new SyncConfigRepository(factory);
        repo.UpdateLastCloudUserId("cloud-old");
        var cfg = repo.Get();
        Assert.Equal("cloud-old", cfg.LastCloudUserId);
    }

    /// <summary>回归保护：当前版本号的 DDL 必须覆盖 SelectSql 用到的全部列，防止同类闪退再次发生。</summary>
    [Fact]
    public void FreshDatabase_All_SyncConfigColumns_Should_Exist()
    {
        var factory = new DbConnectionFactory(_dbPath);
        DbInitializer.Initialize(factory);

        using var conn = factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, enabled, server_url, cloud_user_id, local_user_id, last_cloud_user_id, last_sync_at, last_sync_status, last_sync_msg, device_id FROM sync_config WHERE id=1";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read(), "sync_config 应有默认行 id=1");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* 临时文件清理失败可忽略 */ }
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
