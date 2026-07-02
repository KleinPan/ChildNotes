using ChildNotes.Core.Common;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

public class BabyService : IBabyService
{
    private readonly ChildNotesDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IBabyAccessService _babyAccess;

    public BabyService(ChildNotesDbContext db, ICurrentUserService current, IBabyAccessService babyAccess)
    {
        _db = db;
        _current = current;
        _babyAccess = babyAccess;
    }

    public async Task<List<BabyDto>> ListBabiesAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        // 用户加入的家庭成员 + 自己创建的宝宝
        var babyIds = await _db.BabyMembers
            .Where(m => m.UserId == uid && m.Status == StatusConstants.BabyMember.Active)
            .Select(m => m.BabyId).Distinct().ToListAsync(ct);
        var babies = await _db.Babies
            .Where(b => babyIds.Contains(b.Id))
            .OrderBy(b => b.Id).ToListAsync(ct);
        return babies.Select(ToBabyDto).ToList();
    }

    public async Task<BabyDto?> GetCurrentBabyAsync(string? babyId, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        Baby? baby = null;
        if (!string.IsNullOrEmpty(babyId))
        {
            await _babyAccess.EnsureAccessAsync(uid, babyId, ct);
            baby = await _db.Babies.FirstOrDefaultAsync(b => b.Id == babyId, ct);
        }
        baby ??= await _babyAccess.GetDefaultBabyAsync(uid, ct);
        return baby is null ? null : ToBabyDto(baby);
    }

    public async Task<BabyDto> CreateBabyAsync(CreateBabyRequest req, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var baby = new Baby
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = uid,
            Name = string.IsNullOrWhiteSpace(req.Name) ? "宝宝" : req.Name,
            Avatar = req.Avatar ?? string.Empty,
            Gender = string.IsNullOrWhiteSpace(req.Gender) ? "boy" : req.Gender,
            BirthDate = req.BirthDate,
        };
        _db.Babies.Add(baby);
        await _db.SaveChangesAsync(ct);

        // 以下两次写入（owner 成员 + 同步现有成员）必须原子：若失败会留下无 owner 的孤儿 Baby。
        await _db.ExecuteInTransactionAsync(async () =>
        {
            // 为创建者建 owner 成员
            var ownerMember = new BabyMember
            {
                Id = Guid.NewGuid().ToString("N"),
                BabyId = baby.Id,
                UserId = uid,
                RoleCode = "father",
                RoleName = "爸爸",
                IsOwner = true,
                Status = StatusConstants.BabyMember.Active,
            };
            _db.BabyMembers.Add(ownerMember);

            // 将创建者名下其他宝宝的家庭成员同步到新宝宝
            var existingMembers = await _db.BabyMembers
                .Where(m => m.UserId == uid && m.BabyId != baby.Id && m.Status == StatusConstants.BabyMember.Active)
                .GroupBy(m => new { m.UserId, m.RoleCode }).Select(g => g.First())
                .ToListAsync(ct);
            foreach (var m in existingMembers)
            {
                _db.BabyMembers.Add(new BabyMember
                {
                    Id = Guid.NewGuid().ToString("N"),
                    BabyId = baby.Id,
                    UserId = m.UserId,
                    RoleCode = m.RoleCode,
                    RoleName = m.RoleName,
                    IsOwner = m.UserId == uid,
                    Status = StatusConstants.BabyMember.Active,
                });
            }
            await _db.SaveChangesAsync(ct);
        }, ct);
        return ToBabyDto(baby);
    }

    public async Task<BabyDto> UpdateBabyAsync(UpdateBabyRequest req, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var babyId = !string.IsNullOrEmpty(req.Id) ? req.Id : (await GetCurrentBabyAsync(null, ct))?.Id
            ?? throw new NotFoundException("未找到宝宝");
        await _babyAccess.EnsureAccessAsync(uid, babyId, ct);
        var baby = await _db.Babies.FirstOrDefaultAsync(b => b.Id == babyId, ct)
            ?? throw new NotFoundException("宝宝不存在");
        if (req.Name is not null) baby.Name = req.Name;
        if (req.Avatar is not null) baby.Avatar = req.Avatar;
        if (req.Gender is not null) baby.Gender = req.Gender;
        if (req.BirthDate is not null) baby.BirthDate = req.BirthDate;
        await _db.SaveChangesAsync(ct);
        return ToBabyDto(baby);
    }

    public async Task<List<BabyFamilyDto>> ListFamilyMembersAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var babyIds = await _db.BabyMembers
            .Where(m => m.UserId == uid && m.Status == StatusConstants.BabyMember.Active)
            .Select(m => m.BabyId).Distinct().ToListAsync(ct);
        var babies = await _db.Babies.Where(b => babyIds.Contains(b.Id))
            .OrderBy(b => b.Id).ToListAsync(ct);
        var allMembers = await _db.BabyMembers
            .Where(m => babyIds.Contains(m.BabyId) && m.Status == StatusConstants.BabyMember.Active)
            .OrderBy(m => m.BabyId).ThenByDescending(m => m.IsOwner).ThenBy(m => m.Id)
            .ToListAsync(ct);
        var userIds = allMembers.Select(m => m.UserId).Distinct().ToList();
        var users = await _db.AppUsers.Where(u => userIds.Contains(u.Id)).ToListAsync(ct);
        var userMap = users.ToDictionary(u => u.Id);

        var result = new List<BabyFamilyDto>();
        foreach (var baby in babies)
        {
            var fam = new BabyFamilyDto { BabyId = baby.Id, BabyName = baby.Name };
            foreach (var m in allMembers.Where(x => x.BabyId == baby.Id))
            {
                var u = userMap.GetValueOrDefault(m.UserId);
                fam.Members.Add(new BabyMemberDto
                {
                    Id = m.Id,
                    BabyId = m.BabyId,
                    UserId = m.UserId,
                    NickName = u?.NickName ?? "用户",
                    AvatarUrl = u?.AvatarUrl ?? string.Empty,
                    RoleCode = m.RoleCode,
                    RoleName = m.RoleName,
                    Owner = m.IsOwner,
                    Mine = m.UserId == uid,
                });
            }
            result.Add(fam);
        }
        return result;
    }

    public async Task<BabyMemberDto> UpdateMyFamilyRoleAsync(UpdateBabyMemberRoleRequest req, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        await _babyAccess.EnsureAccessAsync(uid, req.BabyId, ct);
        var member = await _db.BabyMembers.FirstOrDefaultAsync(
            m => m.BabyId == req.BabyId && m.UserId == uid, ct);
        if (member is null)
        {
            member = new BabyMember
            {
                Id = Guid.NewGuid().ToString("N"),
                BabyId = req.BabyId,
                UserId = uid,
                RoleCode = req.RoleCode,
                RoleName = FamilyRoles.GetRoleName(req.RoleCode),
                IsOwner = false,
                Status = StatusConstants.BabyMember.Active,
            };
            _db.BabyMembers.Add(member);
        }
        else
        {
            member.RoleCode = req.RoleCode;
            member.RoleName = FamilyRoles.GetRoleName(req.RoleCode);
        }
        await _db.SaveChangesAsync(ct);
        return new BabyMemberDto
        {
            Id = member.Id,
            BabyId = member.BabyId,
            UserId = member.UserId,
            RoleCode = member.RoleCode,
            RoleName = member.RoleName,
            Owner = member.IsOwner,
            Mine = true,
        };
    }

    public async Task<BabyMemberDto> JoinFamilyViaInviteAsync(JoinFamilyRequest req, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var baby = await _db.Babies.FirstOrDefaultAsync(b => b.Id == req.BabyId, ct)
            ?? throw new NotFoundException("宝宝不存在");
        var roleName = string.IsNullOrWhiteSpace(req.RoleName) ? FamilyRoles.GetRoleName(req.RoleCode) : req.RoleName;

        // 给宝宝主人名下所有宝宝都建成员记录
        var ownerBabies = await _db.Babies.Where(b => b.UserId == baby.UserId).ToListAsync(ct);
        // 一次性查询已存在的 BabyMember，避免 N+1
        var ownerBabyIds = ownerBabies.Select(b => b.Id).ToList();
        var existingMemberBabyIds = await _db.BabyMembers
            .Where(m => ownerBabyIds.Contains(m.BabyId) && m.UserId == uid)
            .Select(m => m.BabyId).ToListAsync(ct);
        foreach (var b in ownerBabies)
        {
            if (existingMemberBabyIds.Contains(b.Id)) continue;
            _db.BabyMembers.Add(new BabyMember
            {
                Id = Guid.NewGuid().ToString("N"),
                BabyId = b.Id,
                UserId = uid,
                RoleCode = req.RoleCode,
                RoleName = roleName,
                IsOwner = false,
                Status = StatusConstants.BabyMember.Active,
            });
        }
        await _db.SaveChangesAsync(ct);

        var member = await _db.BabyMembers.FirstAsync(
            m => m.BabyId == req.BabyId && m.UserId == uid, ct);
        return new BabyMemberDto
        {
            Id = member.Id,
            BabyId = member.BabyId,
            UserId = member.UserId,
            RoleCode = member.RoleCode,
            RoleName = member.RoleName,
            Owner = member.IsOwner,
            Mine = true,
        };
    }

    private static BabyDto ToBabyDto(Baby b) => new()
    {
        Id = b.Id,
        UserId = b.UserId,
        Name = b.Name,
        Avatar = b.Avatar,
        Gender = b.Gender,
        BirthDate = b.BirthDate,
        AgeInDays = BabyUtil.GetAgeInDays(b.BirthDate),
    };
}
