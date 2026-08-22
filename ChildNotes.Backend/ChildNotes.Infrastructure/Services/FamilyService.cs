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
        // 只取 Id 投影，避免拉整行
        var familyId = await _db.FamilyMembers
            .Where(fm => fm.UserId == userId)
            .Join(_db.Families, fm => fm.FamilyId, f => f.Id, (fm, f) => new { f.Id, f.CreatedAt })
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return familyId;
    }
}
