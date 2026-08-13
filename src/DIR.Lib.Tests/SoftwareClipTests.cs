using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Clipping on the software renderer.
///
/// <para>The base contract calls clipping optional — on a GPU it is an optimization, and a backend may
/// ignore it. That reasoning does not survive contact with a widget TEST: a widget that trims its
/// content to its bounds (a tab strip's overflowing labels, a panel's scrolling list) draws over
/// everything else on a renderer that ignores the clip, so the headless picture disagrees with the
/// app's about what was drawn — and the disagreement looks like a widget bug rather than a missing
/// backend feature.</para>
/// </summary>
public class SoftwareClipTests
{
    private static RGBAColor32 At(RgbaImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    private static readonly RGBAColor32 Ink = new(255, 0, 0, 255);
    private static readonly RGBAColor32 Ground = new(0, 0, 0, 255);

    [Fact]
    public void AFillIsTrimmedToTheClip()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        r.PushClip(new RectInt(new PointInt(10, 10), new PointInt(5, 5)));
        r.FillRectangle(new RectInt(new PointInt(20, 20), new PointInt(0, 0)), Ink);
        r.PopClip();

        At(r.Surface, 7, 7).ShouldBe(Ink);        // inside
        At(r.Surface, 4, 7).ShouldBe(Ground);     // left of it
        At(r.Surface, 7, 4).ShouldBe(Ground);     // above it
        At(r.Surface, 12, 12).ShouldBe(Ground);   // past the far corner
    }

    [Fact]
    public void PopRestoresTheWholeSurface()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        r.PushClip(new RectInt(new PointInt(10, 10), new PointInt(5, 5)));
        r.PopClip();
        r.FillRectangle(new RectInt(new PointInt(20, 20), new PointInt(0, 0)), Ink);

