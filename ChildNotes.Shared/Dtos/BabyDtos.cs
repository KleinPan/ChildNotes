namespace ChildNotes.Shared.Dtos;

/// <summary>
/// 家庭成员相关 DTO。前后端共享。
/// </summary>
public class BabyFamilyDto
{
    public string BabyId { get; set; } = string.Empty;
    public string BabyName { get; set; } = string.Empty;
    public List<BabyMemberDto> Members { get; set; } = new();
}

public class BabyMemberDto
{
    public string Id { get; set; } = string.Empty;
    public string BabyId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string NickName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool Owner { get; set; }
    public bool Mine { get; set; }
}
