namespace ChildNotes.Services.Storage;

/// <summary>
/// 平台安全存储抽象：用于保存敏感凭据（AccessToken / RefreshToken）。
/// 平台实现：
///   - Android → Android Keystore（加密后写入 SharedPreferences）
///   - Windows → Windows DPAPI（ProtectedData.Protect）
///   - iOS → Keychain
/// Token 不再以明文保存到 SQLite，避免 root 设备被直接读取。
/// </summary>
public interface ISecureStorage
{
    /// <summary>读取指定 key 的值。不存在返回 null。</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>写入/覆盖指定 key 的值。value 为 null 等同于 Delete。</summary>
    Task SetAsync(string key, string? value, CancellationToken ct = default);

    /// <summary>删除指定 key（不存在不报错）。</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>ISecureStorage 中使用的固定 key 常量，避免拼写错误。</summary>
public static class SecureStorageKeys
{
    /// <summary>AccessToken（短期 JWT，过期后用 RefreshToken 续期）。</summary>
    public const string AccessToken = "access_token";

    /// <summary>RefreshToken（长期 Token，用于换取新的 AccessToken；服务端只存 Hash）。</summary>
    public const string RefreshToken = "refresh_token";
}
