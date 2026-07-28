using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Pins <see cref="Renderer{TSurface}.FillRoundedRectangle"/>'s default (scanline) implementation.
/// <para>
/// The invariant worth protecting is the <b>even translucent fill</b>. The obvious way to build a
/// rounded rect from the existing primitives -- a cross of rectangles plus four corner ellipses --
/// looks right with an opaque colour and wrong with a translucent one, because the overlaps blend
/// twice and darken all four corners. Panel backgrounds are exactly the translucent case, so a
/// regression here would be invisible in the unit tests that use opaque colours and obvious on screen.
/// </para>
/// </summary>
public class RoundedRectangleTests
{
    private static readonly RGBAColor32 Backdrop = new RGBAColor32(0, 0, 0, 255);
    private static readonly RGBAColor32 Opaque = new RGBAColor32(255, 255, 255, 255);

    private static RgbaImageRenderer NewRenderer(uint width, uint height)
    {
        var renderer = new RgbaImageRenderer(width, height);
        renderer.Surface.Clear(Backdrop);
        return renderer;
    }

    private static RectInt Rect(int left, int top, int right, int bottom) =>
        new RectInt((right, bottom), (left, top));

    private static RGBAColor32 PixelAt(RgbaImage image, int x, int y)
    {
        var at = (y * image.Width + x) * 4;
        return new RGBAColor32(
            image.Pixels[at], image.Pixels[at + 1], image.Pixels[at + 2], image.Pixels[at + 3]);
    }

    [Fact]
    public void TheCornersAreCutAndTheMiddleIsNot()
    {
        var renderer = NewRenderer(40, 40);

        renderer.FillRoundedRectangle(Rect(0, 0, 40, 40), Opaque, cornerRadius: 10f);

        var image = renderer.Surface;
        PixelAt(image, 0, 0).ShouldBe(Backdrop, "the top-left corner is outside the arc");
        PixelAt(image, 39, 0).ShouldBe(Backdrop);
        PixelAt(image, 0, 39).ShouldBe(Backdrop);
        PixelAt(image, 39, 39).ShouldBe(Backdrop);

        PixelAt(image, 20, 20).ShouldBe(Opaque, "the middle is filled");
        PixelAt(image, 20, 0).ShouldBe(Opaque, "the top edge between the arcs is straight");
        PixelAt(image, 0, 20).ShouldBe(Opaque, "and so is the left edge");
    }

    [Fact]
    public void ATranslucentFillIsEvenEverywhere()
    {
        // The whole reason the default implementation emits non-overlapping spans. A cross-plus-ellipses
        // decomposition passes every other test in this file and fails only this one.
        var renderer = NewRenderer(40, 40);
        var translucent = new RGBAColor32(255, 255, 255, 128);

        renderer.FillRoundedRectangle(Rect(0, 0, 40, 40), translucent, cornerRadius: 12f);

        var image = renderer.Surface;
        var middle = PixelAt(image, 20, 20);
        middle.ShouldNotBe(Backdrop, "the fill must actually have blended");

        // Just inside each arc, where a double-blend would show.
        foreach (var (x, y) in new[] { (5, 5), (34, 5), (5, 34), (34, 34) })
        {
            PixelAt(image, x, y).ShouldBe(middle, $"the corner at ({x},{y}) blended a different number of times");
        }
    }

    [Fact]
    public void AZeroRadiusIsExactlyAPlainRectangle()
    {
        // So a caller can pass a radius through unconditionally and pay nothing when it is off.
        var rounded = NewRenderer(24, 16);
        var square = NewRenderer(24, 16);

        rounded.FillRoundedRectangle(Rect(2, 3, 20, 13), Opaque, cornerRadius: 0f);
        square.FillRectangle(Rect(2, 3, 20, 13), Opaque);

        rounded.Surface.Pixels.ShouldBe(square.Surface.Pixels);
    }

    [Fact]
    public void AnOverLargeRadiusClampsInsteadOfInvertingTheArc()
    {
        // A radius bigger than the shape should give a circle, not a bow-tie or an empty frame.
        var renderer = NewRenderer(40, 40);

        renderer.FillRoundedRectangle(Rect(0, 0, 40, 40), Opaque, cornerRadius: 500f);

        var image = renderer.Surface;
        PixelAt(image, 20, 20).ShouldBe(Opaque, "the centre of a circle is filled");
        PixelAt(image, 20, 0).ShouldBe(Opaque, "and so is the top of its vertical diameter");
        PixelAt(image, 0, 0).ShouldBe(Backdrop, "while the corners are fully cut away");
        PixelAt(image, 39, 39).ShouldBe(Backdrop);
    }

    [Fact]
    public void TheFillStaysInsideTheRect()
    {
        // Rounding the arc inset must never push a span past the edge it was measured from.
        var renderer = NewRenderer(20, 20);

        renderer.FillRoundedRectangle(Rect(5, 5, 15, 15), Opaque, cornerRadius: 4f);

        var image = renderer.Surface;
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                if (x < 5 || x >= 15 || y < 5 || y >= 15)
                {
                    PixelAt(image, x, y).ShouldBe(Backdrop, $"({x},{y}) is outside the requested rect");
                }
            }
        }
    }

    [Fact]
    public void ADegenerateRectDrawsNothing()
    {
        var renderer = NewRenderer(8, 8);

        renderer.FillRoundedRectangle(Rect(4, 4, 4, 4), Opaque, cornerRadius: 3f);

        renderer.Surface.Pixels.ShouldAllBe(b => b == 0 || b == 255);
        PixelAt(renderer.Surface, 4, 4).ShouldBe(Backdrop);
    }
}
