using ChildNotes.Core.Constants;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// 宝宝访问权限校验服务实现。
/// 消除原 AiAnalysisService/BabyService/RecordService/SyncService 中的重复 EnsureBabyAccessAsync 方法。
/// </summary>
public class BabyAccessService : IBabyAccessService
{
    private readonly ChildNotesDbContext _db;

    public BabyAccessService(ChildNotesDbContext db) => _db = db;

    public async Task<bool> HasAccessAsync(long userId, long babyId, CancellationToken ct = default)
    {
        // 用户是宝宝创建者，或为该宝宝 active 成员
        return await _db.Babies.AnyAsync(b => b.Id == babyId && b.UserId == userId, ct)
            || await _db.BabyMembers.AnyAsync(m => m.BabyId == babyId && m.UserId == userId && m.Status == StatusConstants.BabyMember.Active, ct);
    }

    public async Task EnsureAccessAsync(long userId, long babyId, CancellationToken ct = default)
    {
        if (!await HasAccessAsync(userId, babyId, ct))
            throw new ForbiddenException("无权访问该宝宝");
    }

    public async Task<List<long>> GetAccessibleBabyIdsAsync(long userId, CancellationToken ct = default)
    {
        return await _db.Babies
            .Where(b => b.UserId == userId
                || _db.BabyMembers.Any(m => m.BabyId == b.Id && m.UserId == userId && m.Status == StatusConstants.BabyMember.Active))
            .Select(b => b.Id).ToListAsync(ct);
    }

    public async Task<List<Baby>> GetAccessibleBabiesAsync(long userId, CancellationToken ct = default)
    {
        return await _db.Babies
            .Where(b => b.UserId == userId
                || _db.BabyMembers.Any(m => m.BabyId == b.Id && m.UserId == userId && m.Status == StatusConstants.BabyMember.Active))
            .OrderBy(b => b.Id).ToListAsync(ct);
    }

    public async Task<Baby?> GetDefaultBabyAsync(long userId, CancellationToken ct = default)
    {
        return await _db.Babies
            .Where(b => b.UserId == userId
                || _db.BabyMembers.Any(m => m.BabyId == b.Id && m.UserId == userId && m.Status == StatusConstants.BabyMember.Active))
            .OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
    }
}
