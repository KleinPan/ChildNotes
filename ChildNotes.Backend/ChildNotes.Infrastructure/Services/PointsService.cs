using ChildNotes.Core.Config;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Auth;

namespace ChildNotes.Infrastructure.Services;

public class PointsService : IPointsService
{
    private readonly ICurrentUserService _current;
    private readonly IReferrerCodeUtil _referrer;
    private readonly PointsWalletService _wallet;
    private readonly ISignInService _signIn;
    private readonly ILotteryService _lottery;
    private readonly IInviteService _invite;

    public PointsService(
        ICurrentUserService current,
        IReferrerCodeUtil referrer,
        PointsWalletService wallet,
        ISignInService signIn,
        ILotteryService lottery,
        IInviteService invite)
    {
        _current = current;
        _referrer = referrer;
        _wallet = wallet;
        _signIn = signIn;
        _lottery = lottery;
        _invite = invite;
    }

    public async Task<PointsDashboardResponse> GetDashboardAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var points = await _wallet.EnsureAsync(uid, ct);
        var resp = new PointsDashboardResponse
        {
            Points = points.Points,
            TotalEarned = points.TotalEarned,
            TotalSpent = points.TotalSpent,
            ShareReferrerId = _referrer.Encode(uid),
            SignIn = await _signIn.GetSignInSummaryAsync(ct),
            Lottery = await _lottery.GetActiveLotteryAsync(ct),
            Tasks = GetTaskTemplates(),
        };
        resp.InviteRecords = await _invite.GetInviteRecordsAsync(ct);
        return resp;
    }

    private static List<TaskTemplateDto> GetTaskTemplates() => new()
    {
        new TaskTemplateDto
        {
            TaskKey = "invite_mom",
            Title = "邀请宝妈使用",
            Description = "赚取100积分",
            Points = PointsConstants.InviteRewardPoints,
            Action = "share",
        },
    };
}
