using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The triangle-list primitive, and its default scanline fill.
///
/// <para>Anything not made of rectangles, ellipses and text is a triangle list — an arrowhead, a
/// chevron, a chart's filled area. Without one on the abstract renderer, a widget that draws such a
/// mark has to reach past the abstraction to whichever backend can, and one missing primitive is
/// enough to pin a whole UI layer to one renderer.</para>
/// </summary>
public class DrawTrianglesTests
{
    private static readonly RGBAColor32 Ink = new(255, 0, 0, 255);
    private static readonly RGBAColor32 Ground = new(0, 0, 0, 255);

    private static RGBAColor32 At(RgbaImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    private static int InkCount(RgbaImage img)
    {
        var n = 0;
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                if (At(img, x, y) != Ground) n++;
        return n;
    }

    [Fact]
    public void ARightTriangleFillsItsInteriorAndNothingElse()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        // (2,2) -- (18,2) -- (2,18): the upper-left half of a 16x16 square.
        r.DrawTriangles([2, 2, 18, 2, 2, 18], Ink);

        At(r.Surface, 4, 4).ShouldBe(Ink);        // well inside
        At(r.Surface, 15, 15).ShouldBe(Ground);   // the other side of the hypotenuse
        At(r.Surface, 1, 1).ShouldBe(Ground);     // outside entirely

        // Half of 16x16, give or take the rows the centre-line rule rounds.
        InkCount(r.Surface).ShouldBeInRange(112, 152);
    }

    /// <summary>Two triangles in one call, which is what makes it a LIST — the pan cursor's four
    /// arrowheads go out as one span rather than four calls.</summary>
    [Fact]
    public void EveryTriangleInTheListIsDrawn()
    {
        using var r = new RgbaImageRenderer(40, 20);
        r.Surface.Clear(Ground);

        r.DrawTriangles([2, 2, 10, 2, 2, 10, 30, 2, 38, 2, 38, 10], Ink);

        At(r.Surface, 4, 4).ShouldBe(Ink);
        At(r.Surface, 36, 4).ShouldBe(Ink);
    }

    /// <summary>
    /// A tip one pixel wide survives. Rounding both ends of a span to the same integer would drop it,
    /// and a mark whose point is missing reads as blunt rather than as a mark drawn slightly wrong —
    /// which is the whole visual purpose of an arrowhead.
    /// </summary>
    [Fact]
    public void ANarrowTipIsNotRoundedAway()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        // Apex at the top, widening downward.
        r.DrawTriangles([10, 2, 4, 16, 16, 16], Ink);

        var topRowInk = 0;
        for (var x = 0; x < 20; x++)
            if (At(r.Surface, x, 2) != Ground) topRowInk++;

        topRowInk.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ADegenerateTriangleDrawsNothingAndDoesNotThrow()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        r.DrawTriangles([5, 5, 5, 5, 5, 5], Ink);        // all three points the same
        r.DrawTriangles([2, 5, 18, 5, 10, 5], Ink);      // colinear, zero height

        InkCount(r.Surface).ShouldBe(0);
    }

    /// <summary>A trailing partial triangle is ignored rather than read past — the span is vertex
    /// data, and a caller that miscounts should not get an out-of-range read.</summary>
    [Fact]
    public void AnIncompleteTrailingTriangleIsIgnored()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        r.DrawTriangles([2, 2, 18, 2, 2, 18, 9, 9], Ink);   // one triangle + a stray vertex

        At(r.Surface, 4, 4).ShouldBe(Ink);
    }

    [Fact]
    public void TrianglesRespectTheClip()
    {
        using var r = new RgbaImageRenderer(20, 20);
        r.Surface.Clear(Ground);

        r.PushClip(new RectInt(new PointInt(20, 20), new PointInt(10, 0)));
        r.DrawTriangles([2, 2, 18, 2, 2, 18], Ink);
        r.PopClip();

        At(r.Surface, 4, 4).ShouldBe(Ground);   // left of the clip
        At(r.Surface, 11, 3).ShouldBe(Ink);     // inside both the triangle and the clip
    }
}
