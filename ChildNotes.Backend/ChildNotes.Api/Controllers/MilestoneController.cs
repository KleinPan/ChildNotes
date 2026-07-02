using ChildNotes.Core.Services;
using ChildNotes.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers;

/// <summary>
/// 成长时刻（里程碑）HTTP API。对齐小程序端 /api/records/milestone 系列：
/// - GET    /api/records/milestones           查询列表
/// - POST   /api/records/milestone            新增
/// - PUT    /api/records/milestone/{id}       更新
/// - DELETE /api/records/milestone/{id}       删除（软删）
/// </summary>
[Route("api/records")]
public class MilestoneController : AppBaseController
{
    private readonly IMilestoneService _milestone;
    public MilestoneController(IMilestoneService milestone) => _milestone = milestone;

    [HttpGet("milestones")]
    public async Task<List<MilestoneRecordDto>> List([FromQuery] string? babyId, CancellationToken ct)
        => await _milestone.ListAsync(babyId, ct);

    [HttpPost("milestone")]
    public async Task<object> Add([FromBody] MilestoneRecordDto dto, CancellationToken ct)
    {
        var id = await _milestone.AddAsync(dto, ct);
        return new { id };
    }

    [HttpPut("milestone/{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] MilestoneRecordDto dto, CancellationToken ct)
    {
        var ok = await _milestone.UpdateAsync(id, dto, ct);
        return ok ? Ok() : NotFound();
    }

    [HttpDelete("milestone/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var ok = await _milestone.DeleteAsync(id, ct);
        return ok ? Ok() : NotFound();
    }
}
