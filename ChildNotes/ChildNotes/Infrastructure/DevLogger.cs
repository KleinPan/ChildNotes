using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;

namespace ChildNotes.Infrastructure;

/// <summary>
/// 统一日志服务。所有日志同时写入：
/// 1. 内部环形缓冲区（供 DevLogOverlay 浮层显示，仅 Debug 且开关打开时）
/// 2. ReleaseLogger 文件日志（按天滚动，异步写入，自动脱敏）
///
/// 注意：曾用 [Conditional("DEBUG")] 在 Release 裁剪调用点，但导致 Release 构建无法
/// 通过日志排查问题（如键盘上推不生效）。现已移除条件编译，所有调用点在 Release 也执行。
/// 日志量可控：ReleaseLogger 内置速率限制 + 文件大小滚动，不会无限增长。
/// </summary>
public static class DevLogger
{
    /// <summary>
    /// 环形缓冲区容量。扩大到 2000 行以容纳 LLM 调用明细日志（单次调用会产生 2 条：请求 + 响应）。
    /// 原 500 行在 AI 解析密集使用时会在几秒内被覆盖，无法回看。
    /// </summary>
    private const int MaxLines = 2000;

    /// <summary>日志级别。用于"程序日志"页筛选。</summary>
    public enum Level
    {
        Info = 0,
        Warn = 1,
        Error = 2,
        Debug = 3,
    }

    public sealed class LogEntry
    {
        public DateTime Time { get; init; }
        public string Tag { get; init; } = "";
        public string Message { get; init; } = "";
        public Level Level { get; init; } = Level.Info;
        public string Full => $"[{Time:HH:mm:ss.fff}][{Tag}][{Level}] {Message}";
    }

    private static readonly ObservableCollection<LogEntry> _entries = new();
    public static IReadOnlyList<LogEntry> Entries => _entries;
    public static event Action<LogEntry>? Logged;

    /// <summary>当前运行平台简称（Win/And/iOS/macOS），用于日志开头标识。</summary>
    public static string PlatformTag =>
        OperatingSystem.IsAndroid() ? "And"
        : OperatingSystem.IsIOS() ? "iOS"
        : OperatingSystem.IsMacOS() ? "mac"
        : OperatingSystem.IsWindows() ? "Win"
        : "???";

    /// <summary>
    /// 写入一条日志。同时输出到 Debug、环形缓冲区（DevLogOverlay）、ReleaseLogger 文件。
    /// 不再使用 [Conditional("DEBUG")]，确保 Release 构建也能通过文件日志排查问题。
    /// </summary>
    public static void Log(string tag, string message, Level level = Level.Info)
    {
        var fullTag = $"{PlatformTag}/{tag}";
        var entry = new LogEntry { Time = DateTime.Now, Tag = fullTag, Message = message, Level = level };

        // 1. 始终输出到 Debug（logcat/调试器输出）
        Debug.WriteLine(entry.Full);

        // 2. 写入文件日志（异步、脱敏、按天滚动）
        //    ReleaseLogger 已初始化时生效，未初始化则跳过（不抛异常）
        switch (level)
        {
            case Level.Warn:
                ReleaseLogger.Warn(fullTag, message);
                break;
            case Level.Error:
                ReleaseLogger.Error(fullTag, message);
                break;
            default:
                ReleaseLogger.Info(fullTag, message);
                break;
        }

        // 3. 写入环形缓冲区（供 DevLogOverlay 显示）
        //    确保 ObservableCollection 在 UI 线程修改，否则安卓会崩
        if (Dispatcher.UIThread.CheckAccess())
            AddEntry(entry);
        else
            Dispatcher.UIThread.Post(() => AddEntry(entry));
    }

    /// <summary>记录警告日志。</summary>
    public static void Warn(string tag, string message) => Log(tag, message, Level.Warn);

    /// <summary>记录错误日志。</summary>
    public static void Error(string tag, string message) => Log(tag, message, Level.Error);

    /// <summary>
    /// 记录大模型（LLM）调用明细：请求参数、响应摘要、状态、耗时、错误信息。
    /// 单次调用产生两条日志（请求 + 响应），便于在"程序日志"页定位问题。
    /// </summary>
    public static void LogLlmCall(
        string tag,
        string requestSummary,
        string? responseSummary,
        bool success,
        long elapsedMs,
        string? errorMessage = null)
    {
        var status = success ? "OK" : "FAIL";
        var msg = success
            ? $"LLM [{status}] {elapsedMs}ms | Req: {requestSummary} | Resp: {responseSummary ?? "(空)"}"
            : $"LLM [{status}] {elapsedMs}ms | Req: {requestSummary} | Error: {errorMessage ?? "(无)"}";
        Log(tag, msg, success ? Level.Info : Level.Error);
    }

    /// <summary>记录异常（含完整堆栈与 InnerException 链）。</summary>
    public static void Log(string tag, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ex.GetType().Name + ": " + ex.Message);
        var ie = ex.InnerException;
        while (ie is not null)
        {
            sb.AppendLine("  ---> " + ie.GetType().Name + ": " + ie.Message);
            ie = ie.InnerException;
        }
        sb.AppendLine(ex.StackTrace);
        Log(tag, sb.ToString(), Level.Error);
    }

    public static void Clear()
    {
        if (Dispatcher.UIThread.CheckAccess())
            _entries.Clear();
        else
            Dispatcher.UIThread.Post(() => _entries.Clear());
    }

    private static void AddEntry(LogEntry entry)
    {
        try
        {
            if (_entries.Count >= MaxLines)
                _entries.RemoveAt(0);
            _entries.Add(entry);
            Logged?.Invoke(entry);
        }
        catch
        {
            // 浮层本身出问题不影响主流程
        }
    }
}
