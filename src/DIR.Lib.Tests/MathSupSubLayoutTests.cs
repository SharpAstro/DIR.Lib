using DIR.Lib.MathLayout;
using SharpAstro.Fonts;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Numeric, non-visual regression tests for <see cref="SupSubBox"/>'s
/// script positioning. The MathLayoutBaselineTests catch end-to-end visual
/// regressions but require human eyeballing to interpret a failure; these
/// tests assert the underlying geometry directly so a sign-flip or zero-out
/// fails with a precise pixel value rather than a "0.13% pixels differ"
/// message.
///
/// <para>The historical bug this exists to catch: zeroing italic correction
/// for <see cref="BigOperatorBox"/> bases collapses sub and super to the
/// same x — an integral's "0" subscript no longer aligns with the bottom
/// curl of ∫. Asserting <c>SubXShift &lt; 0 &lt; SupXShift</c> for that
/// configuration regresses any future "should we apply italic correction
/// here?" decision in numbers, not pixels.</para>
/// </summary>
public sealed class MathSupSubLayoutTests
{
    private const float FontSize = 64f;

    private static readonly string StixPath =
        Path.Combine(AppContext.BaseDirectory, "Fonts", "STIX2Math.otf");

    private static readonly string DejaVuPath =
        Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");

    private static BoxStyle Style(string fontPath) =>
        new(fontPath, FontSize, new RGBAColor32(0, 0, 0, 255));

    /// <summary>
    /// STIX defines an italics correction for the displaystyle ∫ variant
    /// (540 FU). If this disappears the sub/super placement collapses
    /// — the SupSubBox tests below would also fail, but this stand-alone
    /// probe pins down the layer (font data) so a regression points at
    /// the right place.
    /// </summary>
    [Fact]
    public void Stix_DisplaystyleIntegral_VariantHasItalicsCorrection()
    {
        var style = Style(StixPath);
        var big = new BigOperatorBox(0x222B, style);

        big.VariantGlyphId.ShouldBeGreaterThan(0u,
            "STIX must produce a stretchy variant for ∫ at displaystyle");

        var ic = BoxStyle.SharedRasterizer.GetItalicsCorrectionByGidPx(
            style.FontPath, big.RenderFontSize, big.VariantGlyphId);

        ic.ShouldNotBeNull("STIX's displaystyle ∫ variant should carry an italics correction");
        ic.Value.ShouldBeGreaterThan(0f);
    }

    /// <summary>
    /// For ∫ at displaystyle (stretchy variant path), both
    /// <see cref="SupSubBox.SupXShift"/> and <see cref="SupSubBox.SubXShift"/>
    /// are zero — the kerning is folded into the bitmap-scanned anchors
    /// (sup anchored at top-hook ink-right, sub at bottom-curl ink-right).
    /// Italic correction would double-shift on top of the anchor scan.
    ///
    /// <para>The sub anchor must sit strictly left of the box width
    /// (advance) — that's the height-aware kern pulling the sub flush
    /// with the bottom curl, well inside the design advance.</para>
    /// </summary>
    [Fact]
    public void Stix_IntegralSupSub_VariantPath_AnchorsScanned_NoShifts()
    {
        var style = Style(StixPath);
        var big = new BigOperatorBox(0x222B, style);
        var box = new SupSubBox(
            big,
            new GlyphBox("∞", style.Smaller()),
            new GlyphBox("0", style.Smaller()),
            style);

        box.SupXShift.ShouldBe(0f, tolerance: 0.01f,
            "stretchy variant path skips italic correction — bitmap scan handles slope");
        box.SubXShift.ShouldBe(0f, tolerance: 0.01f,
            "sub same — kern is in the anchor, not the shift");

        // Variant rendered: HasVariant should be true so we know the
        // scanned-anchor path was taken (rather than the GlyphBox
        // fallback which would still apply italic correction).
        big.VariantGlyphId.ShouldBeGreaterThan(0u);
    }

