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

    /// <summary>
    /// Single-level by contract: a second push REPLACES the first. Stated as a test because the
    /// alternative — intersecting, or stacking — is the reading a caller would guess, and the GPU
    /// backend does not do it either.
    /// </summary>
    [Fact]
    public void ASecondPushReplacesTheFirst()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        r.PushClip(new RectInt(new PointInt(8, 8), new PointInt(0, 0)));
        r.PushClip(new RectInt(new PointInt(20, 20), new PointInt(12, 12)));
        r.FillRectangle(new RectInt(new PointInt(20, 20), new PointInt(0, 0)), Ink);
        r.PopClip();

        At(r.Surface, 14, 14).ShouldBe(Ink);      // the SECOND rect is what applies
        At(r.Surface, 4, 4).ShouldBe(Ground);     // the first no longer does
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
}
