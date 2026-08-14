using System.Security.Cryptography;
using System.Text;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services.Storage;

/// <summary>
/// Windows DPAPI 安全存储实现：使用 ProtectedData.Protect 加密敏感数据后写入本地文件。
/// 适用于 Windows 桌面调试平台。Android 平台由 AndroidSecureStorage（基于 Keystore）覆盖。
///
/// 存储方式：每个 key 对应一个文件，文件名为 key 的 Base64 编码（避免路径非法字符），
/// 文件内容为 ProtectedData.Protect 加密后的字节流。
///
/// 安全性：
///   - DPAPI 加密密钥由 Windows 用户登录凭据派生，仅在当前用户会话下可解密
///   - 其他用户/其他机器无法解密
///   - 非 root 设备无法直接读取明文
/// </summary>
public sealed class DpapiSecureStorage : ISecureStorage
{
    private static readonly string StorageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChildNotes", "secure");

    private readonly object _lock = new();

    public DpapiSecureStorage()
    {
        try { Directory.CreateDirectory(StorageDir); }
        catch (Exception ex) { DevLogger.Log("SecureStorage", $"CreateDirectory failed: {ex.Message}"); }
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var path = GetFilePath(key);
            if (!File.Exists(path)) return Task.FromResult<string?>(null);

            byte[] cipher;
            lock (_lock)
            {
                ct.ThrowIfCancellationRequested();
                cipher = File.ReadAllBytes(path);
            }

            // DPAPI 解密：使用 CurrentUser 范围，密钥与 Windows 用户绑定
            var plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex)
        {
            DevLogger.Log("SecureStorage", $"GetAsync({key}) failed: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        try
        {
            var path = GetFilePath(key);
            if (value is null)
            {
                if (File.Exists(path)) File.Delete(path);
                return Task.CompletedTask;
            }

            var plain = Encoding.UTF8.GetBytes(value);
            var cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            lock (_lock)
            {
                ct.ThrowIfCancellationRequested();
                File.WriteAllBytes(path, cipher);
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("SecureStorage", $"SetAsync({key}) failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var path = GetFilePath(key);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            DevLogger.Log("SecureStorage", $"DeleteAsync({key}) failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    /// <summary>获取 key 对应的文件路径。文件名为 key 的 Base64 编码以避免路径非法字符。</summary>
    private static string GetFilePath(string key)
    {
        // Base64 中的 / 会破坏路径结构，替换为下划线
        var safeName = Convert.ToBase64String(Encoding.UTF8.GetBytes(key)).Replace('/', '_');
        return Path.Combine(StorageDir, safeName + ".bin");
    }
}
