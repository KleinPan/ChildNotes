using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

/// <inheritdoc />
public class FamilyService : IFamilyService
{
    private readonly ChildNotesDbContext _db;

    public FamilyService(ChildNotesDbContext db) => _db = db;

    public async Task<List<FamilyDto>> GetUserFamiliesAsync(string userId, CancellationToken ct = default)
    {
        // 按 CreatedAt 排序保证 currentFamilyId 确定性；ThenBy(Id) 兜底同毫秒创建的并列
        return await _db.FamilyMembers
            .Where(fm => fm.UserId == userId)
            .Join(_db.Families, fm => fm.FamilyId, f => f.Id, (fm, f) => new { fm, f })
            .OrderBy(x => x.f.CreatedAt).ThenBy(x => x.f.Id)
            .Select(x => new FamilyDto
            {
                Id = x.f.Id,
                Name = x.f.Name,
                Role = x.fm.Role,
            })
            .ToListAsync(ct);
    }

    public async Task<string?> GetCurrentFamilyIdAsync(string userId, CancellationToken ct = default)
    {
        // 当前家庭 = 最近加入的家庭（FamilyMember.CreatedAt DESC）。
        // 单家庭用户（注册自建）行为不变；被批准加入他人家庭后，分区立即切换到共享家庭，
        // 同步 push/pull 与权限过滤才作用于家庭数据（家庭共享主链路，见 family-identity-architecture.md）。
        // 不能按 Family.CreatedAt 升序取最早：申请人自建家庭总是更早创建，会永远停留在
        // 自建家庭，补写 FamilyMember 也无法让共享生效。ThenByDescending(Id) 兜底同毫秒并列。
        var familyId = await _db.FamilyMembers
            .Where(fm => fm.UserId == userId)
            .Join(_db.Families, fm => fm.FamilyId, f => f.Id, (fm, f) => new { f.Id, fm.CreatedAt })
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return familyId;
    }
}
