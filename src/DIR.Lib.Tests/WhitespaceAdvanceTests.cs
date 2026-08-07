using System.IO;
using System.Text;
using DIR.Lib;
using SharpAstro.Fonts;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// A glyph with no ink still has an advance. These tests pin that: a space is an empty outline
/// whose <c>hmtx</c> advance is the whole reason it exists, so the rasterizer must carry that
/// advance out even though there are no pixels to carry with it, and the atlas must record it.
///
/// <para>The bug this locks down: every rasterize entry point bailed out with <c>default</c> on an
/// empty outline, dropping the advance. Whitespace drawn by codepoint papered over it by borrowing
/// the <c>'n'</c> glyph's advance, but a shaped run addresses glyphs by <em>id</em> — a shaper emits
/// the real space glyph, not a codepoint — and that path had nothing to borrow, so it advanced the
/// pen by zero and ran every word of shaped text together.</para>
/// </summary>
public sealed class WhitespaceAdvanceTests : IDisposable
{
    private static readonly string FontPath = Path.Combine("Fonts", "DejaVuSans.ttf");

    private readonly ManagedFontRasterizer _rasterizer = new();

    public void Dispose() => _rasterizer.Dispose();

    /// <summary>The font's own glyph id for U+0020, resolved the way a shaper would.</summary>
    private uint SpaceGid =>
        _rasterizer.ResolveGlyphIdentity(FontPath, new Rune(' '), charCode: -1, GlyphMapHint.Auto).Gid;

    private const float Size = 64f;

    // --- the rasterizer ------------------------------------------------------------------------

    [Fact]
    public void TheSpaceGlyphHasItsOwnGlyphId()
        // Everything below addresses the space by id, so a font that maps it to .notdef would make
        // these tests vacuous rather than failing.
        => SpaceGid.ShouldBeGreaterThan(0u);

    [Fact]
    public void AnInkFreeGlyphKeepsItsAdvance_Mtsdf()
    {
        var space = _rasterizer.RasterizeGlyphMtsdfByGid(FontPath, Size, SpaceGid);

        space.Width.ShouldBe(0, "a space has no ink");
        space.Height.ShouldBe(0);
        space.AdvanceX.ShouldBeGreaterThan(0f, "but it still moves the pen");
    }

