namespace ChildNotes.Shared.Services;

/// <summary>
/// 北京时间（UTC+8）工具。
/// 后端服务器时区为 UTC，DateTime.Now 返回 UTC 时间，导致：
/// 1. LLM prompt 的 {NowText} 给了错误当前时间，误导 AI 时间推断
/// 2. NormalizeAmbiguousTime 的"取最近过去时刻"基准错误（如用户晚上 8 点输入"6点半"被判为早上 6:30）
/// 前端（手机）本地时区即用户时区，无需使用此类（继续用 DateTime.Now）。
/// </summary>
public static class ChinaTime
{
    private static readonly TimeZoneInfo? Zone = TryGetZone();

    private static TimeZoneInfo? TryGetZone()
    {
        // Linux/ICU 用 IANA ID，Windows 用 TimeZoneInfo 本地 ID
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        return null;
    }

    /// <summary>当前北京时间（Kind=Unspecified，表示墙上时钟时间）。时区加载失败时退化为 UTC+8 计算。</summary>
    public static DateTime Now =>
        Zone is not null
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone)
            : DateTime.UtcNow.AddHours(8);

    /// <summary>北京时间的今天零点。</summary>
    public static DateTime Today => Now.Date;
}
