namespace ChildNotes.Shared.Constants;

/// <summary>
/// AI 智能记解析来源常量。
/// 用于 <see cref="ChildNotes.Shared.Dtos.AiNoteParseItem.Source"/> 字段，
/// 标识一条解析结果是由 AI 模型产生还是规则兜底产生。
/// </summary>
public static class ParseSource
{
    /// <summary>由 DeepSeek 等 LLM 解析产生。</summary>
    public const string Ai = "ai";

    /// <summary>AI 不可用时由本地正则规则降级产生。</summary>
    public const string Rule = "rule";
}
