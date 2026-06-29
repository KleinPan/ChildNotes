using ChildNotes.Core.Config;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Auth;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

public class SignInService : ISignInService
{
    private readonly ChildNotesDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly PointsWalletService _wallet;

    public SignInService(ChildNotesDbContext db, ICurrentUserService current, PointsWalletService wallet)
    {
        _db = db;
        _current = current;
        _wallet = wallet;
    }

    public Task<SignInRuleDto> GetSignInRuleAsync(CancellationToken ct = default)
        => Task.FromResult(BuildSignInRule());

    public async Task<SignInSummaryDto> GetSignInSummaryAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        return await BuildSignInSummaryAsync(uid, ct);
    }

    public async Task SignInAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var today = DateTime.Today;

        var alreadySigned = await _db.SignInRecords.AnyAsync(r => r.UserId == uid && r.SignDate == today, ct);
        if (alreadySigned) throw new BusinessException("今日已签到", 400, "ALREADY_SIGNED_IN");

        var last = await _db.SignInRecords
            .Where(r => r.UserId == uid)
            .OrderByDescending(r => r.SignDate).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var continuousDays = last is not null && last.SignDate == today.AddDays(-1)
            ? last.ContinuousDays + 1 : 1;
        var cycleDay = ((Math.Max(1, continuousDays) - 1) % PointsConstants.SignInCycleDays) + 1;
        var reward = PointsConstants.CalculateSignInReward(cycleDay);

        var now = DateTime.UtcNow;
        _db.SignInRecords.Add(new SignInRecord
        {
            UserId = uid,
            SignDate = today,
            SignTime = now,
            ContinuousDays = continuousDays,
            CycleDay = cycleDay,
            RewardPoints = reward,
            CreatedAt = now,
        });
        await _wallet.ChangeAsync(uid, reward, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<SignInSummaryDto> BuildSignInSummaryAsync(long userId, CancellationToken ct)
    {
        var today = DateTime.Today;
        var todayRec = await _db.SignInRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.SignDate == today, ct);
        var last = todayRec ?? await _db.SignInRecords
            .Where(r => r.UserId == userId && r.SignDate < today)
            .OrderByDescending(r => r.SignDate).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var continuousDays = last?.ContinuousDays ?? 0;
        var cycleDay = continuousDays == 0 ? 1
            : ((continuousDays - 1) % PointsConstants.SignInCycleDays) + 1;
        var todayReward = todayRec?.RewardPoints ?? PointsConstants.CalculateSignInReward(cycleDay);
        var nextCycleDay = (cycleDay % PointsConstants.SignInCycleDays) + 1;
        var nextReward = PointsConstants.CalculateSignInReward(nextCycleDay);

        var timeline = new List<SignInTimelineItemDto>();
        for (int i = -3; i <= 3; i++)
        {
            var date = today.AddDays(i);
            var signed = await _db.SignInRecords.AnyAsync(r => r.UserId == userId && r.SignDate == date, ct);
            var cd = signed && last is not null
                ? ((Math.Max(1, last.ContinuousDays) - 1) % PointsConstants.SignInCycleDays) + 1 : cycleDay;
            var reward = signed ? PointsConstants.CalculateSignInReward(cd) : (i > 0 ? nextReward : 0);
            timeline.Add(new SignInTimelineItemDto
            {
                Date = date.ToString("yyyy-MM-dd"),
                Label = i == 0 ? "今天" : i == -1 ? "昨天" : i == 1 ? "明天" : date.ToString("MM-dd"),
                Today = i == 0,
                Signed = signed,
                RewardPoints = reward,
                DisplayReward = signed ? $"+{reward}" : "-",
            });
        }

        return new SignInSummaryDto
        {
            TodaySigned = todayRec is not null,
            ContinuousDays = continuousDays,
            TodayRewardPoints = todayReward,
            NextRewardPoints = nextReward,
            Rule = BuildSignInRule(),
            Timeline = timeline,
        };
    }

    private static SignInRuleDto BuildSignInRule()
    {
        var rule = new SignInRuleDto();
        rule.Rewards = new()
        {
            new() { Day = 1, Points = 1, Label = "基础奖励" },
            new() { Day = 3, Points = 3, Label = "连续第3天" },
            new() { Day = 5, Points = 5, Label = "连续第5天" },
            new() { Day = 7, Points = 7, Label = "连续第7天" },
            new() { Day = 30, Points = 30, Label = "连续第30天" },
        };
        return rule;
    }
}
