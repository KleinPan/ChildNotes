using System.Text;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services;

/// <summary>
/// "程序日志"页导出服务：将当前筛选后的日志（含 LLM 调用明细）导出为 .txt 文件。
/// 复用 LogExportService.WriteFileAsync 实现跨平台文件写入。
/// </summary>
public static class AppLogExportService
{
    /// <summary>
    /// 导出给定日志条目到 .txt 文件。
    /// </summary>
    public static async Task<LogExportResult> ExportAsync(IReadOnlyList<DevLogger.LogEntry> entries)
    {
        if (entries.Count == 0)
            return LogExportResult.Fail("当前无日志可导出");

        var sb = new StringBuilder(8192);
        sb.AppendLine("===== ChildNotes 程序日志 =====");
        sb.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"平台: {DevLogger.PlatformTag}");
        sb.AppendLine($"条目数: {entries.Count}");
        sb.AppendLine("================================");
        sb.AppendLine();

        foreach (var e in entries)
        {
            sb.AppendLine(e.Full);
        }

        var content = sb.ToString();
        var fileName = $"ChildNotes_applog_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

        try
        {
            var path = await LogExportService.WriteFileAsync(fileName, content);
            return LogExportResult.Ok(path, entries.Count);
        }
        catch (Exception ex)
        {
            DevLogger.Error("AppLogExport", $"导出失败: {ex.Message}");
            return LogExportResult.Fail(ex.Message);
        }
    }
}
