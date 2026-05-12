using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Tests for <see cref="Renderer{TSurface}.DrawPolyline"/> /
/// <see cref="Renderer{TSurface}.DrawLineDashed"/> /
/// <see cref="Renderer{TSurface}.DrawPolylineDashed"/>. These are virtual
/// defaults that compose <c>DrawLine</c>; the tests verify segment placement,
/// dash/gap accounting, and that degenerate inputs (single point, zero-length
/// segments) don't throw.
/// </summary>
public class DrawPolylineTests
{
    private static readonly RGBAColor32 White = new(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly RGBAColor32 Black = new(0x00, 0x00, 0x00, 0xFF);

    private static bool IsLit(RgbaImage image, int x, int y)
    {
        if (x < 0 || x >= image.Width || y < 0 || y >= image.Height) return false;
        var offset = (y * image.Width + x) * 4;
        return image.Pixels[offset] > 0 || image.Pixels[offset + 1] > 0 || image.Pixels[offset + 2] > 0;
    }

    private static int CountLitPixels(RgbaImage image)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (IsLit(image, x, y)) count++;
            }
        }
        return count;
    }

    /// <summary>Counts horizontal run-length groups on row <paramref name="y"/> — used
    /// to assert that a dashed line produced N distinct dash runs.</summary>
    private static int CountHorizontalRuns(RgbaImage image, int y, int xMin, int xMax)
    {
        var runs = 0;
        var inRun = false;
        for (var x = xMin; x <= xMax; x++)
        {
            var lit = IsLit(image, x, y);
            if (lit && !inRun) { runs++; inRun = true; }
            else if (!lit && inRun) { inRun = false; }
        }
        return runs;
    }

    // --- DrawPolyline ---

    [Fact]
    public void DrawPolyline_Empty_DoesNothing()
    {
        var renderer = new RgbaImageRenderer(50, 50);
        renderer.Surface.Clear(Black);

        renderer.DrawPolyline([], White);

        CountLitPixels(renderer.Surface).ShouldBe(0);
    }

    [Fact]
    public void DrawPolyline_SinglePoint_DoesNothing()
    {
        var renderer = new RgbaImageRenderer(50, 50);
        renderer.Surface.Clear(Black);

        renderer.DrawPolyline([(25f, 25f)], White);

        CountLitPixels(renderer.Surface).ShouldBe(0);
    }

    [Fact]
    public void DrawPolyline_TwoPoints_EquivalentToDrawLine()
    {
        var a = new RgbaImageRenderer(100, 100);
        a.Surface.Clear(Black);
        a.DrawPolyline([(10f, 50f), (90f, 50f)], White);

        var b = new RgbaImageRenderer(100, 100);
        b.Surface.Clear(Black);
        b.DrawLine(10f, 50f, 90f, 50f, White);

        // Pixel-identical: polyline default impl is one DrawLine call.
        a.Surface.Pixels.ShouldBe(b.Surface.Pixels);
    }

    [Fact]
    public void DrawPolyline_ThreePoints_DrawsBothSegments()
    {
        var renderer = new RgbaImageRenderer(100, 100);
        renderer.Surface.Clear(Black);

        // V-shape: (10,10) -> (50,50) -> (90,10)
        renderer.DrawPolyline([(10f, 10f), (50f, 50f), (90f, 10f)], White);

        // Both endpoints and apex should be lit (within 1px tolerance for diagonals)
        var leftLit = IsLit(renderer.Surface, 10, 10) || IsLit(renderer.Surface, 11, 10) || IsLit(renderer.Surface, 10, 11);
        var apexLit = IsLit(renderer.Surface, 50, 50) || IsLit(renderer.Surface, 49, 50) || IsLit(renderer.Surface, 50, 49);
        var rightLit = IsLit(renderer.Surface, 90, 10) || IsLit(renderer.Surface, 89, 10) || IsLit(renderer.Surface, 90, 11);

        leftLit.ShouldBeTrue("Left endpoint should be lit");
        apexLit.ShouldBeTrue("Apex (shared vertex) should be lit");
        rightLit.ShouldBeTrue("Right endpoint should be lit");
    }

    [Fact]
    public void DrawPolyline_HyperbolaShape_AllRowsLit()
    {
        // Simulates MetricSampleMap's main use case: a smooth curve sampled
        // at 1px x-intervals. Each x should contribute at least one lit pixel
        // (no horizontal gaps in the curve).
        const int W = 200;
        const int H = 100;
        var renderer = new RgbaImageRenderer(W, H);
        renderer.Surface.Clear(Black);

        var points = new (float X, float Y)[W];
        for (var x = 0; x < W; x++)
        {
            // Parabola scaled to fit
            var t = (x - W / 2f) / (W / 2f);
            points[x] = (x, H / 2f + t * t * 30f);
        }

        renderer.DrawPolyline(points, White, thickness: 1);

        // Every x in the interior should have at least one lit pixel within ±1 row
        for (var x = 5; x < W - 5; x++)
        {
            var anyLit = false;
            for (var y = 0; y < H && !anyLit; y++)
            {
                if (IsLit(renderer.Surface, x, y)) anyLit = true;
            }
            anyLit.ShouldBeTrue($"Column x={x} should have at least one lit pixel");
        }
    }

    // --- DrawLineDashed ---

    [Fact]
    public void DrawLineDashed_Horizontal_ProducesAlternatingRuns()
    {
        var renderer = new RgbaImageRenderer(200, 50);
        renderer.Surface.Clear(Black);

        // 180px line, dash=10, gap=10 -> period=20, so 9 dashes
        renderer.DrawLineDashed(10f, 25f, 190f, 25f, White, dashLength: 10f, gapLength: 10f);

        var runs = CountHorizontalRuns(renderer.Surface, 25, 0, 199);
        runs.ShouldBe(9, $"Expected 9 dashes (180/20), got {runs}");
    }

    [Fact]
    public void DrawLineDashed_Horizontal_DashWidth_MatchesParameter()
    {
        var renderer = new RgbaImageRenderer(200, 50);
        renderer.Surface.Clear(Black);

        // 100px line, dash=20, gap=10 -> 4 full periods (80px) + 1 dash (20px) at end
        // First dash should span 20 pixels (x=10..29)
        renderer.DrawLineDashed(10f, 25f, 110f, 25f, White, dashLength: 20f, gapLength: 10f);

        // Count run lengths
        var runLengths = new List<int>();
        var inRun = false;
        var runStart = 0;
        for (var x = 0; x < 200; x++)
        {
            var lit = IsLit(renderer.Surface, x, 25);
            if (lit && !inRun) { inRun = true; runStart = x; }
            else if (!lit && inRun) { inRun = false; runLengths.Add(x - runStart); }
        }
        if (inRun) runLengths.Add(200 - runStart);

        // First dash should be ~20 pixels (allow ±1 for endpoint inclusivity)
        runLengths.ShouldNotBeEmpty();
        runLengths[0].ShouldBeInRange(19, 21, $"First dash length {runLengths[0]} not in [19,21]");
    }

    [Fact]
    public void DrawLineDashed_ZeroDash_FallsBackToSolidLine()
    {
        var a = new RgbaImageRenderer(100, 50);
        a.Surface.Clear(Black);
        a.DrawLineDashed(10f, 25f, 90f, 25f, White, dashLength: 0f, gapLength: 10f);

        var b = new RgbaImageRenderer(100, 50);
        b.Surface.Clear(Black);
        b.DrawLine(10f, 25f, 90f, 25f, White);

        // Solid line fallback should be pixel-identical to DrawLine
        a.Surface.Pixels.ShouldBe(b.Surface.Pixels);
    }

    [Fact]
    public void DrawLineDashed_NegativeGap_FallsBackToSolidLine()
    {
        var a = new RgbaImageRenderer(100, 50);
        a.Surface.Clear(Black);
        a.DrawLineDashed(10f, 25f, 90f, 25f, White, dashLength: 10f, gapLength: -1f);

        var b = new RgbaImageRenderer(100, 50);
        b.Surface.Clear(Black);
        b.DrawLine(10f, 25f, 90f, 25f, White);

        a.Surface.Pixels.ShouldBe(b.Surface.Pixels);
    }

    [Fact]
    public void DrawLineDashed_ZeroLength_DoesNothing()
    {
        var renderer = new RgbaImageRenderer(50, 50);
        renderer.Surface.Clear(Black);

        renderer.DrawLineDashed(25f, 25f, 25f, 25f, White, dashLength: 5f, gapLength: 5f);

        // Zero-length: ux/uy division would be NaN if not guarded; with the guard, no draw.
        CountLitPixels(renderer.Surface).ShouldBe(0);
    }

    [Fact]
    public void DrawLineDashed_Vertical_ProducesVerticalDashes()
    {
        var renderer = new RgbaImageRenderer(50, 200);
        renderer.Surface.Clear(Black);

        renderer.DrawLineDashed(25f, 10f, 25f, 190f, White, dashLength: 10f, gapLength: 10f);

        // 180px / 20px period = 9 dashes (along Y)
        var runs = 0;
        var inRun = false;
        for (var y = 0; y < 200; y++)
        {
            var lit = IsLit(renderer.Surface, 25, y);
            if (lit && !inRun) { runs++; inRun = true; }
            else if (!lit && inRun) { inRun = false; }
        }
        runs.ShouldBe(9);
    }

    // --- DrawPolylineDashed ---

    [Fact]
    public void DrawPolylineDashed_TwoPoints_EquivalentToDrawLineDashed()
    {
        var a = new RgbaImageRenderer(200, 50);
        a.Surface.Clear(Black);
        a.DrawPolylineDashed([(10f, 25f), (190f, 25f)], White, dashLength: 10f, gapLength: 10f);

        var b = new RgbaImageRenderer(200, 50);
        b.Surface.Clear(Black);
        b.DrawLineDashed(10f, 25f, 190f, 25f, White, dashLength: 10f, gapLength: 10f);

        a.Surface.Pixels.ShouldBe(b.Surface.Pixels);
    }

    [Fact]
    public void DrawPolylineDashed_Empty_DoesNothing()
    {
        var renderer = new RgbaImageRenderer(50, 50);
        renderer.Surface.Clear(Black);

        renderer.DrawPolylineDashed([], White, 5f, 5f);

        CountLitPixels(renderer.Surface).ShouldBe(0);
    }

    [Fact]
    public void DrawPolylineDashed_MultipleSegments_EachIndependentlyDashed()
    {
        // Two perpendicular segments. Each should be dashed individually
        // (the dash pattern resets at each vertex — matches ImageMagick).
        var renderer = new RgbaImageRenderer(200, 200);
        renderer.Surface.Clear(Black);

        renderer.DrawPolylineDashed(
            [(10f, 50f), (190f, 50f), (190f, 190f)],
            White, dashLength: 10f, gapLength: 10f);

        // Horizontal segment row 50: 180/20 = 9 dashes. Scan stops at x=189
        // to exclude the shared vertex pixel (190, 50) — the vertical segment's
        // first dash lights it, which would register as a 10th isolated run.
        var hRuns = CountHorizontalRuns(renderer.Surface, 50, 0, 189);
        hRuns.ShouldBe(9, $"Horizontal segment should have 9 dashes, got {hRuns}");

        // Vertical segment column 190: 140/20 = 7 dashes
        var vRuns = 0;
        var inRun = false;
        for (var y = 51; y < 200; y++)
        {
            var lit = IsLit(renderer.Surface, 190, y);
            if (lit && !inRun) { vRuns++; inRun = true; }
            else if (!lit && inRun) { inRun = false; }
        }
        vRuns.ShouldBe(7, $"Vertical segment should have 7 dashes, got {vRuns}");
    }
}
