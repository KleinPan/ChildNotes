using ChildNotes.Core.Config;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Auth;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ChildNotes.Shared.Constants;

namespace ChildNotes.Infrastructure.Services;

public class PointsService : IPointsService
{
    private readonly ICurrentUserService _current;
    private readonly IReferrerCodeUtil _referrer;
    private readonly PointsWalletService _wallet;
    private readonly ISignInService _signIn;
    private readonly ILotteryService _lottery;
    private readonly IInviteService _invite;
    private readonly ChildNotesDbContext _db;
    private readonly IBabyAccessService _babyAccess;

    public PointsService(
        ICurrentUserService current,
        IReferrerCodeUtil referrer,
        PointsWalletService wallet,
        ISignInService signIn,
        ILotteryService lottery,
        IInviteService invite,
        ChildNotesDbContext db,
        IBabyAccessService babyAccess)
    {
        _current = current;
        _referrer = referrer;
        _wallet = wallet;
        _signIn = signIn;
        _lottery = lottery;
        _invite = invite;
        _db = db;
        _babyAccess = babyAccess;
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
            Tasks = await BuildTasksAsync(uid, ct),
        };
        resp.InviteRecords = await _invite.GetInviteRecordsAsync(ct);
        return resp;
    }

    public async Task<List<TaskTemplateDto>> GetTasksAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        return await BuildTasksAsync(uid, ct);
    }

    public async Task<ClaimTaskResponse> ClaimTaskAsync(string taskKey, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var normalizedKey = (taskKey ?? string.Empty).Trim();

        // 校验任务 key 合法性，并取出奖励积分
        if (!PointsConstants.DailyTaskRewards.TryGetValue(normalizedKey, out var reward))
        {
            throw new BusinessException("未知任务", 400, "UNKNOWN_TASK");
        }

        // DateTime.Today 返回 Kind=Local，直接传给 LINQ 查询 timestamptz 字段会报
        // "Cannot write DateTime with Kind=Local"。转成 UTC Kind 避免报错。
        var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Utc);
        // 日常任务幂等：用户 + 任务 key + 当日。CreatedAt 为 UTC，按本地日期换算为 UTC 区间查询。
        var todayStartUtc = today;
        var todayEndUtc = todayStartUtc.AddDays(1);
        if (await IsDailyTaskClaimedInternalAsync(uid, normalizedKey, todayStartUtc, todayEndUtc, ct))
        {
            throw new BusinessException("今日已领取该任务奖励", 400, "TASK_ALREADY_CLAIMED");
        }

        // weekly_growth 额外校验：本周内是否已领取过
        if (normalizedKey == "weekly_growth")
        {
            var weekStart = today.Date.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
            if (today.DayOfWeek == DayOfWeek.Sunday) weekStart = weekStart.AddDays(-7);
            var weekStartUtc = weekStart.ToUniversalTime();
            var weekEndUtc = weekStartUtc.AddDays(7);
            if (await IsDailyTaskClaimedInternalAsync(uid, normalizedKey, weekStartUtc, weekEndUtc, ct))
            {
                throw new BusinessException("本周已领取该任务奖励", 400, "TASK_ALREADY_CLAIMED");
            }
        }

        // 判断任务是否完成
        var isCompleted = await IsDailyTaskCompletedAsync(uid, normalizedKey, today, ct);
        if (!isCompleted)
        {
            throw new BusinessException("任务尚未完成", 400, "TASK_NOT_COMPLETED");
        }

        var now = DateTime.UtcNow;
        _db.TaskRecords.Add(new TaskRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = uid,
            TaskType = "daily_task",
            TaskKey = normalizedKey,
            RelatedUserId = null,
            Points = reward,
            Status = StatusConstants.TaskRecord.Completed,
            PayloadJson = $"{{\"date\":\"{today:yyyy-MM-dd}\"}}",
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.ExecuteInTransactionAsync(async () =>
        {
            await _wallet.ChangeAsync(uid, reward, ct);
            await _db.SaveChangesAsync(ct);
        }, ct);

        var points = await _wallet.EnsureAsync(uid, ct);
        return new ClaimTaskResponse
        {
            AwardedPoints = reward,
            Points = points.Points,
            TotalEarned = points.TotalEarned,
        };
    }

    /// <summary>
    /// 构建任务列表：邀请任务（完成即入账，IsClaimed=true）+ 日常任务（需手动领取）。
    /// </summary>
    private async Task<List<TaskTemplateDto>> BuildTasksAsync(string userId, CancellationToken ct)
    {
        var tasks = new List<TaskTemplateDto>();

        // 邀请任务：展示为已完成领取（实际奖励在 BindReferrerAsync 时已入账）
        tasks.Add(new TaskTemplateDto
        {
            TaskKey = "invite_mom",
            Title = "邀请宝妈使用",
            Description = "赚取100积分",
            Points = PointsConstants.InviteRewardPoints,
            Action = "share",
            IsCompleted = false,
            IsClaimed = false,
        });

        // 日常任务
        // DateTime.Today 返回 Kind=Local，传给 IsDailyTaskCompletedAsync 的 LINQ 查询
        // 会报 "Cannot write DateTime with Kind=Local"。转成 UTC Kind。
        var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Utc);
        var babyIds = await _babyAccess.GetAccessibleBabyIdsAsync(userId, ct);
        var hasBabies = babyIds.Count > 0;

        // claimed 状态合并查询：一次取回本周内（含当日）全部 daily_task 领取记录，内存匹配各 key。
        // 当日 3 个 key（daily_record/daily_feed/daily_diaper）按 [今日0点, 明日0点) 判断，
        // weekly_growth 按本周区间判断，与原逐 key 查询语义一致。
        var weekStart = today.Date.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (today.DayOfWeek == DayOfWeek.Sunday) weekStart = weekStart.AddDays(-7);
        var weekStartUtc = weekStart.ToUniversalTime();
        var weekEndUtc = weekStartUtc.AddDays(7);
        var todayStartUtc = today.ToUniversalTime();
        var todayEndUtc = todayStartUtc.AddDays(1);
        var weekClaimed = await _db.TaskRecords.AsNoTracking()
            .Where(t => t.UserId == userId && t.TaskType == "daily_task"
                && t.CreatedAt >= weekStartUtc && t.CreatedAt < weekEndUtc)
            .Select(t => new { t.TaskKey, t.CreatedAt }).ToListAsync(ct);
        var todayClaimedKeys = weekClaimed
            .Where(t => t.CreatedAt >= todayStartUtc && t.CreatedAt < todayEndUtc)
            .Select(t => t.TaskKey).ToHashSet();
        var weekClaimedKeys = weekClaimed.Select(t => t.TaskKey).ToHashSet();

        // completed 状态合并查询：一次取回当日 distinct 记录类型，内存判断当日 3 个 key
        // （daily_record 为当日任意记录的超集条件，daily_feed/daily_diaper 按类型判断）；
        // weekly_growth 保持独立 7 天范围查询（区间不同，不合并）。
        var todayRecordTypes = hasBabies
            ? (await _db.ChildRecords.AsNoTracking()
                .Where(r => babyIds.Contains(r.BabyId!) && r.RecordDate == today && !r.Deleted)
                .Select(r => r.RecordType).Distinct().ToListAsync(ct)).ToHashSet()
            : new HashSet<string>();

        foreach (var (key, reward) in PointsConstants.DailyTaskRewards)
        {
            var isCompleted = hasBabies && (key switch
            {
                "daily_record" => todayRecordTypes.Count > 0,
                "daily_feed" => todayRecordTypes.Contains(RecordType.Feed),
                "daily_diaper" => todayRecordTypes.Contains(RecordType.Diaper),
                "weekly_growth" => await _db.ChildRecords.AnyAsync(
                    r => babyIds.Contains(r.BabyId!) && r.RecordType == RecordType.Growth
                        && r.RecordDate >= today.AddDays(-6) && !r.Deleted, ct),
                _ => false,
            });
            var isClaimed = key == "weekly_growth"
                ? weekClaimedKeys.Contains(key)
                : todayClaimedKeys.Contains(key);
            tasks.Add(new TaskTemplateDto
            {
                TaskKey = key,
                Title = GetDailyTaskTitle(key),
                Description = GetDailyTaskDesc(key),
                Points = reward,
                Action = "claim",
                IsCompleted = isCompleted,
                IsClaimed = isClaimed,
            });
        }

        return tasks;
    }

    /// <summary>判断日常任务是否已完成（未领取但条件已达成）。</summary>
    private async Task<bool> IsDailyTaskCompletedAsync(
        string userId, string taskKey, DateTime today, CancellationToken ct, List<string>? babyIds = null)
    {
        babyIds ??= await _babyAccess.GetAccessibleBabyIdsAsync(userId, ct);
        if (babyIds.Count == 0) return false;

        return taskKey switch
        {
            "daily_record" => await _db.ChildRecords.AnyAsync(
                r => babyIds.Contains(r.BabyId!) && r.RecordDate == today && !r.Deleted, ct),
            "daily_feed" => await _db.ChildRecords.AnyAsync(
                r => babyIds.Contains(r.BabyId!) && r.RecordDate == today && r.RecordType == RecordType.Feed && !r.Deleted, ct),
            "daily_diaper" => await _db.ChildRecords.AnyAsync(
                r => babyIds.Contains(r.BabyId!) && r.RecordDate == today && r.RecordType == RecordType.Diaper && !r.Deleted, ct),
            "weekly_growth" => await _db.ChildRecords.AnyAsync(
                r => babyIds.Contains(r.BabyId!) && r.RecordType == RecordType.Growth
                    && r.RecordDate >= today.AddDays(-6) && !r.Deleted, ct),
            _ => false,
        };
    }

    /// <summary>统一的领取记录查询：判断 [startUtc, endUtc) 区间内是否已有领取记录。</summary>
    private async Task<bool> IsDailyTaskClaimedInternalAsync(
        string userId, string taskKey, DateTime startUtc, DateTime endUtc, CancellationToken ct)
        => await _db.TaskRecords.AnyAsync(
            t => t.UserId == userId
                && t.TaskType == "daily_task"
                && t.TaskKey == taskKey
                && t.CreatedAt >= startUtc && t.CreatedAt < endUtc, ct);

    private static string GetDailyTaskTitle(string key) => key switch
    {
        "daily_record" => "每日记录",
        "daily_feed" => "喂奶打卡",
        "daily_diaper" => "换尿布打卡",
        "weekly_growth" => "每周成长",
        _ => key,
    };

    private static string GetDailyTaskDesc(string key) => key switch
    {
        "daily_record" => "记录一条宝宝数据",
        "daily_feed" => "记录一次喂奶",
        "daily_diaper" => "记录一次换尿布",
        "weekly_growth" => "记录一次身高体重",
        _ => string.Empty,
    };
}
