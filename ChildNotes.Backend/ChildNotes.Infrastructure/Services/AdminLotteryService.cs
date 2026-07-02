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

public class AdminLotteryService : IAdminLotteryService
{
    private static readonly HashSet<string> ValidStatuses = new()
    {
        StatusConstants.AdminLottery.Draft,
        StatusConstants.AdminLottery.Published,
        StatusConstants.AdminLottery.Closed,
    };

    private readonly ChildNotesDbContext _db;
    private readonly ICurrentAdminService _current;

    public AdminLotteryService(ChildNotesDbContext db, ICurrentAdminService current)
    {
        _db = db;
        _current = current;
    }

    public async Task<AdminPageResponse<AdminLotteryDto>> ListLotteriesAsync(
        int page, int pageSize, string? status, CancellationToken ct = default)
    {
        (page, pageSize, var skip) = PagingHelper.Normalize(page, pageSize);

        var q = _db.AdminLotteryActivities.AsNoTracking();
        if (!string.IsNullOrEmpty(status) && status != "all")
            q = q.Where(a => a.Status == status);

        var total = await q.LongCountAsync(ct);
        var list = await q.OrderByDescending(a => a.UpdatedAt)
            .Skip(skip).Take(pageSize).ToListAsync(ct);
        var ids = list.Select(a => a.Id).ToList();
        var prizes = await _db.AdminLotteryPrizes
            .Where(p => ids.Contains(p.ActivityId))
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        return new AdminPageResponse<AdminLotteryDto>
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Records = list.Select(a => ToDto(a, prizes.Where(p => p.ActivityId == a.Id).ToList())).ToList(),
        };
    }

    public async Task<AdminLotteryDto?> GetLotteryAsync(string id, CancellationToken ct = default)
    {
        var a = await _db.AdminLotteryActivities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return null;
        var prizes = await _db.AdminLotteryPrizes.AsNoTracking()
            .Where(p => p.ActivityId == id).OrderBy(p => p.Id).ToListAsync(ct);
        return ToDto(a, prizes);
    }

    public async Task<AdminLotteryDto> CreateLotteryAsync(AdminLotteryRequest req, CancellationToken ct = default)
    {
        Validate(req);
        var adminId = _current.RequireAdminId();
        var now = DateTime.UtcNow;
        var status = NormalizeStatus(req.Status, StatusConstants.AdminLottery.Draft);

        var activity = new AdminLotteryActivity
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = req.Title.Trim(),
            Description = req.Description ?? "",
            CoverImage = req.CoverImage ?? "",
            StartTime = req.StartTime,
            DrawTime = req.DrawTime,
            CostPoints = Math.Max(0, req.CostPoints),
            WinnerCount = req.WinnerCount <= 0 ? 1 : req.WinnerCount,
            Status = status,
            PublishTime = status == StatusConstants.AdminLottery.Published ? now : null,
            CreatedBy = adminId,
            UpdatedBy = adminId,
        };
        _db.AdminLotteryActivities.Add(activity);
        await _db.SaveChangesAsync(ct);
        // activity 与 prizes 必须原子：失败会留下无奖品的活动。
        await _db.ExecuteInTransactionAsync(async () =>
        {
            await ReplacePrizesAsync(activity.Id, req.Prizes, now, ct);
        }, ct);
        return (await GetLotteryAsync(activity.Id, ct))!;
    }

    public async Task<AdminLotteryDto> UpdateLotteryAsync(string id, AdminLotteryRequest req, CancellationToken ct = default)
    {
        Validate(req);
        var activity = await _db.AdminLotteryActivities.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("抽奖活动不存在");
        var adminId = _current.RequireAdminId();
        var now = DateTime.UtcNow;

        activity.Title = req.Title.Trim();
        activity.Description = req.Description ?? "";
        activity.CoverImage = req.CoverImage ?? "";
        activity.StartTime = req.StartTime;
        activity.DrawTime = req.DrawTime;
        activity.CostPoints = Math.Max(0, req.CostPoints);
        activity.WinnerCount = req.WinnerCount <= 0 ? 1 : req.WinnerCount;
        var status = NormalizeStatus(req.Status, activity.Status);
        activity.Status = status;
        if (status == StatusConstants.AdminLottery.Published && activity.PublishTime is null) activity.PublishTime = now;
        activity.UpdatedBy = adminId;
        await _db.SaveChangesAsync(ct);
        // activity 更新与 prizes 替换必须原子：失败会留下旧 prizes 与新 activity 不一致。
        await _db.ExecuteInTransactionAsync(async () =>
        {
            await ReplacePrizesAsync(activity.Id, req.Prizes, now, ct);
        }, ct);
        return (await GetLotteryAsync(activity.Id, ct))!;
    }

    public async Task<AdminLotteryDto> PublishLotteryAsync(string id, CancellationToken ct = default)
    {
        var activity = await _db.AdminLotteryActivities.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("抽奖活动不存在");
        var prizeCount = await _db.AdminLotteryPrizes.CountAsync(p => p.ActivityId == id, ct);
        if (prizeCount <= 0) throw new BusinessException("Please add at least one prize before publishing");

        var adminId = _current.RequireAdminId();
        activity.Status = StatusConstants.AdminLottery.Published;
        if (activity.PublishTime is null) activity.PublishTime = DateTime.UtcNow;
        activity.UpdatedBy = adminId;
        await _db.SaveChangesAsync(ct);
        return (await GetLotteryAsync(activity.Id, ct))!;
    }

    public async Task<AdminLotteryDto> CloseLotteryAsync(string id, CancellationToken ct = default)
    {
        var activity = await _db.AdminLotteryActivities.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("抽奖活动不存在");
        var adminId = _current.RequireAdminId();
        activity.Status = StatusConstants.AdminLottery.Closed;
        activity.UpdatedBy = adminId;
        await _db.SaveChangesAsync(ct);
        return (await GetLotteryAsync(activity.Id, ct))!;
    }

    private static void Validate(AdminLotteryRequest req)
    {
        if (req is null) throw new BusinessException("请求不能为空");
        if (string.IsNullOrWhiteSpace(req.Title)) throw new BusinessException("标题不能为空");
        if (req.StartTime == default) throw new BusinessException("开始时间不能为空");
        if (req.DrawTime == default) throw new BusinessException("开奖时间不能为空");
        if (req.DrawTime < req.StartTime) throw new BusinessException("开奖时间必须晚于开始时间");
    }

    private static string NormalizeStatus(string? status, string fallback)
        => !string.IsNullOrEmpty(status) && ValidStatuses.Contains(status) ? status : fallback;

    private async Task ReplacePrizesAsync(string activityId, List<AdminLotteryPrizeDto> prizes, DateTime now, CancellationToken ct)
    {
        var old = await _db.AdminLotteryPrizes.Where(p => p.ActivityId == activityId).ToListAsync(ct);
        if (old.Count > 0) _db.AdminLotteryPrizes.RemoveRange(old);
        foreach (var p in prizes ?? new())
        {
            if (string.IsNullOrWhiteSpace(p.PrizeName)) continue;
            _db.AdminLotteryPrizes.Add(new AdminLotteryPrize
            {
                Id = Guid.NewGuid().ToString("N"),
                ActivityId = activityId,
                PrizeName = p.PrizeName.Trim(),
                PrizeIntro = p.PrizeIntro ?? "",
                PrizeImage = p.PrizeImage ?? "",
                PrizeCount = p.PrizeCount <= 0 ? 1 : p.PrizeCount,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    private static AdminLotteryDto ToDto(AdminLotteryActivity a, List<AdminLotteryPrize> prizes) => new()
    {
        Id = a.Id,
        Title = a.Title,
        Description = a.Description,
        CoverImage = a.CoverImage,
        StartTime = a.StartTime,
        DrawTime = a.DrawTime,
        CostPoints = a.CostPoints,
        WinnerCount = a.WinnerCount,
        Status = a.Status,
        PublishTime = a.PublishTime,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
        Prizes = prizes.Select(p => new AdminLotteryPrizeDto
        {
            Id = p.Id,
            PrizeName = p.PrizeName,
            PrizeIntro = p.PrizeIntro,
            PrizeImage = p.PrizeImage,
            PrizeCount = p.PrizeCount,
        }).ToList(),
    };
}
