using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// 积分钱包操作：统一积分增减与余额查询，供 PointsService/SignInService/LotteryService/InviteService 复用。
/// </summary>
public class PointsWalletService
{
    private readonly ChildNotesDbContext _db;
    public PointsWalletService(ChildNotesDbContext db) => _db = db;

    public async Task<UserPoints> EnsureAsync(long userId, CancellationToken ct)
    {
        var p = await _db.UserPoints.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (p is not null) return p;
        var now = DateTime.UtcNow;
        p = new UserPoints { UserId = userId, Points = 0, TotalEarned = 0, TotalSpent = 0, CreatedAt = now, UpdatedAt = now };
        _db.UserPoints.Add(p);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { p = await _db.UserPoints.FirstAsync(x => x.UserId == userId, ct); }
        return p;
    }

    public async Task ChangeAsync(long userId, int delta, CancellationToken ct)
    {
        var p = await EnsureAsync(userId, ct);
        if (delta < 0 && p.Points < -delta) throw new BusinessException("积分不足", 400, "INSUFFICIENT_POINTS");
        p.Points += delta;
        if (delta > 0) p.TotalEarned += delta;
        else p.TotalSpent += -delta;
        p.UpdatedAt = DateTime.UtcNow;
    }
}
