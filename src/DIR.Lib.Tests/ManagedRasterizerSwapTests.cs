using System.Text;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Parallel tests against <see cref="ManagedFontRasterizer"/>, mirroring the
/// FreeType-backed tests under different class names so both paths can be
/// validated side-by-side during the Phase 12 swap.
///
/// <para>Once the managed path is production-validated, the FreeType-backed
/// tests will be deleted and these become the canonical suite.</para>
/// </summary>
public class ManagedRasterizerSwapTests
{
    private static readonly string SubsetFont = Path.Combine("Fonts", "XXTIIT_Arial_subset.ttf");

    [Fact]
    public void Managed_RegisterFontFromMemory_LoadsSuccessfully()
    {
        if (!File.Exists(SubsetFont)) return;
        using var r = new ManagedFontRasterizer();
        var ok = r.RegisterFontFromMemory("mem:swap", File.ReadAllBytes(SubsetFont));
        ok.ShouldBeTrue();
    }

    [Theory]
    [InlineData('A')]
    [InlineData('z')]
    [InlineData('0')]
    public void Managed_SystemFont_RasterizeGlyph_ProducesNonEmpty(int codepoint)
    {
        const string sysFont = @"C:\Windows\Fonts\tahoma.ttf";
        if (!File.Exists(sysFont)) return;
        using var r = new ManagedFontRasterizer();
        var bmp = r.RasterizeGlyph(sysFont, 24f, new Rune((char)codepoint));
        bmp.Width.ShouldBeGreaterThan(0);
        bmp.Height.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Managed_EmbeddedSubset_FindsGlyphsViaSymbolPUA()
    {
        if (!File.Exists(SubsetFont)) return;
        using var r = new ManagedFontRasterizer();
        r.RegisterFontFromMemory("mem:emb", File.ReadAllBytes(SubsetFont));
        // XXTIIT+Arial: charCode 1='w' via Symbol PUA (U+F001).
        var bmp = r.RasterizeGlyphWithCharCode("mem:emb", 24f,
            new Rune('w'), 1, GlyphMapHint.EmbeddedSubset);
        bmp.Width.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("XXTIIT_Arial_subset.ttf", new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
    [InlineData("Tahoma_subset.ttf",       new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
    [InlineData("ISOCPEUR_subset.ttf",     new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
    public void Managed_SubsetFonts_HaveAtLeastOneRenderableHint(string fontFile, uint[] charCodes)
    {
        var path = Path.Combine("Fonts", fontFile);
        if (!File.Exists(path)) return;

        using var r = new ManagedFontRasterizer();
        r.RegisterFontFromMemory($"mem:hint_{fontFile}", File.ReadAllBytes(path));
        var hints = new[] { GlyphMapHint.Auto, GlyphMapHint.EmbeddedSubset,
                            GlyphMapHint.CharCodeIsGID, GlyphMapHint.Unicode };
        var anyHit = 0;
        foreach (var cc in charCodes)
            foreach (var hint in hints)
            {
                var bmp = r.RasterizeGlyphWithCharCode($"mem:hint_{fontFile}", 24f,
                    new Rune('?'), cc, hint);
                if (bmp.Width > 0) anyHit++;
            }
        anyHit.ShouldBeGreaterThan(0,
            $"managed rasterizer found no glyphs for {fontFile} under any hint");
    }

    [Fact]
    public void Managed_ColorEmoji_ProducesColoredBitmap()
    {
        var path = Path.Combine("Fonts", "Noto-COLRv1.ttf");
        if (!File.Exists(path)) return;
        using var r = new ManagedFontRasterizer();
        r.RegisterFontFromMemory("mem:colr", File.ReadAllBytes(path));
        // U+1F534 RED CIRCLE — well-known bright glyph in COLR v1 build.
        var bmp = r.RasterizeGlyph("mem:colr", 64f, new Rune(0x1F534));
        bmp.Width.ShouldBeGreaterThan(0);
        bmp.IsColored.ShouldBeTrue();

        // Average opaque pixel should look red-ish.
        int rSum = 0, gSum = 0, bSum = 0, n = 0;
        for (var i = 0; i < bmp.Rgba.Length; i += 4)
            if (bmp.Rgba[i + 3] > 0)
            {
                rSum += bmp.Rgba[i];
                gSum += bmp.Rgba[i + 1];
                bSum += bmp.Rgba[i + 2];
                n++;
            }
        n.ShouldBeGreaterThan(0);
        var avgR = rSum / n;
        avgR.ShouldBeGreaterThan(gSum / n);
        avgR.ShouldBeGreaterThan(bSum / n);
    }

    [Fact]
    public void Managed_BitmapEmoji_RendersCBDT()
    {
        var path = Path.Combine("Fonts", "NotoColorEmoji.ttf");
        if (!File.Exists(path)) return;
        using var r = new ManagedFontRasterizer();
        r.RegisterFontFromMemory("mem:cbdt", File.ReadAllBytes(path));
        var bmp = r.RasterizeGlyph("mem:cbdt", 64f, new Rune(0x1F600)); // 😀
        bmp.Width.ShouldBeGreaterThan(0);
        bmp.IsColored.ShouldBeTrue();
    }

    [Fact]
    public void Managed_IsConcurrentlyCallable()
    {
        const string sysFont = @"C:\Windows\Fonts\tahoma.ttf";
        if (!File.Exists(sysFont)) return;
        using var r = new ManagedFontRasterizer();
        var expected = r.RasterizeGlyph(sysFont, 24f, new Rune('M'));
        Parallel.For(0, 64, _ =>
        {
            var bmp = r.RasterizeGlyph(sysFont, 24f, new Rune('M'));
            bmp.Width.ShouldBe(expected.Width);
            bmp.Height.ShouldBe(expected.Height);
        });
    }
}
