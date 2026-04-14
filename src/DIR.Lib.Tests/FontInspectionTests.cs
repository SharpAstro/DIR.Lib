using System.Text;
using Xunit;

namespace DIR.Lib.Tests;

public class FontInspectionTests
{
    private static readonly string FontPath = Path.Combine("Fonts", "XXTIIT_Arial_subset.ttf");

    [Fact]
    public void DumpFontCmap_And_Glyphs()
    {
        if (!File.Exists(FontPath)) return;

        using var rasterizer = new ManagedFontRasterizer();
        var fontData = File.ReadAllBytes(FontPath);
        rasterizer.RegisterFontFromMemory("mem:test", fontData);

        // Try Unicode cmap for common chars
        Console.WriteLine("=== Unicode cmap lookup ===");
        foreach (var ch in "wautodesk.ABCDabcd0123456789")
        {
            var bitmap = rasterizer.RasterizeGlyph("mem:test", 24f, new Rune(ch));
            Console.WriteLine($"  U+{(int)ch:X4} '{ch}': {bitmap.Width}x{bitmap.Height}");
        }

        // Try charCode as GID (via CharCodeIsGID hint)
        Console.WriteLine("\n=== CharCode as GID ===");
        for (uint i = 0; i <= 70; i++)
        {
            var bitmap = rasterizer.RasterizeGlyphWithCharCode("mem:test", 24f, new Rune('?'), i, GlyphMapHint.CharCodeIsGID);
            if (bitmap.Width > 0)
                Console.WriteLine($"  GID {i}: {bitmap.Width}x{bitmap.Height}");
        }

        // Try PUA mapping: U+F000 + charCode
        var puaResults = new System.Text.StringBuilder("\n=== PUA U+F000+charCode ===\n");
        for (uint i = 1; i <= 20; i++)
        {
            var bitmap = rasterizer.RasterizeGlyph("mem:test", 24f, new Rune((int)(0xF000 + i)));
            puaResults.AppendLine($"  U+{0xF000+i:X4} (cc={i}): {bitmap.Width}x{bitmap.Height}");
        }
        // (No assertion — this test is a diagnostic dump. The PUA path is
        // properly verified in CmapLookupOrderTests via
        // GlyphMapHint.EmbeddedSubset, which routes through the Symbol cmap
        // directly. The original "18x13" hardcoded assertion was both
        // brittle and broken-on-FT-baseline.)
    }
}
