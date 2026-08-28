using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers;

[ApiController]
[Route("api/smart-analysis")]
public class AiAnalysisController : AppBaseController
{
    private readonly IAiAnalysisService _ai;
    private readonly IAiNoteService _aiNote;
    public AiAnalysisController(IAiAnalysisService ai, IAiNoteService aiNote)
    {
        _ai = ai;
        _aiNote = aiNote;
    }

    /// <summary>
    /// 生成 7 天喂养分析。
    /// usePointsForOverage：本周免费次数用尽后是否改用积分抵扣放行（默认 false，超限报 AI_LIMIT_EXCEEDED）。
    /// 前端超限弹窗中用户选择"积分抵扣继续"后，携带 true 重新请求本接口。
    /// </summary>
    [HttpPost("generate")]
    public async Task<AiAnalysisRecordDto> Generate([FromBody] GenerateAiAnalysisRequest? req,
        [FromQuery] bool usePointsForOverage, CancellationToken ct)
        => await _ai.GenerateAsync(req ?? new(), ResolveBabyIdFromRequest(), usePointsForOverage, ct);

    [HttpGet("list")]
    public async Task<List<AiAnalysisRecordDto>> List(CancellationToken ct)
        => await _ai.ListAsync(ResolveBabyIdFromRequest(), ct);

    [HttpGet("{id}")]
    public async Task<AiAnalysisRecordDto?> GetById(string id, CancellationToken ct)
        => await _ai.GetByIdAsync(id, ct);

    /// <summary>
    /// 查询当前 AI 喂养分析所需消耗的积分数量。
    /// 前端在生成分析前调用此接口实时获取积分成本。
    /// </summary>
    [HttpGet("cost")]
    public ActionResult<AiAnalysisCostResponse> GetCost()
        => Ok(new AiAnalysisCostResponse
        {
            CostPoints = _ai.AnalysisCostPoints,
            // 超限积分抵扣单价（不含 CostPoints）：本周免费次数用尽后，选择抵扣需额外消耗的积分
            OveragePointsCost = MembershipConstants.AiAnalysisOveragePointsCost,
        });

    /// <summary>
    /// AI 智能记：将自然语言文本解析为一条或多条结构化育儿记录 DTO。
    /// 仅做解析，不落库；调用方需自行持久化。
    /// 优先调用 AI，失败时降级到规则解析。
    /// usePointsForOverage：今日免费次数用尽后是否改用积分抵扣放行（默认 false，超限报 AI_NOTE_LIMIT_EXCEEDED）。
    /// 前端超限弹窗中用户选择"积分抵扣继续"后，携带 true 重新请求本接口。
    /// </summary>
    [HttpPost("parse-note")]
    public async Task<AiNoteParseBatchResponse> ParseNote([FromBody] AiNoteParseRequest req,
        [FromQuery] bool usePointsForOverage, CancellationToken ct)
        => await _aiNote.ParseAsync(req, ResolveBabyIdFromRequest(), usePointsForOverage, ct);
}

/// <summary>AI 喂养分析积分消耗响应。</summary>
public class AiAnalysisCostResponse
{
    /// <summary>单次分析消耗的积分数量。</summary>
    public int CostPoints { get; set; }

    /// <summary>免费次数用尽后的超限积分抵扣单价（积分/次，不含 CostPoints）。见 MembershipConstants.AiAnalysisOveragePointsCost。</summary>
    public int OveragePointsCost { get; set; }
}
