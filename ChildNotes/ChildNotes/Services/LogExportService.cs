using System.Text;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services;

/// <summary>
/// 日志导出服务：同时尝试导出 DevLogger 内存日志与 ReleaseLogger 文件日志（app-*.log）。
/// - DevLogger.Entries：应用内调试日志环形缓冲区（DevLogger 已移除 [Conditional("DEBUG")]，Debug/Release 均写入）。
/// - ReleaseLogger 文件日志：用于闪退后的问题定位。
/// 跨平台兼容：
/// - Android 10+ (API 29+)：通过 MediaStore.Downloads 写入公共 Download 目录（无需存储权限）。
/// - Android 9 及以下 (API &lt; 29)：直接写 /storage/emulated/0/Download（需 WRITE_EXTERNAL_STORAGE，已由 manifest 声明）。
/// - 桌面端 (Windows/macOS/Linux)：写入 SpecialFolder.MyDocuments/ChildNotes/logs。
/// </summary>
public static class LogExportService
{
    /// <summary>
    /// 导出日志到 .txt 文件。
    /// 优先合并 ReleaseLogger 的文件日志（崩溃排查用）；若 DevLogger 有条目也一并追加。
    /// </summary>
    /// <returns>导出结果（成功则含文件路径/提示；失败则含错误信息）。</returns>
    public static async Task<LogExportResult> ExportAsync()
    {
        var devEntries = DevLogger.Entries;
        var releaseFiles = ReleaseLogger.GetLogFiles();

        if (devEntries.Count == 0 && releaseFiles.Count == 0)
        {
            return LogExportResult.Fail("当前无日志可导出");
        }

        // 构造日志文本：头部信息 + Release 文件日志 + Dev 内存日志
        var sb = new StringBuilder(8192);
        sb.AppendLine("===== ChildNotes 日志 =====");
        sb.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"平台: {DevLogger.PlatformTag}");
        sb.AppendLine($"应用版本: {GetAppVersion()}");
        sb.AppendLine($"Dev 日志条目数: {devEntries.Count}");
        sb.AppendLine($"Release 日志文件数: {releaseFiles.Count}");
        sb.AppendLine("================================");
        sb.AppendLine();

        // 1. Release 文件日志（Serilog 写入的 app-*.log，含崩溃堆栈）
        if (releaseFiles.Count > 0)
        {
            sb.AppendLine("----- Release 日志（Serilog 文件） -----");
            foreach (var file in releaseFiles)
            {
                sb.AppendLine($"### 文件: {file.Name} (修改于 {file.LastWriteTime:yyyy-MM-dd HH:mm:ss}) ###");
                try
                {
                    var content = await File.ReadAllTextAsync(file.FullName);
                    sb.AppendLine(content);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[读取失败: {ex.Message}]");
                }
                sb.AppendLine();
            }
        }

        // 2. Dev 内存日志（Debug 阶段的应用内调试日志）
        if (devEntries.Count > 0)
        {
            sb.AppendLine("----- Dev 日志（内存浮层） -----");
            foreach (var entry in devEntries)
            {
                sb.AppendLine(entry.Full);
            }
        }

        var allContent = sb.ToString();
        var fileName = $"ChildNotes_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

