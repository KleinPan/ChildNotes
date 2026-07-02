using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChildNotes.Core.Constants;
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
    public JwtTokenService(IOptions<JwtOptions> opt) => _opt = opt.Value;

    public (string token, DateTime expireAt) CreateToken(AppUser user)
    {
        var expireAt = DateTime.UtcNow.AddDays(_opt.ExpireDays);
        var claims = new[]
        {
            new Claim("uid", user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            claims: claims,
            expires: expireAt,
            signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expireAt);
    }
}
