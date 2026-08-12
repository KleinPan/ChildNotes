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
            // 宝宝 ID 用户可见（用于加入家庭），截取 GUID 前 8 位缩短显示。
            // 16^8=42 亿组合，用户量远小于此，冲突概率可忽略；后期用户上来再加唯一性校验。
            Id = Guid.NewGuid().ToString("N")[..8],
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

        // 补建 owner baby_member 记录：早期创建的宝宝可能没有 owner 成员记录
        // （baby_member 功能上线前创建的宝宝），导致家人列表查不到这些宝宝。
        // 仅检查 IsOwner=true 的记录是否存在，避免历史脏数据干扰补建。
        var myBabies = await _db.Babies.Where(b => b.UserId == uid).Select(b => b.Id).ToListAsync(ct);
        if (myBabies.Count > 0)
        {
            var existingOwnerBabyIds = await _db.BabyMembers
                .Where(m => m.UserId == uid && myBabies.Contains(m.BabyId) && m.IsOwner)
                .Select(m => m.BabyId).ToListAsync(ct);
            var missingBabyIds = myBabies.Except(existingOwnerBabyIds).ToList();
            if (missingBabyIds.Count > 0)
            {
                foreach (var babyId in missingBabyIds)
                {
                    _db.BabyMembers.Add(new BabyMember
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        BabyId = babyId,
                        UserId = uid,
                        RoleCode = "guardian",
                        RoleName = "监护人",
                        IsOwner = true,
                        Status = StatusConstants.BabyMember.Active,
                    });
                }
                await _db.SaveChangesAsync(ct);
            }
        }

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
        var now = DateTime.UtcNow;
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
            // join 时更新 baby.UpdatedAt，让新成员下次增量同步能拉到 baby 记录本身
            // （否则 baby.UpdatedAt 还是创建时的旧时间，被 since > UpdatedAt 过滤掉，
            // 导致新成员本地 baby 表没有该宝宝，显示 0 个宝宝）
            b.UpdatedAt = now;
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

    // ===== 移除成员 + 加入申请/审批 =====

    public async Task RemoveMemberAsync(RemoveMemberRequest req, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        // 仅 owner 可移除；通过 baby.UserId == uid 判定 owner（与 BabyMember.IsOwner 等价）
        var baby = await _db.Babies.FirstOrDefaultAsync(b => b.Id == req.BabyId, ct)
            ?? throw new NotFoundException("宝宝不存在");
        if (baby.UserId != uid)
            throw new ForbiddenException("仅宝宝创建者可移除家庭成员");

        if (req.TargetUserId == uid)
            throw new BusinessException("不能移除自己，请使用退出家庭功能");

        var member = await _db.BabyMembers.FirstOrDefaultAsync(
            m => m.BabyId == req.BabyId && m.UserId == req.TargetUserId, ct);
        if (member is null) return; // 幂等：已不存在视为成功
        if (member.IsOwner)
            throw new BusinessException("不能移除宝宝创建者");

        // 软删除：Status=removed，UpdatedAt 推进以便同步到其他设备
        member.Status = StatusConstants.BabyMember.Removed;
        member.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<FamilyJoinRequestDto> RequestJoinAsync(JoinFamilyRequest req, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var baby = await _db.Babies.FirstOrDefaultAsync(b => b.Id == req.BabyId, ct)
            ?? throw new NotFoundException("宝宝不存在");

        // owner 不需要申请
        if (baby.UserId == uid)
            throw new BusinessException("您是该宝宝的创建者，无需申请加入");

        // 已是 active 成员，直接返回已加入状态（幂等）
        var existingMember = await _db.BabyMembers.FirstOrDefaultAsync(
            m => m.BabyId == req.BabyId && m.UserId == uid, ct);
        if (existingMember is not null && existingMember.Status == StatusConstants.BabyMember.Active)
            throw new BusinessException("您已加入该家庭");

        // 拒绝已有 pending 申请重复提交
        var pending = await _db.FamilyJoinRequests.FirstOrDefaultAsync(
            r => r.BabyId == req.BabyId && r.ApplicantUserId == uid
                && r.Status == StatusConstants.FamilyJoinRequest.Pending, ct);
        if (pending is not null)
            throw new BusinessException("已有待审批的申请，请等待创建者处理");

        var roleName = string.IsNullOrWhiteSpace(req.RoleName)
            ? FamilyRoles.GetRoleName(req.RoleCode) : req.RoleName;
        var now = DateTime.UtcNow;
        var request = new FamilyJoinRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            BabyId = req.BabyId,
            ApplicantUserId = uid,
            RoleCode = req.RoleCode,
            RoleName = roleName,
            Status = StatusConstants.FamilyJoinRequest.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.FamilyJoinRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        return await BuildJoinRequestDtoAsync(request, baby, ct);
    }

    public async Task<List<FamilyJoinRequestDto>> ListPendingJoinRequestsAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        // 当前用户是 owner 的宝宝 ID 集合
        var myBabyIds = await _db.Babies.Where(b => b.UserId == uid).Select(b => b.Id).ToListAsync(ct);
        if (myBabyIds.Count == 0) return new();

        var reqs = await _db.FamilyJoinRequests
            .Where(r => myBabyIds.Contains(r.BabyId) && r.Status == StatusConstants.FamilyJoinRequest.Pending)
            .OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return await BuildJoinRequestDtosAsync(reqs, ct);
    }

    public async Task<List<FamilyJoinRequestDto>> ListMyJoinRequestsAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var reqs = await _db.FamilyJoinRequests
            .Where(r => r.ApplicantUserId == uid)
            .OrderByDescending(r => r.UpdatedAt).ToListAsync(ct);
        return await BuildJoinRequestDtosAsync(reqs, ct);
    }

    public async Task<FamilyJoinRequestDto> ProcessJoinRequestAsync(ProcessJoinRequestDto req, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var request = await _db.FamilyJoinRequests.FirstOrDefaultAsync(r => r.Id == req.RequestId, ct)
            ?? throw new NotFoundException("加入申请不存在");
        if (request.Status != StatusConstants.FamilyJoinRequest.Pending)
            throw new BusinessException("该申请已被处理");

        // 校验当前用户是该宝宝 owner
        var baby = await _db.Babies.FirstOrDefaultAsync(b => b.Id == request.BabyId, ct)
            ?? throw new NotFoundException("宝宝不存在");
        if (baby.UserId != uid)
            throw new ForbiddenException("仅宝宝创建者可处理加入申请");

        var now = DateTime.UtcNow;
        request.ProcessedAt = now;
        request.UpdatedAt = now;

        if (req.Approve)
        {
            request.Status = StatusConstants.FamilyJoinRequest.Approved;
            // 给 owner 名下所有宝宝都建/复活成员记录（与原 JoinFamilyViaInviteAsync 语义一致）
            var ownerBabies = await _db.Babies.Where(b => b.UserId == uid).ToListAsync(ct);
            var ownerBabyIds = ownerBabies.Select(b => b.Id).ToList();
            var existingMembers = await _db.BabyMembers
                .Where(m => ownerBabyIds.Contains(m.BabyId) && m.UserId == request.ApplicantUserId)
                .ToListAsync(ct);
            var existingBabyIds = existingMembers.Select(m => m.BabyId).ToHashSet();
            foreach (var b in ownerBabies)
            {
                if (existingBabyIds.Contains(b.Id))
                {
                    // 复活旧记录（可能是 removed 状态）
                    var em = existingMembers.First(m => m.BabyId == b.Id);
                    em.Status = StatusConstants.BabyMember.Active;
                    em.RoleCode = request.RoleCode;
                    em.RoleName = request.RoleName;
                    em.UpdatedAt = now;
                }
                else
                {
                    _db.BabyMembers.Add(new BabyMember
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        BabyId = b.Id,
                        UserId = request.ApplicantUserId,
                        RoleCode = request.RoleCode,
                        RoleName = request.RoleName,
                        IsOwner = false,
                        Status = StatusConstants.BabyMember.Active,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                }
                // 推进 baby.UpdatedAt 让新成员下次同步能拉到 baby 本身
                b.UpdatedAt = now;
            }
        }
        else
        {
            request.Status = StatusConstants.FamilyJoinRequest.Rejected;
        }
        await _db.SaveChangesAsync(ct);
        return await BuildJoinRequestDtoAsync(request, baby, ct);
    }

    private async Task<FamilyJoinRequestDto> BuildJoinRequestDtoAsync(FamilyJoinRequest r, Baby baby, CancellationToken ct)
    {
        var applicant = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == r.ApplicantUserId, ct);
        return new FamilyJoinRequestDto
        {
            Id = r.Id,
            BabyId = r.BabyId,
            BabyName = baby.Name,
            ApplicantUserId = r.ApplicantUserId,
            ApplicantNickName = applicant?.NickName ?? "用户",
            ApplicantAvatarUrl = applicant?.AvatarUrl ?? string.Empty,
            RoleCode = r.RoleCode,
            RoleName = r.RoleName,
            Status = r.Status,
            ProcessedAt = r.ProcessedAt,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
        };
    }

    private async Task<List<FamilyJoinRequestDto>> BuildJoinRequestDtosAsync(List<FamilyJoinRequest> reqs, CancellationToken ct)
    {
        if (reqs.Count == 0) return new();
        var babyIds = reqs.Select(r => r.BabyId).Distinct().ToList();
        var applicantIds = reqs.Select(r => r.ApplicantUserId).Distinct().ToList();
        var babies = await _db.Babies.Where(b => babyIds.Contains(b.Id)).ToListAsync(ct);
        var users = await _db.AppUsers.Where(u => applicantIds.Contains(u.Id)).ToListAsync(ct);
        var babyMap = babies.ToDictionary(b => b.Id);
        var userMap = users.ToDictionary(u => u.Id);
        return reqs.Select(r => new FamilyJoinRequestDto
        {
            Id = r.Id,
            BabyId = r.BabyId,
            BabyName = babyMap.GetValueOrDefault(r.BabyId)?.Name ?? "宝宝",
            ApplicantUserId = r.ApplicantUserId,
            ApplicantNickName = userMap.GetValueOrDefault(r.ApplicantUserId)?.NickName ?? "用户",
            ApplicantAvatarUrl = userMap.GetValueOrDefault(r.ApplicantUserId)?.AvatarUrl ?? string.Empty,
            RoleCode = r.RoleCode,
            RoleName = r.RoleName,
            Status = r.Status,
            ProcessedAt = r.ProcessedAt,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
        }).ToList();
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
