namespace ChildNotes.Infrastructure.Auth;

/// <summary>
/// 统一密码哈希抽象，消除原 AdminPasswordUtil 与 PasswordHasher 两套实现。
/// 通过 <see cref="PasswordHashOptions"/> 配置迭代次数等参数，用户与管理员可使用不同配置。
/// </summary>
public interface IPasswordHasher
{
    /// <summary>对密码进行哈希，返回 "salt:hash" 单字段格式。</summary>
    string Hash(string password);

    /// <summary>校验密码是否匹配 "salt:hash" 格式的存储值。</summary>
    bool Verify(string password, string stored);
}

/// <summary>密码哈希参数配置。</summary>
public class PasswordHashOptions
{
    /// <summary>迭代次数。OWASP 2023 推荐 PBKDF2-SHA256 ≥ 600000。默认 120000（对齐 Admin 原值）。</summary>
    public int Iterations { get; set; } = 120_000;

    /// <summary>Salt 字节数，默认 16。</summary>
    public int SaltSize { get; set; } = 16;

    /// <summary>Hash 字节数，默认 32。</summary>
    public int HashSize { get; set; } = 32;
}
