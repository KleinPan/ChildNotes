using ChildNotes.Core.Common;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

public class AdminDashboardService : IAdminDashboardService
{
    private readonly ChildNotesDbContext _db;
    public AdminDashboardService(ChildNotesDbContext db) => _db = db;

    public async Task<AdminOverviewResponse> GetOverviewAsync(CancellationToken ct = default)
    {
        var todayStart = DateTime.Today;
        var todayStartUtc = todayStart.ToUniversalTime();

        return new AdminOverviewResponse
        {
            TotalUsers = await _db.AppUsers.LongCountAsync(ct),
            TodayUsers = await _db.AppUsers.LongCountAsync(u => u.CreatedAt >= todayStartUtc, ct),
            TotalBabies = await _db.Babies.LongCountAsync(ct),
            TodayBabies = await _db.Babies.LongCountAsync(b => b.CreatedAt >= todayStartUtc, ct),
            DraftLotteryCount = await _db.AdminLotteryActivities.LongCountAsync(a => a.Status == StatusConstants.AdminLottery.Draft, ct),
            PublishedLotteryCount = await _db.AdminLotteryActivities.LongCountAsync(a => a.Status == StatusConstants.AdminLottery.Published, ct),
        };
    }

    public async Task<AdminPageResponse<AdminUserListItemDto>> ListUsersAsync(
        int page, int pageSize, string? keyword, CancellationToken ct = default)
    {
        (page, pageSize, var skip) = PagingHelper.Normalize(page, pageSize);

        var q = _db.AppUsers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
            q = q.Where(u => u.NickName.Contains(keyword));

        var total = await q.LongCountAsync(ct);
        var users = await q.OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id)
            .Skip(skip).Take(pageSize).ToListAsync(ct);
        var userIds = users.Select(u => u.Id).ToList();
        var babyCounts = await _db.BabyMembers
            .Where(m => userIds.Contains(m.UserId) && m.Status == StatusConstants.BabyMember.Active)
            .GroupBy(m => m.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        return new AdminPageResponse<AdminUserListItemDto>
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Records = users.Select(u => new AdminUserListItemDto
            {
                Id = u.Id,
                NickName = u.NickName,
                AvatarUrl = u.AvatarUrl,
                Gender = u.Gender,
                ReferrerUserId = u.ReferrerUserId,
                BabyCount = babyCounts.GetValueOrDefault(u.Id, 0),
                CreatedAt = u.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            }).ToList(),
        };
    }

    public async Task<AdminPageResponse<AdminBabyListItemDto>> ListBabiesAsync(
        int page, int pageSize, string? keyword, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 20 : Math.Min(100, pageSize);
        var skip = (page - 1) * pageSize;

        var q = _db.Babies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
            q = q.Where(b => b.Name.Contains(keyword));

        var total = await q.LongCountAsync(ct);
        var babies = await q.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .Skip(skip).Take(pageSize).ToListAsync(ct);
        var babyIds = babies.Select(b => b.Id).ToList();
        var ownerIds = babies.Select(b => b.UserId).Distinct().ToList();

        var members = await _db.BabyMembers
            .Where(m => babyIds.Contains(m.BabyId) && m.Status == StatusConstants.BabyMember.Active)
            .GroupBy(m => m.BabyId)
            .Select(g => new { BabyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BabyId, x => x.Count, ct);
        var owners = await _db.AppUsers
            .Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return new AdminPageResponse<AdminBabyListItemDto>
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Records = babies.Select(b => new AdminBabyListItemDto
            {
                Id = b.Id,
                Name = b.Name,
                Avatar = b.Avatar ?? "",
                Gender = b.Gender,
                BirthDate = b.BirthDate,
                AgeDays = BabyUtil.GetAgeInDays(b.BirthDate),
                OwnerUserId = b.UserId,
                OwnerNickName = owners.GetValueOrDefault(b.UserId)?.NickName ?? "",
                MemberCount = members.GetValueOrDefault(b.Id, 0),
                CreatedAt = DateTimeFormatter.FormatDateTime(b.CreatedAt),
            }).ToList(),
        };
    }
}
