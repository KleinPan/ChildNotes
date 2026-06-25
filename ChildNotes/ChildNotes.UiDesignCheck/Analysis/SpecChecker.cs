using Avalonia;
using ChildNotes.UiDesignCheck.Spec;
using ChildNotes.UiDesignCheck.Reporting;

namespace ChildNotes.UiDesignCheck.Analysis;

public sealed class SpecChecker
{
    private readonly DesignSpec _spec;

    public SpecChecker(DesignSpec spec)
    {
        _spec = spec;
    }

    public List<Violation> Check(ElementInfo root, Func<Rect, SampledColor> sample)
    {
        var violations = new List<Violation>();
        var all = VisualTreeExtractor.Flatten(root)
            .Where(e => e.IsVisible && e.Width > 0 && e.Height > 0)
            .ToList();

        var topLevel = new HashSet<ElementInfo>(root.Children);

        foreach (var el in all)
        {
            if (el != root)
                CheckColors(el, sample, violations);
            CheckCornerRadius(el, violations);
            CheckTypography(el, violations);
            CheckSpacing(el, violations);
            CheckTouchTarget(el, violations);
            if (topLevel.Contains(el) || el == root)
                CheckEdgeMargin(el, violations);
        }

        CheckLayout(root, all, violations);
        return violations;
    }

    private void CheckColors(ElementInfo el, Func<Rect, SampledColor> sample, List<Violation> violations)
    {
        if (string.IsNullOrEmpty(el.BackgroundHex) || !el.BackgroundHex.StartsWith("#")) return;

        var declared = ParseHex(el.BackgroundHex);
        if (declared.a < 0xFF) return;

        if (el.Width < 6 || el.Height < 6) return;

        if (IsTemplatePart(el)) return;

        var cr = el.CornerRadius ?? 0;
        var minDim = Math.Min(el.Width, el.Height);
        var isCircular = cr >= minDim / 2 - 1;
        var isSmallChip = el.Width <= 60 && el.Height <= 30 && cr > 0;
        var isSmallIcon = minDim <= 28 && el.Width <= 60 && el.Height <= 60;

        SampledColor actual;
        if (isCircular)
        {
            var off = minDim * 0.3;
            actual = sample(new Rect(el.Bounds.Center.X - 3, el.Bounds.Center.Y - off - 3, 6, 6));
        }
        else if (isSmallChip || isSmallIcon)
        {
            var edgeOff = Math.Max(2, cr / 2);
            var cx = el.Bounds.X + edgeOff;
            var cy = el.Bounds.Center.Y - 1.5;
            actual = sample(new Rect(cx, cy, 3, 3));
        }
        else
        {
            actual = sample(el.Bounds);
        }
        var dist = PixelSampler.ColorDistance(actual, declared);
        if (dist > _spec.ColorTolerance)
        {
            var matched = ClosestSpecColor(actual);
            violations.Add(new Violation
            {
                Severity = dist > _spec.ColorTolerance * 3 ? Severity.Error : Severity.Warn,
                Category = "颜色方案",
                Element = Describe(el),
                Location = Loc(el),
                Rule = "背景色渲染与声明值一致",
                Expected = el.BackgroundHex,
                Actual = actual.Hex,
                Deviation = $"{dist:F1} (容差 {_spec.ColorTolerance})",
                Suggestion = matched is not null
                    ? $"渲染色接近规范 {matched.Role}({matched.Hex})，若有意为之可忽略；否则检查样式覆盖或资源绑定。"
                    : $"渲染色与声明背景色偏差过大，检查父级透明度/叠加或样式未生效。"
            });
        }
    }

