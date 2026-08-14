namespace ChildNotes.Core.Dtos;

/// <summary>发送验证码请求。</summary>
public class SendCodeRequest
{
    public string Email { get; set; } = string.Empty;
}

/// <summary>验证验证码请求（统一注册+登录）。</summary>
public class VerifyCodeRequest
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

/// <summary>刷新 Token 请求。</summary>
public class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>认证响应（verify-code / refresh 返回）。</summary>
public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public LoginUserDto User { get; set; } = new();
    public bool NewUser { get; set; }
}

public class LoginUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
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
