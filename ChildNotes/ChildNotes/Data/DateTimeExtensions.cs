using System.Globalization;

namespace ChildNotes.Data;

/// <summary>
/// 数据库日期字符串解析工具。
/// 统一处理 "O"（round-trip）格式和 "yyyy-MM-dd" 格式，
/// 避免在 Android/iOS 的 ICU 全球化环境下 DateTime.Parse 行为不一致。
/// </summary>
internal static class DateTimeExtensions
{
    /// <summary>
    /// 解析数据库中存储的日期时间字符串（ISO 8601 round-trip 格式或日期格式）。
    /// 使用 DateTimeStyles.RoundtripKind 保证带 "Z" 后缀的 UTC 时间被正确解析。
    /// </summary>
    public static DateTime ParseDb(string s)
    {
        // 优先用 RoundtripKind 解析 "O" 格式（带时区/ Z 后缀）
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt;
        // 回退到不变文化解析
        return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}
