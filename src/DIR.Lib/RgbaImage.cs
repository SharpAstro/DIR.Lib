namespace DIR.Lib;

/// <summary>
/// Simple RGBA pixel buffer (row-major, 4 bytes per pixel).
/// </summary>
public sealed class RgbaImage
{
    public byte[] Pixels { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    // The region every write is clamped to. Defaults to the whole image, which is why a clip costs
    // nothing: each primitive below already tested its bounds against 0/0/Width/Height, and a clip
    // only changes what those four numbers are.
    private int _clipX0, _clipY0, _clipX1, _clipY1;

    public RgbaImage(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
        ResetClip();
    }

    public void Resize(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
        ResetClip();
    }

    /// <summary>
    /// Restricts every subsequent write to the intersection of this rect and the image. Not a stack:
    /// a second call replaces the first, matching the single-level contract on
    /// <see cref="Renderer{TSurface}.PushClip"/>.
    /// </summary>
    public void SetClip(int x0, int y0, int x1, int y1)
    {
        _clipX0 = Math.Clamp(Math.Min(x0, x1), 0, Width);
        _clipY0 = Math.Clamp(Math.Min(y0, y1), 0, Height);
        _clipX1 = Math.Clamp(Math.Max(x0, x1), 0, Width);
        _clipY1 = Math.Clamp(Math.Max(y0, y1), 0, Height);
    }

    /// <summary>Opens the clip back up to the whole image.</summary>
    public void ResetClip()
    {
        _clipX0 = 0;
        _clipY0 = 0;
        _clipX1 = Width;
        _clipY1 = Height;
    }

    /// <summary>Whether a <see cref="SetClip"/> is narrowing writes right now.</summary>
    public bool IsClipped => _clipX0 != 0 || _clipY0 != 0 || _clipX1 != Width || _clipY1 != Height;

    /// <summary>
    /// The half-open region writes are confined to — the whole image unless <see cref="SetClip"/> has
    /// narrowed it. Exposed for the paths that write <see cref="Pixels"/> directly rather than through
    /// the primitives here; those bypass the clip otherwise, which is how a glyph blit went on painting
    /// outside one while every fill respected it.
    /// </summary>
    public (int X0, int Y0, int X1, int Y1) ClipBounds => (_clipX0, _clipY0, _clipX1, _clipY1);

    public void Clear(RGBAColor32 color)
    {
        var packed = (uint)color.Red | ((uint)color.Green << 8) | ((uint)color.Blue << 16) | ((uint)color.Alpha << 24);
        var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(Pixels.AsSpan());
        if (!IsClipped)
        {
            span.Fill(packed);
            return;
        }

        // Clipped: clear the clip region only, and still by OVERWRITING -- a clear replaces what is
        // there, where FillRect would blend a translucent colour into it.
        for (var y = _clipY0; y < _clipY1; y++)
        {
            span.Slice(y * Width + _clipX0, _clipX1 - _clipX0).Fill(packed);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void FillRect(int x0, int y0, int x1, int y1, RGBAColor32 color)
    {
        // Clamp to the clip region, which IS the image unless one was set.
        if (x0 < _clipX0) x0 = _clipX0;
        if (y0 < _clipY0) y0 = _clipY0;
        if (x1 > _clipX1) x1 = _clipX1;
        if (y1 > _clipY1) y1 = _clipY1;
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

                    // Fix up alpha channel with Porter-Duff "over" compositing. The SIMD blend above
                    // applied the RGB formula to the alpha byte too, so EVERY alpha lane is wrong and
                    // every one has to be rewritten -- matching BlendPixel, which is the reference.
                    //
                    // This used to be guarded by `if (dstVec[3] != 0xFF)`, which was wrong twice: it
                    // read the alpha of only the FIRST pixel in the vector and applied that verdict to
                    // all Count/4 of them, and when it did skip it left the RGB-formula value behind
                    // rather than the 255 it claimed. Blending 50% white onto opaque black left alpha
                    // at (128*129 + 255*128) >> 8 = 192. It hid because it only shows where a span is
                    // split between the SIMD body and the scalar tail, and where that falls depends on
                    // Vector<byte>.Count -- so the same fill is self-consistent on a 16-byte vector and
                    // visibly seamed on a 32-byte one.
                    for (var k = pos + 3; k < pos + vecCount; k += 4)
                    {
                        var origDa = dstVec[k - pos];
                        rowSpan[k] = (byte)Math.Min(255, a + origDa - (origDa * a >> 8));
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

        for (var sy = 0; sy < srcH; sy++)
        {
            var dy = dstY + sy;
            if (dy < _clipY0 || dy >= _clipY1) continue;

            var srcRow = sy * srcW * 4;
            var dstRow = dy * w * 4;

            for (var sx = 0; sx < srcW; sx++)
            {
                var dx = dstX + sx;
                if (dx < _clipX0 || dx >= _clipX1) continue;

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
    /// Alpha-blends a color onto the pixel at (x, y). Safe for out-of-bounds coordinates, and for
    /// ones outside the current <see cref="SetClip"/>.
    /// </summary>
    public void BlendPixelAt(int x, int y, RGBAColor32 color)
    {
        if (x < _clipX0 || x >= _clipX1 || y < _clipY0 || y >= _clipY1) return;
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
