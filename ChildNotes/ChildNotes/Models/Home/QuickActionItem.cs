namespace ChildNotes.Models.Home;

public sealed class QuickActionItem
{
    public string Icon { get; }
    public string Label { get; }
    public string Type { get; }
    /// <summary>图标背景色（对齐原版 quick-actions 配色）</summary>
    public string IconBg { get; }
    /// <summary>扇形菜单中的水平偏移（相对 + 按钮中心，px）</summary>
    public double OffsetX { get; }
    /// <summary>扇形菜单中的垂直偏移（相对 + 按钮中心，px）</summary>
    public double OffsetY { get; }
    public QuickActionItem(string icon, string label, string type, string iconBg = "#F7F7F7", double offsetX = 0, double offsetY = 0)
    {
        Icon = icon; Label = label; Type = type; IconBg = iconBg;
        OffsetX = offsetX; OffsetY = offsetY;
    }
}