    [Fact]
    public void AnInkFreeGlyphKeepsItsAdvance_Sdf()
    {
        // No by-gid SDF entry point exists, so this goes in by codepoint — the same RenderSdf tail
        // either way.
        var space = _rasterizer.RasterizeGlyphSdf(FontPath, Size, new Rune(' '));

        space.Width.ShouldBe(0);
        space.AdvanceX.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void AnInkFreeGlyphKeepsItsAdvance_Bitmap()
    {
        var space = _rasterizer.RasterizeGlyphByGid(FontPath, Size, SpaceGid);

        space.Width.ShouldBe(0);
        space.AdvanceX.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void TheAdvanceIsTheFontsOwn_ReadStraightFromHmtx()
    {
        _rasterizer.TryGetOpenTypeFont(FontPath, out var font).ShouldBeTrue();
        var expected = font.Hmtx!.GetAdvanceWidth(SpaceGid) * Size / font.UnitsPerEm;

        _rasterizer.RasterizeGlyphMtsdfByGid(FontPath, Size, SpaceGid).AdvanceX
            .ShouldBe(expected, tolerance: 0.001f);
    }

    [Fact]
    public void TheAdvanceIsNotBorrowedFromTheNGlyph()
    {
        // 'n' is the glyph the codepoint path used to borrow an advance from. A space is far narrower,
        // so pinning the gap is what catches a regression back to the borrowed metric — and asserting
        // both ends keeps the test from passing on a zero advance, which is less than anything.
        var space = _rasterizer.RasterizeGlyphMtsdfByGid(FontPath, Size, SpaceGid).AdvanceX;
        var en = _rasterizer.RasterizeGlyphMtsdf(FontPath, Size, new Rune('n')).AdvanceX;

        en.ShouldBeGreaterThan(0f);
        space.ShouldBeInRange(en * 0.2f, en * 0.75f,
            $"DejaVu Sans space ({space:F2}px) sits well below 'n' ({en:F2}px) at {Size}px");
    }

    [Fact]
    public void AGenuinelyMissingGlyphStillHasNoAdvance()
        // The empty-outline relaxation must not turn "this font has no such glyph" into a phantom
        // space: gid 0 is .notdef and stays a hard miss.
        => _rasterizer.RasterizeGlyphMtsdfByGid(FontPath, Size, 0).AdvanceX.ShouldBe(0f);

    // --- the SDF atlas -------------------------------------------------------------------------

    private SdfFontAtlas CreateAtlas() =>
        new(_rasterizer, maxTextureDimension: 8192, framesInFlight: 2, backend: null, initialPageDim: 256);

    [Fact]
    public void TheAtlasAdvancesThePenForAGidAddressedSpace()
    {
        using var atlas = CreateAtlas();

        var space = atlas.GetGlyphByGid(FontPath, SpaceGid);

        space.Width.ShouldBe(0, "no ink, so nothing is packed into a page");
        space.AdvanceX.ShouldBeGreaterThan(0f, "the shaped-text path must still move the pen");
    }

    [Fact]
    public void BothPathsAgreeOnHowWideASpaceIs()
    {
        // The whole point: text drawn through a shaper (by glyph id) and text drawn without one (by
        // codepoint) must lay out identically. They disagreed by the full width of a space.
        using var atlas = CreateAtlas();

        var byGid = atlas.GetGlyphByGid(FontPath, SpaceGid).AdvanceX;
        var byCodepoint = atlas.GetGlyph(FontPath, SdfFontAtlas.SdfRasterSize, new Rune(' ')).AdvanceX;

        byGid.ShouldBe(byCodepoint, tolerance: 0.001f);
    }

    [Fact]
    public void AnInkFreeGlyphIsCachedRatherThanRasterizedAgainEveryLookup()
    {
        using var atlas = CreateAtlas();

        var first = atlas.GetGlyphByGid(FontPath, SpaceGid);
        // rasterizeOnMiss:false is the draw path — it answers from the cache or not at all. A blank
        // glyph that was never recorded would come back as a miss here and be re-queued every single
        // frame, which is both the wrong advance and an endless redraw.
        var cached = atlas.GetGlyphByGid(FontPath, SpaceGid, rasterizeOnMiss: false);

        cached.AdvanceX.ShouldBe(first.AdvanceX);
        cached.AdvanceX.ShouldBeGreaterThan(0f);
    }

    // --- through the public renderer API --------------------------------------------------------

    [Fact]
    public void MeasuringTextChargesTheFontsOwnSpaceWidth()
    {
        // The end-to-end statement of the bug, and the only assertion here that a consumer would
        // recognise: laying out "n n" costs exactly one space more than "nn". It used to cost one
        // 'n' more — DejaVu Sans spaces came out at 1.99x their real width in every measured label.
        using var renderer = new RgbaImageRenderer(10, 10);
        const float size = 32f;

        var withSpace = renderer.MeasureText("n n".AsSpan(), FontPath, size).Width;
        var without = renderer.MeasureText("nn".AsSpan(), FontPath, size).Width;

        _rasterizer.TryGetOpenTypeFont(FontPath, out var font).ShouldBeTrue();
        var space = font.Hmtx!.GetAdvanceWidth(SpaceGid) * size / font.UnitsPerEm;

        (withSpace - without).ShouldBe(space, tolerance: 0.001f);
    }

    [Theory]
    [InlineData(' ')]   // SPACE
    [InlineData(' ')]   // NO-BREAK SPACE
    [InlineData('　')]   // IDEOGRAPHIC SPACE
    public void EveryFlavourOfSpaceAdvances(char ch)
    {
        // NBSP and the ideographic space are ordinary glyphs with ordinary advances — nothing about
        // them is special except that they have no ink, which is exactly the case that was broken.
        using var atlas = CreateAtlas();

        var gid = _rasterizer.ResolveGlyphIdentity(FontPath, new Rune(ch), -1, GlyphMapHint.Auto).Gid;
        Assert.SkipWhen(gid == 0, $"DejaVu Sans has no glyph for U+{(int)ch:X4}");

        atlas.GetGlyphByGid(FontPath, gid).AdvanceX
            .ShouldBeGreaterThan(0f, $"U+{(int)ch:X4} should advance the pen");
    }
}
