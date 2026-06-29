using ChildNotes.Core.Common;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Auth;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

public class LotteryService : ILotteryService
{
    private readonly ChildNotesDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly PointsWalletService _wallet;

    public LotteryService(ChildNotesDbContext db, ICurrentUserService current, PointsWalletService wallet)
    {
        _db = db;
        _current = current;
        _wallet = wallet;
    }

    public async Task<LotterySummaryDto?> GetActiveLotteryAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        return await BuildActiveLotteryAsync(uid, ct);
    }

    public async Task JoinLotteryAsync(long activityId, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var activity = await _db.LotteryActivities.FirstOrDefaultAsync(a => a.Id == activityId, ct)
            ?? throw new BusinessException("抽奖活动不存在", 400, "LOTTERY_NOT_FOUND");
        if (activity.Status != StatusConstants.Lottery.Active) throw new BusinessException("抽奖活动不存在或已结束", 400, "LOTTERY_NOT_ACTIVE");

        var joined = await _db.LotteryParticipations
            .AnyAsync(p => p.ActivityId == activityId && p.UserId == uid, ct);
        if (joined) throw new BusinessException("您已参与本期抽奖", 400, "ALREADY_JOINED_LOTTERY");

        var cost = activity.CostPoints <= 0 ? 30 : activity.CostPoints;
        await _wallet.ChangeAsync(uid, -cost, ct);

        var now = DateTime.UtcNow;
        _db.LotteryParticipations.Add(new LotteryParticipation
        {
            ActivityId = activityId,
            UserId = uid,
            CostPoints = cost,
            Status = StatusConstants.LotteryParticipation.Joined,
            CreatedAt = now,
            UpdatedAt = now,
        });
        activity.ParticipantCount++;
        activity.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<LotteryHistoryItemDto>> GetLotteryHistoryAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var list = await (from p in _db.LotteryParticipations
                          join a in _db.LotteryActivities on p.ActivityId equals a.Id
                          where p.UserId == uid
                          orderby p.CreatedAt descending
                          select new { p, a }).ToListAsync(ct);
        var activityIds = list.Select(x => x.a.Id).Distinct().ToList();
        var prizeMap = await _db.LotteryPrizes
            .Where(pr => activityIds.Contains(pr.ActivityId))
            .GroupBy(pr => pr.ActivityId)
            .ToDictionaryAsync(g => g.Key, g => g.First().PrizeName, ct);
        return list.Select(x => new LotteryHistoryItemDto
        {
            ActivityId = x.a.Id,
            Title = x.a.Title,
            PrizeName = prizeMap.GetValueOrDefault(x.a.Id, ""),
            CostPoints = x.p.CostPoints,
            Status = x.p.Status,
            JoinedAt = DateTimeFormatter.FormatDateTime(x.p.CreatedAt),
            DrawTime = DateTimeFormatter.FormatDateTime(x.a.DrawTime),
        }).ToList();
    }

    private async Task<LotterySummaryDto?> BuildActiveLotteryAsync(long userId, CancellationToken ct)
    {
        var activity = await _db.LotteryActivities
            .Where(a => a.Status == StatusConstants.Lottery.Active && a.DrawTime > DateTime.UtcNow)
            .OrderBy(a => a.DrawTime)
            .FirstOrDefaultAsync(ct);
        if (activity is null) return null;

        var prize = await _db.LotteryPrizes
            .Where(pr => pr.ActivityId == activity.Id)
            .OrderBy(pr => pr.Id).FirstOrDefaultAsync(ct);

        var joined = await _db.LotteryParticipations
            .AnyAsync(p => p.ActivityId == activity.Id && p.UserId == userId, ct);

        var avatars = await (from p in _db.LotteryParticipations
                             join u in _db.AppUsers on p.UserId equals u.Id
                             where p.ActivityId == activity.Id
                             orderby p.CreatedAt descending
                             select u.AvatarUrl).Take(12).ToListAsync(ct);

        return new LotterySummaryDto
        {
            ActivityId = activity.Id,
            Title = activity.Title,
            Description = activity.Description,
            DrawTime = activity.DrawTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            CostPoints = activity.CostPoints <= 0 ? 30 : activity.CostPoints,
            ParticipantCount = activity.ParticipantCount,
            WinnerCount = activity.WinnerCount <= 0 ? 1 : activity.WinnerCount,
            AlreadyJoined = joined,
            PrizeName = prize?.PrizeName ?? "",
            PrizeIntro = prize?.PrizeIntro ?? "",
            PrizeImage = prize?.PrizeImage ?? "",
            ParticipantAvatars = avatars,
        };
    }
}
