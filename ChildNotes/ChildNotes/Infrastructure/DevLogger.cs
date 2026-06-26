using System.Collections.ObjectModel;
using System.Text;

namespace ChildNotes.Infrastructure;

/// <summary>
/// 开发阶段统一日志服务。所有日志写入环形缓冲区并显示在 DevLogOverlay 浮层上，
/// 便于在 Android 真机无 adb 时直接在应用内排查问题。
/// </summary>
public static class DevLogger
{
    private const int MaxLines = 300;

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

    public static void Log(string tag, string message)
    {
        var entry = new LogEntry { Time = DateTime.Now, Tag = tag, Message = message };
        System.Diagnostics.Debug.WriteLine(entry.Full);

        if (_entries.Count >= MaxLines)
            _entries.RemoveAt(0);
        _entries.Add(entry);
        Logged?.Invoke(entry);
    }

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

    public static void Clear()
    {
        _entries.Clear();
    }
}
