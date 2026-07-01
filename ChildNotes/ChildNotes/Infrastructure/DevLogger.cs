using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;

namespace ChildNotes.Infrastructure;

/// <summary>
/// 开发阶段统一日志服务。所有日志写入环形缓冲区并显示在 DevLogOverlay 浮层上，
/// 便于在 Android 真机无 adb 时直接在应用内排查问题。
/// Release 构建中通过 [Conditional("DEBUG")] 自动裁剪所有调用点，零运行时开销。
/// </summary>
public static class DevLogger
{
    private const int MaxLines = 500;

    public sealed class LogEntry
    {
        public DateTime Time { get; init; }
        public string Tag { get; init; } = "";
        public string Message { get; init; } = "";
        public string Full => $"[{Time:HH:mm:ss.fff}][{Tag}] {Message}";
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
    /// 写入一条日志。[Conditional("DEBUG")] 保证 Release 构建中所有调用点被编译器自动消除，
    /// 零运行时开销。符合项目规则「严禁在 Android Release 路径上保留未受控调试逻辑」。
    /// </summary>
    [Conditional("DEBUG")]
    public static void Log(string tag, string message)
    {
        // 日志开头加平台标识，便于在跨平台日志中区分来源
        var entry = new LogEntry { Time = DateTime.Now, Tag = $"{PlatformTag}/{tag}", Message = message };
        // 始终先输出到 Debug，确保即使 UI 线程出问题也能在 logcat 看到
        Debug.WriteLine(entry.Full);

        // 确保 ObservableCollection 在 UI 线程修改，否则安卓会崩
        if (Dispatcher.UIThread.CheckAccess())
            AddEntry(entry);
        else
            Dispatcher.UIThread.Post(() => AddEntry(entry));
    }

    /// <summary>记录异常（含完整堆栈与 InnerException 链）。同样受 [Conditional("DEBUG")] 裁剪。</summary>
    [Conditional("DEBUG")]
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
        Log(tag, sb.ToString());
    }

    [Conditional("DEBUG")]
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
