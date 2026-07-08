using System.IO;
using System.Text.Json;

namespace ChildNotes.Services;

/// <summary>
/// 隐私协议同意状态持久化。
/// 文件路径：LocalApplicationData/ChildNotes/privacy-consent.json
///
/// 触发再次弹窗的条件：
/// - 未同意过（Agreed=false）
/// - 协议版本升级（Version != CurrentVersion）
/// </summary>
public sealed class PrivacyConsentConfig
{
    /// <summary>是否已同意隐私协议。</summary>
    public bool Agreed { get; set; }

    /// <summary>同意时间（UTC）。</summary>
    public DateTime? AgreedAt { get; set; }

    /// <summary>同意时记录的协议版本号。</summary>
    public string? Version { get; set; }
}

/// <summary>
/// 隐私协议加载与持久化的静态门面。
/// </summary>
public static class PrivacyConsent
{
    /// <summary>当前协议版本号。每次协议内容更新时递增，已同意用户会重新看到弹窗。</summary>
    public const string CurrentVersion = "2026.07.001";

    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChildNotes");

    private static readonly string FilePath = Path.Combine(AppDir, "privacy-consent.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>加载当前同意状态。文件不存在或解析失败时返回默认值（未同意）。</summary>
    public static PrivacyConsentConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new PrivacyConsentConfig();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<PrivacyConsentConfig>(json, JsonOptions) ?? new PrivacyConsentConfig();
        }
        catch
        {
            return new PrivacyConsentConfig();
        }
    }

    /// <summary>标记用户已同意当前版本的隐私协议。</summary>
    public static void Agree()
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            var cfg = new PrivacyConsentConfig
            {
                Agreed = true,
                AgreedAt = DateTime.UtcNow,
                Version = CurrentVersion
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(cfg, JsonOptions));
        }
        catch
        {
            // 非致命：写入失败时下次启动会再次弹窗，不影响用户使用。
        }
    }

    /// <summary>是否需要展示隐私协议弹窗（未同意 或 版本不一致）。</summary>
    public static bool ShouldShow()
    {
        var c = Load();
        return !c.Agreed || c.Version != CurrentVersion;
    }
}
