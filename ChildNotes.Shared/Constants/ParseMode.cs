namespace ChildNotes.Shared.Constants;

/// <summary>
/// AI 智能记解析模式常量。
/// 用于 <see cref="ChildNotes.Shared.Dtos.AiNoteParseRequest.ParseMode"/> 字段，
/// 决定后端是否跳过规则快速路径直接调用 AI。
/// </summary>
public static class ParseMode
{
    /// <summary>
    /// 快速模式（默认）：规则置信度足够高且非复杂文本时直接返回，不调 AI。
    /// 复杂文本（长文本/多逗号）仍自动转 AI。AI 失败时规则兜底。
    /// </summary>
    public const string Fast = "fast";

    /// <summary>
    /// 精准模式：跳过规则快速路径，每次都调 AI。
    /// AI 失败时仍走规则兜底。会更快消耗每日 AI 次数配额。
    /// </summary>
    public const string Precise = "precise";
}
