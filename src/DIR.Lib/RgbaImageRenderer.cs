using System;
using System.Collections.Generic;
using System.Text;

namespace DIR.Lib;

/// <summary>
/// Software renderer that draws onto an <see cref="RgbaImage"/> pixel buffer.
/// Uses <see cref="ManagedFontRasterizer"/> (pure-managed, AOT-compatible)
/// for text rendering. Renderer-agnostic — usable in GUI (chart caching),
/// TUI (Sixel), and tests.
/// </summary>
public class RgbaImageRenderer : Renderer<RgbaImage>
{
    private readonly ManagedFontRasterizer _rasterizer = new();

    // Glyph cache: (fontPath, fontSize, rune) → GlyphBitmap
    private readonly Dictionary<(string Font, float Size, Rune Rune), GlyphBitmap> _glyphCache = new();

    public RgbaImageRenderer(uint width, uint height)
        : base(new RgbaImage((int)width, (int)height)) { }

    public override uint Width => (uint)Surface.Width;
    public override uint Height => (uint)Surface.Height;

    public override void Resize(uint width, uint height) => Surface.Resize((int)width, (int)height);

    public override void FillRectangle(in RectInt rect, RGBAColor32 fillColor)
        => Surface.FillRect(rect.UpperLeft.X, rect.UpperLeft.Y, rect.LowerRight.X, rect.LowerRight.Y, fillColor);

    public override void FillRectangles(ReadOnlySpan<(RectInt Rect, RGBAColor32 Color)> rectangles)
    {
        foreach (var (rect, color) in rectangles)
            Surface.FillRect(rect.UpperLeft.X, rect.UpperLeft.Y, rect.LowerRight.X, rect.LowerRight.Y, color);
    }

    public override void DrawRectangle(in RectInt rect, RGBAColor32 strokeColor, int strokeWidth)
    {
        var x0 = rect.UpperLeft.X;
        var y0 = rect.UpperLeft.Y;
        var x1 = rect.LowerRight.X;
        var y1 = rect.LowerRight.Y;
        var sw = strokeWidth;

        Surface.FillRect(x0, y0, x1, y0 + sw, strokeColor);       // Top
        Surface.FillRect(x0, y1 - sw, x1, y1, strokeColor);       // Bottom
        Surface.FillRect(x0, y0 + sw, x0 + sw, y1 - sw, strokeColor); // Left
        Surface.FillRect(x1 - sw, y0 + sw, x1, y1 - sw, strokeColor); // Right
    }

