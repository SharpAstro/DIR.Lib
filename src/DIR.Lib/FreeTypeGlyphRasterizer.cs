using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, nint> _faces = new();
    private readonly ConcurrentBag<GCHandle> _pinnedBuffers = new(); // keep memory-loaded font data alive

    /// <summary>
    /// Rasterizes a single glyph. Supports both grayscale and colored (COLR/CBDT) fonts.
    /// Accepts <see cref="Rune"/> for full Unicode support including supplementary planes.
    /// </summary>
    /// <summary>
    /// Rasterizes a glyph for CID subset fonts using multiple lookup strategies:
    /// 1. Unicode via font's cmap (works if font has Unicode cmap)
    /// 2. CharCode via font's cmap (works if cmap maps CIDs)
    /// 3. CharCode as direct glyph index (works if CIDToGIDMap is Identity)
    /// </summary>
    public GlyphBitmap RasterizeGlyphWithCharCode(string fontPath, float fontSize, Rune codepoint, uint charCode, bool isCidFont = false)
    {
        var face = GetOrLoadFace(fontPath);
        FT_Set_Pixel_Sizes(face, 0, (uint)MathF.Round(fontSize));

        uint glyphIndex = 0;

        // Try 1: Symbol charmap with PUA mapping — PDF subset fonts (e.g. Revit)
        // often use Symbol encoding where glyphs are at U+F000+charCode.
        if (charCode > 0)
        {
            // Try Mac Roman cmap first (platformID=1) — direct charCode mapping
            if (FT_Select_Charmap(face, FT_Encoding_.FT_ENCODING_APPLE_ROMAN) == FT_Error.FT_Err_Ok)
                glyphIndex = FT_Get_Char_Index(face, charCode);

            // Try Symbol cmap with PUA offset
            if (glyphIndex == 0 && FT_Select_Charmap(face, FT_Encoding_.FT_ENCODING_MS_SYMBOL) == FT_Error.FT_Err_Ok)
                glyphIndex = FT_Get_Char_Index(face, 0xF000 + charCode);

            // Restore default charmap
            FT_Select_Charmap(face, FT_Encoding_.FT_ENCODING_UNICODE);
        }

        // Try 2: Unicode lookup via font's cmap
        if (glyphIndex == 0)
            glyphIndex = FT_Get_Char_Index(face, (uint)codepoint.Value);

        // Try 3: CharCode via font's cmap
        if (glyphIndex == 0 && charCode > 0)
            glyphIndex = FT_Get_Char_Index(face, charCode);

        // Try 4: CharCode as direct glyph index (Identity CIDToGIDMap)
        if (glyphIndex == 0 && charCode > 0)
            glyphIndex = charCode;

        if (glyphIndex == 0) return default;
        return RenderLoadedGlyph(face, glyphIndex, fontSize);
    }

    public GlyphBitmap RasterizeGlyph(string fontPath, float fontSize, Rune codepoint)
    {
        var face = GetOrLoadFace(fontPath);

        FT_Set_Pixel_Sizes(face, 0, (uint)MathF.Round(fontSize));

        var glyphIndex = FT_Get_Char_Index(face, (uint)codepoint.Value);
        if (glyphIndex == 0)
            return default;

        return RenderLoadedGlyph(face, glyphIndex, fontSize);
    }

    private static GlyphBitmap RenderLoadedGlyph(FT_FaceRec_* face, uint glyphIndex, float fontSize)
    {
        // Use our COLRv1 paint tree renderer — handles coordinate transforms correctly
        // and renders color glyphs that FreeType's FT_LOAD_COLOR may render incompletely.
        {
            var palette = GetPalette(face, out var paletteSize);
            var colrResult = ColrV1Renderer.TryRender(face, glyphIndex, fontSize, palette, paletteSize);
            if (colrResult.HasValue)
                return colrResult.Value;
        }

        // Fallback: FT_LOAD_COLOR for non-COLRv1 color fonts (CBDT, sbix, COLR v0)
        if (FT_Load_Glyph(face, glyphIndex, FT_LOAD.FT_LOAD_RENDER | FT_LOAD.FT_LOAD_COLOR) is not FT_Error.FT_Err_Ok)
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

    private static FT_Color_* GetPalette(FT_FaceRec_* face, out int size)
    {
        FT_Color_* palette = null;
        FT_Palette_Select(face, 0, &palette);

        FT_Palette_Data_ paletteData;
        if (FT_Palette_Data_Get(face, &paletteData) is FT_Error.FT_Err_Ok)
            size = paletteData.num_palette_entries;
        else
            size = palette != null ? 256 : 0;

        return palette;
    }

    /// <summary>
    /// Registers a font from raw bytes (any format FreeType supports: TTF, OTF, Type1, CFF, CID).
    /// The font is keyed by the given ID string for subsequent RasterizeGlyph calls.
    /// The byte array is pinned in memory for the lifetime of this rasterizer.
    /// </summary>
    public bool RegisterFontFromMemory(string fontId, byte[] fontData)
    {
        if (_faces.ContainsKey(fontId))
            return true;

        var handle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
        var ptr = (byte*)handle.AddrOfPinnedObject();

        FT_FaceRec_* face;
        if (FT_New_Memory_Face(_library.Native, ptr, (nint)fontData.Length, 0, &face) is not FT_Error.FT_Err_Ok)
        {
            handle.Free();
            return false;
        }

        if (((long)face->face_flags & (long)FT_FACE_FLAG.FT_FACE_FLAG_COLOR) != 0)
            FT_Palette_Select(face, 0, null);

        _pinnedBuffers.Add(handle);
        if (!_faces.TryAdd(fontId, (nint)face))
        {
            // Another thread registered it first — dispose our duplicate
            FT_Done_Face(face);
        }
        return true;
    }

    private FT_FaceRec_* GetOrLoadFace(string fontPath)
    {
        if (_faces.TryGetValue(fontPath, out var existing))
            return (FT_FaceRec_*)existing;

        // Memory-registered fonts must already be in _faces
        if (fontPath.StartsWith("mem:"))
            throw new InvalidOperationException($"Memory font not registered: '{fontPath}'");

        var pathPtr = Marshal.StringToCoTaskMemUTF8(fontPath);
        try
        {
            FT_FaceRec_* face;
            if (FT_New_Face(_library.Native, (byte*)pathPtr, 0, &face) is not FT_Error.FT_Err_Ok)
                throw new InvalidOperationException($"FT_New_Face failed for '{fontPath}'");

            // Activate color palette for COLR/COLRv1 fonts
            if (((long)face->face_flags & (long)FT_FACE_FLAG.FT_FACE_FLAG_COLOR) != 0)
                FT_Palette_Select(face, 0, null);

            if (!_faces.TryAdd(fontPath, (nint)face))
            {
                // Another thread loaded it first — dispose our duplicate and use theirs
                FT_Done_Face(face);
            }
            return (FT_FaceRec_*)_faces[fontPath];
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
        while (_pinnedBuffers.TryTake(out var handle))
            handle.Free();
        _library.Dispose();
    }
}
