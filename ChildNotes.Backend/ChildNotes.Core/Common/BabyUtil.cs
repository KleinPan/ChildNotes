namespace ChildNotes.Core.Common;

/// <summary>
/// 宝宝相关计算工具，消除年龄计算等重复逻辑。
/// </summary>
public static class BabyUtil
{
    /// <summary>根据出生日期计算宝宝年龄（天），无出生日期返回 0。</summary>
    public static int GetAgeInDays(DateTime? birthDate)
        => birthDate.HasValue ? (int)(DateTime.Today - birthDate.Value).TotalDays : 0;
}
