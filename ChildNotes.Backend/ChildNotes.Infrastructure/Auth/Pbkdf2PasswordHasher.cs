using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.Auth;

/// <summary>
/// PBKDF2-HMAC-SHA256 密码哈希实现，存储格式 "iterations:salt:hash"（Base64）。
/// 通过 <see cref="PasswordHashOptions"/> 配置迭代次数，支持渐进升级。
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private readonly PasswordHashOptions _opt;

    public Pbkdf2PasswordHasher(IOptions<PasswordHashOptions> opt) => _opt = opt.Value;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(_opt.SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, _opt.Iterations, HashAlgorithmName.SHA256, _opt.HashSize);
        return $"{_opt.Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }
}