        At(r.Surface, 0, 0).ShouldBe(Ink);
        At(r.Surface, 19, 19).ShouldBe(Ink);
    }

    [Fact]
    public void AClipOutsideTheImageDrawsNothing()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        r.PushClip(new RectInt(new PointInt(60, 60), new PointInt(50, 50)));
        r.FillRectangle(new RectInt(new PointInt(20, 20), new PointInt(0, 0)), Ink);
        r.PopClip();

        At(r.Surface, 10, 10).ShouldBe(Ground);
    }

    /// <summary>
    /// A clear replaces what is under it rather than blending, and under a clip it must still do that
    /// — to the clip region only. Both halves matter: a clip that let the clear through would wipe the
    /// surface, and a clear routed through the blend path would half-mix a translucent colour instead
    /// of replacing what was there.
    /// </summary>
    [Fact]
    public void AClearUnderAClipReplacesInsideAndSparesOutside()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        r.Surface.SetClip(5, 5, 10, 10);
        r.Surface.Clear(Ink);
        r.Surface.ResetClip();

        At(r.Surface, 7, 7).ShouldBe(Ink);
        At(r.Surface, 2, 2).ShouldBe(Ground);
        r.Surface.IsClipped.ShouldBeFalse();
    }

    /// <summary>Text is blitted rather than filled, so it takes a different path to the pixels and
    /// needs its own guard — this is the case a widget hits first, since trimming an overflowing label
    /// is the commonest reason to clip at all.</summary>
    [Fact]
    public void AGlyphBlitIsTrimmedToTheClip()
    {
        using var r = new RgbaImageRenderer(80, 40);
        r.Surface.Clear(Ground);

        var font = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");

        // Right half only: whatever the face, a run filling the box has ink in the left half.
        r.PushClip(new RectInt(new PointInt(80, 40), new PointInt(40, 0)));
        r.DrawText("HHHHHHHHHH", font, 24f, Ink, new RectInt(new PointInt(80, 40), new PointInt(0, 0)));
        r.PopClip();

        var leftInk = 0;
        for (var y = 0; y < 40; y++)
            for (var x = 0; x < 40; x++)
                if (At(r.Surface, x, y) != Ground) leftInk++;

        leftInk.ShouldBe(0);
    }

    // --- nesting ---------------------------------------------------------------------------------
    //
    // The pair used to be single-level, which reads as a simplification and is not one: a panel that
    // clips to its bounds and then clips again per row has to intersect the two itself, and has to
    // re-push its OWN rect to get back — so the inner draw needs to know the outer widget's geometry.
    // A stack that intersects is what lets a child state its own bounds and nothing else.

    [Fact]
    public void AnInnerClipNarrowsRatherThanReplacing()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        r.PushClip(new RectInt(new PointInt(15, 15), new PointInt(5, 5)));   // outer
        r.PushClip(new RectInt(new PointInt(20, 20), new PointInt(10, 10))); // inner, overhanging
        r.FillRectangle(new RectInt(new PointInt(20, 20), new PointInt(0, 0)), Ink);
        r.PopClip();
        r.PopClip();

        At(r.Surface, 12, 12).ShouldBe(Ink);      // in both
        At(r.Surface, 7, 7).ShouldBe(Ground);     // outer only — the inner one excluded it
        At(r.Surface, 17, 17).ShouldBe(Ground);   // inner only — it does NOT escape the outer
    }

    [Fact]
    public void PoppingAnInnerClipRestoresTheOuterOne()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        r.PushClip(new RectInt(new PointInt(15, 15), new PointInt(5, 5)));
        r.PushClip(new RectInt(new PointInt(20, 20), new PointInt(10, 10)));
        r.PopClip();
        r.FillRectangle(new RectInt(new PointInt(20, 20), new PointInt(0, 0)), Ink);
        r.PopClip();

        At(r.Surface, 7, 7).ShouldBe(Ink);        // back to the outer clip, not to the whole surface
        At(r.Surface, 2, 2).ShouldBe(Ground);
        r.Surface.IsClipped.ShouldBeFalse();      // …and the last pop opened it fully
    }

    [Fact]
    public void DisjointNestingDrawsNothing()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        r.PushClip(new RectInt(new PointInt(8, 8), new PointInt(0, 0)));
        r.PushClip(new RectInt(new PointInt(20, 20), new PointInt(12, 12)));
        r.FillRectangle(new RectInt(new PointInt(20, 20), new PointInt(0, 0)), Ink);
        r.PopClip();
        r.PopClip();

        // No overlap, so the region is empty. It must not invert into a positive-width rect — RectInt
        // measures its sides with Math.Abs, so an unclamped intersection would report a size and paint.
        for (var y = 0; y < 20; y++)
            for (var x = 0; x < 20; x++)
                At(r.Surface, x, y).ShouldBe(Ground);
    }

    [Fact]
    public void ClipDepthTracksThePairs()
    {
        using var r = new RgbaImageRenderer(20, 20);

        r.ClipDepth.ShouldBe(0);
        r.PushClip(new RectInt(new PointInt(10, 10), new PointInt(0, 0)));
        r.ClipDepth.ShouldBe(1);
        r.PushClip(new RectInt(new PointInt(8, 8), new PointInt(2, 2)));
        r.ClipDepth.ShouldBe(2);
        r.PopClip();
        r.PopClip();
        r.ClipDepth.ShouldBe(0);
    }

    /// <summary>An unmatched pop is the bug this stack exists to make impossible to miss: as a "reset
    /// the clip" it works on every backend that sets the region absolutely, and silently unclips a
    /// nested draw on one that does not.</summary>
    [Fact]
    public void PoppingWithNothingPushedThrows()
    {
        using var r = new RgbaImageRenderer(20, 20);

        Should.Throw<InvalidOperationException>(() => r.PopClip());

        r.PushClip(new RectInt(new PointInt(10, 10), new PointInt(0, 0)));
        r.PopClip();
        Should.Throw<InvalidOperationException>(() => r.PopClip());
    }
}
