using System.Collections.Concurrent;
using System.Text;
using SharpAstro.Fonts;
using FontsHint = SharpAstro.Fonts.Tables.Cmap.GlyphMapHint;

namespace DIR.Lib;

/// <summary>
/// Pure-managed glyph rasterizer backed by SharpAstro.Fonts.
/// Drop-in replacement for <see cref="FreeTypeGlyphRasterizer"/> — same
/// public API, no native dependencies, no GC pinning, AOT-compatible.
///
/// <para>Loaded fonts are cached per (path or memory-id). Cache lookup is
/// lock-free (<see cref="ConcurrentDictionary{TKey,TValue}"/>); per-glyph
/// rendering allocates only the result bitmap.</para>
/// </summary>
public sealed class ManagedFontRasterizer : IDisposable
{
    private readonly ConcurrentDictionary<string, OpenTypeFont> _fonts = new();

    /// <summary>
    /// Rasterize a glyph with PDF char-code + cmap lookup hint.
    /// </summary>
    public GlyphBitmap RasterizeGlyphWithCharCode(string fontPath, float fontSize,
        Rune codepoint, uint charCode, GlyphMapHint hint = GlyphMapHint.Auto)
    {
        var font = GetOrLoad(fontPath);
        // DIR.Lib.GlyphMapHint and SharpAstro.Fonts' enum share value layout
        // (Auto=0, EmbeddedSubset=1, CharCodeIsGID=2, Unicode=3) so the cast
        // is a no-op at runtime.
        var gid = font.GetGlyphId((uint)codepoint.Value, charCode, (FontsHint)hint);
        if (gid == 0) return default;
        return Render(font, gid, fontSize);
    }

    /// <summary>
    /// Rasterize a glyph by Unicode codepoint via the preferred Unicode cmap.
    /// </summary>
    public GlyphBitmap RasterizeGlyph(string fontPath, float fontSize, Rune codepoint)
    {
        var font = GetOrLoad(fontPath);
        var gid = font.GetGlyphId((uint)codepoint.Value);
        if (gid == 0) return default;
        return Render(font, gid, fontSize);
    }

    /// <summary>
    /// Register a font from raw bytes under <paramref name="fontId"/>. The
    /// byte array is retained — do not mutate after passing in.
    /// </summary>
    public bool RegisterFontFromMemory(string fontId, byte[] fontData)
    {
        if (_fonts.ContainsKey(fontId)) return true;
        try
        {
            var font = OpenTypeFont.Load(fontData);
            _fonts.TryAdd(fontId, font);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Rasterize a glyph as a signed distance field by Unicode codepoint.
    /// Returns a single-channel SDF bitmap suitable for GPU SDF text rendering.
    /// </summary>
    public SdfGlyphBitmap RasterizeGlyphSdf(string fontPath, float fontSize, Rune codepoint, float spread = 4f)
    {
        var font = GetOrLoad(fontPath);
        var gid = font.GetGlyphId((uint)codepoint.Value);
        if (gid == 0) return default;
        return RenderSdf(font, gid, fontSize, spread);
    }

    /// <summary>
    /// Rasterize a glyph as a signed distance field using PDF char-code + cmap lookup hint.
    /// Mirrors <see cref="RasterizeGlyphWithCharCode"/> for CID and embedded-subset fonts
    /// whose Unicode cmap is absent or unreliable.
    /// </summary>
    public SdfGlyphBitmap RasterizeGlyphSdfWithCharCode(string fontPath, float fontSize,
        Rune codepoint, uint charCode, GlyphMapHint hint = GlyphMapHint.Auto, float spread = 4f)
    {
        var font = GetOrLoad(fontPath);
        var gid = font.GetGlyphId((uint)codepoint.Value, charCode, (FontsHint)hint);
        if (gid == 0) return default;
        return RenderSdf(font, gid, fontSize, spread);
    }

    public void Dispose()
    {
        // Managed fonts don't own native resources — clearing the cache is
        // sufficient. Method retained for API parity with FreeType path.
        _fonts.Clear();
    }

    // ---- Internals ---------------------------------------------------------

    private OpenTypeFont GetOrLoad(string fontPath)
    {
        if (_fonts.TryGetValue(fontPath, out var existing)) return existing;
        if (fontPath.StartsWith("mem:", StringComparison.Ordinal))
            throw new InvalidOperationException($"Memory font not registered: '{fontPath}'");

        var font = OpenTypeFont.LoadFromFile(fontPath);
        return _fonts.GetOrAdd(fontPath, font);
    }

    /// <summary>
    /// Convert a SharpAstro.Fonts render result into DIR.Lib's
    /// <see cref="GlyphBitmap"/> shape. Tries the color path first
    /// (COLR/CBDT) and falls back to grayscale-as-white.
    /// </summary>
    private static GlyphBitmap Render(OpenTypeFont font, uint gid, float pixelsPerEm)
    {
        var color = font.RenderColor(gid, pixelsPerEm);
        if (color is { IsEmpty: false })
        {
            var advance = font.Hmtx is not null
                ? font.Hmtx.GetAdvanceWidth(gid) * pixelsPerEm / font.UnitsPerEm
                : 0f;
            return new GlyphBitmap(color.Pixels, color.Width, color.Height,
                color.Left, color.Top, advance, IsColored: true);
        }

        var gray = font.RenderGlyphHinted(gid, pixelsPerEm);
        if (gray.IsEmpty) return default;

        // Expand grayscale alpha to white-RGBA for compositing parity with
        // FreeType's grayscale path in the existing renderer.
        var rgba = new byte[gray.Width * gray.Height * 4];
        for (var i = 0; i < gray.Alpha.Length; i++)
        {
            var di = i * 4;
            rgba[di] = 255;
            rgba[di + 1] = 255;
            rgba[di + 2] = 255;
            rgba[di + 3] = gray.Alpha[i];
        }
        var advanceX = font.Hmtx is not null
            ? font.Hmtx.GetAdvanceWidth(gid) * pixelsPerEm / font.UnitsPerEm
            : 0f;
        return new GlyphBitmap(rgba, gray.Width, gray.Height,
            gray.Left, gray.Top, advanceX, IsColored: false);
    }

    private static SdfGlyphBitmap RenderSdf(OpenTypeFont font, uint gid, float pixelsPerEm, float spread)
    {
        var sdf = font.RenderSdf(gid, pixelsPerEm, spread);
        if (sdf.IsEmpty) return default;

        var advanceX = font.Hmtx is not null
            ? font.Hmtx.GetAdvanceWidth(gid) * pixelsPerEm / font.UnitsPerEm
            : 0f;
        return new SdfGlyphBitmap(sdf.Alpha, sdf.Width, sdf.Height,
            sdf.Left, sdf.Top, advanceX, spread);
    }
}