    private void CheckCornerRadius(ElementInfo el, List<Violation> violations)
    {
        if (!el.CornerRadius.HasValue || el.CornerRadius == 0) return;
        if (IsTemplatePart(el)) return;
        var cr = el.CornerRadius.Value;
        var isCard = HasClass(el, "weui-card", "baby-card", "data-cell", "ai-status-card");
        var isBtn = el.Type == "Button" || HasClass(el, "weui-btn");
        var isChip = HasClass(el, "chip");

        double? expected = null;
        string role = "";
        if (isCard) { expected = _spec.ControlSizes.CardCornerRadius; role = "卡片圆角"; }
        else if (isBtn) { expected = _spec.ControlSizes.BtnCornerRadius; role = "按钮圆角"; }
        else if (isChip) { expected = _spec.ControlSizes.ChipCornerRadius; role = "标签圆角"; }

        if (expected.HasValue && Math.Abs(cr - expected.Value) > _spec.SizeTolerancePx)
        {
            violations.Add(new Violation
            {
                Severity = Severity.Warn,
                Category = "控件尺寸",
                Element = Describe(el),
                Location = Loc(el),
                Rule = $"{role}应符合规范",
                Expected = $"{expected.Value:F0}px",
                Actual = $"{cr:F0}px",
                Deviation = $"{cr - expected.Value:+0.##;-0.##;0}px",
                Suggestion = $"将 CornerRadius 调整为 {expected.Value:F0}。"
            });
        }
    }

    private void CheckTypography(ElementInfo el, List<Violation> violations)
    {
        if (!el.FontSize.HasValue || string.IsNullOrEmpty(el.Text)) return;
        if (IsEmojiOrIcon(el.Text)) return;
        var fs = el.FontSize.Value;
        var tol = Math.Max(_spec.SizeTolerancePx, fs * _spec.SizeToleranceRatio);

        var allowed = _spec.Typography.AllTiers()
            .Concat(new[] { _spec.ControlSizes.MiniBtnFontSize })
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        foreach (var tier in allowed)
        {
            if (Math.Abs(fs - tier) < tol) return;
        }

        var nearest = allowed.MinBy(a => Math.Abs(a - fs));
        if (Math.Abs(nearest - fs) > tol)
        {
            violations.Add(new Violation
            {
                Severity = Severity.Info,
                Category = "字体样式",
                Element = Describe(el),
                Location = Loc(el),
                Rule = "字号应取规范档位之一",
                Expected = $"档位: {string.Join("/", allowed.Select(x => x.ToString("0")))}",
                Actual = $"{fs:F1}px",
                Deviation = $"{fs - nearest:+0.##;-0.##;0}px",
                Suggestion = $"最接近的规范字号为 {nearest:0}px，建议对齐。"
            });
        }
    }

    private void CheckSpacing(ElementInfo el, List<Violation> violations)
    {
        if (el.Padding.HasValue)
        {
            var p = el.Padding.Value;
            if (HasClass(el, "weui-card"))
            {
                CheckThickness(p, _spec.Spacing.CardPadding, _spec.Spacing.CardPadding, "卡片内边距", el, violations);
            }
            else if (HasClass(el, "weui-cell"))
            {
                CheckThickness(p, _spec.Spacing.CellPaddingX, _spec.Spacing.CellPaddingY, "单元格内边距", el, violations);
            }
            else if (HasClass(el, "chip"))
            {
                CheckThickness(p, _spec.Spacing.ChipPaddingX, _spec.Spacing.ChipPaddingY, "标签内边距", el, violations);
            }
        }

        if (el.Margin.HasValue && HasClass(el, "weui-card"))
        {
            var m = el.Margin.Value;
            if (Math.Abs(m.Left - _spec.Spacing.CardMargin) > _spec.SizeTolerancePx
                || Math.Abs(m.Right - _spec.Spacing.CardMargin) > _spec.SizeTolerancePx)
            {
                violations.Add(new Violation
                {
                    Severity = Severity.Warn,
                    Category = "间距比例",
                    Element = Describe(el),
                    Location = Loc(el),
                    Rule = "卡片左右外边距应一致",
                    Expected = $"{_spec.Spacing.CardMargin}px",
                    Actual = $"L={m.Left} R={m.Right}",
                    Deviation = $"{m.Left - _spec.Spacing.CardMargin:+0.##;-0.##;0}px",
                    Suggestion = $"将卡片左右 Margin 调整为 {_spec.Spacing.CardMargin}。"
                });
            }
        }
    }

