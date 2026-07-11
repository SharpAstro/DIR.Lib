using System.Text;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Subset-font glyph selection. Each of these three fixtures exists because of a real customer
/// subset-rendering bug; the first test locks in the invariant that guards those regressions, and
/// <see cref="CompareAllHints"/> is the opt-in diagnostic that first mapped out per-code behaviour.
/// </summary>
public class CmapDumpTests
{
    [Theory]
    [InlineData("XXTIIT_Arial_subset.ttf")]
    [InlineData("Tahoma_subset.ttf")]
    [InlineData("ISOCPEUR_subset.ttf")]
    public void SubsetFont_LowCharCodes_RenderUnderEmbeddedSubsetHint(string fontFile)
    {
        // PDF subset fonts address glyphs by char code through the embedded (often (3,0) symbol)
        // cmap, not by Unicode; a regression in that path blanks the text (Width 0). The low char
        // codes are the dense, always-inked range in all three fixtures — assert each renders a
        // non-empty glyph under the EmbeddedSubset hint. (Higher codes are genuinely hint-dependent,
        // which is what the CompareAllHints dump below exists to explore, so they're not asserted.)
        var fontPath = Path.Combine("Fonts", fontFile);
        using var rasterizer = new ManagedFontRasterizer();
        var fontId = $"mem:{fontFile}";
        rasterizer.RegisterFontFromMemory(fontId, File.ReadAllBytes(fontPath)).ShouldBeTrue();

        for (uint cc = 1; cc <= 10; cc++)
        {
            var bmp = rasterizer.RasterizeGlyphWithCharCode(fontId, 24f, new Rune('?'), cc, GlyphMapHint.EmbeddedSubset);
            bmp.Width.ShouldBeGreaterThan(0, $"{fontFile} cc={cc} under EmbeddedSubset");
            bmp.Height.ShouldBeGreaterThan(0, $"{fontFile} cc={cc} under EmbeddedSubset");
        }
    }

    // Opt-in diagnostic (Explicit = not run in the default suite): dumps every GlyphMapHint's glyph
    // size per char code, the tool that originally revealed which hint each subset font needs.
    [Theory(Explicit = true)]
    [InlineData("XXTIIT_Arial_subset.ttf", new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
    [InlineData("Tahoma_subset.ttf", new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 17, 21, 24, 25, 34, 37, 39, 41 })]
    [InlineData("ISOCPEUR_subset.ttf", new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
    public void CompareAllHints(string fontFile, uint[] charCodes)
    {
        var fontPath = Path.Combine("Fonts", fontFile);
        if (!File.Exists(fontPath)) { Console.Out.WriteLine($"SKIP: {fontPath}"); return; }

        using var rasterizer = new ManagedFontRasterizer();
        var fontId = $"mem:dump_{fontFile}";
        rasterizer.RegisterFontFromMemory(fontId, File.ReadAllBytes(fontPath));

        var hints = new[] { GlyphMapHint.Auto, GlyphMapHint.EmbeddedSubset, GlyphMapHint.CharCodeIsGID, GlyphMapHint.Unicode };

        Console.Out.WriteLine($"\n=== {fontFile} ===");
        Console.Out.WriteLine("cc  | Auto       | EmbSubset  | CharIsGID  | Unicode");
        Console.Out.WriteLine("----|------------|------------|------------|--------");

        foreach (var cc in charCodes)
        {
            var sb = new StringBuilder($"{cc,3} |");
            foreach (var hint in hints)
            {
                var bmp = rasterizer.RasterizeGlyphWithCharCode(fontId, 24f, new Rune('?'), cc, hint);
                sb.Append($" {bmp.Width,3}x{bmp.Height,-3}     |");
            }
            Console.Out.WriteLine(sb.ToString());
        }

        // Also try: pure Unicode lookup for common chars
        Console.Out.WriteLine("\nPure Unicode RasterizeGlyph:");
        foreach (var ch in "DATERVNMCOabcdefgh0123")
        {
            var bmp = rasterizer.RasterizeGlyph(fontId, 24f, new Rune(ch));
            if (bmp.Width > 0)
                Console.Out.Write($" '{ch}'={bmp.Width}x{bmp.Height}");
        }
        Console.Out.WriteLine();

        // Write results to a file for easy reading
        var outPath = Path.Combine(Path.GetTempPath(), $"cmap_dump_{Path.GetFileNameWithoutExtension(fontFile)}.txt");
        using var sw = new StreamWriter(outPath);
        sw.WriteLine($"=== {fontFile} ===");
        sw.WriteLine("cc  | Auto       | EmbSubset  | CharIsGID  | Unicode");
        sw.WriteLine("----|------------|------------|------------|--------");
        foreach (var cc in charCodes)
        {
            var sb2 = new StringBuilder($"{cc,3} |");
            foreach (var hint in hints)
            {
                var bmp2 = rasterizer.RasterizeGlyphWithCharCode(fontId, 24f, new Rune('?'), cc, hint);
                sb2.Append($" {bmp2.Width,3}x{bmp2.Height,-3}     |");
            }
            sw.WriteLine(sb2.ToString());
        }
        Console.Out.WriteLine($"Results written to: {outPath}");
    }
}
