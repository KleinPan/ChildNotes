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

        // 升级后写入/读回 last_cloud_user_id（fixup 读取此字段做登出遗留数据归位）
        var repo = new SyncConfigRepository(factory);
        var cfg = repo.Get();
        cfg.LastCloudUserId = "cloud-old";
        repo.Save(cfg);
        cfg = repo.Get();
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
        cmd.CommandText = "SELECT id, enabled, server_url, cloud_user_id, local_user_id, last_cloud_user_id, current_family_id, last_bound_family_id, identity_fixup_done, last_sync_at, last_sync_status, last_sync_msg, device_id FROM sync_config WHERE id=1";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read(), "sync_config 应有默认行 id=1");
    }

    /// <summary>Family-centric（阶段 1B）：v6 老库升级到 v7 后 current_family_id 列存在且可读写。</summary>
    [Fact]
    public void V6Database_Should_Add_CurrentFamilyId_Column_On_Upgrade()
    {
        var factory = new DbConnectionFactory(_dbPath);
        DbInitializer.Initialize(factory);

        // 模拟 v6 老库：删掉 v7 新增列并回退版本号
        using (var conn = factory.Create())
        {
            Exec(conn, "ALTER TABLE sync_config DROP COLUMN current_family_id;");
            Exec(conn, "PRAGMA user_version = 6;");
        }

        // 升级路径：user_version=6 < CurrentSchemaVersion → 跑 DDL → AddColumnIfNotExists 补列
        DbInitializer.Initialize(factory);

        var repo = new SyncConfigRepository(factory);
        repo.UpdateCurrentFamilyId("family-1");
        var cfg = repo.Get();
        Assert.Equal("family-1", cfg.CurrentFamilyId);
    }

    /// <summary>Family-centric（阶段 1C）：v7 老库升级到 v8 后 last_bound_family_id / identity_fixup_done 列存在。</summary>
    [Fact]
    public void V7Database_Should_Add_1C_Columns_On_Upgrade()
    {
        var factory = new DbConnectionFactory(_dbPath);
        DbInitializer.Initialize(factory);

        // 模拟 v7 老库：删掉 v8 新增列并回退版本号
        using (var conn = factory.Create())
        {
            Exec(conn, "ALTER TABLE sync_config DROP COLUMN last_bound_family_id;");
            Exec(conn, "ALTER TABLE sync_config DROP COLUMN identity_fixup_done;");
            Exec(conn, "PRAGMA user_version = 7;");
        }

        DbInitializer.Initialize(factory);

        var repo = new SyncConfigRepository(factory);
        repo.UpdateLastBoundFamilyId("family-1");
        var cfg = repo.Get();
        Assert.Equal("family-1", cfg.LastBoundFamilyId);
        Assert.Equal(0, cfg.IdentityFixupDone);
    }

    /// <summary>
    /// 阶段 1C 一次性身份 fixup（已登录路径）：家庭业务表存量 CloudUserId 名下的行归位到
    /// LocalDataSpaceId；个人表 L → C 归并；baby_member（云端成员名单）不动；
    /// 标志置位 + 幂等可重跑。
    /// </summary>
    [Fact]
    public void IdentityFixup_LoggedIn_MigratesFamilyTablesToL_AndPersonalToC()
    {
        var factory = new DbConnectionFactory(_dbPath);
        DbInitializer.Initialize(factory);
        const string L = "local-1", C = "cloud-1";
        const string ownBabyId = "baby-own";

        // 布置存量数据：模拟旧版本（user_id = CloudUserId 的家庭业务数据 + 个人数据）
        using (var conn = factory.Create())
        {
            Exec(conn, $"UPDATE sync_config SET local_user_id='{L}', cloud_user_id='{C}', current_family_id='F1' WHERE id=1;");
            Exec(conn, $"INSERT INTO baby (id, user_id, name, gender, created_at, updated_at) VALUES ('{ownBabyId}', '{C}', 'A的宝宝', 'M', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');");
            Exec(conn, $"INSERT INTO baby_member (id, baby_id, user_id, role_code, role_name, is_owner, created_at, updated_at) VALUES ('bm-1', '{ownBabyId}', '{C}', 'owner', 'owner', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');");
            Exec(conn, $"INSERT INTO baby_member (id, baby_id, user_id, role_code, role_name, is_owner, created_at, updated_at) VALUES ('bm-2', '{ownBabyId}', 'member-cloud-2', 'member', 'member', 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');");
            Exec(conn, $"INSERT INTO child_record (id, user_id, baby_id, record_type, record_date, record_time, payload_json, created_at, updated_at) VALUES ('rec-1', '{C}', '{ownBabyId}', 'feed', '2026-01-01', '2026-01-01T00:00:00Z', '{{}}', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');");
            Exec(conn, $"INSERT INTO user_points (id, user_id, points, total_earned, total_spent, created_at, updated_at) VALUES ('pts-l', '{L}', 10, 10, 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');");
            Exec(conn, $"INSERT INTO sign_in_record (id, user_id, sign_date, continuous_days, reward, created_at) VALUES ('si-1', '{C}', '2026-01-01', 1, 1, '2026-01-01T00:00:00Z');");
        }

        var repo = new SyncConfigRepository(factory);
        repo.RunIdentityFixup(L, C, lastCloudId: null, currentFamilyId: "F1");

        using (var conn = factory.Create())
        {
            // 家庭业务表：归位到 L
            Assert.Equal(L, Scalar(conn, $"SELECT user_id FROM baby WHERE id='{ownBabyId}';"));
            Assert.Equal(L, Scalar(conn, $"SELECT user_id FROM child_record WHERE id='rec-1';"));
            // baby_member：云端成员名单不动（含其他成员的 CloudUserId）
            Assert.Equal(C, Scalar(conn, $"SELECT user_id FROM baby_member WHERE baby_id='{ownBabyId}' AND user_id='{C}';"));
            Assert.Equal("member-cloud-2", Scalar(conn, $"SELECT user_id FROM baby_member WHERE baby_id='{ownBabyId}' AND user_id='member-cloud-2';"));
            // 个人表：L 积分行归并到 C（离线个人数据 → 账号名下）
            Assert.Equal(C, Scalar(conn, "SELECT user_id FROM user_points WHERE id='pts-l';"));
            Assert.Equal(C, Scalar(conn, "SELECT user_id FROM sign_in_record WHERE id='si-1';"));
        }

        // 标志 + last_bound_family_id 已写入；重跑幂等（0 变化）
        var cfg = repo.Get();
        Assert.Equal(1, cfg.IdentityFixupDone);
        Assert.Equal("F1", cfg.LastBoundFamilyId);
        var affected2 = repo.RunIdentityFixup(L, C, lastCloudId: null, currentFamilyId: "F1");
        Assert.Equal(0, affected2);
    }

    /// <summary>fixup 未登录路径：lastCloudUserId 遗留的个人数据迁回 L；last_cloud_user_id 清空。</summary>
    [Fact]
    public void IdentityFixup_LoggedOut_MigratesLastCloudPersonalRowsToL()
    {
        var factory = new DbConnectionFactory(_dbPath);
        DbInitializer.Initialize(factory);
        const string L = "local-2", lastC = "cloud-old";

        using (var conn = factory.Create())
        {
            Exec(conn, $"UPDATE sync_config SET local_user_id='{L}', cloud_user_id='', last_cloud_user_id='{lastC}' WHERE id=1;");
            Exec(conn, $"INSERT INTO user_points (id, user_id, points, total_earned, total_spent, created_at, updated_at) VALUES ('pts-c', '{lastC}', 20, 20, 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');");
            Exec(conn, $"INSERT INTO baby (id, user_id, name, gender, created_at, updated_at) VALUES ('b-1', '{lastC}', '宝宝', 'M', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');");
        }

        var repo = new SyncConfigRepository(factory);
        repo.RunIdentityFixup(L, cloudId: null, lastCloudId: lastC, currentFamilyId: null);

        using (var conn = factory.Create())
        {
            Assert.Equal(L, Scalar(conn, "SELECT user_id FROM baby WHERE id='b-1';"));
            Assert.Equal(L, Scalar(conn, "SELECT user_id FROM user_points WHERE id='pts-c';"));
        }
        var cfg = repo.Get();
        Assert.Equal(1, cfg.IdentityFixupDone);
        Assert.Equal(string.Empty, cfg.LastCloudUserId);
    }

    /// <summary>
    /// 阶段 2 rebind 事务（设计文档 6.4）：单事务完成 sync_config 四字段更新
    /// （cloud_user_id / current_family_id / last_bound_family_id / last_sync_at=NULL）
    /// + baby / child_record / milestone 三表 synced_at 全清。
    /// </summary>
    [Fact]
    public void ExecuteRebind_UpdatesSyncConfig_AndClearsSyncedAt()
    {
        var factory = new DbConnectionFactory(_dbPath);
        DbInitializer.Initialize(factory);
        const string C = "cloud-1", F1 = "family-old", F2 = "family-new";

        using (var conn = factory.Create())
        {
            Exec(conn, $"UPDATE sync_config SET cloud_user_id='{C}', current_family_id='{F1}', last_bound_family_id='{F1}', local_user_id='L1' WHERE id=1;");
            Exec(conn, "UPDATE sync_config SET last_sync_at='2026-01-01T00:00:00Z' WHERE id=1;");
            Exec(conn, $"INSERT INTO baby (id, user_id, name, gender, created_at, updated_at, synced_at) VALUES ('b-1', 'L1', '宝宝', 'M', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', '2026-01-02T00:00:00Z');");
            Exec(conn, $"INSERT INTO child_record (id, user_id, baby_id, record_type, record_date, record_time, payload_json, created_at, updated_at, synced_at) VALUES ('rec-1', 'L1', 'b-1', 'feed', '2026-01-01', '2026-01-01T00:00:00Z', '{{}}', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', '2026-01-02T00:00:00Z');");
            Exec(conn, $"INSERT INTO milestone (id, user_id, baby_id, title, record_date, created_at, updated_at, synced_at) VALUES ('ms-1', 'L1', 'b-1', 'T', '2026-01-01', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', '2026-01-02T00:00:00Z');");
        }

        var repo = new SyncConfigRepository(factory);
        repo.ExecuteRebind(C, F2);

        using (var conn = factory.Create())
        {
            // sync_config：四字段全部更新，last_sync_at 清空（下次 Full Pull Only）
            Assert.Equal(C, Scalar(conn, "SELECT cloud_user_id FROM sync_config WHERE id=1;"));
            Assert.Equal(F2, Scalar(conn, "SELECT current_family_id FROM sync_config WHERE id=1;"));
            Assert.Equal(F2, Scalar(conn, "SELECT last_bound_family_id FROM sync_config WHERE id=1;"));
            Assert.Equal(0, CountScalar(conn, "SELECT COUNT(*) FROM sync_config WHERE last_sync_at IS NOT NULL;"));
            // 三张家庭业务表 synced_at 全清（重推候选 → 服务端 cross-family skip 决定终态）
            Assert.Equal(0, CountScalar(conn, "SELECT COUNT(*) FROM baby WHERE synced_at IS NOT NULL;"));
            Assert.Equal(0, CountScalar(conn, "SELECT COUNT(*) FROM child_record WHERE synced_at IS NOT NULL;"));
            Assert.Equal(0, CountScalar(conn, "SELECT COUNT(*) FROM milestone WHERE synced_at IS NOT NULL;"));
        }

        // user_id 不动（Family-centric：本地数据空间恒定）
        using (var conn = factory.Create())
        {
            Assert.Equal("L1", Scalar(conn, "SELECT user_id FROM baby WHERE id='b-1';"));
        }
    }

    /// <summary>阶段 2 派生规则（设计文档第 4 节）：同 ANDROID_ID 派生稳定 Id；不同 prefix 互不相同；null 回退 GUID。</summary>
    [Fact]
    public void DeviceIdentityDerivation_StableAndDistinct()
    {
        const string androidId = "test-android-id-123";
        var d1 = Infrastructure.DeviceIdentityDerivation.DeriveDeviceId(androidId);
        var d2 = Infrastructure.DeviceIdentityDerivation.DeriveDeviceId(androidId);
        var l1 = Infrastructure.DeviceIdentityDerivation.DeriveLocalDataSpaceId(androidId);
        var l2 = Infrastructure.DeviceIdentityDerivation.DeriveLocalDataSpaceId(androidId);

        Assert.Equal(d1, d2);            // 同输入确定性
        Assert.Equal(l1, l2);
        Assert.NotEqual(d1, l1);         // 不同 prefix 互不相同
        Assert.Equal(64, d1.Length);     // SHA256 hex
        Assert.DoesNotContain("-", d1);  // 非 GUID 格式

        // null / 空串 → GUID 回退（32 位 N 格式）
        var g1 = Infrastructure.DeviceIdentityDerivation.DeriveDeviceId(null);
        var g2 = Infrastructure.DeviceIdentityDerivation.DeriveLocalDataSpaceId(string.Empty);
        Assert.Matches("^[0-9a-f]{32}$", g1);
        Assert.Matches("^[0-9a-f]{32}$", g2);
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

    private static string Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (string)cmd.ExecuteScalar()!;
    }

    private static long CountScalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }
}
