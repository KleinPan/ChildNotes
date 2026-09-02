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
    /// 截断 DateTime 到微秒精度（1 μs = 10 ticks，即去掉第 7 位小数的 100ns 余数）。
    /// PostgreSQL timestamp(6) 只存微秒：客户端若保留 .NET 的 100ns 精度，
    /// Push 上送的时间戳经服务端截断后与存储值相等，LWW 的严格大于判断
    /// （item.UpdatedAt &gt; existing.UpdatedAt）恒为 false，导致记录被永久跳过、反复重推。
    /// </summary>
    public static DateTime TruncateToMicroseconds(this DateTime dt)
    {
        var ticks = dt.Ticks;
        return new DateTime(ticks - ticks % 10, dt.Kind);
    }

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
