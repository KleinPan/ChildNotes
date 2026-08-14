using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Security;
using Android.Security.Keystore;
using ChildNotes.Services.Storage;
using Javax.Crypto;
using Javax.Crypto.Spec; // GCMParameterSpec 所在命名空间

namespace ChildNotes.Android.Services;

/// <summary>
/// Android Keystore 安全存储实现：用 Android KeyStore 系统生成 AES-256-GCM 密钥，
/// 加密敏感数据后写入 SharedPreferences。
///
/// 安全性：
///   - 密钥由 Android KeyStore 系统生成并保管，不可导出（硬件级隔离，Secure Enclave / TEE）
///   - 密钥绑定应用签名（不需要用户解锁，应用卸载即清除密钥）
///   - 加密数据写入应用私有 SharedPreferences（/data/data/&lt;pkg&gt;/shared_prefs）
///   - 非 root 设备无法直接读取明文
///
/// 设计要点：
///   - 密钥别名固定（childnotes_secure_storage），首次使用时自动生成
///   - 密钥不要求用户认证（ShouldRequireAuthentication=false）：应用前台即可解密
///   - 加密结果包含 IV + 密文（Base64），解密时拆分 IV 与密文
/// </summary>
public sealed class AndroidSecureStorage : ISecureStorage
{
    private const string KeyStoreAlias = "childnotes_secure_storage";
    private const string KeyStoreType = "AndroidKeyStore";
    private const string PrefsName = "childnotes_secure_prefs";
    private const string CipherTransformation = "AES/GCM/NoPadding";
    private const int GcmIvLength = 12;
    private const int GcmTagLength = 128; // bits

    private readonly Context _context;

    public AndroidSecureStorage(Context context)
    {
        _context = context;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var prefs = _context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var stored = prefs?.GetString(key, null);
            if (string.IsNullOrEmpty(stored)) return Task.FromResult<string?>(null);

            var cipher = GetDecryptCipher();
            var bytes = Convert.FromBase64String(stored);
            // 前 12 字节为 IV，后面是密文（GCM 模式下 auth tag 由 cipher.DoFinal 自动处理/校验）
            var iv = new byte[GcmIvLength];
            var cipherText = new byte[bytes.Length - GcmIvLength];
            Buffer.BlockCopy(bytes, 0, iv, 0, GcmIvLength);
            Buffer.BlockCopy(bytes, GcmIvLength, cipherText, 0, cipherText.Length);
            // GCM 必须用 GCMParameterSpec（不能用 IvParameterSpec）：
            //   - Javax.Crypto 对 AES/GCM/NoPadding 强制要求 GCMParameterSpec
            //   - 用 IvParameterSpec 会抛 InvalidAlgorithmParameterException
            //   - 否则会出现 SetAsync 成功 → 重启 → GetAsync 解密失败 → Token 丢失 → 用户被登出
            // GcmTagLength=128 是 GCM 标准 tag 长度（16 字节），与加密时默认值对齐
            cipher.Init(CipherMode.DecryptMode, GetOrCreateKey(), new GCMParameterSpec(GcmTagLength, iv));

            var plain = cipher.DoFinal(cipherText);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("SecureStorage", $"GetAsync({key}) failed: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        try
        {
            var prefs = _context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            if (value is null)
            {
                prefs?.Edit()?.Remove(key)?.Apply();
                return Task.CompletedTask;
            }

            var cipher = GetEncryptCipher();
            cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
            var iv = cipher.IV;
            var plain = Encoding.UTF8.GetBytes(value);
            var cipherText = cipher.DoFinal(plain);

            // 合并 IV + 密文（Base64）
            var combined = new byte[iv.Length + cipherText.Length];
            Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
            Buffer.BlockCopy(cipherText, 0, combined, iv.Length, cipherText.Length);

            prefs?.Edit()?.PutString(key, Convert.ToBase64String(combined))?.Apply();
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("SecureStorage", $"SetAsync({key}) failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var prefs = _context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            prefs?.Edit()?.Remove(key)?.Apply();
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("SecureStorage", $"DeleteAsync({key}) failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private static SecretKey GetOrCreateKey()
    {
        var ks = KeyStore.GetInstance(KeyStoreType);
        ks.Load(null); // 初始化，不读取已有 keystore 条目

        if (ks.ContainsAlias(KeyStoreAlias))
        {
            var entry = (KeyStore.SecretKeyEntry?)ks.GetEntry(KeyStoreAlias, null);
            if (entry is not null) return entry.SecretKey;
        }

        // 密钥不存在，生成新密钥
        // .NET Android 绑定：KeyStorePurpose 枚举（非 Java 的 KeyProperties.PURPOSE_*）
        var gen = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeyStoreType);
        var spec = new KeyGenParameterSpec.Builder(KeyStoreAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            // 不要求用户认证：应用前台即可解密（避免每次同步都需要生物识别）
            .SetUserAuthenticationRequired(false)
            // 应用卸载时自动删除密钥（Android 6.0+ 默认行为，显式声明以增强可读性）
            .Build();
        gen.Init(spec);
        return gen.GenerateKey();
    }

    private static Cipher GetEncryptCipher() => Cipher.GetInstance(CipherTransformation);
    private static Cipher GetDecryptCipher() => Cipher.GetInstance(CipherTransformation);
}
