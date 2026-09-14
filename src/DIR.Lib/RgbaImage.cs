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

    // The content→device transform, decomposed to exact integers. A GPU backend folds this into its
    // projection and every vertex follows it for free; there is no projection here, so the mapping has
    // to happen where the pixels are written -- which is affordable only because the transform is
    // CONSTRAINED. A quarter turn maps an axis-aligned rect to an axis-aligned rect, so a rectangle
    // fill stays one rectangle fill, and a glyph blit stays a pixel permutation: no resampling, no
    // holes, and no second buffer.
    //
    // Held as quarter-turns plus an integer offset rather than as a matrix, so the map is exact --
    // every content pixel lands on exactly one device pixel, and text stays on the pixel grid.
    private int _rot;      // clockwise quarter turns, 0..3
    private int _tx, _ty;  // integer translation, applied after the rotation
    private bool _mapped;  // false = identity: every write below takes exactly its original path

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
    /// Restricts every subsequent write to the intersection of this rect and the image. Absolute, not
    /// a stack: a second call replaces the first. Nesting is <see cref="Renderer{TSurface}.PushClip"/>'s
    /// job, and it hands this the already-intersected region.
    /// </summary>
    public void SetClip(int x0, int y0, int x1, int y1)
    {
        // Mapped like any other rect: the caller clips in content space, the pixels it guards are in
        // device space. A clip left unmapped would trim the wrong edge of a rotated frame.
        (x0, y0, x1, y1) = MapRect(x0, y0, x1, y1);
        _clipX0 = Math.Clamp(Math.Min(x0, x1), 0, Width);
        _clipY0 = Math.Clamp(Math.Min(y0, y1), 0, Height);
        _clipX1 = Math.Clamp(Math.Max(x0, x1), 0, Width);
        _clipY1 = Math.Clamp(Math.Max(y0, y1), 0, Height);
    }

    /// <summary>
    /// Whether writes are being remapped — false for the identity, where every primitive takes the
    /// same path it always did. Read by the one text path that writes <see cref="Pixels"/> directly
    /// and therefore has to map for itself.
    /// </summary>
    public bool IsContentMapped => _mapped;

    /// <summary>
    /// Sets the content→device transform every subsequent write is mapped through, so a caller can
    /// keep drawing in content coordinates while the finished image comes out rotated — the software
    /// equivalent of folding the transform into a GPU projection. Text turns with everything else,
    /// because a glyph's pixels are mapped individually rather than its box being moved.
    /// <para><b>Scale must be 1.</b> This is the POST-layout application, and a post-layout scale is
    /// the one component that cannot be done here without resampling: it would have to invent pixels a
    /// rotation never does. A transform that should reflow — DPI, zoom — belongs in the measure
    /// context, applied to design units before layout resolves them. That ordering rule is why this
    /// refuses rather than quietly blurring.</para>
    /// </summary>
    /// <exception cref="NotSupportedException">The transform carries a scale other than 1.</exception>
    public void SetContentTransform(ContentTransform transform)
    {
        if (Math.Abs(transform.Scale - 1f) > 1e-6f)
        {
            throw new NotSupportedException(
                $"RgbaImage can apply a rotation and a translation, but not a scale of {transform.Scale}. " +
                "A scale is a reflowing transform and belongs in the measure context, applied to design " +
                "units before layout resolves them — not to the finished pixels.");
        }

        _rot = (int)transform.Rotation & 3;
        _tx = (int)MathF.Round(transform.Tx);
        _ty = (int)MathF.Round(transform.Ty);
        _mapped = _rot != 0 || _tx != 0 || _ty != 0;
    }

    // Content coordinate -> device coordinate, straight off ContentTransform.ToMatrix3x2() with
    // cos/sin reduced to the {-1, 0, 1} a quarter turn allows:
    //   dx = x*cos - y*sin + Tx,  dy = x*sin + y*cos + Ty.
    private (int X, int Y) MapCorner(int x, int y) => _rot switch
    {
        1 => (_tx - y, _ty + x),   // 90° clockwise
        2 => (_tx - x, _ty - y),   // 180°
        3 => (_tx + y, _ty - x),   // 270° clockwise
        _ => (_tx + x, _ty + y),
    };

    /// <summary>
    /// Maps a half-open content rect to the device rect it covers. Both corners are mapped and then
    /// re-sorted, which is what keeps the interval half-open under a reflection: content [x0, x1)
    /// under a 180° turn is (Tx−x1, Tx−x0], and taking min/max turns that back into [Tx−x1, Tx−x0).
    /// </summary>
    public (int X0, int Y0, int X1, int Y1) MapRect(int x0, int y0, int x1, int y1)
    {
        if (!_mapped) return (x0, y0, x1, y1);
        var (ax, ay) = MapCorner(x0, y0);
        var (bx, by) = MapCorner(x1, y1);
        return (Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by));
    }

    /// <summary>
    /// Maps one content PIXEL to the device pixel it becomes. Distinct from <see cref="MapRect"/> by
    /// exactly the off-by-one that makes rotation look right: a pixel is the box [x, x+1), so under a
    /// 180° turn pixel x becomes device pixel Tx−x−1, not Tx−x. Mapping the box and taking its lower
    /// corner gets that right for all four turns without four special cases.
    /// </summary>
    public (int X, int Y) MapPixel(int x, int y)
    {
        if (!_mapped) return (x, y);
        var (mx, my, _, _) = MapRect(x, y, x + 1, y + 1);
        return (mx, my);
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
        // A quarter turn takes an axis-aligned rect to an axis-aligned rect, so the whole fill below
        // is unchanged -- only the four numbers bounding it move.
        if (_mapped) (x0, y0, x1, y1) = MapRect(x0, y0, x1, y1);

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

        // Under a turn the destination is no longer row-contiguous, so each source pixel is placed
        // individually. That is what rotates the IMAGE rather than just moving its box -- a glyph comes
        // out turned, which is the whole point of applying the transform down here.
        if (_mapped)
        {
            for (var sy = 0; sy < srcH; sy++)
            {
                for (var sx = 0; sx < srcW; sx++)
                {
                    var si = (sy * srcW + sx) * 4;
                    var sa = src[si + 3];
                    if (sa == 0) continue;
                    BlendPixelAt(dstX + sx, dstY + sy,
                        new RGBAColor32(src[si], src[si + 1], src[si + 2], sa));
                }
            }
            return;
        }

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
        if (_mapped) (x, y) = MapPixel(x, y);
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
