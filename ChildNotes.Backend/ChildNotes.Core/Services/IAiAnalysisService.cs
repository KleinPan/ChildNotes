using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface IAiAnalysisService
{
    Task<AiAnalysisRecordDto> GenerateAsync(GenerateAiAnalysisRequest req, string? babyId, CancellationToken ct = default);
    Task<List<AiAnalysisRecordDto>> ListAsync(string? babyId, CancellationToken ct = default);
    Task<AiAnalysisRecordDto?> GetByIdAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// AI 智能记：将自然语言文本解析为结构化育儿记录并存储。
/// 失败时通过规则降级兜底，保证可用性。
/// </summary>
public interface IAiNoteService
{
    /// <summary>解析文本并保存为育儿记录。save=true 时直接落库。</summary>
    Task<AiNoteParseResponse> ParseAndSaveAsync(AiNoteParseRequest req, string? babyId, CancellationToken ct = default);
}
