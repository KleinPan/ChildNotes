using System.IO;
using System.Text;
using System.Text.Json;
using ChildNotes.Data;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using Microsoft.Data.Sqlite;

namespace ChildNotes.Services.Sync;

/// <summary>
/// WebDAV 整库同步服务。
///
/// 策略：
/// 1. 数据库同步：整文件覆盖。对比本地 SQLite 的 updated_at 最大值 与远程 db 文件的 ETag/LastModified。
/// 2. 图片同步：增量。本地 images 目录扫描文件名，远程列出文件名，缺失的互相拉推。
/// 3. 冲突处理：3 人家庭低频并发，简单策略——若远程比本地"新"且本地也有改动，提示用户手动选择。
///
/// 触发时机：
/// - App 启动后（登录后）
/// - 写入记录后（防抖 5 秒）
/// - 手动点击同步按钮
/// </summary>
public sealed class SyncService
{
    private readonly DbConnectionFactory _factory;
    private readonly WebDavConfigRepository _configRepo;
    private readonly string _dbPath;
    private readonly string _imageDir;
    private readonly string _deviceId;

    // 远程文件名
    private const string RemoteDbName = "childnotes.db";
    private const string RemoteDbMetaName = "childnotes.db.meta.json";
    private const string RemoteImageDir = "images/";

    public SyncService(DbConnectionFactory factory, WebDavConfigRepository configRepo, string dbPath, string imageDir)
    {
        _factory = factory;
        _configRepo = configRepo;
        _dbPath = dbPath;
        _imageDir = imageDir;
        _deviceId = GetOrCreateDeviceId();
    }

    /// <summary>同步状态变化事件，UI 可订阅显示进度。</summary>
    public event Action<SyncProgress>? ProgressChanged;

    /// <summary>当前是否正在同步中。</summary>
    public bool IsSyncing { get; private set; }

    /// <summary>
    /// 执行一次完整同步：拉取远程 → 推送本地 → 同步图片。
    /// </summary>
    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var cfg = _configRepo.GetOrCreate();
        if (!cfg.Enabled || string.IsNullOrEmpty(cfg.ServerUrl))
            return SyncResult.Skipped("同步未启用或未配置");

        if (IsSyncing) return SyncResult.Skipped("正在同步中");
        IsSyncing = true;

        var startedAt = DateTime.UtcNow;
        var progress = new SyncProgress { Stage = "开始同步", StartedAt = startedAt };
        RaiseProgress(progress);

