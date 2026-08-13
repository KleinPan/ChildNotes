using System.Linq.Expressions;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Shared.Sync;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// 后端同步服务：增量拉取 + 批量上行。
/// 同步范围：baby + child_record + milestone + sign_in_record（Pull 积分余额）。
/// 权限：只同步当前用户有访问权的宝宝及其记录；签到/积分仅同步当前用户自己的。
/// </summary>
public class SyncService : ISyncService
{
    private readonly ChildNotesDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IBabyAccessService _babyAccess;

    public SyncService(ChildNotesDbContext db, ICurrentUserService current, IBabyAccessService babyAccess)
    {
        _db = db;
        _current = current;
        _babyAccess = babyAccess;
    }

    public async Task<SyncPullResponse> PullAsync(DateTime since, int limit = 500,
        DateTime? cursorTime = null, string? cursorId = null, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var sinceUtc = since.ToUniversalTime();
        // 防御性 clamp，避免恶意传入超大 limit 拖垮服务端
        var pageLimit = Math.Clamp(limit, 1, 2000);

        // 当前用户可访问的宝宝 ID 集合（自己创建 + baby_member active）
        var babyIds = await _babyAccess.GetAccessibleBabyIdsAsync(uid, ct);

        // 复合游标过滤：(UpdatedAt > cursorTime) OR (UpdatedAt == cursorTime AND Id > cursorId)
        // 第一页（cursorTime == null）只用 since 过滤
        // 用 string.Compare 静态方法，EF Core 对它有完整的翻译支持
        var hasCursor = cursorTime is not null && !string.IsNullOrEmpty(cursorId);
        Expression<Func<Baby, bool>> babyCursor = hasCursor
            ? b => babyIds.Contains(b.Id) && b.UpdatedAt > sinceUtc && (b.UpdatedAt > cursorTime!.Value || (b.UpdatedAt == cursorTime.Value && string.Compare(b.Id, cursorId) > 0))
            : b => babyIds.Contains(b.Id) && b.UpdatedAt > sinceUtc;
        Expression<Func<ChildRecord, bool>> recordCursor = hasCursor
            ? r => r.BabyId != null && babyIds.Contains(r.BabyId) && r.UpdatedAt > sinceUtc && (r.UpdatedAt > cursorTime!.Value || (r.UpdatedAt == cursorTime.Value && string.Compare(r.Id, cursorId) > 0))
            : r => r.BabyId != null && babyIds.Contains(r.BabyId) && r.UpdatedAt > sinceUtc;
        Expression<Func<Milestone, bool>> msCursor = hasCursor
            ? m => ((m.BabyId != null && babyIds.Contains(m.BabyId)) || m.UserId == uid) && m.UpdatedAt > sinceUtc && (m.UpdatedAt > cursorTime!.Value || (m.UpdatedAt == cursorTime.Value && string.Compare(m.Id, cursorId) > 0))
            : m => ((m.BabyId != null && babyIds.Contains(m.BabyId)) || m.UserId == uid) && m.UpdatedAt > sinceUtc;
        Expression<Func<SignInRecord, bool>> siCursor = hasCursor
            ? s => s.UserId == uid && s.CreatedAt > sinceUtc && (s.CreatedAt > cursorTime!.Value || (s.CreatedAt == cursorTime.Value && string.Compare(s.Id, cursorId) > 0))
            : s => s.UserId == uid && s.CreatedAt > sinceUtc;
        Expression<Func<BabyMember, bool>> bmCursor = hasCursor
            ? m => babyIds.Contains(m.BabyId) && m.UserId == uid && m.UpdatedAt > sinceUtc && (m.UpdatedAt > cursorTime!.Value || (m.UpdatedAt == cursorTime.Value && string.Compare(m.Id, cursorId) > 0))
            : m => babyIds.Contains(m.BabyId) && m.UserId == uid && m.UpdatedAt > sinceUtc;

        // 加入申请同步：申请人自己提交的 + owner 名下宝宝相关的
        // owner 端用于感知有新申请待审；申请人端用于感知审批结果
        var myOwnedBabyIds = await _db.Babies.Where(b => b.UserId == uid).Select(b => b.Id).ToListAsync(ct);
        Expression<Func<FamilyJoinRequest, bool>> jrCursor = hasCursor
            ? r => (r.ApplicantUserId == uid || myOwnedBabyIds.Contains(r.BabyId))
                && r.UpdatedAt > sinceUtc
                && (r.UpdatedAt > cursorTime!.Value || (r.UpdatedAt == cursorTime.Value && string.Compare(r.Id, cursorId) > 0))
            : r => (r.ApplicantUserId == uid || myOwnedBabyIds.Contains(r.BabyId)) && r.UpdatedAt > sinceUtc;

        var babies = babyIds.Count == 0 ? new() :
            await _db.Babies.AsNoTracking().IgnoreQueryFilters()
                .Where(babyCursor)
                .OrderBy(b => b.UpdatedAt).ThenBy(b => b.Id)
                .Take(pageLimit)
                .ToListAsync(ct);

        var records = babyIds.Count == 0 ? new() :
            await _db.ChildRecords.AsNoTracking().IgnoreQueryFilters()
                .Where(recordCursor)
                .OrderBy(r => r.UpdatedAt).ThenBy(r => r.Id)
                .Take(pageLimit)
                .ToListAsync(ct);

        // 里程碑：按 baby_id 家庭共享（家庭成员可拉到他人创建的里程碑），
        // 兜底 m.UserId == uid 以兼容 BabyId 为 null 的历史数据
        var milestones = babyIds.Count == 0 ? new() :
            await _db.Milestones.AsNoTracking().IgnoreQueryFilters()
            .Where(msCursor)
            .OrderBy(m => m.UpdatedAt).ThenBy(m => m.Id)
            .Take(pageLimit)
            .ToListAsync(ct);

        // 签到记录：仅同步当前用户自己的。SignInRecord 只有 CreatedAt（无 UpdatedAt），
            // 增量基准用 CreatedAt。首签记录 CreatedAt 最早，分页时按 CreatedAt 升序。
            var signIns = await _db.SignInRecords.AsNoTracking()
                .Where(siCursor)
                .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
                .Take(pageLimit)
                .ToListAsync(ct);

        // 当前用户参与的宝宝成员关系（含自己创建和 join 的）。Pull-only，前端用于判断可访问的宝宝。
        var babyMembers = babyIds.Count == 0 ? new() :
            await _db.BabyMembers.AsNoTracking()
                .Where(bmCursor)
                .OrderBy(m => m.UpdatedAt).ThenBy(m => m.Id)
                .Take(pageLimit)
                .ToListAsync(ct);

        // 加入申请：申请人自己 + owner 名下宝宝相关。Pull-only。
        var joinRequests = await _db.FamilyJoinRequests.AsNoTracking()
            .Where(jrCursor)
            .OrderBy(r => r.UpdatedAt).ThenBy(r => r.Id)
            .Take(pageLimit)
            .ToListAsync(ct);

        // 当前用户积分余额（每页都返回，客户端以最后一页为准）。积分是 Pull-only 数据。
        var userPoints = await _db.UserPoints.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == uid, ct);

