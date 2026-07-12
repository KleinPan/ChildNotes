namespace ChildNotes.Core.Dtos;

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? NickName { get; set; }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpireAt { get; set; }
    public LoginUserDto User { get; set; } = new();
    public bool NewUser { get; set; }
}

public class LoginUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string NickName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public int Gender { get; set; }

    /// <summary>会员到期时间（UTC，ISO 8601 字符串）。非会员为 null。</summary>
    public string? MembershipExpireAt { get; set; }

    /// <summary>是否为有效会员。</summary>
    public bool IsMember { get; set; }
}

public class UpdateProfileRequest
{
    public string? NickName { get; set; }
    public string? AvatarUrl { get; set; }
    public int? Gender { get; set; }
}
