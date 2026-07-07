using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface IAiAnalysisService
{
    Task<AiAnalysisRecordDto> GenerateAsync(GenerateAiAnalysisRequest req, string? babyId, CancellationToken ct = default);
    Task<List<AiAnalysisRecordDto>> ListAsync(string? babyId, CancellationToken ct = default);
    Task<AiAnalysisRecordDto?> GetByIdAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// AI 智能记：将自然语言文本解析为结构化育儿记录。
/// 失败时通过规则降级兜底，保证可用性。
/// 注意：本接口仅做解析，不落库；调用方需自行持久化。
/// </summary>
public interface IAiNoteService
{
    /// <summary>解析文本为结构化育儿记录 DTO，不落库。</summary>
    Task<AiNoteParseResponse> ParseAsync(AiNoteParseRequest req, string? babyId, CancellationToken ct = default);
}
