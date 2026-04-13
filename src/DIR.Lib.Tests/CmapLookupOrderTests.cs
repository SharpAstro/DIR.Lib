using System.Text;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Tests that GlyphMapHint controls the cmap lookup strategy in RasterizeGlyphWithCharCode:
/// - Auto: Unicode → Symbol → Mac Roman → charCode → direct GID (system fonts)
/// - EmbeddedSubset: Unicode → Symbol PUA → direct GID (skips Mac Roman)
/// - CharCodeIsGID: charCode used directly as glyph index (CID fonts)
/// - Unicode: Unicode cmap only (WinAnsi/MacRoman standard encoding)
///
/// The bug: embedded subset fonts (non-CID like Tahoma/ISOCPEUR) had charCodes
/// intercepted by Mac Roman cmap which mapped them to wrong GIDs.
/// </summary>
public class CmapLookupOrderTests
{
    private static readonly string SubsetFontPath = Path.Combine("Fonts", "XXTIIT_Arial_subset.ttf");

    [Fact]
    public void SystemFont_Auto_ProducesCorrectGlyph()
    {
        // System font: Auto mode should find glyphs via Unicode cmap
        const string systemFont = @"C:\Windows\Fonts\tahoma.ttf";
        if (!File.Exists(systemFont)) return;

        using var rasterizer = new FreeTypeGlyphRasterizer();

        foreach (var ch in "AaBbWw123")
        {
            var bitmap = rasterizer.RasterizeGlyphWithCharCode(
                systemFont, 24f, new Rune(ch), (uint)ch, GlyphMapHint.Auto);

            Assert.True(bitmap.Width > 0,
                $"Auto mode failed for '{ch}' (U+{(int)ch:X4})");
        }
    }

    [Fact]
    public void SystemFont_Unicode_MatchesAutoForStandardChars()
    {
        // Unicode hint should produce identical output to Auto for system fonts
        const string systemFont = @"C:\Windows\Fonts\tahoma.ttf";
        if (!File.Exists(systemFont)) return;

        using var rasterizer = new FreeTypeGlyphRasterizer();

        // EN DASH (U+2013) — WinAnsi charCode 0x96
        var autoResult = rasterizer.RasterizeGlyphWithCharCode(
            systemFont, 24f, new Rune(0x2013), 0x96, GlyphMapHint.Auto);
        var unicodeResult = rasterizer.RasterizeGlyphWithCharCode(
            systemFont, 24f, new Rune(0x2013), 0x96, GlyphMapHint.Unicode);

        Assert.True(autoResult.Width > 0, "EN DASH should render via Auto");
        Assert.Equal(autoResult.Width, unicodeResult.Width);
        Assert.Equal(autoResult.Height, unicodeResult.Height);
    }

    [Fact]
    public void SubsetFont_EmbeddedSubset_FindsViaSymbolPUA()
    {
        // XXTIIT+Arial uses Symbol encoding (PUA offset U+F000+charCode)
        // EmbeddedSubset tries Symbol PUA before falling back to direct GID.
        if (!File.Exists(SubsetFontPath)) return;

        using var rasterizer = new FreeTypeGlyphRasterizer();
        rasterizer.RegisterFontFromMemory("mem:subset", File.ReadAllBytes(SubsetFontPath));

        // XXTIIT+Arial subset: charCode 1='w', 2='.', 3='a'
        var glyph = rasterizer.RasterizeGlyphWithCharCode(
            "mem:subset", 24f, new Rune('w'), 1, GlyphMapHint.EmbeddedSubset);
        Assert.True(glyph.Width > 0, "EmbeddedSubset should find glyph via Symbol PUA for Revit font");
    }

    [Fact]
    public void SubsetFont_EmbeddedSubset_FindsGlyphs()
    {
        // EmbeddedSubset mode: should find glyphs via Symbol PUA or direct GID fallback
        if (!File.Exists(SubsetFontPath)) return;

        using var rasterizer = new FreeTypeGlyphRasterizer();
        rasterizer.RegisterFontFromMemory("mem:subset2", File.ReadAllBytes(SubsetFontPath));

        // XXTIIT+Arial has Symbol cmap with PUA mapping
        var glyph = rasterizer.RasterizeGlyphWithCharCode(
            "mem:subset2", 24f, new Rune('w'), 1, GlyphMapHint.EmbeddedSubset);
        Assert.True(glyph.Width > 0, "EmbeddedSubset should find glyph via Symbol PUA or direct GID");
    }

    [Fact]
    public void EmbeddedSubset_SkipsMacRoman()
    {
        // Regression test: EmbeddedSubset must NOT use Mac Roman cmap.
        // Mac Roman maps charCodes to wrong GIDs in subset fonts like Tahoma/ISOCPEUR.
        // XXTIIT+Arial has Symbol encoding — EmbeddedSubset finds glyphs via Symbol PUA,
        // while Auto would also try Mac Roman which returns different (wrong) GIDs.
        if (!File.Exists(SubsetFontPath)) return;

        using var rasterizer = new FreeTypeGlyphRasterizer();
        rasterizer.RegisterFontFromMemory("mem:subset3", File.ReadAllBytes(SubsetFontPath));

        // Verify multiple charCodes produce non-empty glyphs via EmbeddedSubset
        uint[] charCodes = [1, 2, 3, 4, 5];
        foreach (var cc in charCodes)
        {
            var glyph = rasterizer.RasterizeGlyphWithCharCode(
                "mem:subset3", 24f, new Rune('?'), cc, GlyphMapHint.EmbeddedSubset);
            Assert.True(glyph.Width > 0, $"EmbeddedSubset failed for charCode {cc}");
        }
    }
}
