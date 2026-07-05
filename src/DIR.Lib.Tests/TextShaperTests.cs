using System.Collections.Generic;
using System.Text;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// A2 contract for the <see cref="ITextShaper"/> seam. The default <see cref="AdvanceShaper"/>
/// must reproduce the pre-seam per-rune model exactly: one glyph per rune, in
/// <see cref="MemoryExtensions.EnumerateRunes"/> order, clusters at UTF-16 offsets, and — with
/// kerning off — zero positioning adjustments and no glyph substitution. That is what keeps
/// <see cref="Renderer{TSurface}.DrawText"/>/<see cref="Renderer{TSurface}.MeasureText"/>
/// byte-identical (the render baselines are the pixel guard; these lock the shaping contract).
/// The opt-in kerning path is also covered — it is the "kerned text before HarfBuzz" win.
/// </summary>
public class TextShaperTests
{
    private static readonly string FontPath = Path.Combine("Fonts", "DejaVuSans.ttf");

    [Fact]
    public void DefaultShaper_ReproducesEnumerateRunes_WithUtf16Clusters()
    {
        // H, i, an astral emoji (surrogate pair → one Rune, 2 UTF-16 units), then '!'.
        const string text = "Hi\U0001F600!";
        var expected = new List<Rune>();
        foreach (var r in text.EnumerateRunes()) expected.Add(r);

        var buf = new List<ShapedGlyph>();
        // Pass a null rasterizer on purpose: the default (no-kern) shaper must never dereference it.
        AdvanceShaper.Default.Shape(text.AsSpan(), FontPath, 24f, rasterizer: null!, buf);

        Assert.Equal(expected.Count, buf.Count);
        var cluster = 0;
        for (var i = 0; i < buf.Count; i++)
        {
            Assert.Equal(expected[i], buf[i].Source);
            Assert.Null(buf[i].Glyph);            // default shaper doesn't substitute — renderer resolves from Source
            Assert.Equal(0f, buf[i].XAdvanceAdjust);
            Assert.Equal(0f, buf[i].XOffset);
            Assert.Equal(0f, buf[i].YOffset);
            Assert.Equal(cluster, buf[i].Cluster);
            cluster += expected[i].Utf16SequenceLength;
        }

        // The emoji sits at UTF-16 index 2 and occupies 2 units, so '!' lands at cluster 4.
        Assert.Equal(2, buf[2].Cluster);
        Assert.Equal(4, buf[3].Cluster);
    }

    [Fact]
    public void ShapeClearsOutputBuffer_OnReuse()
    {
        var buf = new List<ShapedGlyph>();
        AdvanceShaper.Default.Shape("abc".AsSpan(), FontPath, 24f, null!, buf);
        Assert.Equal(3, buf.Count);
        AdvanceShaper.Default.Shape("x".AsSpan(), FontPath, 24f, null!, buf);
        var only = Assert.Single(buf);             // refilled, not appended
        Assert.Equal(new Rune('x'), only.Source);
    }

    [Fact]
    public void GetKerningPx_ReturnsZero_ForGidsWithNoKernEntry()
    {
        using var rasterizer = new ManagedFontRasterizer();
        // Gids past the font's glyph count can't appear in any kern/GPOS pair → no entry → 0.
        // (Font-independent: unlike a specific glyph pair, which may kern via GPOS class rules.)
        Assert.Equal(0f, rasterizer.GetKerningPx(FontPath, 48f, 65000, 65001));
    }

    [Fact]
    public void KerningShaper_FoldsPairKern_OntoLeftGlyph()
    {
        using var rasterizer = new ManagedFontRasterizer();
        const float size = 48f;
        var gidA = rasterizer.ResolveGlyphIdentity(FontPath, new Rune('A'), -1, GlyphMapHint.Auto).Gid;
        var gidV = rasterizer.ResolveGlyphIdentity(FontPath, new Rune('V'), -1, GlyphMapHint.Auto).Gid;
        var kern = rasterizer.GetKerningPx(FontPath, size, gidA, gidV);

        // DejaVuSans kerns the A/V pair tighter (negative). If this ever regresses to 0 the font or
        // its kern/GPOS table changed — the rest of the assertions would be vacuous, so guard it.
        Assert.True(kern < 0f, $"expected DejaVuSans to kern A/V negative, got {kern}");

        var buf = new List<ShapedGlyph>();
        new AdvanceShaper(applyKerning: true).Shape("AV".AsSpan(), FontPath, size, rasterizer, buf);

        Assert.Equal(2, buf.Count);
        Assert.Equal(kern, buf[0].XAdvanceAdjust, 3);   // folded onto the LEFT glyph of the pair
        Assert.Equal(0f, buf[1].XAdvanceAdjust);        // right glyph unadjusted (no pair to its right)

        // Default shaper leaves both untouched.
        new AdvanceShaper(applyKerning: false).Shape("AV".AsSpan(), FontPath, size, rasterizer, buf);
        Assert.Equal(0f, buf[0].XAdvanceAdjust);
        Assert.Equal(0f, buf[1].XAdvanceAdjust);
    }

    [Fact]
    public void MeasureText_WithKerning_ShrinksWidth_ByExactlyThePairKern()
    {
        using var renderer = new RgbaImageRenderer(10, 10);
        const float size = 48f;

        var unkerned = renderer.MeasureText("AV".AsSpan(), FontPath, size).Width;

        // Recover the kern the same way the shaper will apply it, to assert an exact fold.
        using var rasterizer = new ManagedFontRasterizer();
        var gidA = rasterizer.ResolveGlyphIdentity(FontPath, new Rune('A'), -1, GlyphMapHint.Auto).Gid;
        var gidV = rasterizer.ResolveGlyphIdentity(FontPath, new Rune('V'), -1, GlyphMapHint.Auto).Gid;
        var kern = rasterizer.GetKerningPx(FontPath, size, gidA, gidV);
        Assert.True(kern < 0f);

        renderer.TextShaper = new AdvanceShaper(applyKerning: true);
        var kerned = renderer.MeasureText("AV".AsSpan(), FontPath, size).Width;

        Assert.True(kerned < unkerned);
        Assert.Equal(unkerned + kern, kerned, 3);   // width folds in exactly one pair's kern
    }

    [Fact]
    public void SettingTextShaperNull_RestoresDefault()
    {
        using var renderer = new RgbaImageRenderer(10, 10);
        renderer.TextShaper = new AdvanceShaper(applyKerning: true);
        renderer.TextShaper = null!;
        Assert.Same(AdvanceShaper.Default, renderer.TextShaper);
    }
}
