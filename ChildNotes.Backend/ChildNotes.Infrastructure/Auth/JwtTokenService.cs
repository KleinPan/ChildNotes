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

    /// <summary>JWT 签发者标识（issuer）。验证时必须与 TokenValidationParameters.ValidIssuer 匹配。</summary>
    public string Issuer { get; set; } = "childnotes";

    /// <summary>JWT 接收方标识（audience）。验证时必须与 TokenValidationParameters.ValidAudience 匹配。</summary>
    public string Audience { get; set; } = "childnotes-app";
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
            // 标准 claims
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Iss, _opt.Issuer),
            new Claim(JwtRegisteredClaimNames.Aud, _opt.Audience),
            // 业务 claims
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
        hash = HashToken(raw, RefreshTokenHashIterations);
        var expireAt = DateTime.UtcNow.AddDays(_emailOpt.RefreshTokenExpireDays);
        return (raw, expireAt);
    }

    /// <summary>
    /// RefreshToken hash 的 PBKDF2 迭代次数。
    /// RefreshToken 是 48 字节高熵随机数（384 bit），暴力破解在数学上不可行，
    /// 无需密码级 60 万次拉伸；且 AuthService.RefreshAsync 需遍历全部候选 token
    /// 逐一验证，600k 时单条约 300ms、全表遍历可达 8 秒（实测值），
    /// 易触发客户端 15s 超时形成 rotation 丢失。降到 100k 提速 6 倍。
    /// </summary>
    private const int RefreshTokenHashIterations = 100_000;

    /// <summary>
    /// 计算 Token Hash（PBKDF2 格式）。
    /// 迭代次数写入格式首段，验证时按存储值执行——旧数据（600000:...）天然兼容。
    /// </summary>
    /// <param name="iterations">
    /// PBKDF2 迭代次数：验证码（6 位数字，低熵）用默认 600k 抗暴力破解；
    /// RefreshToken（48 字节随机，高熵）用 100k 加速遍历验证。
    /// </param>
    public string HashToken(string raw, int iterations = 600_000)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(raw, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"{iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
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
        var jwt = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            expires: expireAt,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
