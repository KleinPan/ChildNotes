using ChildNotes.Core.Constants;

namespace ChildNotes.Core.Dtos;

public class GenerateAiAnalysisRequest
{
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
}

public class AiAnalysisRecordDto
{
    public string Id { get; set; } = string.Empty;
    public string BabyId { get; set; } = string.Empty;
    public string BabyName { get; set; } = string.Empty;
    public string RangeStartDate { get; set; } = string.Empty;
    public string RangeEndDate { get; set; } = string.Empty;
    public string AnalysisText { get; set; } = string.Empty;
    public string DataQualityTip { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

public class UploadResponse
{
    public string Url { get; set; } = string.Empty;
}

/// <summary>AI 智能记笔记解析请求。</summary>
public class AiNoteParseRequest
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>AI 智能记笔记解析响应：包含识别出的记录类型与结构化字段。</summary>
public record AiNoteParseResponse
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
    public bool Saved { get; set; }
    public string? RecordId { get; set; }

    /// <summary>
    /// 解析来源：标识本条记录是由 AI 还是规则兜底产生。
    /// 取值见 <see cref="ChildNotes.Core.Constants.ParseSource"/>。
    /// </summary>
    public string Source { get; set; } = ParseSource.Ai;
}
