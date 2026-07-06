using System.Text;
using Shouldly;
using Xunit;

namespace DIR.Lib.Shaping.Tests;

/// <summary>
/// The <see cref="ShapingTextShaper"/> adapter end to end: itemize → shape (via the engine) →
/// emit <see cref="ShapedGlyph"/>. Exercises substitution (ligatures), positioning (kerning as a
/// pixel advance delta), RTL ordering, cluster mapping, the FUnit→pixel scale, and the
/// clear-and-refill contract. Rides the DejaVuSans fixture (fi/fl ligatures, GPOS kern, and an
/// 'arab' script with init/medi/fina) shared with DIR.Lib.Tests.
/// </summary>
public class ShapingTextShaperTests
{
    private static readonly string FontPath = Path.Combine("Fonts", "DejaVuSans.ttf");

    private static (ManagedFontRasterizer Rasterizer, ShapingTextShaper Shaper, List<ShapedGlyph> Output) Setup()
        => (new ManagedFontRasterizer(), new ShapingTextShaper(), []);

    [Fact]
    public void Ligature_Fi_SubstitutesToOneGlyph()
    {
        var (rasterizer, shaper, output) = Setup();
        shaper.Shape("fi", FontPath, 16f, rasterizer, output);

        output.Count.ShouldBe(1, "DejaVu ligates f+i into a single 'fi' glyph");
        output[0].Glyph.ShouldNotBeNull(); // substituted identity — renderer keys the atlas by this
        output[0].Cluster.ShouldBe(0);
    }

    [Fact]
    public void PlainLatin_IsOneGlyphPerRune_WithSubstitutedIdentity()
    {
        var (rasterizer, shaper, output) = Setup();
        shaper.Shape("abc", FontPath, 16f, rasterizer, output);

        output.Count.ShouldBe(3);
        output.Select(g => g.Cluster).ShouldBe([0, 1, 2]);
        output.ShouldAllBe(g => g.Glyph != null); // the engine reports a glyph id for every slot
    }

    [Fact]
    public void Kerning_AV_AppliesNegativeAdvanceDeltaToFirstGlyph()
    {
        var (rasterizer, shaper, output) = Setup();
        shaper.Shape("AV", FontPath, 16f, rasterizer, output);

        output.Count.ShouldBe(2);
        output[0].XAdvanceAdjust.ShouldBeLessThan(0f, "DejaVu kerns the A/V pair tighter");
        output[1].XAdvanceAdjust.ShouldBe(0f);
    }

    [Fact]
    public void AdvanceDelta_ScalesWithFontSize()
    {
        var (rasterizer, shaper, output) = Setup();

        shaper.Shape("AV", FontPath, 16f, rasterizer, output);
        var kern16 = output[0].XAdvanceAdjust;

        shaper.Shape("AV", FontPath, 32f, rasterizer, output);
        var kern32 = output[0].XAdvanceAdjust;

        // Deltas are font units scaled by fontSize/unitsPerEm, so doubling the size doubles the px.
        kern32.ShouldBe(kern16 * 2f, tolerance: 0.001f);
    }

    [Fact]
    public void Arabic_Joins_AndEmitsRtlVisualOrder()
    {
        var (rasterizer, shaper, output) = Setup();
        shaper.Shape("بب", FontPath, 16f, rasterizer, output); // beh + beh

        output.Count.ShouldBe(2);
        output.ShouldAllBe(g => g.Glyph != null);
        // RTL: glyphs come back in visual (left-to-right) order, so clusters descend.
        output[0].Cluster.ShouldBe(1);
        output[1].Cluster.ShouldBe(0);
        // Joining actually happened: the two positional forms are not the same isolated glyph.
        output[0].Glyph!.Value.Gid.ShouldNotBe(output[1].Glyph!.Value.Gid);
    }

    [Fact]
    public void EmptyText_ProducesNoGlyphs_AndClearsOutput()
    {
        var (rasterizer, shaper, output) = Setup();
        output.Add(new ShapedGlyph(new Rune('x'), Glyph: null, 99, 1f, 1f, 1f)); // stale content

        shaper.Shape("", FontPath, 16f, rasterizer, output);

        output.ShouldBeEmpty("Shape clears and refills the output list");
    }

    [Fact]
    public void Bidi_AutoRtlParagraph_ReordersRunsVisually()
    {
        // Hebrew alef, Latin 'A', Hebrew bet. Auto resolves an RTL paragraph (first strong is
        // Hebrew), so the bidi algorithm orders the runs visually: bet (cluster 2), 'A' (1),
        // alef (0). The pre-bidi itemizer would have emitted them in logical order [0,1,2].
        var (rasterizer, shaper, output) = Setup();
        shaper.Shape("אAב", FontPath, 16f, rasterizer, output);

        output.Count.ShouldBe(3);
        output.Select(g => g.Cluster).ShouldBe([2, 1, 0]);
    }

    [Fact]
    public void Bidi_ForcedLtrParagraph_KeepsLogicalOrder()
    {
        // Same text, but a forced LTR paragraph: the two Hebrew letters are isolated level-1 runs
        // separated by the Latin 'A', so no run pair reverses and the order stays logical [0,1,2].
        var rasterizer = new ManagedFontRasterizer();
        var shaper = new ShapingTextShaper(ShapingTextShaper.ParagraphDirection.LeftToRight);
        var output = new List<ShapedGlyph>();
        shaper.Shape("אAב", FontPath, 16f, rasterizer, output);

        output.Count.ShouldBe(3);
        output.Select(g => g.Cluster).ShouldBe([0, 1, 2]);
    }
}
