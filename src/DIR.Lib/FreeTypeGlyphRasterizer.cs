using System.Runtime.InteropServices;
using System.Text;
using FreeTypeSharp;
using static FreeTypeSharp.FT;

namespace DIR.Lib;

/// <summary>
/// Rasterizes glyphs to RGBA bitmaps using FreeType2 via FreeTypeSharp.
/// Supports both grayscale and colored (COLR/CBDT) fonts.
/// </summary>
public sealed unsafe class FreeTypeGlyphRasterizer : IDisposable
{
    private readonly FreeTypeLibrary _library = new();
    private readonly Dictionary<string, nint> _faces = new();

    /// <summary>
    /// Rasterizes a single glyph. Supports both grayscale and colored (COLR/CBDT) fonts.
    /// Accepts <see cref="Rune"/> for full Unicode support including supplementary planes.
    /// </summary>
    public GlyphBitmap RasterizeGlyph(string fontPath, float fontSize, Rune codepoint)
    {
        var face = GetOrLoadFace(fontPath);

        FT_Set_Pixel_Sizes(face, 0, (uint)MathF.Round(fontSize));

        var glyphIndex = FT_Get_Char_Index(face, (uint)codepoint.Value);
        if (glyphIndex == 0)
            return default;

        if (FT_Load_Glyph(face, glyphIndex, FT_LOAD.FT_LOAD_RENDER | FT_LOAD.FT_LOAD_COLOR) is not FT_Error.FT_Err_Ok)
            return default;

        if (FT_Render_Glyph(face->glyph, FT_Render_Mode_.FT_RENDER_MODE_NORMAL) is not FT_Error.FT_Err_Ok)
            return default;

        ref var bitmap = ref face->glyph->bitmap;
        var width = (int)bitmap.width;
        var height = (int)bitmap.rows;
        var pitch = bitmap.pitch;
        var buffer = bitmap.buffer;

        if (width == 0 || height == 0 || buffer == null)
            return default;

        var isColored = bitmap.pixel_mode == FT_Pixel_Mode_.FT_PIXEL_MODE_BGRA;
        var bitmapLeft = face->glyph->bitmap_left;
        var bitmapTop = face->glyph->bitmap_top;
        var advanceX = face->glyph->advance.x / 64f;

        var rgba = new byte[width * height * 4];
        if (isColored)
        {
            // BGRA color font (COLR/CBDT/SVG) — convert BGRA → RGBA
            for (var y = 0; y < height; y++)
            {
                var srcRow = buffer + y * pitch;
                for (var x = 0; x < width; x++)
                {
                    var si = x * 4;
                    var di = (y * width + x) * 4;
                    rgba[di] = srcRow[si + 2];     // R ← B
                    rgba[di + 1] = srcRow[si + 1]; // G
                    rgba[di + 2] = srcRow[si];     // B ← R
                    rgba[di + 3] = srcRow[si + 3]; // A
                }
            }
        }
        else
        {
            // Grayscale — white glyph with alpha from bitmap
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var alpha = buffer[y * pitch + x];
                    var offset = (y * width + x) * 4;
                    rgba[offset] = 255;
                    rgba[offset + 1] = 255;
                    rgba[offset + 2] = 255;
                    rgba[offset + 3] = alpha;
                }
            }
        }

        return new GlyphBitmap(rgba, width, height, bitmapLeft, bitmapTop, advanceX, isColored);
    }

    private FT_FaceRec_* GetOrLoadFace(string fontPath)
    {
        if (_faces.TryGetValue(fontPath, out var existing))
            return (FT_FaceRec_*)existing;

        var pathPtr = Marshal.StringToCoTaskMemUTF8(fontPath);
        try
        {
            FT_FaceRec_* face;
            if (FT_New_Face(_library.Native, (byte*)pathPtr, 0, &face) is not FT_Error.FT_Err_Ok)
                throw new InvalidOperationException($"FT_New_Face failed for '{fontPath}'");
            _faces[fontPath] = (nint)face;
            return face;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    public void Dispose()
    {
        foreach (var face in _faces.Values)
            FT_Done_Face((FT_FaceRec_*)face);
        _faces.Clear();
        _library.Dispose();
    }
}
