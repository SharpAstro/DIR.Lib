namespace DIR.Lib;

/// <summary>
/// Simple RGBA pixel buffer (row-major, 4 bytes per pixel).
/// </summary>
public sealed class RgbaImage
{
    public byte[] Pixels { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public RgbaImage(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    public void Resize(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    public void Clear(RGBAColor32 color)
    {
        var pixels = Pixels;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.Red;
            pixels[i + 1] = color.Green;
            pixels[i + 2] = color.Blue;
            pixels[i + 3] = color.Alpha;
        }
    }

    public void FillRect(int x0, int y0, int x1, int y1, RGBAColor32 color)
    {
        // Clamp to bounds
        if (x0 < 0) x0 = 0;
        if (y0 < 0) y0 = 0;
        if (x1 > Width) x1 = Width;
        if (y1 > Height) y1 = Height;
        if (x0 >= x1 || y0 >= y1) return;

        var pixels = Pixels;
        var w = Width;
        var a = color.Alpha;

        if (a == 255)
        {
            // Opaque fast path
            for (var y = y0; y < y1; y++)
            {
                var rowOffset = y * w * 4;
                for (var x = x0; x < x1; x++)
                {
                    var i = rowOffset + x * 4;
                    pixels[i] = color.Red;
                    pixels[i + 1] = color.Green;
                    pixels[i + 2] = color.Blue;
                    pixels[i + 3] = 255;
                }
            }
        }
        else if (a > 0)
        {
            // Alpha blend: out = src*a + dst*(1-a)
            for (var y = y0; y < y1; y++)
            {
                var rowOffset = y * w * 4;
                for (var x = x0; x < x1; x++)
                {
                    var i = rowOffset + x * 4;
                    BlendPixel(pixels, i, color.Red, color.Green, color.Blue, a);
                }
            }
        }
    }

    public void BlitRgba(int dstX, int dstY, byte[] src, int srcW, int srcH)
    {
        var pixels = Pixels;
        var w = Width;
        var h = Height;

        for (var sy = 0; sy < srcH; sy++)
        {
            var dy = dstY + sy;
            if (dy < 0 || dy >= h) continue;

            var srcRow = sy * srcW * 4;
            var dstRow = dy * w * 4;

            for (var sx = 0; sx < srcW; sx++)
            {
                var dx = dstX + sx;
                if (dx < 0 || dx >= w) continue;

                var si = srcRow + sx * 4;
                var di = dstRow + dx * 4;
                var sa = src[si + 3];

                if (sa == 255)
                {
                    pixels[di] = src[si];
                    pixels[di + 1] = src[si + 1];
                    pixels[di + 2] = src[si + 2];
                    pixels[di + 3] = 255;
                }
                else if (sa > 0)
                {
                    BlendPixel(pixels, di, src[si], src[si + 1], src[si + 2], sa);
                }
            }
        }
    }

    private static void BlendPixel(byte[] pixels, int i, byte sr, byte sg, byte sb, byte sa)
    {
        var da = pixels[i + 3];
        if (da == 0)
        {
            pixels[i] = sr;
            pixels[i + 1] = sg;
            pixels[i + 2] = sb;
            pixels[i + 3] = sa;
        }
        else
        {
            var a = sa + 1;
            var inv = 256 - sa;
            pixels[i] = (byte)((sr * a + pixels[i] * inv) >> 8);
            pixels[i + 1] = (byte)((sg * a + pixels[i + 1] * inv) >> 8);
            pixels[i + 2] = (byte)((sb * a + pixels[i + 2] * inv) >> 8);
            pixels[i + 3] = (byte)Math.Min(255, da + sa - (da * sa >> 8));
        }
    }
}
