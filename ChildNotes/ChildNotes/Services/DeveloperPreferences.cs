using System.IO;
using System.Text.Json;

namespace ChildNotes.Services;

/// <summary>
/// 开发者选项偏好设置：持久化存储到 JSON 文件。
/// 文件路径：LocalApplicationData/ChildNotes/developer-options.json
///
/// 注意：日志悬浮层（DevLogOverlay）已移除，日志改写入文件（ReleaseLogger），
/// 通过开发者选项页直接查看实时日志并支持导出。
/// 当前 EnableAnimations 字段被 AnimationService 实际使用（App.axaml.cs 中读取）。
/// </summary>
public sealed class DeveloperOptionsConfig
{
    /// <summary>是否启用动画效果（默认 true）。关闭后所有弹窗、过渡、反馈动画将禁用。</summary>
    public bool EnableAnimations { get; set; } = true;
}

/// <summary>
/// 开发者选项配置的加载与持久化。
/// </summary>
public static class DeveloperPreferences
{
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChildNotes");

    public static string ConfigFilePath { get; } = Path.Combine(AppDir, "developer-options.json");

    private static readonly DeveloperOptionsConfig Default = new();

    /// <summary>加载配置；文件不存在或解析失败时返回默认值。</summary>
    public static DeveloperOptionsConfig Load()
    {
        if (!File.Exists(ConfigFilePath)) return Clone(Default);
        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            var cfg = JsonSerializer.Deserialize<DeveloperOptionsConfig>(json, JsonOptions);
            return cfg ?? Clone(Default);
        }
        catch
        {
            return Clone(Default);
        }
    }

    /// <summary>持久化配置到文件。</summary>
    public static void Save(DeveloperOptionsConfig cfg)
    {
        Directory.CreateDirectory(AppDir);
        var json = JsonSerializer.Serialize(cfg, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static DeveloperOptionsConfig Clone(DeveloperOptionsConfig src) => new()
    {
        EnableAnimations = src.EnableAnimations
    };
}
