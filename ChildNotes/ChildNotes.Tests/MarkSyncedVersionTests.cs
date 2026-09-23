using ChildNotes.Data;
using ChildNotes.Data.Repositories;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace ChildNotes.Tests;

/// <summary>
/// MarkSynced 版本条件回归（防误标毒丸修复）：
/// 同步期间用户编辑记录（updated_at 推进）后，无条件的 MarkSynced 会把编辑后的行也标为已同步，
/// 下次增量 `updated_at &gt; since OR synced_at IS NULL` 将其过滤 → 本地编辑永久不上送。
/// 修复后：版本匹配 → 设 synced_at；版本已变 → 清 synced_at=NULL，下次同步重新推送。
/// 同时覆盖 id 快照分块（GetPendingIds/GetByIds，消除 OFFSET 分页错位）。
/// </summary>
public class MarkSyncedVersionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly RecordRepository _repo;

    public MarkSyncedVersionTests()
    {
        Batteries_V2.Init();
        _dbPath = Path.Combine(Path.GetTempPath(), $"cn_marksynced_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        DbInitializer.Initialize(_factory);
        _repo = new RecordRepository(_factory);
    }

    private void InsertRecord(string id, string updatedAtUtc, string? syncedAtUtc = null)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // 时间字符串必须与生产写入路径一致：AddUtc 统一写 "O" round-trip 格式（7 位小数 + Z），
        // MarkSynced 的版本比较是 TEXT 精确相等，格式不同则永不匹配。
        string O(string utc) => DateTime.Parse(utc).ToUniversalTime().ToString("O");
        cmd.CommandText = @"
INSERT INTO child_record
(id, user_id, baby_id, record_type, record_date, record_time, payload_json, deleted, created_at, updated_at, synced_at)
VALUES (@id, 'user-1', 'baby-1', 'feed', '2026-09-01', @rt, '{}', 0, @c, @u, @s)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@rt", O(updatedAtUtc));
        cmd.Parameters.AddWithValue("@c", O(updatedAtUtc));
        cmd.Parameters.AddWithValue("@u", O(updatedAtUtc));
        cmd.Parameters.AddWithValue("@s", syncedAtUtc is null ? DBNull.Value : (object)O(syncedAtUtc));
        cmd.ExecuteNonQuery();
    }

    private string? GetSyncedAt(string id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT synced_at FROM child_record WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var result = cmd.ExecuteScalar();
        return result is DBNull or null ? null : (string)result;
    }

    private DateTime LocalOf(string utc) => DateTime.Parse(utc).ToUniversalTime().ToLocalTime();

    [Fact]
    public void VersionUnchanged_SetsSyncedAt()
    {
        const string updatedAt = "2026-09-01T08:00:00Z";
        InsertRecord("r1", updatedAt);

        // 推送时读取的实体 UpdatedAt 为 Local kind（与 ApiSyncService 传参一致）
        _repo.MarkSynced(new[] { ("r1", LocalOf(updatedAt)) }, DateTime.Parse("2026-09-01T09:00:00Z").ToUniversalTime());

        Assert.NotNull(GetSyncedAt("r1"));
    }

    [Fact]
    public void VersionChangedDuringSync_ClearsSyncedAt_SoRowRepushes()
    {
        const string pushedVersion = "2026-09-01T08:00:00Z";
        InsertRecord("r1", pushedVersion);

        // 模拟"批次读取后、MarkSynced 前"用户编辑：updated_at 推进（走生产 "O" 格式）
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE child_record SET updated_at=@u WHERE id='r1'";
            cmd.Parameters.AddWithValue("@u", DateTime.Parse("2026-09-01T08:30:00Z").ToUniversalTime().ToString("O"));
            cmd.ExecuteNonQuery();
        }

        // 用推送时快照版本 MarkSynced：版本不匹配 → synced_at 清 NULL（而非误标）
        _repo.MarkSynced(new[] { ("r1", LocalOf(pushedVersion)) }, DateTime.Parse("2026-09-01T09:00:00Z").ToUniversalTime());

        Assert.Null(GetSyncedAt("r1"));

        // 编辑后的行必须被 GetPendingIds 重新纳入推送（synced_at IS NULL 分支兜底）
        var pending = _repo.GetPendingIds(DateTime.Parse("2026-09-01T09:00:00Z").ToUniversalTime());
        Assert.Contains("r1", pending);
    }

    [Fact]
    public void VersionUnchanged_ButNeverSynced_SetsSyncedAt()
    {
        const string updatedAt = "2026-09-01T08:00:00Z";
        InsertRecord("r1", updatedAt, syncedAtUtc: null);
        _repo.MarkSynced(new[] { ("r1", LocalOf(updatedAt)) }, DateTime.Parse("2026-09-01T09:00:00Z").ToUniversalTime());
        Assert.NotNull(GetSyncedAt("r1"));
    }

    [Fact]
    public void GetPendingIds_And_GetByIds_SnapshotChunking()
    {
        InsertRecord("r1", "2026-09-01T08:00:00Z", syncedAtUtc: "2026-09-01T08:05:00Z"); // 已同步且无更新 → 不推送
        InsertRecord("r2", "2026-09-01T08:10:00Z"); // 未同步 → 推送（synced_at IS NULL 分支）
        InsertRecord("r3", "2026-09-01T08:20:00Z", syncedAtUtc: "2026-09-01T08:05:00Z"); // updated_at > since → 推送

        var since = DateTime.Parse("2026-09-01T08:15:00Z").ToUniversalTime();
        var ids = _repo.GetPendingIds(since);

        // r3 满足增量；r2 满足 synced_at IS NULL 兜底；r1 两者皆否
        Assert.Equal(new[] { "r2", "r3" }, ids);

        var rows = _repo.GetByIds(ids);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Id == "r2");
        Assert.Contains(rows, r => r.Id == "r3");
    }

    public void Dispose()
    {
        try { SQLitePCL.Batteries_V2.Init(); if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
