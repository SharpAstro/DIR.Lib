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
        var packed = (uint)color.Red | ((uint)color.Green << 8) | ((uint)color.Blue << 16) | ((uint)color.Alpha << 24);
        System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(Pixels.AsSpan()).Fill(packed);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
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
            // Pack RGBA into a single uint32 for fast single-write
            var packed = (uint)color.Red | ((uint)color.Green << 8) | ((uint)color.Blue << 16) | 0xFF000000u;

            // Single-pixel fast path: skip inner loop overhead
            if (x1 - x0 == 1 && y1 - y0 == 1)
            {
                var i = (y0 * w + x0) * 4;
                System.Runtime.CompilerServices.Unsafe.WriteUnaligned(ref pixels[i], packed);
                return;
            }

            // Opaque span path: cast to uint span and Fill per row (memset-like)
            for (var y = y0; y < y1; y++)
            {
                var byteOffset = (y * w + x0) * 4;
                var spanWidth = x1 - x0;
                System.Runtime.InteropServices.MemoryMarshal
                    .Cast<byte, uint>(pixels.AsSpan(byteOffset, spanWidth * 4))
                    .Fill(packed);
            }
        }
        else if (a > 0)
        {
            // Alpha blend: out = src*a/256 + dst*(256-a)/256
            // SIMD path: process Vector<byte>.Count bytes per iteration (16/32/64 depending on HW).
            // Each pixel is 4 bytes (RGBA), so we blend Count/4 pixels per vector op.
            var spanWidth = x1 - x0;
            var rowBytes = spanWidth * 4;

            // Build source vector: repeated RGBA pattern across the full vector width
            var vecCount = System.Numerics.Vector<byte>.Count;
            Span<byte> srcPattern = stackalloc byte[vecCount];
            for (var j = 0; j < vecCount; j += 4)
            {
                srcPattern[j] = color.Red;
                srcPattern[j + 1] = color.Green;
                srcPattern[j + 2] = color.Blue;
                srcPattern[j + 3] = color.Alpha;
            }
            var srcVec = new System.Numerics.Vector<byte>(srcPattern);

            // Alpha and inverse-alpha as ushort vectors for 16-bit multiply
            // Use (a+1) and (256-a) so that (x*(a+1))>>8 gives correct blend for a=255
            Span<ushort> alphaPattern = stackalloc ushort[System.Numerics.Vector<ushort>.Count];
            Span<ushort> invAlphaPattern = stackalloc ushort[System.Numerics.Vector<ushort>.Count];
            var alpha16 = (ushort)(a + 1);
            var invAlpha16 = (ushort)(256 - a);
            alphaPattern.Fill(alpha16);
            invAlphaPattern.Fill(invAlpha16);
            var alphaVec = new System.Numerics.Vector<ushort>(alphaPattern);
            var invAlphaVec = new System.Numerics.Vector<ushort>(invAlphaPattern);

            for (var y = y0; y < y1; y++)
            {
                var byteOffset = (y * w + x0) * 4;
                var rowSpan = pixels.AsSpan(byteOffset, rowBytes);
                var pos = 0;

                // SIMD loop: blend vecCount bytes at a time
                while (pos + vecCount <= rowBytes)
                {
                    var dstVec = new System.Numerics.Vector<byte>(rowSpan.Slice(pos, vecCount));

                    // Widen src and dst to ushort for 16-bit arithmetic
                    System.Numerics.Vector.Widen(srcVec, out var srcLo, out var srcHi);
                    System.Numerics.Vector.Widen(dstVec, out var dstLo, out var dstHi);

                    // Blend: (src * alpha + dst * invAlpha) >> 8
                    var blendLo = (srcLo * alphaVec + dstLo * invAlphaVec) >>> 8;
                    var blendHi = (srcHi * alphaVec + dstHi * invAlphaVec) >>> 8;

                    // Narrow back to byte
                    var result = System.Numerics.Vector.Narrow(blendLo, blendHi);
                    result.CopyTo(rowSpan.Slice(pos, vecCount));

                    // Fix up alpha channel with Porter-Duff "over" compositing.
                    // The SIMD blend applied the RGB formula to alpha too, which is
                    // wrong for non-opaque destinations. Skip when destination was
                    // opaque (dstVec alpha bytes are all 0xFF) - the result is always 255.
                    if (dstVec[3] != 0xFF)
                    {
                        for (var k = pos + 3; k < pos + vecCount; k += 4)
                        {
                            var origDa = dstVec[k - pos];
                            rowSpan[k] = (byte)Math.Min(255, a + origDa - (origDa * a >> 8));
                        }
                    }

                    pos += vecCount;
                }

                // Scalar tail for remaining pixels
                while (pos + 4 <= rowBytes)
                {
                    BlendPixel(pixels, byteOffset + pos, color.Red, color.Green, color.Blue, a);
                    pos += 4;
                }
            }
        }
    }

    public void DrawHLine(int x0, int x1, int y, RGBAColor32 color)
        => FillRect(x0, y, x1, y + 1, color);

    public void DrawVLine(int x, int y0, int y1, RGBAColor32 color)
        => FillRect(x, y0, x + 1, y1, color);

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

    /// <summary>
    /// Alpha-blends a color onto the pixel at (x, y). Safe for out-of-bounds coordinates.
    /// </summary>
    public void BlendPixelAt(int x, int y, RGBAColor32 color)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        var i = (y * Width + x) * 4;
        BlendPixel(Pixels, i, color.Red, color.Green, color.Blue, color.Alpha);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void BlendPixel(byte[] pixels, int i, byte sr, byte sg, byte sb, byte sa)
    {
        // Branch-free blend matching the SIMD path.
        // RGB: (src * (a+1) + dst * (256-a)) >> 8
        // Alpha: srcA + dstA - (dstA * srcA >> 8) (standard Porter-Duff "over" compositing)
        var a = sa + 1;
        var inv = 256 - sa;
        pixels[i] = (byte)((sr * a + pixels[i] * inv) >> 8);
        pixels[i + 1] = (byte)((sg * a + pixels[i + 1] * inv) >> 8);
        pixels[i + 2] = (byte)((sb * a + pixels[i + 2] * inv) >> 8);
        pixels[i + 3] = (byte)Math.Min(255, sa + pixels[i + 3] - (pixels[i + 3] * sa >> 8));
    }
}