        try
        {
            var path = await WriteFileAsync(fileName, allContent);
            var totalLines = devEntries.Count + releaseFiles.Count;
            return LogExportResult.Ok(path, totalLines);
        }
        catch (Exception ex)
        {
            DevLogger.Log("LogExport", $"导出失败: {ex}");
            ReleaseLogger.Error("LogExport", ex, "Log export failed");
            return LogExportResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 按平台写入文件。供 AppLogExportService 复用。
    /// </summary>
    internal static async Task<string> WriteFileAsync(string fileName, string content)
    {
        if (OperatingSystem.IsAndroid())
        {
            return await WriteToAndroidDownloadAsync(fileName, content);
        }

        // 桌面端：写入"我的文档/ChildNotes/logs"
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ChildNotes", "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    /// <summary>
    /// Android 平台写入 Download 目录。
    /// - API 29+ (Android 10+)：通过 MediaStore.Downloads，无需权限。
    /// - API &lt; 29：直接写公共 Download 路径（需 WRITE_EXTERNAL_STORAGE 权限）。
    /// 通过反射调用 Android API，避免在共享项目引用 Mono.Android。
    /// </summary>
    private static async Task<string> WriteToAndroidDownloadAsync(string fileName, string content)
    {
        var apiLevel = GetAndroidApiLevel();

        // Android 10+ 使用 MediaStore
        if (apiLevel >= 29)
        {
            return await WriteViaMediaStoreAsync(fileName, content);
        }

        // Android 9 及以下：直接写文件路径
        // /storage/emulated/0/Download/
        var downloadDir = "/storage/emulated/0/Download";
        Directory.CreateDirectory(downloadDir);
        var path = Path.Combine(downloadDir, fileName);
        await File.WriteAllTextAsync(path, content);

        // 通知 MediaScanner 扫描新文件，使其在文件管理器中立即可见
        TryNotifyMediaScanner(path);
        return path;
    }

    /// <summary>
    /// 通过 MediaStore.Downloads 写入文件（Android 10+）。
    /// 使用反射调用 Android API，避免共享项目直接依赖 Mono.Android。
    /// </summary>
    private static async Task<string> WriteViaMediaStoreAsync(string fileName, string content)
    {
        // 获取 Android Context
        var appType = Type.GetType("Android.App.Application, Mono.Android")
            ?? throw new InvalidOperationException("无法加载 Mono.Android 类型");
        var context = appType.GetProperty("Context")?.GetValue(null)
            ?? throw new InvalidOperationException("Android Context 为空");

        // context.ContentResolver
        var contentResolver = context.GetType().GetMethod("get_ContentResolver")?.Invoke(context, null)
            ?? throw new InvalidOperationException("ContentResolver 为空");

        // MediaStore.Downloads.ExternalContentUri（静态属性）
        var mediaStoreType = Type.GetType("Android.Provider.MediaStore, Mono.Android")
            ?? throw new InvalidOperationException("无法加载 MediaStore");
        var downloadsType = mediaStoreType.GetNestedType("Downloads")
            ?? throw new InvalidOperationException("无法加载 MediaStore.Downloads");

        // MediaStore.Downloads.ExternalContentUri
        var externalContentUri = downloadsType.GetProperty("ExternalContentUri")?.GetValue(null)
            ?? throw new InvalidOperationException("无法获取 ExternalContentUri");

        // 创建 ContentValues：DISPLAY_NAME = fileName, MIME_TYPE = text/plain
        var contentValuesType = Type.GetType("Android.Content.ContentValues, Mono.Android")
            ?? throw new InvalidOperationException("无法加载 ContentValues");
        var values = Activator.CreateInstance(contentValuesType)!;
        contentValuesType.GetMethod("Put", new[] { typeof(string), typeof(string) })
            ?.Invoke(values, new object[] { "_display_name", fileName });
        contentValuesType.GetMethod("Put", new[] { typeof(string), typeof(string) })
            ?.Invoke(values, new object[] { "mime_type", "text/plain" });

        // Android 10 (API 29) 需设置 IS_PENDING=1；Android 11+ (API 30+) 由系统自动管理。
        if (GetAndroidApiLevel() == 29)
        {
            contentValuesType.GetMethod("Put", new[] { typeof(string), typeof(int) })
                ?.Invoke(values, new object[] { "is_pending", 1 });
        }

        // resolver.Insert(uri, values) → Uri of new file
        var insertMethod = contentResolver.GetType().GetMethod("Insert",
            new[] { externalContentUri.GetType(), contentValuesType });
        var fileUri = insertMethod?.Invoke(contentResolver, new[] { externalContentUri, values })
            ?? throw new InvalidOperationException("MediaStore.Insert 返回空");

        // resolver.OpenOutputStream(uri) → Stream
        var openOutputStreamMethod = contentResolver.GetType().GetMethods()
            .FirstOrDefault(m => m.Name == "OpenOutputStream" && m.GetParameters().Length == 1);
        if (openOutputStreamMethod is null)
            throw new InvalidOperationException("无法找到 OpenOutputStream 方法");

        var stream = openOutputStreamMethod.Invoke(contentResolver, new[] { fileUri })
            ?? throw new InvalidOperationException("OpenOutputStream 返回空");

        // 写入内容
        using var writer = new StreamWriter((System.IO.Stream)stream);
        await writer.WriteAsync(content);
        await writer.FlushAsync();

        // Android 10：写完后清除 IS_PENDING
        if (GetAndroidApiLevel() == 29)
        {
            contentValuesType.GetMethod("Put", new[] { typeof(string), typeof(int) })
                ?.Invoke(values, new object[] { "is_pending", 0 });
            contentResolver.GetType().GetMethod("Update",
                new[] { externalContentUri.GetType(), contentValuesType, typeof(string), typeof(string[]) })
                ?.Invoke(contentResolver, new[] { fileUri, values, null, null });
        }

        return $"Download/{fileName}";
    }

    /// <summary>获取 Android API Level（不可用时返回 0）。</summary>
    private static int GetAndroidApiLevel()
    {
        if (!OperatingSystem.IsAndroid()) return 0;
        try
        {
            var buildVersion = Type.GetType("Android.OS.BuildVersions, Mono.Android");
            if (buildVersion is null) return 0;
            var sdkIntProp = buildVersion.GetProperty("SdkInt");
            if (sdkIntProp?.GetValue(null) is int apiLevel) return apiLevel;
        }
        catch { }
        return 0;
    }

    /// <summary>通知 MediaScanner 扫描文件（Android 9 及以下，使文件在文件管理器中立即可见）。</summary>
    private static void TryNotifyMediaScanner(string path)
    {
        try
        {
            var appType = Type.GetType("Android.App.Application, Mono.Android");
            if (appType is null) return;
            var context = appType.GetProperty("Context")?.GetValue(null);
            if (context is null) return;

            // 从 Java.IO.File 构造 Uri
            var fileType = Type.GetType("Java.IO.File, Mono.Android");
            if (fileType is null) return;
            var file = Activator.CreateInstance(fileType, path);

            // Uri.FromFile(file)
            var uriType = Type.GetType("Android.Net.Uri, Mono.Android");
            if (uriType is null) return;
            var fromFileMethod = uriType.GetMethod("FromFile");
            var uri = fromFileMethod?.Invoke(null, new[] { file });
            if (uri is null) return;

            // Intent(ActionMediaScannerScanFile, uri)
            var intentType = Type.GetType("Android.Content.Intent, Mono.Android");
            if (intentType is null) return;
            var actionMediaScannerScanFile = intentType.GetField("ActionMediaScannerScanFile")?.GetValue(null)
                as string;
            if (string.IsNullOrEmpty(actionMediaScannerScanFile)) return;

            var intent = Activator.CreateInstance(intentType, actionMediaScannerScanFile, uri);
            // context.SendBroadcast(intent)
            context.GetType().GetMethod("SendBroadcast", new[] { intentType })
                ?.Invoke(context, new[] { intent });
        }
        catch
        {
            // 扫描通知失败不影响文件已写入的事实
        }
    }

    private static string GetAppVersion()
    {
        try
        {
            var asm = typeof(LogExportService).Assembly;
            var ver = asm.GetName().Version;
            return ver?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}

/// <summary>日志导出结果。</summary>
public sealed class LogExportResult
{
    public bool Success { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public int LineCount { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;

    public static LogExportResult Ok(string filePath, int lineCount) => new()
    {
        Success = true,
        FilePath = filePath,
        LineCount = lineCount
    };

    public static LogExportResult Fail(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}
