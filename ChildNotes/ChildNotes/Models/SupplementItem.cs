namespace ChildNotes.Models;

/// <summary>
/// 用户自定义的补充剂/用药项（对应 user_supplement_item 表）。
/// 与系统默认项区分，按 type 分别存储 supplement / medicine 两类。
/// </summary>
public sealed class SupplementItem
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    /// <summary>"supplement"（补充剂）或 "medicine"（用药）</summary>
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
