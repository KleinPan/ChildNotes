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
