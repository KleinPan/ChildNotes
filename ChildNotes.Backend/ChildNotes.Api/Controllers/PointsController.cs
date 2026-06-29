using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers;

[Route("api/points")]
public class PointsController : AppBaseController
{
    private readonly IPointsService _points;
    private readonly ISignInService _signIn;
    private readonly ILotteryService _lottery;
    private readonly IInviteService _invite;

    public PointsController(
        IPointsService points,
        ISignInService signIn,
        ILotteryService lottery,
        IInviteService invite)
    {
        _points = points;
        _signIn = signIn;
        _lottery = lottery;
        _invite = invite;
    }

    [HttpGet("dashboard")]
    public async Task<PointsDashboardResponse> Dashboard(CancellationToken ct)
        => await _points.GetDashboardAsync(ct);

    [HttpGet("sign-in-rule")]
    public async Task<SignInRuleDto> SignInRule(CancellationToken ct)
        => await _signIn.GetSignInRuleAsync(ct);

    [HttpPost("sign-in")]
    public async Task<PointsDashboardResponse> SignIn(CancellationToken ct)
    {
        await _signIn.SignInAsync(ct);
        return await _points.GetDashboardAsync(ct);
    }

    [HttpGet("lottery/active")]
    public async Task<LotterySummaryDto?> ActiveLottery(CancellationToken ct)
        => await _lottery.GetActiveLotteryAsync(ct);

    [HttpPost("lottery/{activityId:long}/join")]
    public async Task<PointsDashboardResponse> JoinLottery(long activityId, CancellationToken ct)
    {
        await _lottery.JoinLotteryAsync(activityId, ct);
        return await _points.GetDashboardAsync(ct);
    }

    [HttpGet("lottery/history")]
    public async Task<List<LotteryHistoryItemDto>> LotteryHistory(CancellationToken ct)
        => await _lottery.GetLotteryHistoryAsync(ct);

    [HttpGet("invite-records")]
    public async Task<List<InviteRecordDto>> InviteRecords(CancellationToken ct)
        => await _invite.GetInviteRecordsAsync(ct);
}