    /// <summary>
    /// Italic letter bases (math context) get italic correction too — the
    /// same ±shift placement, just smaller magnitude. This regresses any
    /// change that limits italic correction to operator bases and forgets
    /// the original (and more common) case.
    /// </summary>
    [Fact]
    public void Stix_ItalicLetterSupSub_SubShiftsLeft_SuperShiftsRight()
    {
        var style = Style(StixPath);
        // MathGlyphBox remaps "x" to U+1D465 (math italic x) when the font
        // covers it, which is what triggers a non-zero italic correction.
        var box = new SupSubBox(
            new MathGlyphBox("x", style, MathStyle.Italic),
            new GlyphBox("2", style.Smaller()),
            new GlyphBox("i", style.Smaller()),
            style);

        box.SupXShift.ShouldBeGreaterThan(0f);
        box.SubXShift.ShouldBeLessThan(0f);
    }

    /// <summary>
    /// Upright bases — "0", "1", and digits in general — have zero italic
    /// correction by font design. Sub and super should land at the same
    /// x (no horizontal split). Catches a regression that would over-eagerly
    /// apply italic correction.
    /// </summary>
    [Fact]
    public void Stix_UprightDigitBase_NoHorizontalScriptShift()
    {
        var style = Style(StixPath);
        var box = new SupSubBox(
            new GlyphBox("0", style),
            new GlyphBox("a", style.Smaller()),
            new GlyphBox("b", style.Smaller()),
            style);

        box.SupXShift.ShouldBe(0f, tolerance: 0.01f);
        box.SubXShift.ShouldBe(0f, tolerance: 0.01f);
    }

    /// <summary>
    /// DejaVu has no MATH italics-correction coverage at all (no MATH
    /// MathGlyphInfo subtable for these glyphs). The fallback path zero-
    /// shifts cleanly; this guards against a regression that would crash
    /// or NaN-out on a font without the data.
    /// </summary>
    [Fact]
    public void DejaVu_IntegralSupSub_NoShiftWhenFontLacksItalicsCorrection()
    {
        var style = Style(DejaVuPath);
        var box = new SupSubBox(
            new BigOperatorBox(0x222B, style),
            new GlyphBox("∞", style.Smaller()),
            new GlyphBox("0", style.Smaller()),
            style);

        box.SupXShift.ShouldBe(0f, tolerance: 0.01f);
        box.SubXShift.ShouldBe(0f, tolerance: 0.01f);
    }

    /// <summary>
    /// Vertical placement sanity-check. The super baseline sits above the
    /// main baseline by SupShift; the sub baseline sits below by SubShift.
    /// Both should be strictly positive — a sign flip here would put the
    /// super under the base and the sub above it.
    /// </summary>
    [Fact]
    public void Stix_VerticalShifts_AreStrictlyPositive()
    {
        var style = Style(StixPath);
        var box = new SupSubBox(
            new MathGlyphBox("x", style, MathStyle.Italic),
            new GlyphBox("2", style.Smaller()),
            new GlyphBox("i", style.Smaller()),
            style);

        box.SupShift.ShouldBeGreaterThan(0f);
        box.SubShift.ShouldBeGreaterThan(0f);
    }

    /// <summary>
    /// Box width must include the super's right-shift so a standalone
    /// SupSubBox over ∫ doesn't clip its ∞ on the right edge of the canvas
    /// and an HBox sibling doesn't overlap. This caught a regression where
    /// dropping the shift from Width caused ∞ to land outside the rasterized
    /// region.
    /// </summary>
    [Fact]
    public void Stix_IntegralSupSub_WidthIncludesSuperShift()
    {
        var style = Style(StixPath);
        var sup = new GlyphBox("∞", style.Smaller());
        var box = new SupSubBox(
            new BigOperatorBox(0x222B, style),
            sup,
            new GlyphBox("0", style.Smaller()),
            style);

        // The super lands at base.advance + scriptKern + supXShift. Box.Width
        // must reach at least that x plus the super's own width.
        var big = new BigOperatorBox(0x222B, style);
        var supRightEdge = big.Width + box.ScriptKern + box.SupXShift + sup.Width;
        box.Width.ShouldBeGreaterThanOrEqualTo(supRightEdge);
    }
}
