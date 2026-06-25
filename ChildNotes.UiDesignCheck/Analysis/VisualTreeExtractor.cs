using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ChildNotes.UiDesignCheck.Analysis;

public sealed class ElementInfo
{
    public string Type { get; set; } = "";
    public string? Name { get; set; }
    public string? Classes { get; set; }
    public string? Text { get; set; }
    public Rect Bounds { get; set; }
    public double X => Bounds.X;
    public double Y => Bounds.Y;
    public double Width => Bounds.Width;
    public double Height => Bounds.Height;

    public string? BackgroundHex { get; set; }
    public string? ForegroundHex { get; set; }
    public string? BorderBrushHex { get; set; }
    public double BorderThickness { get; set; }
    public double? CornerRadius { get; set; }
    public double? FontSize { get; set; }
    public string? FontWeight { get; set; }
    public string? FontFamily { get; set; }
    public Thickness? Padding { get; set; }
    public Thickness? Margin { get; set; }
    public double? Opacity { get; set; }
    public bool IsVisible { get; set; }

    public List<ElementInfo> Children { get; } = new();
}

public static class VisualTreeExtractor
{
    public static ElementInfo Extract(Control root)
    {
        return Build(root, root);
    }

    private static ElementInfo Build(Control control, Control root)
    {
        var topLeft = control.TranslatePoint(new Point(0, 0), root) ?? new Point(0, 0);
        var bounds = new Rect(topLeft.X, topLeft.Y, control.Bounds.Width, control.Bounds.Height);

        var info = new ElementInfo
        {
            Type = control.GetType().Name,
            Name = control.Name,
            Classes = control.Classes.Count > 0 ? string.Join(" ", control.Classes) : null,
            Bounds = bounds,
            IsVisible = control.IsVisible,
            Opacity = control.Opacity,
        };

        if (control is TextBlock tb)
        {
            info.Text = tb.Text;
            info.FontSize = tb.FontSize;
            info.FontWeight = tb.FontWeight.ToString();
            info.FontFamily = tb.FontFamily?.Name;
            info.ForegroundHex = BrushToHex(tb.Foreground);
        }
        else if (control is ContentControl cc && cc.Content is string s)
        {
            info.Text = s;
        }

        if (control is TemplatedControl tc)
        {
            info.BackgroundHex ??= BrushToHex(tc.Background);
            info.ForegroundHex ??= BrushToHex(tc.Foreground);
            info.BorderBrushHex ??= BrushToHex(tc.BorderBrush);
            info.BorderThickness = tc.BorderThickness.Top;
            info.FontSize ??= tc.FontSize;
            info.FontWeight ??= tc.FontWeight.ToString();
            info.FontFamily ??= tc.FontFamily?.Name;
            if (tc.CornerRadius != default) info.CornerRadius ??= tc.CornerRadius.TopLeft;
            info.Padding ??= tc.Padding;
        }

        if (control is Border border)
        {
            info.BackgroundHex ??= BrushToHex(border.Background);
            info.BorderBrushHex ??= BrushToHex(border.BorderBrush);
            info.BorderThickness = Math.Max(border.BorderThickness.Top, border.BorderThickness.Left);
            info.CornerRadius ??= border.CornerRadius.TopLeft;
            info.Padding ??= border.Padding;
        }
        info.Margin = control.Margin;

        foreach (var child in control.GetVisualChildren().OfType<Control>())
        {
            info.Children.Add(Build(child, root));
        }

        return info;
    }

    public static IEnumerable<ElementInfo> Flatten(ElementInfo root)
    {
        yield return root;
        foreach (var c in root.Children)
        {
            foreach (var d in Flatten(c)) yield return d;
        }
    }

    private static string? BrushToHex(IBrush? brush)
    {
        if (brush is null) return null;
        if (brush is ISolidColorBrush sb)
        {
            var c = sb.Color;
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
        }
        return brush.GetType().Name;
    }
}
