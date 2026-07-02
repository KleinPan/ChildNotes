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
}
