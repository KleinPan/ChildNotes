using System.Text;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services;

/// <summary>
/// 日志导出服务：同时尝试导出 DevLogger 内存日志与 ReleaseLogger 文件日志（app-*.log）。
/// - DevLogger.Entries：应用内调试日志环形缓冲区（DevLogger 已移除 [Conditional("DEBUG")]，Debug/Release 均写入）。
/// - ReleaseLogger 文件日志：用于闪退后的问题定位。
/// 跨平台兼容：
/// - Android：写 App 私有外部目录（Context.GetExternalFilesDir，无需权限）+ FileProvider 弹系统分享面板
///   （用户可选"保存到文件"/"发微信"/"发邮件"等）。targetSdk=36 强制 Scoped Storage，不能直接写公共目录。
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
        sb.AppendLine($"===== ChildNotes 日志 [{BuildConfiguration.BuildVariant}] =====");
        sb.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"平台: {DevLogger.PlatformTag}");
        sb.AppendLine($"应用版本: {GetAppVersion()}");
        sb.AppendLine($"构建变体: {BuildConfiguration.BuildVariant}");
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
    /// Android：写 App 私有目录 + 弹系统分享面板（用户可保存到下载/发微信/发邮件）。
    /// 桌面端：写入"我的文档/ChildNotes/logs"。
    /// </summary>
    internal static async Task<string> WriteFileAsync(string fileName, string content)
    {
        if (OperatingSystem.IsAndroid())
        {
            return await WriteToAndroidAndShareAsync(fileName, content);
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
    /// Android 平台：写 App 私有外部目录 + 通过 FileProvider 弹系统分享面板。
    ///
    /// 背景：targetSdk=36 强制 Scoped Storage，直接写 /storage/emulated/0/Aiji 会被拒绝
    /// （AndroidManifest 未声明 WRITE_EXTERNAL_STORAGE，且即使声明了 targetSdk=36 也不允许）。
    /// 改为写 Context.GetExternalFilesDir(null)（私有目录，无需权限）+ FileProvider 分享。
    ///
    /// 通过反射调用 ChildNotes.Android.Services.AndroidLogShareService，避免主项目直接引用 Android 项目。
    /// </summary>
    private static async Task<string> WriteToAndroidAndShareAsync(string fileName, string content)
    {
        // 反射调用 AndroidLogShareService.WriteAndShareAsync(string, string)
        var serviceType = Type.GetType("ChildNotes.Android.Services.AndroidLogShareService, ChildNotes.Android");
        if (serviceType is null)
            throw new InvalidOperationException("AndroidLogShareService 类型未找到（应在 ChildNotes.Android 项目中）");

        var method = serviceType.GetMethod("WriteAndShareAsync", new[] { typeof(string), typeof(string) });
        if (method is null)
            throw new InvalidOperationException("AndroidLogShareService.WriteAndShareAsync 方法未找到");

        var task = (Task<string>?)method.Invoke(null, new object[] { fileName, content });
        if (task is null)
            throw new InvalidOperationException("AndroidLogShareService.WriteAndShareAsync 返回 null");
        return await task;
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