        // 分页判定：任一同步集合达到上限即认为可能有更多数据。
        // 注意：新增同步集合时必须同步加入此判断，否则该集合满页时会漏数据
        // （cursor 被设置但 hasMore=false，客户端 break 退出循环）。
        var hasMore = babies.Count == pageLimit || records.Count == pageLimit
            || milestones.Count == pageLimit || signIns.Count == pageLimit
            || babyMembers.Count == pageLimit || joinRequests.Count == pageLimit;

        // 复合游标取"达上限表"的 (Max(timestamp), 对应 Max(Id)) 中的最小值。
        // 各表按 (timestamp, Id) 排序，达上限时取最后一条的 (timestamp, Id) 作为该表的候选。
        // 然后从所有候选中取 timestamp 最小（相同则 Id 最小）的作为 nextCursor。
        // 未达上限的表数据已全部拉完，不参与 cursor 计算。
        SyncCursor? nextCursor = null;
        if (hasMore)
        {
            var candidates = new List<(DateTime ts, string id)>(5);
            if (records.Count == pageLimit) { var last = records[^1]; candidates.Add((last.UpdatedAt, last.Id)); }
            if (babies.Count == pageLimit) { var last = babies[^1]; candidates.Add((last.UpdatedAt, last.Id)); }
            if (milestones.Count == pageLimit) { var last = milestones[^1]; candidates.Add((last.UpdatedAt, last.Id)); }
            if (signIns.Count == pageLimit) { var last = signIns[^1]; candidates.Add((last.CreatedAt, last.Id)); }
            if (babyMembers.Count == pageLimit) { var last = babyMembers[^1]; candidates.Add((last.UpdatedAt, last.Id)); }
            if (joinRequests.Count == pageLimit) { var last = joinRequests[^1]; candidates.Add((last.UpdatedAt, last.Id)); }
            if (candidates.Count > 0)
            {
                var min = candidates.OrderBy(c => c.ts).ThenBy(c => c.id).First();
                nextCursor = new SyncCursor { Timestamp = min.ts, Id = min.id };
            }
        }

