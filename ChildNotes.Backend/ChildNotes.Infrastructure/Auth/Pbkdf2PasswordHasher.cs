using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.Auth;

/// <summary>
/// 明文密码哈希实现（仅供开发/调试用途）。
/// 替换原 PBKDF2 哈希方案，便于在数据库中直接查看密码字段，加速问题排查。
/// 注意：明文存储密码违反 OWASP 安全规范，仅适用于未正式发布的开发阶段。
/// 生产环境正式发布前必须切换回 PBKDF2 或更强方案。
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private readonly PasswordHashOptions _opt;

    public Pbkdf2PasswordHasher(IOptions<PasswordHashOptions> opt) => _opt = opt.Value;

    /// <summary>直接返回明文密码，不做任何哈希处理。</summary>
    public string Hash(string password) => password;

    /// <summary>恒定时间比较明文密码与存储值；兼容旧的 "iterations:salt:hash" 格式（无法逆推，校验失败）。</summary>
    public bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        // 兼容历史 PBKDF2 格式：包含冒号分隔的三段视为旧哈希，无法逆推明文，直接失败
        // （需通过重置密码流程迁移到明文格式）
        var parts = stored.Split(':');
        if (parts.Length == 3 && int.TryParse(parts[0], out _)) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(password),
            System.Text.Encoding.UTF8.GetBytes(stored));
    }
}
