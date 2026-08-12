using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Dtos;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers;

[Route("api/baby")]
public class BabyController : AppBaseController
{
    private readonly IBabyService _baby;
    public BabyController(IBabyService baby) => _baby = baby;

    [HttpGet("current")]
    public async Task<BabyDto?> GetCurrent([FromQuery] string? babyId, CancellationToken ct)
        => await _baby.GetCurrentBabyAsync(babyId, ct);

    [HttpGet("list")]
    public async Task<List<BabyDto>> List(CancellationToken ct)
        => await _baby.ListBabiesAsync(ct);

    [HttpPost("add")]
    public async Task<BabyDto> Add([FromBody] CreateBabyRequest req, CancellationToken ct)
        => await _baby.CreateBabyAsync(req, ct);

    [HttpPut("update")]
    public async Task<BabyDto> Update([FromBody] UpdateBabyRequest req, CancellationToken ct)
        => await _baby.UpdateBabyAsync(req, ct);

    [HttpGet("family/members")]
    public async Task<List<BabyFamilyDto>> FamilyMembers(CancellationToken ct)
        => await _baby.ListFamilyMembersAsync(ct);

    [HttpPut("family/my-role")]
    public async Task<BabyMemberDto> UpdateMyRole([FromBody] UpdateBabyMemberRoleRequest req, CancellationToken ct)
        => await _baby.UpdateMyFamilyRoleAsync(req, ct);

    [HttpPost("family/join")]
    public async Task<BabyMemberDto> JoinFamily([FromBody] JoinFamilyRequest req, CancellationToken ct)
        => await _baby.JoinFamilyViaInviteAsync(req, ct);

    /// <summary>owner 移除家庭成员（软删除）。</summary>
    [HttpDelete("family/member")]
    public async Task<IActionResult> RemoveMember([FromBody] RemoveMemberRequest req, CancellationToken ct)
    {
        await _baby.RemoveMemberAsync(req, ct);
        return NoContent();
    }

    /// <summary>提交加入家庭申请（pending 状态，等待 owner 审批）。</summary>
    [HttpPost("family/join-request")]
    public async Task<FamilyJoinRequestDto> CreateJoinRequest([FromBody] JoinFamilyRequest req, CancellationToken ct)
        => await _baby.RequestJoinAsync(req, ct);

    /// <summary>owner 列出自己所有宝宝下待审的加入申请。</summary>
    [HttpGet("family/join-requests/pending")]
    public async Task<List<FamilyJoinRequestDto>> ListPendingJoinRequests(CancellationToken ct)
        => await _baby.ListPendingJoinRequestsAsync(ct);

    /// <summary>当前用户作为申请人，列出自己的加入申请（含历史）。</summary>
    [HttpGet("family/join-requests/mine")]
    public async Task<List<FamilyJoinRequestDto>> ListMyJoinRequests(CancellationToken ct)
        => await _baby.ListMyJoinRequestsAsync(ct);

    /// <summary>owner 批准或拒绝加入申请。approve=true 批准，false 拒绝。</summary>
    [HttpPost("family/join-request/process")]
    public async Task<FamilyJoinRequestDto> ProcessJoinRequest([FromBody] ProcessJoinRequestDto req, CancellationToken ct)
        => await _baby.ProcessJoinRequestAsync(req, ct);
}
