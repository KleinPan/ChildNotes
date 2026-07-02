using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
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

    [HttpPost("generate")]
    public async Task<AiAnalysisRecordDto> Generate([FromBody] GenerateAiAnalysisRequest? req, CancellationToken ct)
        => await _ai.GenerateAsync(req ?? new(), ResolveBabyIdFromRequest(), ct);

    [HttpGet("list")]
    public async Task<List<AiAnalysisRecordDto>> List(CancellationToken ct)
        => await _ai.ListAsync(ResolveBabyIdFromRequest(), ct);

    [HttpGet("{id}")]
    public async Task<AiAnalysisRecordDto?> GetById(string id, CancellationToken ct)
        => await _ai.GetByIdAsync(id, ct);

    /// <summary>
    /// AI 智能记：将自然语言文本解析为结构化育儿记录并保存。
    /// 优先调用 AI，失败时降级到规则解析。
    /// </summary>
    [HttpPost("parse-note")]
    public async Task<AiNoteParseResponse> ParseNote([FromBody] AiNoteParseRequest req, CancellationToken ct)
        => await _aiNote.ParseAndSaveAsync(req, ResolveBabyIdFromRequest(), ct);
}
