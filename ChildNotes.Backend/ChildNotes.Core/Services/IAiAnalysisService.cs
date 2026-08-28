using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.Core.Services;

public interface IAiAnalysisService
{
    /// <summary>当前 AI 喂养分析消耗的积分数量（由服务器配置动态控制）。</summary>
    int AnalysisCostPoints { get; }

    /// <summary>
    /// 生成 7 天喂养分析。
    /// usePointsForOverage=true 时，本周免费次数用尽后改用积分抵扣放行（扣超限抵扣积分）；
    /// false/未传时超限直接抛 AI_LIMIT_EXCEEDED（历史行为不变）。
    /// </summary>
    Task<AiAnalysisRecordDto> GenerateAsync(GenerateAiAnalysisRequest req, string? babyId, bool usePointsForOverage = false, CancellationToken ct = default);
    Task<List<AiAnalysisRecordDto>> ListAsync(string? babyId, CancellationToken ct = default);
    Task<AiAnalysisRecordDto?> GetByIdAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// AI 智能记：将自然语言文本解析为一条或多条结构化育儿记录。
/// 失败时通过规则降级兜底，保证可用性。
/// 注意：本接口仅做解析，不落库；调用方需自行持久化。
/// </summary>
public interface IAiNoteService
{
    /// <summary>
    /// 解析文本为一条或多条结构化记录 DTO，不落库。
    /// usePointsForOverage=true 时，今日免费次数用尽后改用积分抵扣放行（扣超限抵扣积分）；
    /// false/未传时超限直接抛 AI_NOTE_LIMIT_EXCEEDED（历史行为不变）。
    /// </summary>
    Task<AiNoteParseBatchResponse> ParseAsync(AiNoteParseRequest req, string? babyId, bool usePointsForOverage = false, CancellationToken ct = default);
}
