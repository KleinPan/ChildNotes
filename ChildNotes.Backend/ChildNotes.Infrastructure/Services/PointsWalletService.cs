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

    public async Task<UserPoints> EnsureAsync(string userId, CancellationToken ct)
    {
        var p = await _db.UserPoints.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (p is not null) return p;
        var now = DateTime.UtcNow;
        p = new UserPoints { Id = Guid.NewGuid().ToString("N"), UserId = userId, Points = 0, TotalEarned = 0, TotalSpent = 0, CreatedAt = now, UpdatedAt = now };
        _db.UserPoints.Add(p);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { p = await _db.UserPoints.FirstAsync(x => x.UserId == userId, ct); }
        return p;
    }

    /// <summary>
    /// 原子地增减积分。扣减时通过数据库层 WHERE 条件防止并发超扣，
    /// 不依赖行级锁或 RowVersion 字段。
    /// Npgsql 等支持 ExecuteUpdate 的 provider 走原子 SQL；InMemory 降级为 EF 跟踪模式（仅测试用）。
    /// </summary>
    public async Task ChangeAsync(string userId, int delta, CancellationToken ct)
    {
        if (delta == 0) return;
        var p = await EnsureAsync(userId, ct);

        // InMemory 不支持 ExecuteUpdateAsync，降级到 EF 跟踪模式（测试环境无并发）。
        if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            if (delta < 0 && p.Points < -delta) throw new BusinessException("积分不足", 400, "INSUFFICIENT_POINTS");
            p.Points += delta;
            if (delta > 0) p.TotalEarned += delta;
            else p.TotalSpent += -delta;
            p.UpdatedAt = DateTime.UtcNow;
            return;
        }

        var now = DateTime.UtcNow;
        // 扣减时 WHERE 加 points + delta >= 0；增加时不限制。
        // ExecuteUpdateAsync 在数据库层原子执行，避免 EF 跟踪实体后被并发覆盖。
        var rows = await _db.UserPoints
            .Where(x => x.UserId == userId && (delta > 0 || x.Points + delta >= 0))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Points, x => x.Points + delta)
                .SetProperty(x => x.TotalEarned, x => delta > 0 ? x.TotalEarned + delta : x.TotalEarned)
                .SetProperty(x => x.TotalSpent, x => delta < 0 ? x.TotalSpent + (-delta) : x.TotalSpent)
                .SetProperty(x => x.UpdatedAt, now), ct);

        if (rows == 0)
        {
            // 仅在扣减场景下 WHERE 不命中（积分不足或并发竞争）。
            throw new BusinessException("积分不足", 400, "INSUFFICIENT_POINTS");
        }
    }
}
