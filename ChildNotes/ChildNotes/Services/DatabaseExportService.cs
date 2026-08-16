using System.IO;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services;

/// <summary>
/// 本地数据库导出/导入服务：把 childnotes.db 导出给用户取走，或从用户选择的文件导入。

///
/// 背景：项目当前用 release 风格的 dev APK（包名 com.babydiary.app.dev），dumpsys flags=0x0
/// 没带 DEBUGGABLE 位，导致 adb run-as / adb root 全部不可用，开发者拿不到私有目录里的 db。
/// 这个服务在 App 内提供"导出/导入数据库"入口，通过 FileProvider 弹系统分享面板，
/// 用户可自行保存到下载目录 / 发到 IM / 邮件 / 网盘，相当于绕开 run-as 限制的兜底通路。
///
/// 平台实现：
/// - 桌面端：复制到 SpecialFolder.MyDocuments/ChildNotes/db/ 并返回绝对路径。
/// - Android：先做 WAL checkpoint 把活跃事务落盘，再通过反射调用
///   ChildNotes.Android.Services.AndroidLogShareService.WriteDbAndShareAsync，
///   把 db 复制到 external-files 目录后弹系统分享面板。
///
/// 与 LogExportService 的差异：
/// - 日志导出导出 .txt（可读），database 导出导出 .db（二进制）。
/// - database 导出对所有构建变体可见（IsLogExportVisible 用 IsDevelopmentBuild 限制），
///   因为 release 风格的 dev APK（包名带 .dev 但无 DEBUGGABLE）才是真实使用场景。
/// </summary>
public static class DatabaseExportService
{
    /// <summary>导出本地数据库，返回结果含文件路径（桌面端是绝对路径，Android 端是文件名）。</summary>
    public static async Task<DatabaseExportResult> ExportAsync()
    {
        var dbPath = ServiceProvider.Instance.DbFactory.DbPath;
        if (!File.Exists(dbPath))
        {
            return DatabaseExportResult.Fail($"数据库文件不存在: {dbPath}");
        }

        try
        {
            // 关键步骤：先 WAL checkpoint 把 -wal 里的活跃事务合并到主文件，
            // 否则直接复制 childnotes.db 拿到的可能只是一个空壳（用户最初 1KB 现象的根因）。
            // 用完立即释放连接，避免长时间持锁阻塞后续 UI 线程的访问。
            ServiceProvider.Instance.DbFactory.Checkpoint();

            if (OperatingSystem.IsAndroid())
            {
                return await ExportOnAndroidAsync(dbPath);
            }

            return ExportOnDesktop(dbPath);
        }
        catch (Exception ex)
        {
            DevLogger.Log("DBExport", $"Export failed: {ex}");
            ReleaseLogger.Error("DBExport", ex, "Database export failed");
            return DatabaseExportResult.Fail(ex.Message);
        }
    }

    private static DatabaseExportResult ExportOnDesktop(string dbPath)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ChildNotes", "db");
        Directory.CreateDirectory(dir);

        var fileName = $"childnotes_{DateTime.Now:yyyyMMdd_HHmmss}.db";
        var destPath = Path.Combine(dir, fileName);

        // File.Copy 默认覆盖=true；源文件可能仍被 SQLite 短暂持有读锁，但 WAL checkpoint
        // 后主文件已是稳定状态。Windows 上拷贝中源文件被独占读不会失败。
        File.Copy(dbPath, destPath, overwrite: true);

