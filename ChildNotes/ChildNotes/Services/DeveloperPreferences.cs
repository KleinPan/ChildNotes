using System.IO;
using System.Text.Json;

namespace ChildNotes.Services;

/// <summary>
/// 开发者选项偏好设置：持久化存储到 JSON 文件。
/// 文件路径：LocalApplicationData/ChildNotes/developer-options.json
/// </summary>
public sealed class DeveloperOptionsConfig
{
    /// <summary>是否显示日志悬浮层（默认 false，避免影响普通用户）。</summary>
    public bool ShowDevLogOverlay { get; set; }
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

    private static readonly DeveloperOptionsConfig Default = new()
    {
        ShowDevLogOverlay = false
    };

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
        ShowDevLogOverlay = src.ShowDevLogOverlay
    };
}
