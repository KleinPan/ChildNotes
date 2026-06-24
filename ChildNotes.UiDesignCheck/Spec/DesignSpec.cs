using System.Text.Json.Serialization;

namespace ChildNotes.UiDesignCheck.Spec;

public sealed class DesignSpec
{
    public string Name { get; set; } = "WeUI Design Spec";
    public int ViewportWidth { get; set; } = 390;
    public int ViewportHeight { get; set; } = 844;

    public ColorPalette Colors { get; set; } = new();
    public SpacingRules Spacing { get; set; } = new();
    public TypographyRules Typography { get; set; } = new();
    public ControlSizeRules ControlSizes { get; set; } = new();
    public LayoutRules Layout { get; set; } = new();

    public double ColorTolerance { get; set; } = 8.0;
    public double SizeTolerancePx { get; set; } = 1.5;
    public double SizeToleranceRatio { get; set; } = 0.04;
}

public sealed class ColorPalette
{
    public SpecColor Bg0 { get; set; } = new("#EDEDED", "页面底色");
    public SpecColor Bg1 { get; set; } = new("#F7F7F7", "次级底色");
    public SpecColor Bg2 { get; set; } = new("#FFFFFF", "卡片底色");
    public SpecColor Fg0 { get; set; } = new("#000000", "主文字");
    public SpecColor Fg1 { get; set; } = new("#000000", "次文字(56%)", alpha: 0x8C);
    public SpecColor Fg2 { get; set; } = new("#000000", "辅助文字(30%)", alpha: 0x4D);
    public SpecColor Green { get; set; } = new("#07C160", "主色/成功");
    public SpecColor Red { get; set; } = new("#FA5151", "警告/危险");
    public SpecColor Orange { get; set; } = new("#FA9D3B", "提示橙");
    public SpecColor Yellow { get; set; } = new("#FFC300", "高亮黄");
    public SpecColor Blue { get; set; } = new("#10AEFF", "链接蓝");
    public SpecColor Purple { get; set; } = new("#6467F0", "紫色");
    public SpecColor Separator { get; set; } = new("#000000", "分隔线(10%)", alpha: 0x1A);

    public IEnumerable<SpecColor> All()
    {
        yield return Bg0; yield return Bg1; yield return Bg2;
        yield return Fg0; yield return Fg1; yield return Fg2;
        yield return Green; yield return Red; yield return Orange;
        yield return Yellow; yield return Blue; yield return Purple;
        yield return Separator;
    }
}

public sealed class SpecColor
{
    public string Hex { get; set; } = "#000000";
    public string Role { get; set; } = "";
    public byte Alpha { get; set; } = 0xFF;

    public SpecColor() { }

    [JsonConstructor]
    public SpecColor(string hex, string role) : this(hex, role, 0xFF) { }

    public SpecColor(string hex, string role, byte alpha)
    {
        Hex = hex; Role = role; Alpha = alpha;
    }

    public (byte r, byte g, byte b, byte a) ToRgba()
    {
        var h = Hex.AsSpan().TrimStart('#');
        byte r = byte.Parse(h.Slice(0, 2), System.Globalization.NumberStyles.HexNumber);
        byte g = byte.Parse(h.Slice(2, 2), System.Globalization.NumberStyles.HexNumber);
        byte b = byte.Parse(h.Slice(4, 2), System.Globalization.NumberStyles.HexNumber);
        return (r, g, b, Alpha);
    }
}

public sealed class SpacingRules
{
    public double CardMargin { get; set; } = 12;
    public double CardPadding { get; set; } = 14;
    public double CellPaddingX { get; set; } = 14;
    public double CellPaddingY { get; set; } = 12;
    public double SectionTitleMarginX { get; set; } = 16;
    public double ChipPaddingX { get; set; } = 12;
    public double ChipPaddingY { get; set; } = 5;
    public double BtnPaddingX { get; set; } = 16;
    public double BtnPaddingY { get; set; } = 12;
    public double TabBarHeight { get; set; } = 56;
}

public sealed class TypographyRules
{
    public string FontFamily { get; set; } = "Segoe UI";
    public double TitleFontSize { get; set; } = 18;
    public double SectionTitleFontSize { get; set; } = 15;
    public double BodyFontSize { get; set; } = 15;
    public double CaptionFontSize { get; set; } = 13;
    public double SmallFontSize { get; set; } = 11;
    public double TabIconFontSize { get; set; } = 22;
    public double TabTextFontSize { get; set; } = 10;
}

public sealed class ControlSizeRules
{
    public double CardCornerRadius { get; set; } = 8;
    public double BtnCornerRadius { get; set; } = 6;
    public double ChipCornerRadius { get; set; } = 14;
    public double AvatarSmall { get; set; } = 56;
    public double AvatarLarge { get; set; } = 84;
    public double MinTouchTarget { get; set; } = 44;
    public double MiniBtnFontSize { get; set; } = 12;
}

public sealed class LayoutRules
{
    public double MinEdgeMargin { get; set; } = 12;
    public double MaxContentWidth { get; set; } = 390;
    public double TabBarExpectedAtBottom { get; set; } = 0;
}