    private void CheckThickness(Thickness t, double expX, double expY, string role, ElementInfo el, List<Violation> violations)
    {
        var tol = _spec.SizeTolerancePx;
        if (Math.Abs(t.Left - expX) > tol || Math.Abs(t.Right - expX) > tol
            || Math.Abs(t.Top - expY) > tol || Math.Abs(t.Bottom - expY) > tol)
        {
            violations.Add(new Violation
            {
                Severity = Severity.Warn,
                Category = "间距比例",
                Element = Describe(el),
                Location = Loc(el),
                Rule = $"{role}应符合规范",
                Expected = $"{expX},{expY}",
                Actual = $"{t.Left},{t.Top},{t.Right},{t.Bottom}",
                Deviation = $"LΔ{t.Left - expX:+0.##;-0.##;0} TΔ{t.Top - expY:+0.##;-0.##;0}",
                Suggestion = $"将 Padding 调整为 {expX},{expY}。"
            });
        }
    }

    private void CheckTouchTarget(ElementInfo el, List<Violation> violations)
    {
        if (el.Type != "Button" && !HasClass(el, "tab-item")) return;
        if (IsTemplatePart(el)) return;
        var min = _spec.ControlSizes.MinTouchTarget;
        if (el.Width < min - _spec.SizeTolerancePx || el.Height < min - _spec.SizeTolerancePx)
        {
            violations.Add(new Violation
            {
                Severity = Severity.Warn,
                Category = "控件尺寸",
                Element = Describe(el),
                Location = Loc(el),
                Rule = "可点击区域不小于最小触控目标",
                Expected = $"{min}x{min}px",
                Actual = $"{el.Width:F0}x{el.Height:F0}px",
                Deviation = $"WΔ{el.Width - min:+0.##;-0.##;0} HΔ{el.Height - min:+0.##;-0.##;0}",
                Suggestion = $"增大 Padding 使点击区域达到 {min}x{min}。"
            });
        }
    }

    private void CheckEdgeMargin(ElementInfo el, List<Violation> violations)
    {
        if (el.Bounds.Width < _spec.ViewportWidth * 0.6) return;
        if (el.Bounds.X < _spec.Layout.MinEdgeMargin - _spec.SizeTolerancePx
            && el.Bounds.X > 0.5)
        {
            violations.Add(new Violation
            {
                Severity = Severity.Info,
                Category = "布局结构",
                Element = Describe(el),
                Location = Loc(el),
                Rule = "通栏元素应保留最小边距",
                Expected = $"≥{_spec.Layout.MinEdgeMargin}px",
                Actual = $"X={el.Bounds.X:F1}px",
                Deviation = $"{el.Bounds.X - _spec.Layout.MinEdgeMargin:+0.##;-0.##;0}px",
                Suggestion = $"为元素增加左右 Margin ≥ {_spec.Layout.MinEdgeMargin}。"
            });
        }
    }

