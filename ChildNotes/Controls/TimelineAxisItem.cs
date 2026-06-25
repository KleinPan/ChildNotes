using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ChildNotes.Controls;

/// <summary>
/// 时间轴单项的左轴列：竖线段 + 圆点。
/// 在 ItemsControl 的 ItemTemplate 中与内容组合使用。
/// 竖线段通过负 Margin（-HalfSpacing）向上下延伸，相邻项拼接形成连贯竖线。
/// </summary>
/// <remarks>
/// 用法（在 ItemTemplate 内）：
/// &lt;Grid ColumnDefinitions="32,*"&gt;
///     &lt;ctrl:TimelineAxisItem Grid.Column="0"
///         LineBrush="#FFAB91" DotBrush="#FFAB91"
///         HalfSpacing="12"/&gt;   &lt;!-- HalfSpacing = ItemSpacing/2 --&gt;
///     &lt;StackPanel Grid.Column="1" Margin="8,0,0,0"&gt;...内容...&lt;/StackPanel&gt;
/// &lt;/Grid&gt;
/// </remarks>
public class TimelineAxisItem : Control
{
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<TimelineAxisItem, IBrush?>(nameof(LineBrush), Brushes.Gray);

    public static readonly StyledProperty<double> LineWidthProperty =
        AvaloniaProperty.Register<TimelineAxisItem, double>(nameof(LineWidth), 2);

    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<TimelineAxisItem, IBrush?>(nameof(DotBrush), Brushes.Gray);

    public static readonly StyledProperty<double> DotSizeProperty =
        AvaloniaProperty.Register<TimelineAxisItem, double>(nameof(DotSize), 14);

    public static readonly StyledProperty<IBrush?> DotBorderBrushProperty =
        AvaloniaProperty.Register<TimelineAxisItem, IBrush?>(nameof(DotBorderBrush), Brushes.White);

    public static readonly StyledProperty<double> DotBorderThicknessProperty =
        AvaloniaProperty.Register<TimelineAxisItem, double>(nameof(DotBorderThickness), 3);

    /// <summary>项间距的一半，用于竖线段向上下延伸的距离，保证拼接连贯</summary>
    public static readonly StyledProperty<double> HalfSpacingProperty =
        AvaloniaProperty.Register<TimelineAxisItem, double>(nameof(HalfSpacing), 12);

    /// <summary>圆点垂直偏移（对齐到内容首行）</summary>
    public static readonly StyledProperty<double> DotTopMarginProperty =
        AvaloniaProperty.Register<TimelineAxisItem, double>(nameof(DotTopMargin), 8);

    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public double LineWidth { get => GetValue(LineWidthProperty); set => SetValue(LineWidthProperty, value); }
    public IBrush? DotBrush { get => GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }
    public double DotSize { get => GetValue(DotSizeProperty); set => SetValue(DotSizeProperty, value); }
    public IBrush? DotBorderBrush { get => GetValue(DotBorderBrushProperty); set => SetValue(DotBorderBrushProperty, value); }
    public double DotBorderThickness { get => GetValue(DotBorderThicknessProperty); set => SetValue(DotBorderThicknessProperty, value); }
    public double HalfSpacing { get => GetValue(HalfSpacingProperty); set => SetValue(HalfSpacingProperty, value); }
    public double DotTopMargin { get => GetValue(DotTopMarginProperty); set => SetValue(DotTopMarginProperty, value); }

    static TimelineAxisItem()
    {
        AffectsRender<TimelineAxisItem>(LineBrushProperty, LineWidthProperty, DotBrushProperty,
            DotSizeProperty, DotBorderBrushProperty, DotBorderThicknessProperty);
    }

    public TimelineAxisItem()
    {
        // 允许超出边界绘制（竖线段负 Margin 延伸）
        ClipToBounds = false;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // 宽度 = 圆点直径 + 边框，高度不约束（由父容器决定）
        var w = DotSize + DotBorderThickness * 2 + 4;
        return new Size(w, 0);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0) return;

        var half = bounds.Width / 2;
        var halfSpacing = HalfSpacing;

        // 竖线段：从 (y=-HalfSpacing) 到 (y=Height+HalfSpacing)，水平居中
        if (LineBrush is not null && LineWidth > 0)
        {
            var x = half - LineWidth / 2;
            var lineRect = new Rect(x, -halfSpacing, LineWidth, bounds.Height + halfSpacing * 2);
            context.DrawRectangle(LineBrush, null, lineRect);
        }

        // 圆点：居中，垂直顶部偏移 DotTopMargin
        if (DotBrush is not null && DotSize > 0)
        {
            var dotY = DotTopMargin;
            var dotX = half - DotSize / 2;
            var dotRect = new Rect(dotX, dotY, DotSize, DotSize);
            var pen = DotBorderBrush is not null && DotBorderThickness > 0
                ? new Pen(DotBorderBrush, DotBorderThickness)
                : null;
            context.DrawGeometry(DotBrush, pen, new EllipseGeometry(dotRect));
        }
    }
}
