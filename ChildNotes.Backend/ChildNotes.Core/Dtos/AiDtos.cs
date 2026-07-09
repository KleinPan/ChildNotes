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
