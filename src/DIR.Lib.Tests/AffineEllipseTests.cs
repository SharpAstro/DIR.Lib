using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The affine ellipse primitive and its CPU default: the image of the unit disc under an arbitrary
/// affine map, given either as four corners or as a centre plus two semi-axis vectors, filled or
/// stroked with a PIXEL width under the one coverage rule the corner <c>DrawEllipse</c> states.
///
/// <para>It sits on the abstract renderer rather than on one backend for the reason
/// <see cref="Renderer{TSurface}.DrawTriangles"/> already records: one missing primitive is enough
/// to pin a whole UI layer to one renderer. A rotated galaxy ellipse drawn over a photograph has to
/// reach the GPU viewer, the CPU raster exporter and the browser alike, so the shape is declared
/// once here with a default every backend inherits, and a GPU renderer overrides it with one
/// draw.</para>
///
/// <para>Ink is opaque red over opaque black, so a pixel's red channel IS its coverage in 255ths,
/// which is what lets area and stroke width be measured as sums rather than counted as pixels.</para>
/// </summary>
public class AffineEllipseTests
{
    private static readonly RGBAColor32 Ink = new(255, 0, 0, 255);
    private static readonly RGBAColor32 Ground = new(0, 0, 0, 255);

    private static RGBAColor32 At(RgbaImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    private static int Red(RgbaImage img, int x, int y) => img.Pixels[((y * img.Width) + x) * 4];

    /// <summary>Total coverage in pixels: every pixel's red channel over 255, summed.</summary>
    private static double CoverageSum(RgbaImage img)
    {
        var sum = 0.0;
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                sum += Red(img, x, y) / 255.0;
        return sum;
    }

    /// <summary>Coverage summed along one row from <paramref name="x0"/> to the right edge.</summary>
    private static double RowCoverage(RgbaImage img, int y, int x0)
    {
        var sum = 0.0;
        for (var x = x0; x < img.Width; x++) sum += Red(img, x, y) / 255.0;
        return sum;
    }

    /// <summary>Coverage summed down one column from <paramref name="y0"/> to the bottom edge.</summary>
    private static double ColumnCoverage(RgbaImage img, int x, int y0)
    {
        var sum = 0.0;
        for (var y = y0; y < img.Height; y++) sum += Red(img, x, y) / 255.0;
        return sum;
    }

    /// <summary>
    /// The two entry points are one shape. The axes form expands to its own corners and reaches the
    /// corner overload, so nothing short of a whole-surface comparison would notice them drifting
    /// apart; the axes are chosen exactly representable so byte equality is a fair demand rather
    /// than a flake waiting on a rounding difference.
    /// </summary>
    [Fact]
    public void TheCornerFormAndTheAxesFormDrawTheSameEllipse()
    {
        using var byAxes = new RgbaImageRenderer(64, 64);
        using var byCorners = new RgbaImageRenderer(64, 64);
        byAxes.Surface.Clear(Ground);
        byCorners.Surface.Clear(Ground);

        var centre = (32f, 32f);
        var u = (20f, 20f);
        var v = (-5f, 5f);

        byAxes.FillEllipse(centre, u, v, Ink);
        byCorners.FillEllipse(
            (centre.Item1 - u.Item1 - v.Item1, centre.Item2 - u.Item2 - v.Item2),
            (centre.Item1 + u.Item1 - v.Item1, centre.Item2 + u.Item2 - v.Item2),
            (centre.Item1 + u.Item1 + v.Item1, centre.Item2 + u.Item2 + v.Item2),
            (centre.Item1 - u.Item1 + v.Item1, centre.Item2 - u.Item2 + v.Item2),
            Ink);

        CoverageSum(byAxes.Surface).ShouldBeGreaterThan(0);
        byAxes.Surface.Pixels.SequenceEqual(byCorners.Surface.Pixels).ShouldBeTrue();
    }

    /// <summary>
    /// The discriminating case, and it is at 45 degrees on purpose: a right-angle turn is only a
    /// swap of width and height, so an implementation that quietly took the bounding box of the
    /// corners would pass it. At 45 degrees that bounding box is a circle of radius 25 which covers
    /// a probe the real ellipse rejects.
    /// </summary>
    [Fact]
    public void ARotatedEllipseTurnsWithItsAxesRatherThanTakingTheirBoundingBox()
    {
        using var r = new RgbaImageRenderer(64, 64);
        r.Surface.Clear(Ground);

        // Semi-major 20*sqrt(2) along (1,1), semi-minor 5*sqrt(2) along (-1,1), about (32,32).
        r.FillEllipse((32f, 32f), (20f, 20f), (-5f, 5f), Ink);

        // On the major axis, well inside.
        At(r.Surface, 46, 46).ShouldBe(Ink);

        // Off the minor axis: 15 px from the centre, so INSIDE the bounding box circle, and 2.2
        // local units out, so outside the ellipse.
        At(r.Surface, 21, 43).ShouldBe(Ground);
    }

    /// <summary>
    /// The twin of the test above, so the discrimination is demonstrated rather than assumed: the
    /// circle that circumscribes the same corners really does cover the probe the ellipse rejected.
    /// Without this, a primitive that drew nothing at all would pass that assertion.
    /// </summary>
    [Fact]
    public void TheCircumscribingCircleDoesCoverTheProbeTheEllipseRejects()
    {
        using var r = new RgbaImageRenderer(64, 64);
        r.Surface.Clear(Ground);

        r.FillEllipse((32f, 32f), (25f, 0f), (0f, 25f), Ink);

        At(r.Surface, 21, 43).ShouldBe(Ink);
    }

    /// <summary>
    /// Area is the invariant that pins the whole map rather than a handful of probes: the image of
    /// the unit disc under an affine map has area <c>pi * |det|</c>, whatever the rotation or shear,
    /// and with an anti-aliased edge the coverage sum lands on it to within the edge's rounding. A
    /// bounding-box implementation of the same input covers pi * 25^2 = 1963 px, so this fails by a
    /// factor of three rather than by a rounding margin.
    /// </summary>
    [Fact]
    public void AShearedEllipseCoversTheAreaItsDeterminantImplies()
    {
        using var r = new RgbaImageRenderer(64, 64);
        r.Surface.Clear(Ground);

        // det = 20*5 - 20*(-5) = 200, so the area is pi * 200 = 628.3 px.
        r.FillEllipse((32f, 32f), (20f, 20f), (-5f, 5f), Ink);

        CoverageSum(r.Surface).ShouldBeInRange(615, 642);
    }

    /// <summary>
    /// The edge is a coverage ramp, not a step. A circle of radius 20.3 puts its boundary 0.2 px past
    /// the centre of pixel 52 on the row through its centre, so that pixel reads about 0.3 covered,
    /// its neighbour inward is solid and its neighbour outward is untouched. A hard-edged
    /// implementation reads 0 or 255 there and nothing between.
    /// </summary>
    [Fact]
    public void TheEdgeIsAntiAliasedByCoverage()
    {
        using var r = new RgbaImageRenderer(64, 64);
        r.Surface.Clear(Ground);

        r.FillEllipse((32f, 32f), (20.3f, 0f), (0f, 20.3f), Ink);

        Red(r.Surface, 51, 32).ShouldBe(255);
        Red(r.Surface, 52, 32).ShouldBeInRange(40, 120);
        Red(r.Surface, 53, 32).ShouldBe(0);
    }

    /// <summary>
    /// The point of a pixel stroke: a 2:1 ellipse stroked 3 px wide crosses its major axis AND its
    /// minor axis in 3 px of ink. The hole-fraction ring this replaced could only make one of those
    /// true, and read 1.5 px across the minor axis when 3 px across the major.
    /// </summary>
    [Fact]
    public void AStrokeIsTheSamePixelWidthAcrossBothAxes()
    {
        using var r = new RgbaImageRenderer(64, 64);
        r.Surface.Clear(Ground);

        r.DrawEllipse((32f, 32f), (24f, 0f), (0f, 12f), Ink, strokeWidth: 3f);

        var acrossMajor = RowCoverage(r.Surface, 32, 32);
        var acrossMinor = ColumnCoverage(r.Surface, 32, 32);

        acrossMajor.ShouldBeInRange(2.8, 3.2);
        acrossMinor.ShouldBeInRange(2.8, 3.2);
        Math.Abs(acrossMajor - acrossMinor).ShouldBeLessThan(0.15);
    }

    /// <summary>
    /// A stroke centred on a closed curve covers perimeter times width, exactly, so long as the
    /// half-width stays under the curve's smallest radius of curvature (6 px here against 1.5).
    /// Ramanujan's perimeter for semi-axes 24 and 12 is 116.3 px, so a 3 px stroke is 349 px of ink
    /// whichever way the ellipse is turned; this one is at 30 degrees so that neither axis lies on
    /// the pixel grid. A hole-fraction ring covering the same corners lays down 239 px.
    /// </summary>
    [Fact]
    public void ARotatedStrokeCoversPerimeterTimesWidth()
    {
        using var r = new RgbaImageRenderer(64, 64);
        r.Surface.Clear(Ground);

        r.DrawEllipse((32f, 32f), (20.7846f, 12f), (-6f, 10.3923f), Ink, strokeWidth: 3f);

        CoverageSum(r.Surface).ShouldBeInRange(336, 362);
    }

    /// <summary>
    /// A stroke is a ring: the interior it encloses is left alone, which is what separates it from a
    /// fill and is the branch a GPU override expresses with one discard.
    /// </summary>
    [Fact]
    public void AStrokeLeavesTheInteriorEmpty()
    {
        using var r = new RgbaImageRenderer(64, 64);
        r.Surface.Clear(Ground);

        r.DrawEllipse((32f, 32f), (20f, 0f), (0f, 20f), Ink, strokeWidth: 4f);

        At(r.Surface, 32, 32).ShouldBe(Ground);   // the centre
        At(r.Surface, 47, 32).ShouldBe(Ground);   // 15 px out, 5 px inside the stroke's inner edge
        At(r.Surface, 51, 32).ShouldBe(Ink);      // 19.5 px out, on the boundary
        At(r.Surface, 32, 60).ShouldBe(Ground);   // outside entirely
    }

    /// <summary>A width of zero or less is no stroke, not a fill and not a hairline.</summary>
    [Fact]
    public void ANonPositiveStrokeDrawsNothing()
    {
        using var r = new RgbaImageRenderer(64, 64);
        r.Surface.Clear(Ground);

        r.DrawEllipse((32f, 32f), (20f, 0f), (0f, 20f), Ink, strokeWidth: 0f);
        r.DrawEllipse((32f, 32f), (20f, 0f), (0f, 20f), Ink, strokeWidth: -2f);

        CoverageSum(r.Surface).ShouldBe(0);
    }

    /// <summary>
    /// A map flattened onto a line has no interior and no inverse. Its width and height are both
    /// non-zero, so a rect-style size check would not catch it; the determinant does.
    /// </summary>
    [Fact]
    public void ADegenerateMapDrawsNothing()
    {
        using var r = new RgbaImageRenderer(64, 64);
        r.Surface.Clear(Ground);

        // v is a multiple of u, so the parallelogram is a segment.
        r.FillEllipse((32f, 32f), (20f, 10f), (-40f, -20f), Ink);

        CoverageSum(r.Surface).ShouldBe(0);
    }

    /// <summary>
    /// The rect-to-corners expansion is one static, so a backend routing its rect overloads through
    /// its affine override cannot pick a different one. A rect's lower-right is exclusive, so the
    /// inscribed ellipse spans exactly the rect.
    /// </summary>
    [Fact]
    public void TheRectExpansionIsTheInscribedEllipse()
    {
        var rect = new RectInt(new PointInt(62, 40), new PointInt(2, 24));

        var (c00, c10, c11, c01) = RgbaImageRenderer.EllipseCorners(rect);

        c00.ShouldBe((2f, 24f));
        c10.ShouldBe((62f, 24f));
        c11.ShouldBe((62f, 40f));
        c01.ShouldBe((2f, 40f));
    }
}
