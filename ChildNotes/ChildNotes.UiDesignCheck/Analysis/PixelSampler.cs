using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ChildNotes.UiDesignCheck.Analysis;

public readonly record struct SampledColor(byte R, byte G, byte B, byte A)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}

public static class PixelSampler
{
    public static SampledColor SampleCenter(WriteableBitmap bmp, Rect bounds)
    {
        var cx = (int)Math.Clamp(bounds.Center.X, 0, bmp.PixelSize.Width - 1);
        var cy = (int)Math.Clamp(bounds.Center.Y, 0, bmp.PixelSize.Height - 1);
        return Sample(bmp, cx, cy);
    }

    public static SampledColor SampleRegionAverage(WriteableBitmap bmp, Rect bounds)
    {
        var x0 = (int)Math.Clamp(bounds.X, 0, bmp.PixelSize.Width - 1);
        var y0 = (int)Math.Clamp(bounds.Y, 0, bmp.PixelSize.Height - 1);
        var x1 = (int)Math.Clamp(bounds.Right, 0, bmp.PixelSize.Width);
        var y1 = (int)Math.Clamp(bounds.Bottom, 0, bmp.PixelSize.Height);
        if (x1 <= x0) x1 = x0 + 1;
        if (y1 <= y0) y1 = y0 + 1;

        long r = 0, g = 0, b = 0, a = 0, count = 0;
        using (var buf = bmp.Lock())
        {
            for (int y = y0; y < y1; y += Math.Max(1, (y1 - y0) / 8))
            {
                for (int x = x0; x < x1; x += Math.Max(1, (x1 - x0) / 8))
                {
                    var p = ReadPixel(buf, x, y);
                    r += p.R; g += p.G; b += p.B; a += p.A; count++;
                }
            }
        }
        if (count == 0) return SampleCenter(bmp, bounds);
        return new SampledColor((byte)(r / count), (byte)(g / count), (byte)(b / count), (byte)(a / count));
    }

    public static SampledColor Sample(WriteableBitmap bmp, int x, int y)
    {
        using var buf = bmp.Lock();
        return ReadPixel(buf, x, y);
    }

    private static SampledColor ReadPixel(ILockedFramebuffer buf, int x, int y)
    {
        var stride = buf.RowBytes;
        var addr = buf.Address + (y * stride) + (x * 4);
        unsafe
        {
            byte b = *(byte*)addr;
            byte g = *(byte*)(addr + 1);
            byte r = *(byte*)(addr + 2);
            byte a = *(byte*)(addr + 3);
            return new SampledColor(r, g, b, a);
        }
    }

    public static double ColorDistance(SampledColor a, (byte r, byte g, byte b, byte a) b)
    {
        var dr = a.R - b.r; var dg = a.G - b.g; var db = a.B - b.b; var da = a.A - b.a;
        return Math.Sqrt(dr * dr + dg * dg + db * db + da * da);
    }
}