    /// <summary>
    /// CPU-optimized DrawLine: calls Surface.FillRect directly, bypassing virtual FillRectangle dispatch.
    /// </summary>
    public override void DrawLine(float x0, float y0, float x1, float y1, RGBAColor32 color, int thickness = 1)
    {
        var t = Math.Max(1, thickness);
        var halfT = (t - 1) / 2;
        var ix0 = (int)x0;
        var iy0 = (int)y0;
        var ix1 = (int)x1;
        var iy1 = (int)y1;
        var img = Surface;

        // Horizontal
        if (iy0 == iy1)
        {
            var xMin = Math.Min(ix0, ix1);
            var xMax = Math.Max(ix0, ix1);
            img.FillRect(xMin, iy0 - halfT, xMax + 1, iy0 - halfT + t, color);
            return;
        }

        // Vertical
        if (ix0 == ix1)
        {
            var yMin = Math.Min(iy0, iy1);
            var yMax = Math.Max(iy0, iy1);
            img.FillRect(ix0 - halfT, yMin, ix0 - halfT + t, yMax + 1, color);
            return;
        }

        // Short diagonal: Bresenham directly to pixel buffer
        var fdx = (double)(ix1 - ix0);
        var fdy = (double)(iy1 - iy0);
        var lenSq = fdx * fdx + fdy * fdy;
        if (lenSq < 0.25) return;

        if (lenSq < 200 * 200)
        {
            var dx = Math.Abs(ix1 - ix0);
            var dy = Math.Abs(iy1 - iy0);
            var sx = ix0 < ix1 ? 1 : -1;
            var sy = iy0 < iy1 ? 1 : -1;
            var err = dx - dy;

            while (true)
            {
                img.FillRect(ix0 - halfT, iy0 - halfT, ix0 - halfT + t, iy0 - halfT + t, color);
                if (ix0 == ix1 && iy0 == iy1) break;
                var e2 = 2 * err;
                if (e2 > -dy) { err -= dy; ix0 += sx; }
                if (e2 < dx) { err += dx; iy0 += sy; }
            }
            return;
        }

        // Long diagonal: scanline quad directly to pixel buffer
        var len = Math.Sqrt(lenSq);
        var hw = t * 0.5;
        var nx = -fdy / len * hw;
        var ny = fdx / len * hw;

        double c0x = ix0 + nx, c0y = iy0 + ny;
        double c1x = ix0 - nx, c1y = iy0 - ny;
        double c2x = ix1 - nx, c2y = iy1 - ny;
        double c3x = ix1 + nx, c3y = iy1 + ny;

        var scanYMin = (int)Math.Floor(Math.Min(Math.Min(c0y, c1y), Math.Min(c2y, c3y)));
        var scanYMax = (int)Math.Ceiling(Math.Max(Math.Max(c0y, c1y), Math.Max(c2y, c3y)));

        ReadOnlySpan<double> edgeX = [c0x, c3x, c2x, c1x, c0x];
        ReadOnlySpan<double> edgeY = [c0y, c3y, c2y, c1y, c0y];

        for (var y = scanYMin; y <= scanYMax; y++)
        {
            var scanY = y + 0.5;
            var xLeft = double.MaxValue;
            var xRight = double.MinValue;

            for (var e = 0; e < 4; e++)
            {
                var ey0d = edgeY[e];
                var ey1d = edgeY[e + 1];
                if ((ey0d <= scanY && ey1d > scanY) || (ey1d <= scanY && ey0d > scanY))
                {
                    var et = (scanY - ey0d) / (ey1d - ey0d);
                    var ex = edgeX[e] + et * (edgeX[e + 1] - edgeX[e]);
                    if (ex < xLeft) xLeft = ex;
                    if (ex > xRight) xRight = ex;
                }
            }

            if (xLeft <= xRight)
            {
                var spanLeft = (int)Math.Round(xLeft);
                var spanRight = (int)Math.Round(xRight);
                if (spanRight <= spanLeft) spanRight = spanLeft + 1;
                img.FillRect(spanLeft, y, spanRight, y + 1, color);
            }
        }
    }

