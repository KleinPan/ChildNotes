namespace ChildNotes.Core.Dtos;

// BabyFamilyDto / BabyMemberDto 已迁移至 ChildNotes.Shared.Dtos（前后端共享）

public class CreateBabyRequest
{
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
}

public class UpdateBabyRequest
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
}

public class BabyDto
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public int AgeInDays { get; set; }
}

public class UpdateBabyMemberRoleRequest
{
    public string BabyId { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
}

public class JoinFamilyRequest
{
    public string BabyId { get; set; } = string.Empty;
    public string RoleCode { get; set; } = "other";
    public string? RoleName { get; set; }
}

/// <summary>移除家庭成员请求。仅 owner 可调用。</summary>
public class RemoveMemberRequest
{
    public string BabyId { get; set; } = string.Empty;
    /// <summary>要移除的用户 ID。</summary>
    public string TargetUserId { get; set; } = string.Empty;
}

/// <summary>加入申请审批操作请求。</summary>
public class ProcessJoinRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    /// <summary>true=批准，false=拒绝。</summary>
    public bool Approve { get; set; }
}

// FamilyJoinRequestDto 已迁移至 ChildNotes.Shared.Dtos（前后端共享）
