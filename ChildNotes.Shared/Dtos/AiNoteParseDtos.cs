using ChildNotes.Shared.Constants;

namespace ChildNotes.Shared.Dtos;

/// <summary>AI 智能记笔记解析请求。</summary>
public class AiNoteParseRequest
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// AI 智能记解析结果中的单条记录。
/// 与原后端 AiNoteParseResponse 字段一致，但删除了从未使用的 Saved/RecordId 字段
/// （后端只解析不落库，这两个字段一直是死字段）。
/// </summary>
public record AiNoteParseItem
{
    public string RecordType { get; set; } = string.Empty;
    public string? RecordSubType { get; set; }
    public string? Time { get; set; }
    public int? Amount { get; set; }
    public int? Duration { get; set; }
    public int? LeftDuration { get; set; }
    public int? RightDuration { get; set; }
    public decimal? Temperature { get; set; }
    public decimal? Height { get; set; }
    public decimal? Weight { get; set; }
    public string? DiaperType { get; set; }
    public string? Note { get; set; }
    public string? Summary { get; set; }
    public double Confidence { get; set; }

    /// <summary>
    /// 解析来源：标识本条记录是由 AI 还是规则兜底产生。
    /// 取值见 <see cref="ParseSource"/>。
    /// </summary>
    public string Source { get; set; } = ParseSource.Ai;
}

/// <summary>
/// AI 智能记批量解析响应。
/// 支持一句话解析出多条记录（如"睡了一觉，喝了奶，换了尿布"）。
/// </summary>
public class AiNoteParseBatchResponse
{
    /// <summary>解析出的记录列表。空列表表示解析失败（调用方应提示用户）。</summary>
    public List<AiNoteParseItem> Items { get; set; } = new();
}
