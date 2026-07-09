using System.Text.RegularExpressions;

namespace ChildNotes.Shared.Services;

/// <summary>
/// 自然语言记录切分器：将复合语句切分为多条子句，供规则降级逐条解析。
/// 前后端共用，避免规则漂移。
///
/// 切分策略（仅用强分隔符 + 显式连词，避免误伤）：
/// - 强分隔符：中文/英文逗号、分号、句号、感叹号、换行
/// - 显式连词：然后/接着/之后/又/还/随后/后来/并且/并
///
/// 注意：不用"到"等时间范围介词切分（如"11点半睡到12点40"是一个睡眠事件）。
/// </summary>
public static class NoteSplitter
{
    // 强分隔符：中英文逗号、分号、句号、感叹号、换行
    private static readonly Regex StrongSeparator = new(
        @"[,，;；。！!\n]+",
        RegexOptions.Compiled);

    // 显式连词：切分时保留连词到后一段
    private static readonly Regex ConjunctionPattern = new(
        @"(?:然后|接着|之后|随后|后来|又|还|并且|并)(?=[^，,。；;])",
        RegexOptions.Compiled);

    /// <summary>
    /// 将复合语句切分为多条子句。
    /// 返回去除空段后的子句列表。若无法切分则返回只含原句的单元素列表。
    /// </summary>
    public static List<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        // 1. 先按显式连词切分（在连词前插入分隔符）
        var withConjSplits = ConjunctionPattern.Replace(text, "\x01");
        // 2. 再按强分隔符切分
        var parts = StrongSeparator.Split(withConjSplits);

        var result = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            var s = p.Replace("\x01", "").Trim();
            if (!string.IsNullOrWhiteSpace(s))
                result.Add(s);
        }
        return result;
    }
}
