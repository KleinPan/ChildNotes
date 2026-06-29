using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// 后端同步服务：增量拉取 + 批量上行。
/// 同步范围：baby + child_record（不含 app_user.password_hash / baby_member.role_code）。
/// 权限：只同步当前用户有访问权的宝宝及其记录。
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

    public async Task<SyncPullResponse> PullAsync(DateTime since, int limit = 500, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var sinceUtc = since.ToUniversalTime();
        // 防御性 clamp，避免恶意传入超大 limit 拖垮服务端
        var pageLimit = Math.Clamp(limit, 1, 2000);

        // 当前用户可访问的宝宝 ID 集合（自己创建 + baby_member active）
        var babyIds = await _babyAccess.GetAccessibleBabyIdsAsync(uid, ct);

        var babies = babyIds.Count == 0 ? new() :
            await _db.Babies.AsNoTracking()
                .Where(b => babyIds.Contains(b.Id) && b.UpdatedAt > sinceUtc)
                .OrderBy(b => b.UpdatedAt)
                .Take(pageLimit)
                .ToListAsync(ct);

        var records = babyIds.Count == 0 ? new() :
            await _db.ChildRecords.AsNoTracking()
                .Where(r => babyIds.Contains(r.BabyId ?? 0) && r.UpdatedAt > sinceUtc)
                .OrderBy(r => r.UpdatedAt)
                .Take(pageLimit)
                .ToListAsync(ct);

        // 分页判定：任一类型达到上限即认为可能有更多数据
        var hasMore = babies.Count == pageLimit || records.Count == pageLimit;
        // 游标取已拉取数据中最大的 updated_at（两类取较新者）
        DateTime? nextCursor = null;
        if (records.Count > 0) nextCursor = records.Max(r => r.UpdatedAt);
        if (babies.Count > 0)
        {
            var babyMax = babies.Max(b => b.UpdatedAt);
            nextCursor = nextCursor.HasValue ? (babyMax > nextCursor ? babyMax : nextCursor) : babyMax;
        }

        return new SyncPullResponse
        {
            Babies = babies.Select(ToBabyItem).ToList(),
            Records = records.Select(ToRecordItem).ToList(),
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
            if (item.BabyId.HasValue && !babyIds.Contains(item.BabyId.Value)) continue;

            var existing = await _db.ChildRecords.FirstOrDefaultAsync(r => r.Id == item.Id, ct);
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

            var existing = await _db.Babies.FirstOrDefaultAsync(b => b.Id == item.Id, ct);
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
                existing.BirthDate = item.BirthDate;
                existing.UpdatedAt = item.UpdatedAt;
                babiesUpserted++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return new SyncBatchResponse
        {
            RecordsUpserted = recordsUpserted,
            BabiesUpserted = babiesUpserted,
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
        BirthDate = i.BirthDate,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
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
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
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
        existing.UpdatedAt = src.UpdatedAt;
    }
}
