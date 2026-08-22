namespace ChildNotes.Core.Dtos;

/// <summary>发送验证码请求。</summary>
public class SendCodeRequest
{
    /// <summary>邮箱地址。必须在 [1, 256] 字符内，且符合 RFC 邮箱格式。</summary>
    public string Email { get; set; } = string.Empty;
}

/// <summary>验证验证码请求（统一注册+登录）。</summary>
public class VerifyCodeRequest
{
    /// <summary>邮箱地址。必须在 [1, 256] 字符内，且符合 RFC 邮箱格式。</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>验证码。固定 6 位数字。</summary>
    public string Code { get; set; } = string.Empty;
}

/// <summary>刷新 Token 请求。</summary>
public class RefreshRequest
{
    /// <summary>RefreshToken 明文（Base64）。服务端只存 Hash。</summary>
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

    /// <summary>用户所属家庭列表（一人多家庭 API 预留，MVP 单家庭）。</summary>
    public List<FamilyDto> Families { get; set; } = new();

    /// <summary>当前家庭 Id（MVP = Families[0].Id；无家庭时为 null）。</summary>
    public string? CurrentFamilyId { get; set; }
}

/// <summary>家庭信息（登录响应 / 家庭接口共用）。</summary>
public class FamilyDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>当前用户在该家庭的角色：owner / member / readonly。</summary>
    public string Role { get; set; } = string.Empty;
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

/// <summary>
/// 邮箱/验证码格式校验工具（不引入 DataAnnotations 依赖）。
/// AuthService 在执行业务前调用，避免畸形输入进入数据库/SMTP。
/// </summary>
public static class AuthRequestValidator
{
    /// <summary>校验邮箱格式 + 长度。返回 null 表示通过，否则返回错误消息。</summary>
    public static string? ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "邮箱不能为空";
        if (email.Length > 256)
            return "邮箱长度不能超过 256 字符";
        if (email.Length < 5)
            return "邮箱格式无效";
        // 简单但有效的 RFC 5322 子集校验：必须含 @，@ 前后都有内容，域名段含 .
        var at = email.IndexOf('@');
        if (at <= 0 || at >= email.Length - 1)
            return "邮箱格式无效";
        var domain = email.Substring(at + 1);
        if (!domain.Contains('.'))
            return "邮箱格式无效";
        // 不允许空格
        if (email.Contains(' '))
            return "邮箱格式无效";
        return null;
    }

    /// <summary>校验验证码格式。返回 null 表示通过，否则返回错误消息。</summary>
    public static string? ValidateCode(string? code, int expectedLength = 6)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "验证码不能为空";
        if (code.Length != expectedLength)
            return $"验证码必须是 {expectedLength} 位";
        if (!code.All(char.IsDigit))
            return "验证码必须为纯数字";
        return null;
    }
}
