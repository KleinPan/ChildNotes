using ChildNotes.Data;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Sync;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace ChildNotes.Tests;

/// <summary>
/// 复现 v0.7.25 同步崩溃：SQLite Error 19 'UNIQUE constraint failed: baby_member.baby_id, baby_member.user_id'。
/// 根因：baby_member 是 Pull-only（id 由服务端生成），服务端补建/重建成员行后 id 变化，
/// 本地旧行与新下发行 (baby_id, user_id) 相同但 id 不同，ON CONFLICT(id) 匹配不上，
/// INSERT 撞 UNIQUE(baby_id, user_id) 索引。
/// 修复：冲突目标改为逻辑身份 (baby_id, user_id)，命中后采纳服务端 id。
/// </summary>
public class BabyMemberSyncUpsertTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly BabyRepository _repo;

    public BabyMemberSyncUpsertTests()
    {
        Batteries_V2.Init();
        _dbPath = Path.Combine(Path.GetTempPath(), $"cn_member_sync_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        DbInitializer.Initialize(_factory);
        _repo = new BabyRepository(_factory);
    }

    /// <summary>核心回归：同 (baby_id, user_id) 不同 id 的服务端行必须能合并，且采纳服务端 id。</summary>
    [Fact]
    public void ServerRow_WithDifferentId_SameBabyUser_Should_Merge_And_AdoptServerId()
    {
        InsertMember("local-old-id", "baby-1", "user-1", updatedAt: "2026-08-01T00:00:00Z");

        var serverItem = new SyncBabyMemberItem
        {
            Id = "server-new-id", BabyId = "baby-1", UserId = "user-1",
            RoleCode = "owner", RoleName = "爸爸", IsOwner = true, Status = "active",
            CreatedAt = DateTime.Parse("2026-08-20T00:00:00Z").ToUniversalTime(),
            UpdatedAt = DateTime.Parse("2026-08-21T00:00:00Z").ToUniversalTime(),
        };

        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            // v0.7.25 此处抛 SqliteException(19)；修复后正常合并
            bool written = _repo.UpsertMemberFromSync(serverItem, conn, tx);
            tx.Commit();
            Assert.True(written);
        }

        AssertRow("server-new-id", "baby-1", "user-1", "active");
        Assert.Equal(1, CountRows("baby-1", "user-1"));
    }

    [Fact]
    public void SameId_NewerServerRow_Should_Update()
    {
        InsertMember("m-1", "baby-1", "user-1", updatedAt: "2026-08-01T00:00:00Z", status: "active");

        var serverItem = new SyncBabyMemberItem
        {
            Id = "m-1", BabyId = "baby-1", UserId = "user-1",
            RoleCode = "member", RoleName = "妈妈", IsOwner = false, Status = "removed",
            CreatedAt = DateTime.Parse("2026-08-01T00:00:00Z").ToUniversalTime(),
            UpdatedAt = DateTime.Parse("2026-08-02T00:00:00Z").ToUniversalTime(),
        };

        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            Assert.True(_repo.UpsertMemberFromSync(serverItem, conn, tx));
            tx.Commit();
        }

        AssertRow("m-1", "baby-1", "user-1", "removed");
    }

    [Fact]
    public void OlderServerRow_Should_Be_Skipped_By_LWW()
    {
        InsertMember("local-newer-id", "baby-1", "user-1", updatedAt: "2026-08-21T00:00:00Z");

        var serverItem = new SyncBabyMemberItem
        {
            Id = "server-older-id", BabyId = "baby-1", UserId = "user-1",
            RoleCode = "owner", RoleName = "爸爸", IsOwner = true, Status = "active",
            CreatedAt = DateTime.Parse("2026-08-01T00:00:00Z").ToUniversalTime(),
            UpdatedAt = DateTime.Parse("2026-08-01T00:00:00Z").ToUniversalTime(),
        };

        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            Assert.False(_repo.UpsertMemberFromSync(serverItem, conn, tx));
            tx.Commit();
        }

        // 本地行保持不变
        AssertRow("local-newer-id", "baby-1", "user-1", "active");
    }

    [Fact]
    public void NewMemberRow_Should_Insert()
    {
        var serverItem = new SyncBabyMemberItem
        {
            Id = "m-new", BabyId = "baby-2", UserId = "user-1",
            RoleCode = "member", RoleName = "妈妈", IsOwner = false, Status = "active",
            CreatedAt = DateTime.Parse("2026-08-21T00:00:00Z").ToUniversalTime(),
            UpdatedAt = DateTime.Parse("2026-08-21T00:00:00Z").ToUniversalTime(),
        };

        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            Assert.True(_repo.UpsertMemberFromSync(serverItem, conn, tx));
            tx.Commit();
        }

        AssertRow("m-new", "baby-2", "user-1", "active");
    }

    private void InsertMember(string id, string babyId, string userId, string updatedAt, string status = "active")
    {
        using var conn = _factory.Create();
        Exec(conn, $@"
INSERT INTO baby_member (id, baby_id, user_id, role_code, role_name, is_owner, status, created_at, updated_at)
VALUES ('{id}', '{babyId}', '{userId}', 'owner', '爸爸', 1, '{status}', '2026-08-01T00:00:00Z', '{updatedAt}');");
    }

    private void AssertRow(string expectedId, string babyId, string userId, string expectedStatus)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, status FROM baby_member WHERE baby_id = @b AND user_id = @u";
        cmd.Parameters.AddWithValue("@b", babyId);
        cmd.Parameters.AddWithValue("@u", userId);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read(), $"baby_member 应存在 (baby_id={babyId}, user_id={userId})");
        Assert.Equal(expectedId, reader.GetString(0));
        Assert.Equal(expectedStatus, reader.GetString(1));
    }

    private int CountRows(string babyId, string userId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM baby_member WHERE baby_id = @b AND user_id = @u";
        cmd.Parameters.AddWithValue("@b", babyId);
        cmd.Parameters.AddWithValue("@u", userId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* 临时文件清理失败可忽略 */ }
    }
}
