using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ChildNotes.Core.Config;
using ChildNotes.Core.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ChildNotes.Infrastructure.Auth;

public class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public int ExpireDays { get; set; } = 30;
}

public class JwtTokenService
{
    private readonly JwtOptions _opt;
    private readonly EmailAuthOptions _emailOpt;
    public JwtTokenService(IOptions<JwtOptions> opt, IOptions<EmailAuthOptions> emailOpt)
    {
        _opt = opt.Value;
        _emailOpt = emailOpt.Value;
    }

    /// <summary>创建短期 AccessToken。</summary>
    public (string token, DateTime expireAt) CreateAccessToken(AppUser user)
    {
        var expireAt = DateTime.UtcNow.AddMinutes(_emailOpt.AccessTokenExpireMinutes);
        var claims = new[]
        {
            new Claim("uid", user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email),
            new Claim("type", "access"),
        };
        return (WriteToken(claims, expireAt), expireAt);
    }

    /// <summary>生成随机 RefreshToken 明文（只返回给客户端，服务端只存 Hash）。</summary>
    public (string token, DateTime expireAt) CreateRefreshToken(out string hash)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        hash = HashToken(raw);
        var expireAt = DateTime.UtcNow.AddDays(_emailOpt.RefreshTokenExpireDays);
        return (raw, expireAt);
    }

    /// <summary>计算 Token Hash（PBKDF2 格式，复用密码哈希格式）。</summary>
    public string HashToken(string raw)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(raw, salt, 600_000, HashAlgorithmName.SHA256, 32);
        return $"600000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>恒定时间比较 Token Hash。</summary>
    public bool VerifyToken(string raw, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iter)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var computed = Rfc2898DeriveBytes.Pbkdf2(raw, salt, iter, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(computed, expected);
        }
        catch { return false; }
    }

    private string WriteToken(Claim[] claims, DateTime expireAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(claims: claims, expires: expireAt, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
