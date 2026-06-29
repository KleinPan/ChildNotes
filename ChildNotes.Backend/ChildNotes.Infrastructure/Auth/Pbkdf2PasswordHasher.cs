using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.Auth;

/// <summary>
/// PBKDF2-HMAC-SHA256 密码哈希实现，存储格式为 "salt:hash"（Base64）。
/// 通过 <see cref="PasswordHashOptions"/> 配置迭代次数。
/// 替代原 JwtTokenService.cs 中的静态 PasswordHasher 与 AdminPasswordUtil 双套实现。
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
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, _opt.Iterations, HashAlgorithmName.SHA256, _opt.HashSize);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 兼容原 AdminPasswordUtil 的双字段(salt, hash 分开) PBKDF2 实现。
/// 仅用于 Admin 老数据兼容，新代码应统一使用 <see cref="IPasswordHasher"/>（单字段格式）。
/// 通过 <see cref="AdminPasswordHashOptions"/> 配置迭代次数（默认 120000）。
/// </summary>
public class AdminPasswordHasher
{
    private readonly AdminPasswordHashOptions _opt;

    public AdminPasswordHasher(IOptions<AdminPasswordHashOptions> opt) => _opt = opt.Value;

    public (string salt, string hash) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(_opt.SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, _opt.Iterations, HashAlgorithmName.SHA256, _opt.HashSize);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string saltBase64, string hashBase64)
    {
        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expected = Convert.FromBase64String(hashBase64);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, _opt.Iterations, HashAlgorithmName.SHA256, _opt.HashSize);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Admin 密码哈希参数（双字段格式）。</summary>
public class AdminPasswordHashOptions
{
    public int Iterations { get; set; } = 120_000;
    public int SaltSize { get; set; } = 16;
    public int HashSize { get; set; } = 32;
}