    private void CheckLayout(ElementInfo root, List<ElementInfo> all, List<Violation> violations)
    {
        var tabBars = all.Where(e => HasClass(e, "tab-bar")).ToList();
        foreach (var tb in tabBars)
        {
            var expectedBottom = _spec.ViewportHeight;
            var actualBottom = tb.Bounds.Bottom;
            if (Math.Abs(actualBottom - expectedBottom) > _spec.SizeTolerancePx)
            {
                violations.Add(new Violation
                {
                    Severity = Severity.Warn,
                    Category = "布局结构",
                    Element = "底部 TabBar",
                    Location = Loc(tb),
                    Rule = "TabBar 应贴底显示",
                    Expected = $"底部Y={expectedBottom}",
                    Actual = $"底部Y={actualBottom:F1}",
                    Deviation = $"{actualBottom - expectedBottom:+0.##;-0.##;0}px",
                    Suggestion = "检查 TabBar 所在 Grid 行是否为 Auto 且位于最后一行。"
                });
            }
        }

        var offscreen = all.Where(e => !IsTemplatePart(e)
                                    && e.Bounds.Right > _spec.ViewportWidth + 1
                                    && e.Width > 6
                                    && e.Bounds.X < -1).ToList();
        foreach (var e in offscreen.Take(5))
        {
            violations.Add(new Violation
            {
                Severity = Severity.Error,
                Category = "布局结构",
                Element = Describe(e),
                Location = Loc(e),
                Rule = "元素不应溢出可视区域",
                Expected = "在视口内",
                Actual = $"({e.Bounds.X:F0},{e.Bounds.Y:F0},{e.Bounds.Right:F0},{e.Bounds.Bottom:F0})",
                Deviation = "溢出",
                Suggestion = "检查布局约束或 ScrollViewer 是否禁用了水平滚动。"
            });
        }
    }

    private SpecColor? ClosestSpecColor(SampledColor c)
    {
        SpecColor? best = null; double bestDist = double.MaxValue;
        foreach (var sc in _spec.Colors.All())
        {
            var (r, g, b, a) = sc.ToRgba();
            var d = Math.Sqrt(Math.Pow(c.R - r, 2) + Math.Pow(c.G - g, 2) + Math.Pow(c.B - b, 2));
            if (d < bestDist) { bestDist = d; best = sc; }
        }
        return bestDist <= _spec.ColorTolerance * 2 ? best : null;
    }

    private static (byte r, byte g, byte b, byte a) ParseHex(string hex)
    {
        var h = hex.AsSpan().TrimStart('#');
        byte r = byte.Parse(h.Slice(0, 2), System.Globalization.NumberStyles.HexNumber);
        byte g = byte.Parse(h.Slice(2, 2), System.Globalization.NumberStyles.HexNumber);
        byte b = byte.Parse(h.Slice(4, 2), System.Globalization.NumberStyles.HexNumber);
        byte a = h.Length >= 8 ? byte.Parse(h.Slice(6, 2), System.Globalization.NumberStyles.HexNumber) : (byte)0xFF;
        return (r, g, b, a);
    }

    private static bool HasClass(ElementInfo e, params string[] names)
        => e.Classes is not null && names.Any(n => e.Classes.Split(' ').Contains(n));

    private static bool IsTemplatePart(ElementInfo e)
        => !string.IsNullOrEmpty(e.Name) && e.Name.StartsWith("PART_");

    private static bool IsEmojiOrIcon(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        foreach (var ch in trimmed)
        {
            var code = (int)ch;
            if (code >= 0x1F000) return true;
            if (code >= 0x2600 && code <= 0x27BF) return true;
            if (code >= 0x2190 && code <= 0x21FF) return true;
            if (code >= 0xFE00 && code <= 0xFE0F) return true;
            if (code >= 0xD800 && code <= 0xDFFF) return true;
        }
        if (trimmed.Length <= 2)
        {
            foreach (var ch in trimmed)
            {
                var code = (int)ch;
                if (code >= 0x2000 && code <= 0x27BF) return true;
            }
        }
        return false;
    }

    private static string Describe(ElementInfo e)
    {
        var parts = new List<string> { e.Type };
        if (!string.IsNullOrEmpty(e.Name)) parts.Add($"#{e.Name}");
        if (!string.IsNullOrEmpty(e.Classes)) parts.Add($".{e.Classes.Replace(' ', '.')}");
        if (!string.IsNullOrEmpty(e.Text)) parts.Add($"\"{Truncate(e.Text, 16)}\"");
        return string.Join(" ", parts);
    }

    private static string Loc(ElementInfo e) => $"({e.X:F0},{e.Y:F0}) {e.Width:F0}x{e.Height:F0}";

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
