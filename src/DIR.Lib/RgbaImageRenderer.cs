using System;
using System.Collections.Generic;
using System.Text;

namespace DIR.Lib;

/// <summary>
/// Software renderer that draws onto an <see cref="RgbaImage"/> pixel buffer.
/// Uses <see cref="FreeTypeGlyphRasterizer"/> for text rendering.
/// Renderer-agnostic — usable in GUI (chart caching), TUI (Sixel), and tests.
/// </summary>
public class RgbaImageRenderer : Renderer<RgbaImage>
{
    private readonly FreeTypeGlyphRasterizer _rasterizer = new();

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
