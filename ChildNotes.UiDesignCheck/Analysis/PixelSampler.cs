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

        var w = x1 - x0;
        var h = y1 - y0;
        var inset = Math.Max(2, Math.Min(4, Math.Min(w, h) / 6));

        var pixels = new List<SampledColor>(256);
        using (var buf = bmp.Lock())
        {
            if (w > inset * 2 + 4 && h > inset * 2 + 4)
            {
                var step = Math.Max(1, Math.Min(w, h) / 16);
                for (int x = x0 + inset; x < x1 - inset; x += step)
                {
                    pixels.Add(ReadPixel(buf, x, y0 + inset));
                    pixels.Add(ReadPixel(buf, x, y1 - inset - 1));
                }
                for (int y = y0 + inset; y < y1 - inset; y += step)
                {
                    pixels.Add(ReadPixel(buf, x0 + inset, y));
                    pixels.Add(ReadPixel(buf, x1 - inset - 1, y));
                }
            }
            if (pixels.Count == 0)
            {
                for (int y = y0; y < y1; y += Math.Max(1, (y1 - y0) / 8))
                {
                    for (int x = x0; x < x1; x += Math.Max(1, (x1 - x0) / 8))
                    {
                        pixels.Add(ReadPixel(buf, x, y));
                    }
                }
            }
        }
        if (pixels.Count == 0) return SampleCenter(bmp, bounds);
        return MedianColor(pixels);
    }

    private static SampledColor MedianColor(List<SampledColor> pixels)
    {
        var rs = new List<byte>(pixels.Count);
        var gs = new List<byte>(pixels.Count);
        var bs = new List<byte>(pixels.Count);
        var as_ = new List<byte>(pixels.Count);
        foreach (var p in pixels)
        {
            rs.Add(p.R); gs.Add(p.G); bs.Add(p.B); as_.Add(p.A);
        }
        rs.Sort(); gs.Sort(); bs.Sort(); as_.Sort();
        var mid = pixels.Count / 2;
        return new SampledColor(rs[mid], gs[mid], bs[mid], as_[mid]);
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
            if (buf.Format == Avalonia.Platform.PixelFormat.Rgba8888)
            {
                byte r = *(byte*)addr;
                byte g = *(byte*)(addr + 1);
                byte b = *(byte*)(addr + 2);
                byte a = *(byte*)(addr + 3);
                return new SampledColor(r, g, b, a);
            }
            else
            {
                byte b = *(byte*)addr;
                byte g = *(byte*)(addr + 1);
                byte r = *(byte*)(addr + 2);
                byte a = *(byte*)(addr + 3);
                return new SampledColor(r, g, b, a);
            }
        }
    }

    public static double ColorDistance(SampledColor a, (byte r, byte g, byte b, byte a) b)
    {
        var dr = a.R - b.r; var dg = a.G - b.g; var db = a.B - b.b; var da = a.A - b.a;
        return Math.Sqrt(dr * dr + dg * dg + db * db + da * da);
    }
}
