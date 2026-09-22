using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The affine ellipse primitive and its CPU default: the image of the unit disc under an arbitrary
/// affine map, given either as four corners or as a centre plus two semi-axis vectors.
///
/// <para>It sits on the abstract renderer rather than on one backend for the reason
/// <see cref="Renderer{TSurface}.DrawTriangles"/> already records: one missing primitive is enough
/// to pin a whole UI layer to one renderer. A rotated galaxy ellipse drawn over a photograph has to
/// reach the GPU viewer, the CPU raster exporter and the browser alike, so the shape is declared
/// once here with a default every backend inherits, and a GPU renderer overrides it with one
/// draw.</para>
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

    private static int InkCount(RgbaImage img)
    {
        var n = 0;
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                if (At(img, x, y) != Ground) n++;
        return n;
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

        InkCount(byAxes.Surface).ShouldBeGreaterThan(0);
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
    /// the unit disc under an affine map has area <c>pi * |det|</c>, whatever the rotation or shear.
    /// A bounding-box implementation of the same input covers pi * 25^2 = 1963 px, so this fails by
    /// a factor of three rather than by a rounding margin.
    /// </summary>
    [Fact]
    public void AShearedEllipseCoversTheAreaItsDeterminantImplies()
    {
        using var r = new RgbaImageRenderer(64, 64);
        r.Surface.Clear(Ground);

        // det = 20*5 - 20*(-5) = 200, so the area is pi * 200 = 628.3 px.
        r.FillEllipse((32f, 32f), (20f, 20f), (-5f, 5f), Ink);

        InkCount(r.Surface).ShouldBeInRange(578, 679);
    }

    /// <summary>
    /// A hole in local units, which is what a GPU override can express with one discard. The ring
    /// row is two runs, so this also covers the run-closing branch in the default.
    /// </summary>
    [Fact]
    public void ARingLeavesItsCentreHollow()
    {
        using var r = new RgbaImageRenderer(64, 64);
        r.Surface.Clear(Ground);

        r.DrawEllipse((32f, 32f), (20f, 0f), (0f, 20f), Ink, innerRadius: 0.5f);

        At(r.Surface, 32, 32).ShouldBe(Ground);   // inside the hole
        At(r.Surface, 47, 32).ShouldBe(Ink);      // 15 px out, so between 0.5 and 1.0 local
        At(r.Surface, 32, 63).ShouldBe(Ground);   // outside entirely
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

        InkCount(r.Surface).ShouldBe(0);
    }
}
