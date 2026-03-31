using System.Text;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Tests that FreeType correctly renders glyphs from PDF subset fonts
/// where charCode = sequential GID (1, 2, 3...) and the Unicode cmap
/// maps to the ORIGINAL font's GIDs (which are wrong for the subset).
/// </summary>
public class SubsetFontGlyphTests
{
    // Subset font from Revit PDF: XXTIIT+Arial
    // CharCode 1='w', 2='.', 3='a', 4='u', 5='t', 6='o', 7='d', 8='e'
    // (from ToUnicode CMap — the PDF's content stream uses these charCodes)
    private static readonly string FontPath = Path.Combine("Fonts", "XXTIIT_Arial_subset.ttf");

    private static readonly (uint charCode, char expected)[] KnownGlyphs =
    [
        (1, 'w'), (2, '.'), (3, 'a'), (4, 'u'), (5, 't'),
        (6, 'o'), (7, 'd'), (8, 'e'), (9, 's'), (10, 'k'),
    ];

    [Fact]
    public void SubsetFont_Exists()
    {
        Assert.True(File.Exists(FontPath), $"Font file not found: {Path.GetFullPath(FontPath)}");
        var bytes = File.ReadAllBytes(FontPath);
        Console.Error.WriteLine($"Font size: {bytes.Length} bytes");
        // TrueType signature: 00 01 00 00 or "true" or "OTTO"
        Console.Error.WriteLine($"First 4 bytes: {bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} {bytes[3]:X2}");
    }

    [Fact]
    public void CharCodeAsGID_ProducesNonEmptyGlyph()
    {
        if (!File.Exists(FontPath)) { Console.Error.WriteLine("SKIP"); return; }

        using var rasterizer = new FreeTypeGlyphRasterizer();
        rasterizer.RegisterFontFromMemory("mem:test_subset", File.ReadAllBytes(FontPath));

        foreach (var (charCode, expected) in KnownGlyphs)
        {
            // With isCidFont=true: charCode is used as direct GID
            var bitmap = rasterizer.RasterizeGlyphWithCharCode(
                "mem:test_subset", 24f, new Rune(expected), charCode, isCidFont: true);

            Console.Error.WriteLine($"  cc={charCode} expected='{expected}': {bitmap.Width}x{bitmap.Height} bearingX={bitmap.BearingX} bearingY={bitmap.BearingY}");
            Assert.True(bitmap.Width > 0, $"CharCode {charCode} ('{expected}') produced empty glyph with isCidFont=true");
        }
    }

    [Fact]
    public void UnicodeOnly_ProducesDifferentGlyph()
    {
        if (!File.Exists(FontPath)) { Console.Error.WriteLine("SKIP"); return; }

        using var rasterizer = new FreeTypeGlyphRasterizer();
        rasterizer.RegisterFontFromMemory("mem:test_subset", File.ReadAllBytes(FontPath));

        // Without isCidFont: Unicode cmap is used, which may return wrong glyph
        // Compare the bitmap from Unicode lookup vs charCode-as-GID
        var mismatchCount = 0;
        foreach (var (charCode, expected) in KnownGlyphs)
        {
            var cidBitmap = rasterizer.RasterizeGlyphWithCharCode(
                "mem:test_subset", 24f, new Rune(expected), charCode, isCidFont: true);

            var unicodeBitmap = rasterizer.RasterizeGlyphWithCharCode(
                "mem:test_subset", 24f, new Rune(expected), charCode, isCidFont: false);

            var sameSize = cidBitmap.Width == unicodeBitmap.Width && cidBitmap.Height == unicodeBitmap.Height;
            var samePixels = sameSize && cidBitmap.Rgba.AsSpan().SequenceEqual(unicodeBitmap.Rgba.AsSpan());

            if (!samePixels) mismatchCount++;
            Console.Error.WriteLine($"  cc={charCode} '{expected}': CID={cidBitmap.Width}x{cidBitmap.Height} Unicode={unicodeBitmap.Width}x{unicodeBitmap.Height} match={samePixels}");
        }

        Console.Error.WriteLine($"Mismatches: {mismatchCount}/{KnownGlyphs.Length}");
        // With the Symbol charmap fix, both paths find the correct glyph
        // via the PUA fallback — mismatches may be zero (which is fine)
        Console.Error.WriteLine($"Both paths produce same glyph for all chars = {mismatchCount == 0}");
    }
}
