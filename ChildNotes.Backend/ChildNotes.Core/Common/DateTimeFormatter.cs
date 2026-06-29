namespace ChildNotes.Core.Common;

/// <summary>
/// 统一 UTC → 本地时间字符串格式化工具。
/// 消除各服务中重复的 ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") 等模式。
/// </summary>
public static class DateTimeFormatter
{
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    public const string DateFormat = "yyyy-MM-dd";
    public const string DateTimeMinuteFormat = "yyyy-MM-dd HH:mm";

    /// <summary>UTC 时间 → "yyyy-MM-dd HH:mm:ss"</summary>
    public static string FormatDateTime(DateTime? utc)
        => utc.HasValue ? utc.Value.ToLocalTime().ToString(DateTimeFormat) : string.Empty;

    /// <summary>UTC 时间 → "yyyy-MM-dd"</summary>
    public static string FormatDate(DateTime? utc)
        => utc.HasValue ? utc.Value.ToLocalTime().ToString(DateFormat) : string.Empty;

    /// <summary>UTC 时间 → "yyyy-MM-dd HH:mm"</summary>
    public static string FormatDateTimeMinute(DateTime? utc)
        => utc.HasValue ? utc.Value.ToLocalTime().ToString(DateTimeMinuteFormat) : string.Empty;
}