        var sizeBytes = new FileInfo(destPath).Length;
        return DatabaseExportResult.Ok(destPath, sizeBytes);
    }

    private static async Task<DatabaseExportResult> ExportOnAndroidAsync(string dbPath)
    {
        // 反射调用 Android 端：主项目不直接引用 Android 项目，保持抽象。
        var serviceType = Type.GetType("ChildNotes.Android.Services.AndroidLogShareService, ChildNotes.Android");
        if (serviceType is null)
            throw new InvalidOperationException("AndroidLogShareService 类型未找到（应在 ChildNotes.Android 项目中）");

        var method = serviceType.GetMethod("WriteDbAndShareAsync", new[] { typeof(string) });
        if (method is null)
            throw new InvalidOperationException("AndroidLogShareService.WriteDbAndShareAsync 方法未找到");

        var task = (Task<string>?)method.Invoke(null, new object[] { dbPath });
        if (task is null)
            throw new InvalidOperationException("AndroidLogShareService.WriteDbAndShareAsync 返回 null");

        var displayPath = await task;
        // Android 端返回的 displayPath 是文件名（不暴露私有目录结构），
        // size 在主文件 checkpoint 后与源文件一致
        var sizeBytes = new FileInfo(dbPath).Length;
        return DatabaseExportResult.Ok(displayPath, sizeBytes);
    }

    /// <summary>
    /// 导入数据库：从用户选择的 .db 文件恢复本地数据库。
    /// 调用前必须确保当前数据库连接已关闭，导入后需重启应用使新数据库生效。
    ///
    /// 安全检查：
    /// 1. 验证文件是有效 SQLite 数据库（能打开并读取 user_version）
    /// 2. 验证 schema 版本与当前应用兼容（user_version <= CurrentSchemaVersion）
    /// 3. 备份当前数据库到 .before_import 后再替换，失败可手动回滚
    /// </summary>
    public static async Task<DatabaseExportResult> ImportAsync(string? sourceDbPath = null)
    {
        // Android 端：未传入路径时，从 external-files 目录查找 childnotes_import.db
        if (sourceDbPath is null && OperatingSystem.IsAndroid())
        {
            sourceDbPath = await FindImportableDbOnAndroidAsync();
            if (sourceDbPath is null)
            {
                return DatabaseExportResult.Fail(
                    "未找到导入文件。请先通过文件管理器把备份的 childnotes.db 放到 " +
                    "Android/data/com.babydiary.app.dev/files/ 目录并重命名为 childnotes_import.db");
            }
        }

        if (sourceDbPath is null || !File.Exists(sourceDbPath))
        {
            return DatabaseExportResult.Fail($"源数据库文件不存在: {sourceDbPath}");
        }

        try
        {
            var dbPath = ServiceProvider.Instance.DbFactory.DbPath;

            // 1. 验证源文件是有效 SQLite 数据库，并读取 schema 版本
            int sourceVersion;
            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={sourceDbPath}");
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version;";
                sourceVersion = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                return DatabaseExportResult.Fail($"源文件不是有效的 SQLite 数据库: {ex.Message}");
            }

            // 2. 验证 schema 版本兼容性
            // 允许源版本 <= 当前版本（DbInitializer 会增量迁移；若源版本更旧，需先在桌面版迁移到当前版本再导入）
            // 禁止源版本 > 当前版本（可能是新版数据库，导入到旧版 app 会有未知列导致读取出错）
            if (sourceVersion > Data.DbInitializer.CurrentSchemaVersion)
            {
                return DatabaseExportResult.Fail(
                    $"源数据库 schema 版本 (v{sourceVersion}) 高于当前应用支持版本 (v{Data.DbInitializer.CurrentSchemaVersion})，" +
                    "请先更新应用到最新版本再导入。");
            }

            // 3. 关闭当前数据库连接，清空连接池
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // 4. 备份当前数据库（覆盖之前）
            var backupPath = dbPath + ".before_import";
            try
            {
                if (File.Exists(backupPath)) File.Delete(backupPath);
                if (File.Exists(dbPath)) File.Move(dbPath, backupPath);
                // WAL 和 SHM 旁路文件也需要移除，否则新数据库会被旧的 WAL 覆盖
                var wal = dbPath + "-wal";
                var shm = dbPath + "-shm";
                if (File.Exists(wal)) File.Delete(wal);
                if (File.Exists(shm)) File.Delete(shm);
            }
            catch (Exception ex)
            {
                // 备份失败不阻塞导入，但记录日志
                DevLogger.Log("DBImport", $"Backup current db failed (non-fatal): {ex.Message}");
            }

            // 5. 复制源文件到目标位置
            await using (var src = new FileStream(sourceDbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            await using (var dst = new FileStream(dbPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await src.CopyToAsync(dst);
            }

            var sizeBytes = new FileInfo(dbPath).Length;
            DevLogger.Log("DBImport", $"Import success: source v{sourceVersion} -> {dbPath} ({sizeBytes} bytes). Restart required.");

            return DatabaseExportResult.Ok(dbPath, sizeBytes);
        }
        catch (Exception ex)
        {
            DevLogger.Log("DBImport", $"Import failed: {ex}");
            ReleaseLogger.Error("DBImport", ex, "Database import failed");
            return DatabaseExportResult.Fail(ex.Message);
        }
    }

    private static async Task<string?> FindImportableDbOnAndroidAsync()
    {
        // 反射调用 Android 端：主项目不直接引用 Android 项目，保持抽象。
        var serviceType = Type.GetType("ChildNotes.Android.Services.AndroidLogShareService, ChildNotes.Android");
        if (serviceType is null) return null;

        var method = serviceType.GetMethod("FindImportableDbAsync", Type.EmptyTypes);
        if (method is null) return null;

        var task = (Task<string?>?)method.Invoke(null, null);
        return task is null ? null : await task;
    }
}

/// <summary>数据库导出/导入结果。</summary>
public sealed class DatabaseExportResult
{
    public bool Success { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;

    public static DatabaseExportResult Ok(string filePath, long sizeBytes) => new()
    {
        Success = true,
        FilePath = filePath,
        SizeBytes = sizeBytes,
    };

    public static DatabaseExportResult Fail(string error) => new()
    {
        Success = false,
        ErrorMessage = error,
    };
}