        try
        {
            using var client = CreateClient(cfg);
            await client.EnsureFolderAsync("", ct);
            await client.EnsureFolderAsync(RemoteImageDir, ct);

            // 1. 数据库同步
            var dbResult = await SyncDatabaseAsync(client, cfg, progress, ct);

            // 2. 图片同步
            var imgResult = await SyncImagesAsync(client, progress, ct);

            var status = (dbResult.Success && imgResult.Success) ? "success" : "partial";
            _configRepo.UpdateSyncResult(status, startedAt);

            progress.Stage = "完成";
            progress.IsFinished = true;
            RaiseProgress(progress);

            return new SyncResult(true, status, dbResult.Detail + " | " + imgResult.Detail);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Sync", ex);
            _configRepo.UpdateSyncResult("failed", startedAt);
            progress.Stage = "失败: " + ex.Message;
            progress.IsFinished = true;
            progress.IsError = true;
            RaiseProgress(progress);
            return new SyncResult(false, "failed", ex.Message);
        }
        finally
        {
            IsSyncing = false;
        }
    }

    /// <summary>仅推送本地 db 到远程（写入后防抖触发，快速路径）。</summary>
    public async Task<SyncResult> PushDbOnlyAsync(CancellationToken ct = default)
    {
        var cfg = _configRepo.GetOrCreate();
        if (!cfg.Enabled || string.IsNullOrEmpty(cfg.ServerUrl))
            return SyncResult.Skipped("同步未启用");

        if (IsSyncing) return SyncResult.Skipped("正在同步中");
        IsSyncing = true;
        try
        {
            using var client = CreateClient(cfg);
            await client.EnsureFolderAsync("", ct);

            // 先检查远程是否被他人更新过
            var remote = await client.GetFileInfoAsync(RemoteDbName, ct);
            var localMeta = ReadLocalMeta();
            var localMaxUpdated = GetLocalMaxUpdatedAt();

            // 如果远程比本地记录的 last_synced_at 更新，说明有他人推送 → 先 pull
            if (remote != null && localMeta != null && remote.LastModified.HasValue)
            {
                if (remote.LastModified.Value > localMeta.LastSyncedAt.AddSeconds(1))
                {
                    // 远程有更新，需要先拉取合并
                    return await SyncAsync(ct);
                }
            }

            // 推送本地 db
            await using (var fs = File.OpenRead(_dbPath))
            {
                await client.PutAsync(RemoteDbName, fs, ct);
            }

            // 推送 meta
            var newMeta = new SyncMeta
            {
                LastSyncedAt = DateTime.UtcNow,
                LocalMaxUpdatedAt = localMaxUpdated,
                DeviceId = _deviceId
            };
            await using var metaStream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(newMeta)));
            await client.PutAsync(RemoteDbMetaName, metaStream, ct);

            return new SyncResult(true, "success", "推送成功");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Sync", ex);
            return new SyncResult(false, "failed", ex.Message);
        }
        finally
        {
            IsSyncing = false;
        }
    }

    // ===== 数据库同步 =====

    private async Task<(bool Success, string Detail)> SyncDatabaseAsync(
        WebDavClient client, WebDavConfig cfg, SyncProgress progress, CancellationToken ct)
    {
        progress.Stage = "检查远程数据库";
        RaiseProgress(progress);

        var remoteInfo = await client.GetFileInfoAsync(RemoteDbName, ct);
        var localMeta = ReadLocalMeta();
        var localMaxUpdated = GetLocalMaxUpdatedAt();

        // 远程不存在 → 直接推送本地
        if (remoteInfo == null)
        {
            progress.Stage = "首次推送数据库";
            RaiseProgress(progress);
            await PushDbAndMetaAsync(client, localMaxUpdated, ct);
            return (true, "首次推送数据库");
        }

        // 远程存在，读取远程 meta
        var remoteMeta = await ReadRemoteMetaAsync(client, ct);
        var remoteMaxUpdated = remoteMeta?.LocalMaxUpdatedAt ?? DateTime.MinValue;

        bool localHasChanges = localMeta == null || localMaxUpdated > localMeta.LastSyncedAt.AddSeconds(-1);
        bool remoteHasChanges = remoteMaxUpdated > (localMeta?.LastSyncedAt ?? DateTime.MinValue).AddSeconds(-1);

        if (localHasChanges && remoteHasChanges)
        {
            // 双方都有变更 → 冲突
            // 3 人家庭低频场景：用 updated_at 最新者胜（LWW）
            if (localMaxUpdated >= remoteMaxUpdated)
            {
                progress.Stage = "冲突解决：本地较新，推送本地";
                RaiseProgress(progress);
                await PushDbAndMetaAsync(client, localMaxUpdated, ct);
                return (true, "冲突-本地胜");
            }
            else
            {
                progress.Stage = "冲突解决：远程较新，拉取远程";
                RaiseProgress(progress);
                await PullDbAsync(client, remoteMeta!, ct);
                return (true, "冲突-远程胜");
            }
        }

        if (localHasChanges && !remoteHasChanges)
        {
            progress.Stage = "推送本地变更";
            RaiseProgress(progress);
            await PushDbAndMetaAsync(client, localMaxUpdated, ct);
            return (true, "推送本地变更");
        }

        if (!localHasChanges && remoteHasChanges)
        {
            progress.Stage = "拉取远程变更";
            RaiseProgress(progress);
            await PullDbAsync(client, remoteMeta!, ct);
            return (true, "拉取远程变更");
        }

        // 双方都无变更
        return (true, "已是最新");
    }

    private async Task PushDbAndMetaAsync(WebDavClient client, DateTime localMaxUpdated, CancellationToken ct)
    {
        // 注意：推送前先 checkpoint，把 WAL 日志合并到主 db 文件
        CheckpointWal();

        await using (var fs = File.OpenRead(_dbPath))
        {
            await client.PutAsync(RemoteDbName, fs, ct);
        }

        var meta = new SyncMeta
        {
            LastSyncedAt = DateTime.UtcNow,
            LocalMaxUpdatedAt = localMaxUpdated,
            DeviceId = _deviceId
        };
        await using var metaStream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(meta)));
        await client.PutAsync(RemoteDbMetaName, metaStream, ct);

        // 更新本地 meta
        WriteLocalMeta(meta);
    }

    private async Task PullDbAsync(WebDavClient client, SyncMeta remoteMeta, CancellationToken ct)
    {
        // 下载到临时文件
        var tmpPath = _dbPath + ".remote.tmp";
        try
        {
            await using (var remoteStream = await client.GetAsync(RemoteDbName, ct))
            await using (var fs = File.Create(tmpPath))
            {
                await remoteStream.CopyToAsync(fs, ct);
            }

            // 关闭本地连接（由调用方确保），替换 db 文件
            // 注意：DbConnectionFactory 持有的连接需要先释放
            // 这里采用：下载完后，由外部触发重新初始化
            SqliteConnection.ClearAllPools();
            File.Copy(tmpPath, _dbPath, true);

            // 更新本地 meta
            WriteLocalMeta(new SyncMeta
            {
                LastSyncedAt = DateTime.UtcNow,
                LocalMaxUpdatedAt = remoteMeta.LocalMaxUpdatedAt,
                DeviceId = _deviceId
            });
        }
        finally
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
        }
    }

    // ===== 图片同步 =====

    private async Task<(bool Success, string Detail)> SyncImagesAsync(
        WebDavClient client, SyncProgress progress, CancellationToken ct)
    {
        if (!Directory.Exists(_imageDir))
        {
            Directory.CreateDirectory(_imageDir);
            return (true, "图片目录为空");
        }

        progress.Stage = "同步图片";
        RaiseProgress(progress);

        var localFiles = Directory.GetFiles(_imageDir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet();

        var remoteFiles = await client.ListFilesAsync(RemoteImageDir, ct);
        var remoteSet = remoteFiles.ToHashSet();

        int pushed = 0, pulled = 0;

        // 推送本地有、远程没有的
        foreach (var local in localFiles.Except(remoteSet))
        {
            ct.ThrowIfCancellationRequested();
            var localPath = Path.Combine(_imageDir, local);
            await using var fs = File.OpenRead(localPath);
            await client.PutAsync(RemoteImageDir + local, fs, ct);
            pushed++;
        }

        // 拉取远程有、本地没有的
        foreach (var remote in remoteSet.Except(localFiles))
        {
            ct.ThrowIfCancellationRequested();
            var localPath = Path.Combine(_imageDir, remote);
            await using var remoteStream = await client.GetAsync(RemoteImageDir + remote, ct);
            await using var fs = File.Create(localPath);
            await remoteStream.CopyToAsync(fs, ct);
            pulled++;
        }

        progress.Stage = $"图片：推送 {pushed}，拉取 {pulled}";
        RaiseProgress(progress);

        return (true, $"图片推送{pushed}拉取{pulled}");
    }

    // ===== 辅助 =====

    private WebDavClient CreateClient(WebDavConfig cfg)
    {
        return new WebDavClient(cfg.ServerUrl, cfg.Username, cfg.Password, cfg.RemotePath);
    }

    private void RaiseProgress(SyncProgress p)
    {
        DevLogger.Log("Sync", p.Stage);
        ProgressChanged?.Invoke(p);
    }

    /// <summary>获取本地所有业务表 updated_at 的最大值，作为"本地数据版本"。</summary>
    private DateTime GetLocalMaxUpdatedAt()
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT MAX(t) FROM (
                SELECT MAX(updated_at) AS t FROM child_record
                UNION ALL SELECT MAX(updated_at) FROM milestone
                UNION ALL SELECT MAX(updated_at) FROM baby
                UNION ALL SELECT MAX(updated_at) FROM baby_member
                UNION ALL SELECT MAX(updated_at) FROM ai_analysis_record
                UNION ALL SELECT MAX(updated_at) FROM user_points
                UNION ALL SELECT MAX(updated_at) FROM app_user
            );";
        var result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value) return DateTime.MinValue;
        return DateTime.Parse((string)result);
    }

    private void CheckpointWal()
    {
        try
        {
            using var conn = _factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            DevLogger.Log("Sync", $"WAL checkpoint failed: {ex.Message}");
        }
    }

    // ===== 本地 meta 文件 =====

    private string LocalMetaPath => _dbPath + ".meta.json";

    private SyncMeta? ReadLocalMeta()
    {
        if (!File.Exists(LocalMetaPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<SyncMeta>(File.ReadAllText(LocalMetaPath));
        }
        catch { return null; }
    }

    private void WriteLocalMeta(SyncMeta meta)
    {
        File.WriteAllText(LocalMetaPath, JsonSerializer.Serialize(meta));
    }

    private async Task<SyncMeta?> ReadRemoteMetaAsync(WebDavClient client, CancellationToken ct)
    {
        try
        {
            await using var stream = await client.GetAsync(RemoteDbMetaName, ct);
            using var sr = new StreamReader(stream);
            var json = await sr.ReadToEndAsync(ct);
            return JsonSerializer.Deserialize<SyncMeta>(json);
        }
        catch { return null; }
    }

    // ===== 设备 ID =====

    private string GetOrCreateDeviceId()
    {
        var idPath = Path.Combine(Path.GetDirectoryName(_dbPath)!, "device.id");
        if (File.Exists(idPath)) return File.ReadAllText(idPath).Trim();
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(idPath, id);
        return id;
    }
}

/// <summary>同步进度通知。</summary>
public sealed class SyncProgress
{
    public DateTime StartedAt { get; set; }
    public string Stage { get; set; } = "";
    public bool IsFinished { get; set; }
    public bool IsError { get; set; }
}

/// <summary>同步结果。</summary>
public sealed record SyncResult(bool Success, string Status, string Detail)
{
    public static SyncResult Skipped(string reason) => new(false, "skipped", reason);
}

/// <summary>同步元数据。</summary>
public sealed class SyncMeta
{
    public DateTime LastSyncedAt { get; set; }
    public DateTime LocalMaxUpdatedAt { get; set; }
    public string DeviceId { get; set; } = "";
}
