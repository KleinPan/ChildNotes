namespace ChildNotes.Core.Dtos;

public class CreateBabyRequest
{
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
}

public class UpdateBabyRequest
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
}

public class BabyDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public int AgeInDays { get; set; }
}

public class BabyFamilyDto
{
    public long BabyId { get; set; }
    public string BabyName { get; set; } = string.Empty;
    public List<BabyMemberDto> Members { get; set; } = new();
}

public class BabyMemberDto
{
    public long Id { get; set; }
    public long BabyId { get; set; }
    public long UserId { get; set; }
    public string NickName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool Owner { get; set; }
    public bool Mine { get; set; }
}

public class UpdateBabyMemberRoleRequest
{
    public long BabyId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
}

public class JoinFamilyRequest
{
    public long BabyId { get; set; }
    public string RoleCode { get; set; } = "other";
    public string? RoleName { get; set; }
}
