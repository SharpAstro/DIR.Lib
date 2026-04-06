using System.Numerics;
using System.Runtime.InteropServices;
using FreeTypeSharp;
using static FreeTypeSharp.FT;

namespace DIR.Lib;

/// <summary>
/// Renders COLRv1 color glyphs by walking the paint tree.
/// Falls back when FT_LOAD_COLOR doesn't auto-render to BGRA.
/// </summary>
internal static unsafe class ColrV1Renderer
{
    private const int FT_COLOR_INCLUDE_ROOT_TRANSFORM = 0;
    private const int FT_COLOR_NO_ROOT_TRANSFORM = 1;

    // FT_PaintFormat values
    private const int COLR_LAYERS = 1, SOLID = 2, LINEAR_GRADIENT = 4, RADIAL_GRADIENT = 6,
        SWEEP_GRADIENT = 8, GLYPH = 10, COLR_GLYPH = 11,
        TRANSFORM = 12, TRANSLATE = 14, SCALE = 16, ROTATE = 24, SKEW = 28, COMPOSITE = 32;

    // FT_COLR_Paint_ is a union that the generator skips — we define it manually.
    // The union data starts at offset 8 (pointer-aligned after the 4-byte format enum).
    [StructLayout(LayoutKind.Explicit, Size = 136)]
    private struct ColrPaint
    {
        [FieldOffset(0)] public int format;
        // Union data starts at offset 8 (pointer-aligned after the 4-byte enum)
        [FieldOffset(8)] public fixed byte data[128];
    }