        return new SyncPullResponse
        {
            Babies = babies.Select(ToBabyItem).ToList(),
            Records = records.Select(ToRecordItem).ToList(),
            Milestones = milestones.Select(ToMilestoneItem).ToList(),
            SignIns = signIns.Select(ToSignInItem).ToList(),
            BabyMembers = babyMembers.Select(m => new SyncBabyMemberItem
            {
                Id = m.Id,
                BabyId = m.BabyId,
                UserId = m.UserId,
                RoleCode = m.RoleCode,
                RoleName = m.RoleName,
                IsOwner = m.IsOwner,
                Status = m.Status,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt,
            }).ToList(),
            UserPoints = userPoints is null ? null : ToUserPointsItem(userPoints),
            ServerTime = DateTime.UtcNow,
            HasMore = hasMore,
            NextCursor = nextCursor,
        };
    }

    public async Task<SyncBatchResponse> PushAsync(SyncBatchRequest req, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();

        var babyIds = await _babyAccess.GetAccessibleBabyIdsAsync(uid, ct);

        var recordsUpserted = 0;
        foreach (var item in req.Records ?? new())
        {
            // 权限：记录必须属于当前用户可访问的宝宝，且 user_id 必须是当前用户
            if (item.UserId != uid) continue;
            if (!string.IsNullOrEmpty(item.BabyId) && !babyIds.Contains(item.BabyId)) continue;

            var existing = await _db.ChildRecords.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == item.Id, ct);
            if (existing is null)
            {
                _db.ChildRecords.Add(FromItem(item));
                recordsUpserted++;
            }
            else if (item.UpdatedAt > existing.UpdatedAt)
            {
                // LWW 行级合并：远程较新才覆盖
                CopyTo(existing, item);
                recordsUpserted++;
            }
        }

        var babiesUpserted = 0;
        foreach (var item in req.Babies ?? new())
        {
            // 权限：只能 upsert 自己创建的宝宝
            if (item.UserId != uid) continue;

            // IgnoreQueryFilters：需能查到已软删的 baby 以便更新其字段
            var existing = await _db.Babies.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == item.Id, ct);
            if (existing is null)
            {
                _db.Babies.Add(FromItem(item));
                babiesUpserted++;
            }
            else if (item.UpdatedAt > existing.UpdatedAt)
            {
                existing.Name = item.Name;
                existing.Avatar = item.Avatar;
                existing.Gender = item.Gender;
                existing.BirthDate = item.BirthDate is null ? null : DateTime.SpecifyKind(item.BirthDate.Value, DateTimeKind.Utc);
                existing.Deleted = item.Deleted;
                existing.UpdatedAt = DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc);
                babiesUpserted++;
            }
        }

        var milestonesUpserted = 0;
        foreach (var item in req.Milestones ?? new())
        {
            // 权限：里程碑必须属于当前用户可访问的宝宝（与 ChildRecord 一致），
            // 允许家庭成员 push 自己创建的里程碑（UserId 保留为创建者，不强制覆盖）
            if (!string.IsNullOrEmpty(item.BabyId) && !babyIds.Contains(item.BabyId)) continue;

            var existing = await _db.Milestones.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == item.Id, ct);
            if (existing is null)
            {
                _db.Milestones.Add(FromItem(item));
                milestonesUpserted++;
            }
            else if (item.UpdatedAt > existing.UpdatedAt)
            {
                CopyTo(existing, item);
                milestonesUpserted++;
            }
        }

        // 签到记录：客户端上送本地签到（离线签到场景）。以 Id 做幂等 upsert，
        // 不重复发积分——积分发放以服务端签到 API 为准，这里只同步记录本身。
        var signInsUpserted = 0;
        foreach (var item in req.SignIns ?? new())
        {
            if (item.UserId != uid) continue;

            var existing = await _db.SignInRecords.FirstOrDefaultAsync(s => s.Id == item.Id, ct);
            if (existing is null)
            {
                _db.SignInRecords.Add(new SignInRecord
                {
                    Id = item.Id,
                    UserId = item.UserId,
                    SignDate = DateTime.SpecifyKind(item.SignDate, DateTimeKind.Utc),
                    ContinuousDays = item.ContinuousDays,
                    RewardPoints = item.Reward,
                    CreatedAt = DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc),
                });
                signInsUpserted++;
            }
            // 签到记录不可变（无 UpdatedAt），已存在则跳过
        }

        await _db.SaveChangesAsync(ct);
        return new SyncBatchResponse
        {
            RecordsUpserted = recordsUpserted,
            BabiesUpserted = babiesUpserted,
            MilestonesUpserted = milestonesUpserted,
            SignInsUpserted = signInsUpserted,
            ServerTime = DateTime.UtcNow,
        };
    }

    private static SyncBabyItem ToBabyItem(Baby b) => new()
    {
        Id = b.Id,
        UserId = b.UserId,
        Name = b.Name,
        Avatar = b.Avatar ?? "",
        Gender = b.Gender ?? "",
        BirthDate = b.BirthDate,
        Deleted = b.Deleted,
        CreatedAt = b.CreatedAt,
        UpdatedAt = b.UpdatedAt,
    };

    private static SyncRecordItem ToRecordItem(ChildRecord r) => new()
    {
        Id = r.Id,
        UserId = r.UserId,
        BabyId = r.BabyId,
        RecordType = r.RecordType,
        RecordSubType = r.RecordSubType,
        RecordDate = r.RecordDate,
        RecordTime = r.RecordTime,
        AmountMl = r.AmountMl,
        DurationSec = r.DurationSec,
        LeftDurationSec = r.LeftDurationSec,
        RightDurationSec = r.RightDurationSec,
        AbnormalFlag = r.AbnormalFlag,
        TemperatureValue = r.TemperatureValue,
        HeightCm = r.HeightCm,
        WeightKg = r.WeightKg,
        PayloadJson = r.PayloadJson,
        Deleted = r.Deleted,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    private static Baby FromItem(SyncBabyItem i) => new()
    {
        Id = i.Id,
        UserId = i.UserId,
        Name = i.Name,
        Avatar = i.Avatar,
        Gender = i.Gender,
        BirthDate = i.BirthDate is null ? null : DateTime.SpecifyKind(i.BirthDate.Value, DateTimeKind.Utc),
        Deleted = i.Deleted,
        CreatedAt = DateTime.SpecifyKind(i.CreatedAt, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(i.UpdatedAt, DateTimeKind.Utc),
    };

    private static ChildRecord FromItem(SyncRecordItem i) => new()
    {
        Id = i.Id,
        UserId = i.UserId,
        BabyId = i.BabyId,
        RecordType = i.RecordType,
        RecordSubType = i.RecordSubType,
        RecordDate = DateTime.SpecifyKind(i.RecordDate, DateTimeKind.Utc),
        RecordTime = DateTime.SpecifyKind(i.RecordTime, DateTimeKind.Utc),
        AmountMl = i.AmountMl,
        DurationSec = i.DurationSec,
        LeftDurationSec = i.LeftDurationSec,
        RightDurationSec = i.RightDurationSec,
        AbnormalFlag = i.AbnormalFlag,
        TemperatureValue = i.TemperatureValue,
        HeightCm = i.HeightCm,
        WeightKg = i.WeightKg,
        PayloadJson = i.PayloadJson,
        Deleted = i.Deleted,
        CreatedAt = DateTime.SpecifyKind(i.CreatedAt, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(i.UpdatedAt, DateTimeKind.Utc),
    };

    private static void CopyTo(ChildRecord existing, SyncRecordItem src)
    {
        existing.RecordType = src.RecordType;
        existing.RecordSubType = src.RecordSubType;
        existing.RecordDate = DateTime.SpecifyKind(src.RecordDate, DateTimeKind.Utc);
        existing.RecordTime = DateTime.SpecifyKind(src.RecordTime, DateTimeKind.Utc);
        existing.AmountMl = src.AmountMl;
        existing.DurationSec = src.DurationSec;
        existing.LeftDurationSec = src.LeftDurationSec;
        existing.RightDurationSec = src.RightDurationSec;
        existing.AbnormalFlag = src.AbnormalFlag;
        existing.TemperatureValue = src.TemperatureValue;
        existing.HeightCm = src.HeightCm;
        existing.WeightKg = src.WeightKg;
        existing.PayloadJson = src.PayloadJson;
        existing.Deleted = src.Deleted;
        existing.UpdatedAt = DateTime.SpecifyKind(src.UpdatedAt, DateTimeKind.Utc);
    }

    private static SyncMilestoneItem ToMilestoneItem(Milestone m) => new()
    {
        Id = m.Id,
        UserId = m.UserId,
        BabyId = m.BabyId,
        Title = m.Title,
        Content = m.Content,
        RecordDate = m.RecordDate,
        PhotosJson = m.PhotosJson,
        Deleted = m.Deleted,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
    };

    private static Milestone FromItem(SyncMilestoneItem i) => new()
    {
        Id = i.Id,
        UserId = i.UserId,
        BabyId = i.BabyId,
        Title = i.Title,
        Content = i.Content,
        RecordDate = DateTime.SpecifyKind(i.RecordDate, DateTimeKind.Utc),
        PhotosJson = i.PhotosJson ?? "[]",
        Deleted = i.Deleted,
        CreatedAt = DateTime.SpecifyKind(i.CreatedAt, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(i.UpdatedAt, DateTimeKind.Utc),
    };

    private static void CopyTo(Milestone existing, SyncMilestoneItem src)
    {
        existing.Title = src.Title;
        existing.Content = src.Content;
        existing.RecordDate = DateTime.SpecifyKind(src.RecordDate, DateTimeKind.Utc);
        existing.PhotosJson = src.PhotosJson ?? "[]";
        existing.BabyId = src.BabyId;
        existing.Deleted = src.Deleted;
        existing.UpdatedAt = DateTime.SpecifyKind(src.UpdatedAt, DateTimeKind.Utc);
    }

    private static SyncSignInItem ToSignInItem(SignInRecord s) => new()
    {
        Id = s.Id,
        UserId = s.UserId,
        SignDate = s.SignDate,
        ContinuousDays = s.ContinuousDays,
        Reward = s.RewardPoints,
        CreatedAt = s.CreatedAt,
    };

    private static SyncUserPointsItem ToUserPointsItem(UserPoints p) => new()
    {
        Id = p.Id,
        UserId = p.UserId,
        Points = p.Points,
        TotalEarned = p.TotalEarned,
        TotalSpent = p.TotalSpent,
        UpdatedAt = p.UpdatedAt,
    };
}
