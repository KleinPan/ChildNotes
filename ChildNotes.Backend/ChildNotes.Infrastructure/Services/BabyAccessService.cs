using ChildNotes.Core.Constants;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// 宝宝访问权限校验服务实现。
/// 统一 AiAnalysisService/BabyService/RecordService/SyncService 中的权限校验逻辑到 EnsureAccessAsync 方法。
/// Family-centric（见 docs/development/family-identity-architecture.md）：
/// 范围查询（列表/默认宝宝）按用户当前家庭过滤；单点访问校验保留 owner+baby_member 兼容路径
/// （迁移后两者等价，legacy 路径兜底未迁移的边缘数据与既有邀请流程，阶段 3 统一收敛到 FamilyMember）。
/// </summary>
public class BabyAccessService : IBabyAccessService
{
    private readonly ChildNotesDbContext _db;
    private readonly IFamilyService _familyService;

    public BabyAccessService(ChildNotesDbContext db, IFamilyService familyService)
    {
        _db = db;
        _familyService = familyService;
    }

    public async Task<bool> HasAccessAsync(string userId, string babyId, CancellationToken ct = default)
    {
        // 用户是宝宝创建者，或为该宝宝 active 成员
        return await _db.Babies.AnyAsync(b => b.Id == babyId && b.UserId == userId, ct)
            || await _db.BabyMembers.AnyAsync(m => m.BabyId == babyId && m.UserId == userId && m.Status == StatusConstants.BabyMember.Active, ct);
    }

    public async Task EnsureAccessAsync(string userId, string babyId, CancellationToken ct = default)
    {
        if (!await HasAccessAsync(userId, babyId, ct))
            throw new ForbiddenException("无权访问该宝宝");
    }

    public async Task<List<string>> GetAccessibleBabyIdsAsync(string userId, CancellationToken ct = default)
    {
        // Family 过滤：当前家庭的全部宝宝（owner+成员统一为家庭归属）。
        // 忽略软删过滤器：同步通道需要包含已软删的 baby，以便多设备间传递删除状态。
        var fid = await _familyService.GetCurrentFamilyIdAsync(userId, ct);
        if (fid is null) return new();
        return await _db.Babies.IgnoreQueryFilters()
            .Where(b => b.FamilyId == fid)
            .Select(b => b.Id).ToListAsync(ct);
    }

    public async Task<List<Baby>> GetAccessibleBabiesAsync(string userId, CancellationToken ct = default)
    {
        var fid = await _familyService.GetCurrentFamilyIdAsync(userId, ct);
        if (fid is null) return new();
        return await _db.Babies
            .Where(b => b.FamilyId == fid)
            .OrderBy(b => b.Id).ToListAsync(ct);
    }

    public async Task<Baby?> GetDefaultBabyAsync(string userId, CancellationToken ct = default)
    {
        var fid = await _familyService.GetCurrentFamilyIdAsync(userId, ct);
        if (fid is null) return null;
        return await _db.Babies
            .Where(b => b.FamilyId == fid)
            .OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
    }
}