    [DllImport("freetype", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FT_Get_Color_Glyph_Paint(FT_FaceRec_* face, uint base_glyph, int root_transform, FT_Opaque_Paint_* paint);

    [DllImport("freetype", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FT_Get_Paint(FT_FaceRec_* face, FT_Opaque_Paint_ opaque_paint, ColrPaint* paint);

    [DllImport("freetype", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FT_Get_Paint_Layers(FT_FaceRec_* face, FT_LayerIterator_* iterator, FT_Opaque_Paint_* paint);

    [DllImport("freetype", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FT_Get_Colorline_Stops(FT_FaceRec_* face, FT_ColorStop_* color_stop, FT_ColorStopIterator_* iterator);

    public static GlyphBitmap? TryRender(FT_FaceRec_* face, uint glyphIndex, float fontSize, FT_Color_* palette, int paletteSize)
    {
        FT_Opaque_Paint_ rootPaint = default;
        if (FT_Get_Color_Glyph_Paint(face, glyphIndex, FT_COLOR_NO_ROOT_TRANSFORM, &rootPaint) == 0)
            return null;

        var advance = (int)(face->glyph->advance.x / 64);
        var size = Math.Max(advance, (int)MathF.Round(fontSize));
        if (size <= 0) size = (int)MathF.Round(fontSize);

        var surface = new RgbaImage(size, size);
        RenderPaint(face, rootPaint, surface, palette, paletteSize, fontSize, Matrix3x2.Identity);

        var (x0, y0, x1, y1) = FindBounds(surface);
        if (x0 >= x1 || y0 >= y1)
            return null;

        var w = x1 - x0;
        var h = y1 - y0;
        var rgba = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
            Buffer.BlockCopy(surface.Pixels, ((y0 + row) * surface.Width + x0) * 4, rgba, row * w * 4, w * 4);

        return new GlyphBitmap(rgba, w, h, x0, (int)(fontSize - y0), advance, IsColored: true);
    }

    private static void RenderPaint(FT_FaceRec_* face, FT_Opaque_Paint_ opaquePaint, RgbaImage surface,
        FT_Color_* palette, int paletteSize, float fontSize, in Matrix3x2 xform)
    {
        ColrPaint paint;
        if (FT_Get_Paint(face, opaquePaint, &paint) == 0)
            return;

        var data = paint.data;
        switch (paint.format)
        {
            case COLR_LAYERS:
            {
                var iter = *(FT_LayerIterator_*)data;
                FT_Opaque_Paint_ layerPaint;
                while (FT_Get_Paint_Layers(face, &iter, &layerPaint) != 0)
                    RenderPaint(face, layerPaint, surface, palette, paletteSize, fontSize, xform);
                break;
            }

            case GLYPH:
            {
                var childPaint = *(FT_Opaque_Paint_*)data;
                var childGlyphID = *(uint*)(data + sizeof(FT_Opaque_Paint_));
                RenderGlyphWithPaint(face, childPaint, childGlyphID, surface, palette, paletteSize, fontSize, xform);
                break;
            }

            case COLR_GLYPH:
            {
                var glyphID = *(uint*)data;
                FT_Opaque_Paint_ subPaint = default;
                if (FT_Get_Color_Glyph_Paint(face, glyphID, FT_COLOR_NO_ROOT_TRANSFORM, &subPaint) != 0)
                    RenderPaint(face, subPaint, surface, palette, paletteSize, fontSize, xform);
                break;
            }

            case TRANSFORM:
            {
                // PaintTransform: { FT_Opaque_Paint_ paint, FT_Affine23 affine }
                var childPaint = *(FT_Opaque_Paint_*)data;
                var affineBase = data + sizeof(FT_Opaque_Paint_);
                var ps = nint.Size; // FT_Fixed size
                var xx = *(nint*)affineBase / 65536f;
                var xy = *(nint*)(affineBase + ps) / 65536f;
                var dx = *(nint*)(affineBase + ps * 2) / 65536f;
                var yx = *(nint*)(affineBase + ps * 3) / 65536f;
                var yy = *(nint*)(affineBase + ps * 4) / 65536f;
                var dy = *(nint*)(affineBase + ps * 5) / 65536f;
                // Matrix3x2(m11=xx, m12=yx, m21=xy, m22=yy, m31=dx, m32=dy)
                var childXform = new Matrix3x2(xx, yx, xy, yy, dx, dy);
                RenderPaint(face, childPaint, surface, palette, paletteSize, fontSize, childXform * xform);
                break;
            }

            case TRANSLATE:
            {
                // PaintTranslate: { FT_Opaque_Paint_ paint, FT_Fixed dx, FT_Fixed dy }
                var childPaint = *(FT_Opaque_Paint_*)data;
                var txBase = data + sizeof(FT_Opaque_Paint_);
                var dx = *(nint*)txBase / 65536f;
                var dy = *(nint*)(txBase + nint.Size) / 65536f;
                RenderPaint(face, childPaint, surface, palette, paletteSize, fontSize,
                    Matrix3x2.CreateTranslation(dx, dy) * xform);
                break;
            }

            case SCALE:
            {
                // PaintScale: { FT_Opaque_Paint_ paint, FT_Fixed scaleX, FT_Fixed scaleY, FT_Fixed centerX, FT_Fixed centerY }
                var childPaint = *(FT_Opaque_Paint_*)data;
                var scBase = data + sizeof(FT_Opaque_Paint_);
                var ps = nint.Size;
                var sx = *(nint*)scBase / 65536f;
                var sy = *(nint*)(scBase + ps) / 65536f;
                var cx = *(nint*)(scBase + ps * 2) / 65536f;
                var cy = *(nint*)(scBase + ps * 3) / 65536f;
                RenderPaint(face, childPaint, surface, palette, paletteSize, fontSize,
                    Matrix3x2.CreateScale(sx, sy, new Vector2(cx, cy)) * xform);
                break;
            }

            case ROTATE:
            {
                // PaintRotate: { FT_Opaque_Paint_ paint, FT_Fixed angle, FT_Fixed centerX, FT_Fixed centerY }
                var childPaint = *(FT_Opaque_Paint_*)data;
                var rotBase = data + sizeof(FT_Opaque_Paint_);
                var ps = nint.Size;
                var angle = *(nint*)rotBase / 65536f; // turns (1.0 = 360°)
                var cx = *(nint*)(rotBase + ps) / 65536f;
                var cy = *(nint*)(rotBase + ps * 2) / 65536f;
                var radians = angle * 2f * MathF.PI;
                RenderPaint(face, childPaint, surface, palette, paletteSize, fontSize,
                    Matrix3x2.CreateRotation(radians, new Vector2(cx, cy)) * xform);
                break;
            }

            case SKEW:
            {
                // PaintSkew: { FT_Opaque_Paint_ paint, FT_Fixed xSkewAngle, FT_Fixed ySkewAngle, FT_Fixed centerX, FT_Fixed centerY }
                var childPaint = *(FT_Opaque_Paint_*)data;
                var skBase = data + sizeof(FT_Opaque_Paint_);
                var ps = nint.Size;
                var xAngle = *(nint*)skBase / 65536f; // turns
                var yAngle = *(nint*)(skBase + ps) / 65536f;
                var cx = *(nint*)(skBase + ps * 2) / 65536f;
                var cy = *(nint*)(skBase + ps * 3) / 65536f;
                var xTan = MathF.Tan(xAngle * 2f * MathF.PI);
                var yTan = MathF.Tan(yAngle * 2f * MathF.PI);
                // translate(-center) * skew * translate(center)
                var center = new Vector2(cx, cy);
                var skew = Matrix3x2.CreateTranslation(-center)
                    * new Matrix3x2(1, yTan, xTan, 1, 0, 0)
                    * Matrix3x2.CreateTranslation(center);
                RenderPaint(face, childPaint, surface, palette, paletteSize, fontSize,
                    skew * xform);
                break;
            }

            case COMPOSITE:
            {
                var sourcePaint = *(FT_Opaque_Paint_*)data;
                var backdropPaint = *(FT_Opaque_Paint_*)(data + sizeof(FT_Opaque_Paint_) + sizeof(int));
                RenderPaint(face, backdropPaint, surface, palette, paletteSize, fontSize, xform);
                RenderPaint(face, sourcePaint, surface, palette, paletteSize, fontSize, xform);
                break;
            }
        }
    }

    private static void RenderGlyphWithPaint(FT_FaceRec_* face, FT_Opaque_Paint_ fillPaint, uint glyphID,
        RgbaImage surface, FT_Color_* palette, int paletteSize, float fontSize, in Matrix3x2 xform)
    {
        ColrPaint fill;
        if (FT_Get_Paint(face, fillPaint, &fill) == 0)
            return;

        // Unwrap transforms, accumulating them for gradient coordinate mapping
        var fillXform = Matrix3x2.Identity;
        while (fill.format is TRANSFORM or TRANSLATE or SCALE or ROTATE or SKEW)
        {
            var innerPaint = *(FT_Opaque_Paint_*)fill.data;
            var ps = nint.Size;
            var fBase = fill.data + sizeof(FT_Opaque_Paint_);
            switch (fill.format)
            {
                case TRANSFORM:
                {
                    var xx = *(nint*)fBase / 65536f;
                    var xy = *(nint*)(fBase + ps) / 65536f;
                    var tdx = *(nint*)(fBase + ps * 2) / 65536f;
                    var yx = *(nint*)(fBase + ps * 3) / 65536f;
                    var yy = *(nint*)(fBase + ps * 4) / 65536f;
                    var tdy = *(nint*)(fBase + ps * 5) / 65536f;
                    fillXform = new Matrix3x2(xx, yx, xy, yy, tdx, tdy) * fillXform;
                    break;
                }
                case TRANSLATE:
                {
                    var tdx = *(nint*)fBase / 65536f;
                    var tdy = *(nint*)(fBase + ps) / 65536f;
                    fillXform = Matrix3x2.CreateTranslation(tdx, tdy) * fillXform;
                    break;
                }
                case SCALE:
                {
                    var sx = *(nint*)fBase / 65536f;
                    var sy = *(nint*)(fBase + ps) / 65536f;
                    var cx = *(nint*)(fBase + ps * 2) / 65536f;
                    var cy = *(nint*)(fBase + ps * 3) / 65536f;
                    fillXform = Matrix3x2.CreateScale(sx, sy, new Vector2(cx, cy)) * fillXform;
                    break;
                }
                case ROTATE:
                {
                    var angle = *(nint*)fBase / 65536f;
                    var cx = *(nint*)(fBase + ps) / 65536f;
                    var cy = *(nint*)(fBase + ps * 2) / 65536f;
                    fillXform = Matrix3x2.CreateRotation(angle * 2f * MathF.PI, new Vector2(cx, cy)) * fillXform;
                    break;
                }
                case SKEW:
                {
                    var xAngle = *(nint*)fBase / 65536f;
                    var yAngle = *(nint*)(fBase + ps) / 65536f;
                    var cx = *(nint*)(fBase + ps * 2) / 65536f;
                    var cy = *(nint*)(fBase + ps * 3) / 65536f;
                    var center = new Vector2(cx, cy);
                    fillXform = (Matrix3x2.CreateTranslation(-center)
                        * new Matrix3x2(1, MathF.Tan(yAngle * 2f * MathF.PI), MathF.Tan(xAngle * 2f * MathF.PI), 1, 0, 0)
                        * Matrix3x2.CreateTranslation(center)) * fillXform;
                    break;
                }
            }
            if (FT_Get_Paint(face, innerPaint, &fill) == 0)
                return;
        }

        // Render the glyph outline as a grayscale mask
        if (FT_Load_Glyph(face, glyphID, FT_LOAD.FT_LOAD_RENDER | FT_LOAD.FT_LOAD_NO_BITMAP) is not FT_Error.FT_Err_Ok)
            return;

        ref var bmp = ref face->glyph->bitmap;
        if (bmp.width == 0 || bmp.rows == 0 || bmp.buffer == null)
            return;

        var left = face->glyph->bitmap_left;
        var top = (int)(fontSize - face->glyph->bitmap_top);

        // Read color stops for gradients
        var stops = ReadColorStops(face, &fill, palette, paletteSize);

        // Read gradient geometry for radial
        float cx0 = 0, cy0 = 0, r0 = 0, cx1 = 0, cy1 = 0, r1 = 0;
        if (fill.format is RADIAL_GRADIENT)
        {
            // FT_PaintRadialGradient: { FT_ColorLine, FT_Vector c0, FT_Pos r0, FT_Vector c1, FT_Pos r1 }
            // ColorLine: extend(int=4) + pad(4) + FT_ColorStopIterator_(uint+uint+ptr+bool+pad = ~24) ≈ 32 bytes
            // Approximate: read vectors after the colorline
            var colorLineSize = sizeof(int) + 4 + sizeof(uint) * 2 + nint.Size + 8; // approximate
            var vecBase = fill.data + colorLineSize;
            var posSize = nint.Size;
            // Gradient coordinates are in font design units (FT_Pos as 16.16 fixed point)
            var scale = fontSize / face->units_per_EM;
            cx0 = *(nint*)vecBase / 65536f * scale;
            cy0 = *(nint*)(vecBase + posSize) / 65536f * scale;
            r0 = *(nint*)(vecBase + posSize * 2) / 65536f * scale;
            cx1 = *(nint*)(vecBase + posSize * 3) / 65536f * scale;
            cy1 = *(nint*)(vecBase + posSize * 4) / 65536f * scale;
            r1 = *(nint*)(vecBase + posSize * 5) / 65536f * scale;
        }

        for (var y = 0; y < bmp.rows; y++)
        {
            for (var x = 0; x < bmp.width; x++)
            {
                var pos = Vector2.Transform(new Vector2(left + x, top + y), xform);
                var dx = (int)MathF.Round(pos.X);
                var dy = (int)MathF.Round(pos.Y);

                var maskAlpha = bmp.pixel_mode == FT_Pixel_Mode_.FT_PIXEL_MODE_GRAY
                    ? bmp.buffer[y * bmp.pitch + x]
                    : bmp.buffer[y * bmp.pitch + x * 4 + 3];
                if (maskAlpha == 0) continue;

                RGBAColor32 color;
                if (fill.format == SOLID)
                {
                    var colorIdx = *(FT_ColorIndex_*)fill.data;
                    color = GetPaletteColor(palette, paletteSize, colorIdx);
                }
                else if (fill.format is RADIAL_GRADIENT && stops.Length > 0)
                {
                    // Map pixel position into gradient space via inverse fill transform
                    Matrix3x2.Invert(fillXform, out var invFill);
                    var gradPos = Vector2.Transform(new Vector2(left + x, top + y), invFill);
                    var t = ComputeRadialGradientT(gradPos.X, gradPos.Y, cx0, cy0, r0, cx1, cy1, r1);
                    color = InterpolateStops(stops, t);
                }
                else if (fill.format is LINEAR_GRADIENT or SWEEP_GRADIENT && stops.Length > 0)
                {
                    Matrix3x2.Invert(fillXform, out var invFill);
                    var gradPos = Vector2.Transform(new Vector2(left + x, top + y), invFill);
                    var t = gradPos.X / Math.Max(1, bmp.width);
                    color = InterpolateStops(stops, Math.Clamp(t, 0f, 1f));
                }
                else
                {
                    color = new RGBAColor32(128, 128, 128, 255); // fallback gray
                }

                surface.BlendPixelAt(dx, dy, color.WithAlpha(maskAlpha));
            }
        }
    }

    private static (RGBAColor32 Color, float Stop)[] ReadColorStops(FT_FaceRec_* face, ColrPaint* paint,
        FT_Color_* palette, int paletteSize)
    {
        if (paint->format is not (RADIAL_GRADIENT or LINEAR_GRADIENT or SWEEP_GRADIENT))
            return [];

        // ColorLine starts at the beginning of the paint union data
        // FT_ColorLine: { FT_PaintExtend extend (int), FT_FT_ColorStopIterator_ }
        // FT_ColorStopIterator_ offset: after extend(4) + padding to align
        var iterOffset = sizeof(int) + 4; // extend + pad
        var iter = *(FT_ColorStopIterator_*)(paint->data + iterOffset);

        var stops = new (RGBAColor32 Color, float Stop)[iter.num_color_stops];
        for (var i = 0; i < stops.Length; i++)
        {
            FT_ColorStop_ stop;
            if (FT_Get_Colorline_Stops(face, &stop, &iter) == 0)
                break;
            var color = GetPaletteColor(palette, paletteSize, stop.color);
            var offset = stop.stop_offset / 65536f; // FT_Fixed 16.16
            stops[i] = (color, offset);
        }

        return stops;
    }

    private static float ComputeRadialGradientT(float px, float py, float cx0, float cy0, float r0,
        float cx1, float cy1, float r1)
    {
        // Simple radial: compute distance from center0, normalize by radius
        var dx = px - cx0;
        var dy = py - cy0;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        var maxR = MathF.Max(r0, r1);
        return maxR > 0 ? Math.Clamp(dist / maxR, 0f, 1f) : 0f;
    }

    private static RGBAColor32 InterpolateStops((RGBAColor32 Color, float Stop)[] stops, float t)
    {
        if (stops.Length == 0) return new RGBAColor32(0, 0, 0, 255);
        if (stops.Length == 1 || t <= stops[0].Stop) return stops[0].Color;
        if (t >= stops[^1].Stop) return stops[^1].Color;

        for (var i = 1; i < stops.Length; i++)
            if (t <= stops[i].Stop)
            {
                var range = stops[i].Stop - stops[i - 1].Stop;
                var frac = range > 0 ? (t - stops[i - 1].Stop) / range : 0f;
                return RGBAColor32.Lerp(stops[i - 1].Color, stops[i].Color, frac);
            }

        return stops[^1].Color;
    }

    private static RGBAColor32 GetPaletteColor(FT_Color_* palette, int paletteSize, FT_ColorIndex_ colorIdx)
    {
        if (palette == null || colorIdx.palette_index >= paletteSize)
            return new RGBAColor32(0, 0, 0, 255);

        var c = palette[colorIdx.palette_index];
        var alpha = (byte)Math.Clamp((colorIdx.alpha * 255 + 8192) / 16384, 0, 255);
        return new RGBAColor32(c.red, c.green, c.blue, (byte)((c.alpha * alpha + 127) / 255));
    }

    private static (int x0, int y0, int x1, int y1) FindBounds(RgbaImage img)
    {
        int x0 = img.Width, y0 = img.Height, x1 = 0, y1 = 0;
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                if (img.Pixels[(y * img.Width + x) * 4 + 3] > 0)
                {
                    if (x < x0) x0 = x;
                    if (x + 1 > x1) x1 = x + 1;
                    if (y < y0) y0 = y;
                    if (y + 1 > y1) y1 = y + 1;
                }
        return (x0, y0, x1, y1);
    }
}