    /// <summary>
    /// CPU-optimized DrawEllipse: midpoint algorithm calling Surface.FillRect directly.
    /// </summary>
    public override void DrawEllipse(in RectInt rect, RGBAColor32 strokeColor, float strokeWidth = 1f)
    {
        var icx = (rect.UpperLeft.X + rect.LowerRight.X) / 2;
        var icy = (rect.UpperLeft.Y + rect.LowerRight.Y) / 2;
        var irx = Math.Abs(rect.LowerRight.X - rect.UpperLeft.X) / 2;
        var iry = Math.Abs(rect.LowerRight.Y - rect.UpperLeft.Y) / 2;
        if (irx < 1 || iry < 1) return;

        var sw = Math.Max(1, (int)strokeWidth);
        var img = Surface;

        if (sw <= 1)
        {
            // Midpoint algorithm with direct pixel buffer writes
            long rx2 = (long)irx * irx;
            long ry2 = (long)iry * iry;
            long twoRx2 = 2 * rx2;
            long twoRy2 = 2 * ry2;

            var x = 0;
            var y = iry;
            long px = 0;
            long py = twoRx2 * y;

            var spanX0 = 0;
            var spanY = y;
            var d1 = ry2 - rx2 * iry + rx2 / 4.0;
            while (px < py)
            {
                x++;
                px += twoRy2;
                if (d1 < 0)
                {
                    d1 += ry2 + px;
                }
                else
                {
                    EmitSpanDirect(img, icx, icy, spanX0, x - 1, spanY, strokeColor);
                    y--;
                    py -= twoRx2;
                    d1 += ry2 + px - py;
                    spanX0 = x;
                    spanY = y;
                }
            }
            EmitSpanDirect(img, icx, icy, spanX0, x, spanY, strokeColor);

            var d2 = ry2 * (x + 0.5) * (x + 0.5) + rx2 * (y - 1.0) * (y - 1.0) - rx2 * ry2;
            while (y >= 0)
            {
                y--;
                py -= twoRx2;
                if (d2 > 0)
                {
                    d2 += rx2 - py;
                }
                else
                {
                    x++;
                    px += twoRy2;
                    d2 += rx2 - py + px;
                }
                EmitSpanDirect(img, icx, icy, x, x, y, strokeColor);
            }
        }
        else
        {
            // Thick: fall back to base scanline ring (already uses FillRectangle virtual)
            base.DrawEllipse(rect, strokeColor, strokeWidth);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void EmitSpanDirect(RgbaImage img, int cx, int cy, int x0, int x1, int y, RGBAColor32 color)
    {
        img.FillRect(cx + x0, cy + y, cx + x1 + 1, cy + y + 1, color);
        img.FillRect(cx + x0, cy - y, cx + x1 + 1, cy - y + 1, color);
        img.FillRect(cx - x1, cy + y, cx - x0 + 1, cy + y + 1, color);
        img.FillRect(cx - x1, cy - y, cx - x0 + 1, cy - y + 1, color);
    }

    public override void FillEllipse(in RectInt rect, RGBAColor32 fillColor)
    {
        var x0 = rect.UpperLeft.X;
        var y0 = rect.UpperLeft.Y;
        var x1 = rect.LowerRight.X;
        var y1 = rect.LowerRight.Y;

        var cx = (x0 + x1) * 0.5f;
        var cy = (y0 + y1) * 0.5f;
        var rx = (x1 - x0) * 0.5f;
        var ry = (y1 - y0) * 0.5f;

        if (rx <= 0 || ry <= 0) return;

        var rxSq = rx * rx;
        var rySq = ry * ry;

        for (var y = y0; y < y1; y++)
        {
            var dy = y + 0.5f - cy;
            var xSpan = rxSq * (1 - dy * dy / rySq);
            if (xSpan <= 0) continue;
            var halfW = MathF.Sqrt(xSpan);
            var left = (int)MathF.Ceiling(cx - halfW);
            var right = (int)MathF.Floor(cx + halfW);
            Surface.FillRect(left, y, right + 1, y + 1, fillColor);
        }
    }

    public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
        RGBAColor32 fontColor, in RectInt layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
    {
        if (text.IsEmpty) return;

        var textStr = text.ToString();
        var lines = textStr.Split('\n');

        var lineHeight = fontSize * 1.3f;
        var totalHeight = lines.Length * lineHeight;

        var layoutX = (float)layout.UpperLeft.X;
        var layoutY = (float)layout.UpperLeft.Y;
        var layoutW = (float)layout.Width;
        var layoutH = (float)layout.Height;

        var startY = vertAlignment switch
        {
            TextAlign.Center => layoutY + (layoutH - totalHeight) / 2f,
            TextAlign.Far => layoutY + layoutH - totalHeight,
            _ => layoutY
        };

        for (var lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx];
            if (string.IsNullOrEmpty(line)) continue;

            // Compute visual text metrics
            var advanceSum = 0f;
            var firstBearingX = 0;
            var lastRightEdge = 0f;
            var maxAscent = 0;
            var maxDescent = 0;
            var first = true;
            foreach (var mc in line.EnumerateRunes())
            {
                var g = GetGlyph(fontFamily, fontSize, mc);
                if (first && g.Width > 0) { firstBearingX = g.BearingX; first = false; }
                if (g.Width > 0) { lastRightEdge = advanceSum + g.BearingX + g.Width; }
                if (g.BearingY > maxAscent) maxAscent = g.BearingY;
                var descent = g.Height - g.BearingY;
                if (descent > maxDescent) maxDescent = descent;
                advanceSum += g.AdvanceX;
            }
            var visualWidth = first ? advanceSum : lastRightEdge - firstBearingX;

            var penX = horizAlignment switch
            {
                TextAlign.Center => layoutX + (layoutW - visualWidth) / 2f - firstBearingX,
                TextAlign.Far => layoutX + layoutW - visualWidth - firstBearingX,
                _ => layoutX
            };
            var penY = startY + lineIdx * lineHeight;

            // Place baseline so visual bounds are centered in line
            var baseline = penY + (lineHeight + maxAscent - maxDescent) / 2f;

            foreach (var ch in line.EnumerateRunes())
            {
                var glyph = GetGlyph(fontFamily, fontSize, ch);
                if (glyph.Width == 0)
                {
                    penX += glyph.AdvanceX;
                    continue;
                }

                var gx = (int)(penX + glyph.BearingX);
                var gy = (int)(baseline - glyph.BearingY);

                if (glyph.IsColored)
                {
                    Surface.BlitRgba(gx, gy, glyph.Rgba, glyph.Width, glyph.Height);
                }
                else
                {
                    BlitGlyphTinted(gx, gy, glyph, fontColor);
                }

                penX += glyph.AdvanceX;
            }
        }
    }

    private GlyphBitmap GetGlyph(string fontPath, float fontSize, Rune rune)
    {
        fontSize = MathF.Round(fontSize);
        var key = (fontPath, fontSize, rune);
        if (_glyphCache.TryGetValue(key, out var cached))
            return cached;

        if (Rune.IsWhiteSpace(rune))
        {
            var refGlyph = GetGlyph(fontPath, fontSize, new Rune('n'));
            var space = new GlyphBitmap([], 0, 0, 0, 0, refGlyph.AdvanceX);
            _glyphCache[key] = space;
            return space;
        }

        var bitmap = _rasterizer.RasterizeGlyph(fontPath, fontSize, rune);
        _glyphCache[key] = bitmap;
        return bitmap;
    }

    private void BlitGlyphTinted(int dstX, int dstY, GlyphBitmap glyph, RGBAColor32 color)
    {
        var src = glyph.Rgba;
        var w = glyph.Width;
        var h = glyph.Height;
        var pixels = Surface.Pixels;
        var surfW = Surface.Width;
        var surfH = Surface.Height;

        for (var sy = 0; sy < h; sy++)
        {
            var dy = dstY + sy;
            if (dy < 0 || dy >= surfH) continue;

            var srcRow = sy * w * 4;
            var dstRow = dy * surfW * 4;

            for (var sx = 0; sx < w; sx++)
            {
                var dx = dstX + sx;
                if (dx < 0 || dx >= surfW) continue;

                var si = srcRow + sx * 4;
                var alpha = src[si + 3];
                if (alpha == 0) continue;

                var di = dstRow + dx * 4;
                var a = (byte)((alpha * color.Alpha + 127) / 255);
                if (a == 0) continue;

                if (a == 255)
                {
                    pixels[di] = color.Red;
                    pixels[di + 1] = color.Green;
                    pixels[di + 2] = color.Blue;
                    pixels[di + 3] = 255;
                }
                else
                {
                    var inv = 256 - a;
                    var aa = a + 1;
                    pixels[di] = (byte)((color.Red * aa + pixels[di] * inv) >> 8);
                    pixels[di + 1] = (byte)((color.Green * aa + pixels[di + 1] * inv) >> 8);
                    pixels[di + 2] = (byte)((color.Blue * aa + pixels[di + 2] * inv) >> 8);
                    pixels[di + 3] = (byte)Math.Min(255, pixels[di + 3] + a - (pixels[di + 3] * a >> 8));
                }
            }
        }
    }

    public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
    {
        var width = 0f;
        var maxAscent = 0;
        var maxDescent = 0;
        foreach (var ch in text.EnumerateRunes())
        {
            var glyph = GetGlyph(fontFamily, fontSize, ch);
            width += glyph.AdvanceX;
            if (glyph.BearingY > maxAscent) maxAscent = glyph.BearingY;
            var descent = glyph.Height - glyph.BearingY;
            if (descent > maxDescent) maxDescent = descent;
        }
        return (width, maxAscent + maxDescent);
    }

    public override void Dispose()
    {
        _rasterizer.Dispose();
        _glyphCache.Clear();
    }
}
