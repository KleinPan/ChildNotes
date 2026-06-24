using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ChildNotes.UiDesignCheck.Reporting;

namespace ChildNotes.UiDesignCheck.Reporting;

public static class OverlayRenderer
{
    public static void Render(string screenshotPath, string outputPath, ScreenReport report)
    {
        using var bmp = new Bitmap(screenshotPath);
        var pixelSize = new PixelSize(bmp.PixelSize.Width, bmp.PixelSize.Height);
        using var rtb = new RenderTargetBitmap(pixelSize);
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.DrawImage(bmp, new Rect(bmp.Size), new Rect(bmp.Size));

            foreach (var v in report.Violations)
            {
                var rect = ParseRect(v.Location);
                if (rect is null) continue;

                var (outline, fill) = v.Severity switch
                {
                    Severity.Error => (Colors.Red, Color.FromArgb(0x22, 0xE7, 0x4C, 0x3C)),
                    Severity.Warn => (Colors.Orange, Color.FromArgb(0x22, 0xE6, 0x7E, 0x22)),
                    _ => (Colors.DodgerBlue, Color.FromArgb(0x22, 0x34, 0x98, 0xDB)),
                };

                var pen = new Pen(new SolidColorBrush(outline), 2);
                ctx.DrawRectangle(Brushes.Transparent, pen, rect.Value);
                ctx.DrawRectangle(new SolidColorBrush(fill), null, rect.Value);
            }
        }
        using var fs = File.OpenWrite(outputPath);
        rtb.Save(fs);
    }

    private static Rect? ParseRect(string location)
    {
        var span = location.AsSpan().TrimStart('(');
        var close = span.IndexOf(')');
        if (close < 0) return null;
        var head = span[..close].ToString();
        var rest = span[(close + 1)..].Trim().ToString();

        var parts = head.Split(',');
        if (parts.Length < 2) return null;
        if (!double.TryParse(parts[0], out var x)) return null;
        if (!double.TryParse(parts[1], out var y)) return null;

        var sizeParts = rest.Split('x');
        if (sizeParts.Length < 2) return null;
        if (!double.TryParse(sizeParts[0], out var w)) return null;
        if (!double.TryParse(sizeParts[1], out var h)) return null;
        return new Rect(x, y, w, h);
    }
}
