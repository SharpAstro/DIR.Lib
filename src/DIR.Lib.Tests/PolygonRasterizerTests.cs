using DIR.Lib;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The scanline polygon rasterizer, asserted on pixels.
///
/// <para>Every interesting property of a rasterizer is a pixel property, and none of them survive
/// being checked at the API level. The shapes are chosen so the right answer is arithmetic rather
/// than a baseline image: a donut has a hole of known size, and the same two contours read
/// differently under the two fill rules in a known place.</para>
/// </summary>
public class PolygonRasterizerTests
{
    private static readonly RGBAColor32 Black = new(0, 0, 0, 255);
    private static readonly RGBAColor32 White = new(255, 255, 255, 255);

    [Fact]
    public void ARectangleFillsItsInterior()
    {
        var image = Fill([10, 10, 90, 10, 90, 90, 10, 90], [], evenOdd: false);

        Assert.Equal(0, Red(image, 50, 50));
        Assert.Equal(255, Red(image, 5, 5));
    }

    [Fact]
    public void ADonutLeavesItsHoleUnfilled()
    {
        // Inner contour wound the opposite way, which is how a counter is drawn.
        var image = Fill(
            [10, 10, 90, 10, 90, 90, 10, 90, 40, 60, 60, 60, 60, 40, 40, 40],
            [0, 4], evenOdd: false);

        Assert.Equal(0, Red(image, 20, 50));
        Assert.Equal(255, Red(image, 50, 50));
    }

    [Fact]
    public void EvenOddLeavesAHoleWhereWindingDoesNot()
    {
        // Both contours wound the SAME way, so only the fill rule separates the two readings.
        float[] points = [10, 10, 90, 10, 90, 90, 10, 90, 40, 40, 60, 40, 60, 60, 40, 60];

        Assert.Equal(0, Red(Fill(points, [0, 4], evenOdd: false), 50, 50));
        Assert.Equal(255, Red(Fill(points, [0, 4], evenOdd: true), 50, 50));
    }

    [Fact]
    public void EdgesAreAntiAliasedRatherThanSteppedToPixelCentres()
    {
        var image = Fill([10, 10, 50.5f, 10, 50.5f, 90, 10, 90], [], evenOdd: false);

        var edge = Red(image, 50, 50);
        Assert.True(edge is > 0 and < 255, $"expected a partial pixel at the boundary, got {edge}");
    }

    [Fact]
    public void OverlappingShapesInOneMaskDoNotDarkenEachOther()
    {
        // The property the mask exists for: coverage unions rather than sums, so one paint
        // operation made of overlapping pieces is not heavier where the pieces meet.
        var image = new RgbaImage(100, 100);
        image.Clear(White);

        var mask = new CoverageMask(100, 100);
        var rasterizer = new PolygonRasterizer();

        // Two half-covering slivers over the same pixel column.
        rasterizer.FillInto(mask, [10, 10, 50.5f, 10, 50.5f, 90, 10, 90], default, evenOdd: false);
        var once = ReadAfterFlush(rasterizer, image, mask);

        var image2 = new RgbaImage(100, 100);
        image2.Clear(White);
        var mask2 = new CoverageMask(100, 100);
        rasterizer.FillInto(mask2, [10, 10, 50.5f, 10, 50.5f, 90, 10, 90], default, evenOdd: false);
        rasterizer.FillInto(mask2, [10, 10, 50.5f, 10, 50.5f, 90, 10, 90], default, evenOdd: false);
        mask2.FlushTo(image2, Black);

        Assert.True(once is > 0 and < 255, $"expected partial coverage to make this meaningful, got {once}");
        Assert.Equal(once, Red(image2, 50, 50));
    }

    [Fact]
    public void AMaskIsReusableAfterFlushing()
    {
        var image = new RgbaImage(100, 100);
        image.Clear(White);

        var mask = new CoverageMask(100, 100);
        var rasterizer = new PolygonRasterizer();

        rasterizer.FillInto(mask, [10, 10, 40, 10, 40, 40, 10, 40], default, evenOdd: false);
        mask.FlushTo(image, Black);
        Assert.True(mask.IsEmpty);

        // The second shape must not carry any of the first one's coverage with it.
        rasterizer.FillInto(mask, [60, 60, 90, 60, 90, 90, 60, 90], default, evenOdd: false);
        mask.FlushTo(image, Black);

        Assert.Equal(0, Red(image, 20, 20));
        Assert.Equal(0, Red(image, 70, 70));
        Assert.Equal(255, Red(image, 50, 50));
    }

    private static int ReadAfterFlush(PolygonRasterizer _, RgbaImage image, CoverageMask mask)
    {
        mask.FlushTo(image, Black);
        return Red(image, 50, 50);
    }

    private static RgbaImage Fill(float[] points, int[] contourStarts, bool evenOdd)
    {
        var image = new RgbaImage(100, 100);
        image.Clear(White);

        var mask = new CoverageMask(100, 100);
        new PolygonRasterizer().FillInto(mask, points, contourStarts, evenOdd);
        mask.FlushTo(image, Black);
        return image;
    }

    private static int Red(RgbaImage image, int x, int y) => image.Pixels[(y * image.Width + x) * 4];
}
