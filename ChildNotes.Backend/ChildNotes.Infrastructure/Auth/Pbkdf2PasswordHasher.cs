using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.Auth;

/// <summary>
/// PBKDF2-SHA256 密码哈希实现。
/// 格式：iterations:salt:hash（Base64 编码，冒号分隔）。
/// 兼容历史明文密码：Verify 时自动识别，NeedsUpgrade 标记需迁移。
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private readonly PasswordHashOptions _opt;

    public Pbkdf2PasswordHasher(IOptions<PasswordHashOptions> opt) => _opt = opt.Value;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(_opt.SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            _opt.Iterations,
            HashAlgorithmName.SHA256,
            _opt.HashSize);
        return $"{_opt.Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split(':');
        // PBKDF2 格式：iterations:salt:hash（3 段，首段为整数）
        if (parts.Length == 3 && int.TryParse(parts[0], out var iterations))
        {
            return VerifyPbkdf2(password, stored, iterations, parts[1], parts[2]);
        }
        // 历史明文格式：直接恒定时间比较（兼容旧数据，NeedsUpgrade 会标记迁移）
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(password),
            System.Text.Encoding.UTF8.GetBytes(stored));
    }

    public bool NeedsUpgrade(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split(':');
        // 不是 PBKDF2 格式（3 段 + 首段为整数）则需要升级
        return parts.Length != 3 || !int.TryParse(parts[0], out _);
    }

    private static bool VerifyPbkdf2(string password, string stored, int iterations, string saltBase64, string hashBase64)
    {
        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expectedHash = Convert.FromBase64String(hashBase64);
            var computedHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
