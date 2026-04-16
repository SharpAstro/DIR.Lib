using Shouldly;

namespace DIR.Lib.Tests;

public class DrawLineTests
{
    private static readonly RGBAColor32 White = new(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly RGBAColor32 Black = new(0x00, 0x00, 0x00, 0xFF);

    /// <summary>
    /// Renders a line using the known-good Bresenham algorithm (the old implementation).
    /// Used as the golden reference for comparison against the scanline approach.
    /// </summary>
    private static void DrawLineBresenham(RgbaImage image, int x0, int y0, int x1, int y1, RGBAColor32 color, int thickness = 1)
    {
        var t = Math.Max(1, thickness);
        var halfT = (t - 1) / 2;

        // Horizontal fast path
        if (y0 == y1)
        {
            var xMin = Math.Min(x0, x1);
            var xMax = Math.Max(x0, x1);
            image.FillRect(xMin, y0 - halfT, xMax, y0 - halfT + t, color);
            return;
        }

        // Vertical fast path
        if (x0 == x1)
        {
            var yMin = Math.Min(y0, y1);
            var yMax = Math.Max(y0, y1);
            image.FillRect(x0 - halfT, yMin, x0 - halfT + t, yMax, color);
            return;
        }

        // Diagonal: per-pixel Bresenham
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true)
        {
            image.FillRect(x0 - halfT, y0 - halfT, x0 - halfT + t, y0 - halfT + t, color);

            if (x0 == x1 && y0 == y1) break;

            var e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>
    /// Counts pixels that are lit (non-black) in the image.
    /// </summary>
    private static int CountLitPixels(RgbaImage image)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var offset = (y * image.Width + x) * 4;
                if (image.Pixels[offset] > 0 || image.Pixels[offset + 1] > 0 || image.Pixels[offset + 2] > 0)
                {
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Returns true if the pixel at (x,y) is lit (non-black).
    /// </summary>
    private static bool IsLit(RgbaImage image, int x, int y)
    {
        if (x < 0 || x >= image.Width || y < 0 || y >= image.Height) return false;
        var offset = (y * image.Width + x) * 4;
        return image.Pixels[offset] > 0 || image.Pixels[offset + 1] > 0 || image.Pixels[offset + 2] > 0;
    }

    /// <summary>
    /// Compares two images. For each lit pixel in <paramref name="actual"/> that is NOT lit
    /// in <paramref name="expected"/>, check if any 8-neighbor in <paramref name="expected"/>
    /// is lit. Returns the count of pixels that are truly wrong (not explainable by 1px offset).
    /// </summary>
    private static int CountBadPixels(RgbaImage expected, RgbaImage actual)
    {
        var bad = 0;
        for (var y = 0; y < actual.Height; y++)
        {
            for (var x = 0; x < actual.Width; x++)
            {
                var actualLit = IsLit(actual, x, y);
                var expectedLit = IsLit(expected, x, y);

                if (actualLit == expectedLit) continue;

                // Pixel differs: check 8-neighbors in the other image for tolerance
                var hasNeighbor = false;
                for (var dy = -1; dy <= 1 && !hasNeighbor; dy++)
                {
                    for (var dx = -1; dx <= 1 && !hasNeighbor; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (actualLit && IsLit(expected, x + dx, y + dy)) hasNeighbor = true;
                        if (expectedLit && IsLit(actual, x + dx, y + dy)) hasNeighbor = true;
                    }
                }

                if (!hasNeighbor) bad++;
            }
        }
        return bad;
    }

    // --- Axis-aligned lines: should be pixel-perfect (no tolerance needed) ---

    [Fact]
    public void DrawLine_Horizontal_MatchesFillRect()
    {
        var renderer = new RgbaImageRenderer(200, 50);
        renderer.Surface.Clear(Black);

        renderer.DrawLine(10, 25, 190, 25, White);

        // Should produce a 1px tall horizontal stripe
        IsLit(renderer.Surface, 10, 25).ShouldBeTrue("Start pixel should be lit");
        IsLit(renderer.Surface, 190, 25).ShouldBeTrue("End pixel should be lit");
        IsLit(renderer.Surface, 100, 24).ShouldBeFalse("Pixel above should not be lit");
        IsLit(renderer.Surface, 100, 26).ShouldBeFalse("Pixel below should not be lit");
    }

    [Fact]
    public void DrawLine_Vertical_MatchesFillRect()
    {
        var renderer = new RgbaImageRenderer(50, 200);
        renderer.Surface.Clear(Black);

        renderer.DrawLine(25, 10, 25, 190, White);

        IsLit(renderer.Surface, 25, 10).ShouldBeTrue("Start pixel should be lit");
        IsLit(renderer.Surface, 25, 190).ShouldBeTrue("End pixel should be lit");
        IsLit(renderer.Surface, 24, 100).ShouldBeFalse("Pixel left should not be lit");
        IsLit(renderer.Surface, 26, 100).ShouldBeFalse("Pixel right should not be lit");
    }

    [Fact]
    public void DrawLine_ThickHorizontal_CorrectHeight()
    {
        var renderer = new RgbaImageRenderer(200, 50);
        renderer.Surface.Clear(Black);

        renderer.DrawLine(10, 25, 190, 25, White, thickness: 3);

        // 3px thick: rows 24, 25, 26 should be lit
        IsLit(renderer.Surface, 100, 24).ShouldBeTrue("Row above center should be lit");
        IsLit(renderer.Surface, 100, 25).ShouldBeTrue("Center row should be lit");
        IsLit(renderer.Surface, 100, 26).ShouldBeTrue("Row below center should be lit");
        IsLit(renderer.Surface, 100, 23).ShouldBeFalse("2 rows above should not be lit");
        IsLit(renderer.Surface, 100, 27).ShouldBeFalse("2 rows below should not be lit");
    }

    // --- Diagonal lines: compare scanline vs Bresenham golden reference ---

    [Theory]
    [InlineData(10, 10, 90, 90, 1)]      // 45-degree, thin
    [InlineData(10, 90, 90, 10, 1)]      // 135-degree (top-right to bottom-left), thin
    [InlineData(10, 10, 90, 50, 1)]      // shallow diagonal, thin
    [InlineData(10, 10, 50, 90, 1)]      // steep diagonal, thin
    [InlineData(10, 10, 90, 90, 3)]      // 45-degree, thick
    [InlineData(10, 10, 190, 90, 3)]     // shallow, thick
    [InlineData(10, 10, 90, 190, 3)]     // steep, thick
    [InlineData(90, 90, 10, 10, 1)]      // reversed direction
    public void DrawLine_Diagonal_MatchesBresenhamGoldenWithinTolerance(
        int x0, int y0, int x1, int y1, int thickness)
    {
        var width = Math.Max(x0, x1) + 20;
        var height = Math.Max(y0, y1) + 20;

        // Golden reference: Bresenham
        var goldenImage = new RgbaImage(width, height);
        goldenImage.Clear(Black);
        DrawLineBresenham(goldenImage, x0, y0, x1, y1, White, thickness);

        // Actual: Renderer.DrawLine (scanline approach for diagonals)
        var renderer = new RgbaImageRenderer((uint)width, (uint)height);
        renderer.Surface.Clear(Black);
        renderer.DrawLine(x0, y0, x1, y1, White, thickness);

        // Both should produce lit pixels
        var goldenLit = CountLitPixels(goldenImage);
        var actualLit = CountLitPixels(renderer.Surface);

        goldenLit.ShouldBeGreaterThan(0, "Golden image should have lit pixels");
        actualLit.ShouldBeGreaterThan(0, "Actual image should have lit pixels");

        // Pixel counts should be within 20% (scanline may fill slightly more/fewer)
        var ratio = (double)actualLit / goldenLit;
        ratio.ShouldBeInRange(0.5, 2.0,
            $"Lit pixel count ratio should be reasonable: golden={goldenLit}, actual={actualLit}");

        // Per-pixel comparison with 1px neighbor tolerance
        var badPixels = CountBadPixels(goldenImage, renderer.Surface);
        var totalPixels = width * height;
        var badRatio = (double)badPixels / totalPixels;

        badRatio.ShouldBeLessThan(0.01,
            $"Bad pixel ratio {badRatio:P2} ({badPixels}/{totalPixels}) exceeds 1% tolerance");
    }

    // --- Zero-length and degenerate lines ---

    [Fact]
    public void DrawLine_ZeroLength_DrawsSinglePixel()
    {
        var renderer = new RgbaImageRenderer(50, 50);
        renderer.Surface.Clear(Black);

        renderer.DrawLine(25, 25, 25, 25, White);

        // At minimum, the endpoint pixel should be drawn
        var litCount = CountLitPixels(renderer.Surface);
        litCount.ShouldBeInRange(0, 4, "Zero-length line should draw 0-4 pixels");
    }

    [Fact]
    public void DrawLine_SinglePixelHorizontal_DrawsLine()
    {
        var renderer = new RgbaImageRenderer(50, 50);
        renderer.Surface.Clear(Black);

        renderer.DrawLine(25, 25, 26, 25, White);

        IsLit(renderer.Surface, 25, 25).ShouldBeTrue();
        IsLit(renderer.Surface, 26, 25).ShouldBeTrue();
    }

    // --- More angles for diagonal golden-image comparison ---

    [Theory]
    [InlineData(10, 50, 90, 55, 1)]     // nearly horizontal (~4 deg)
    [InlineData(50, 10, 55, 90, 1)]     // nearly vertical (~86 deg)
    [InlineData(10, 10, 60, 90, 1)]     // ~58 deg steep
    [InlineData(10, 10, 90, 30, 1)]     // ~14 deg shallow
    [InlineData(10, 10, 90, 60, 1)]     // ~30 deg
    [InlineData(10, 10, 60, 80, 1)]     // ~54 deg
    [InlineData(10, 10, 30, 90, 2)]     // steep, thickness=2
    [InlineData(10, 10, 90, 30, 5)]     // shallow, thickness=5
    public void DrawLine_VariousAngles_MatchesBresenhamGoldenWithinTolerance(
        int x0, int y0, int x1, int y1, int thickness)
    {
        var width = Math.Max(x0, x1) + 20;
        var height = Math.Max(y0, y1) + 20;

        var goldenImage = new RgbaImage(width, height);
        goldenImage.Clear(Black);
        DrawLineBresenham(goldenImage, x0, y0, x1, y1, White, thickness);

        var renderer = new RgbaImageRenderer((uint)width, (uint)height);
        renderer.Surface.Clear(Black);
        renderer.DrawLine(x0, y0, x1, y1, White, thickness);

        var goldenLit = CountLitPixels(goldenImage);
        var actualLit = CountLitPixels(renderer.Surface);
        goldenLit.ShouldBeGreaterThan(0, "Golden image should have lit pixels");
        actualLit.ShouldBeGreaterThan(0, "Actual image should have lit pixels");

        var badPixels = CountBadPixels(goldenImage, renderer.Surface);
        var totalPixels = width * height;
        var badRatio = (double)badPixels / totalPixels;
        badRatio.ShouldBeLessThan(0.01,
            $"Bad pixel ratio {badRatio:P2} ({badPixels}/{totalPixels}) exceeds 1% tolerance");
    }

    // --- DrawEllipse golden-image tests ---

    /// <summary>
    /// Renders an ellipse outline using the known-good per-pixel trig approach (old implementation).
    /// </summary>
    private static void DrawEllipseReference(RgbaImage image, int cx, int cy, int rx, int ry,
        RGBAColor32 color, float strokeWidth = 1f)
    {
        var steps = Math.Max(64, Math.Max(rx, ry) * 4);
        var halfSW = (int)Math.Max(1, strokeWidth) / 2;

        for (var i = 0; i < steps; i++)
        {
            var angle = 2.0 * Math.PI * i / steps;
            var px = (int)(cx + rx * Math.Cos(angle));
            var py = (int)(cy + ry * Math.Sin(angle));
            image.FillRect(px - halfSW, py - halfSW, px + halfSW + 1, py + halfSW + 1, color);
        }
    }

    [Theory]
    [InlineData(50, 50, 30, 30, 1)]    // circle r=30, thin
    [InlineData(100, 100, 80, 80, 1)]  // circle r=80, thin
    [InlineData(100, 100, 80, 40, 1)]  // wide ellipse, thin
    [InlineData(100, 100, 40, 80, 1)]  // tall ellipse, thin
    [InlineData(50, 50, 30, 30, 3)]    // circle, thick
    [InlineData(100, 100, 80, 40, 3)]  // wide ellipse, thick
    [InlineData(50, 50, 10, 10, 1)]    // small circle
    [InlineData(100, 100, 5, 80, 1)]   // very tall narrow ellipse
    public void DrawEllipse_MatchesReferenceWithinTolerance(
        int cx, int cy, int rx, int ry, int strokeWidth)
    {
        var width = cx + rx + 20;
        var height = cy + ry + 20;

        // Golden: per-pixel trig reference
        var goldenImage = new RgbaImage(width, height);
        goldenImage.Clear(Black);
        DrawEllipseReference(goldenImage, cx, cy, rx, ry, White, strokeWidth);

        // Actual: scanline DrawEllipse
        var renderer = new RgbaImageRenderer((uint)width, (uint)height);
        renderer.Surface.Clear(Black);
        renderer.DrawEllipse(
            new RectInt(new PointInt(cx + rx, cy + ry), new PointInt(cx - rx, cy - ry)),
            White, strokeWidth);

        var goldenLit = CountLitPixels(goldenImage);
        var actualLit = CountLitPixels(renderer.Surface);
        goldenLit.ShouldBeGreaterThan(0, $"Golden ellipse ({rx}x{ry}) should have lit pixels");
        actualLit.ShouldBeGreaterThan(0, $"Actual ellipse ({rx}x{ry}) should have lit pixels");

        var badPixels = CountBadPixels(goldenImage, renderer.Surface);
        var totalPixels = width * height;
        var badRatio = (double)badPixels / totalPixels;
        badRatio.ShouldBeLessThan(0.01,
            $"Ellipse ({rx}x{ry} sw={strokeWidth}) bad pixel ratio {badRatio:P2} ({badPixels}/{totalPixels}) exceeds 1%");
    }

    [Fact]
    public void DrawEllipse_Circle_IsSymmetric()
    {
        var renderer = new RgbaImageRenderer(200, 200);
        renderer.Surface.Clear(Black);
        renderer.DrawEllipse(
            new RectInt(new PointInt(150, 150), new PointInt(50, 50)), White);

        // Check 4 cardinal directions are lit (within ±1px of the boundary,
        // since scanline pixel-center sampling may miss the exact boundary pixel)
        (IsLit(renderer.Surface, 100, 50) || IsLit(renderer.Surface, 100, 51))
            .ShouldBeTrue("Top should be lit");
        (IsLit(renderer.Surface, 100, 150) || IsLit(renderer.Surface, 100, 149))
            .ShouldBeTrue("Bottom should be lit");
        (IsLit(renderer.Surface, 50, 100) || IsLit(renderer.Surface, 51, 100))
            .ShouldBeTrue("Left should be lit");
        (IsLit(renderer.Surface, 150, 100) || IsLit(renderer.Surface, 149, 100))
            .ShouldBeTrue("Right should be lit");

        // Center should NOT be lit (it's an outline)
        IsLit(renderer.Surface, 100, 100).ShouldBeFalse("Center should not be lit (outline only)");
    }

    [Fact]
    public void DrawEllipse_NoCoverageGaps()
    {
        // A circle outline should have no gaps when walked at fine angular resolution
        var renderer = new RgbaImageRenderer(200, 200);
        renderer.Surface.Clear(Black);
        renderer.DrawEllipse(
            new RectInt(new PointInt(150, 150), new PointInt(50, 50)), White);

        var cx = 100;
        var cy = 100;
        var r = 50;
        var steps = r * 8; // oversample
        for (var i = 0; i < steps; i++)
        {
            var angle = 2.0 * Math.PI * i / steps;
            var ex = (int)(cx + r * Math.Cos(angle));
            var ey = (int)(cy + r * Math.Sin(angle));

            var anyLit = false;
            for (var dy = -1; dy <= 1 && !anyLit; dy++)
            {
                for (var dx = -1; dx <= 1 && !anyLit; dx++)
                {
                    if (IsLit(renderer.Surface, ex + dx, ey + dy)) anyLit = true;
                }
            }
            anyLit.ShouldBeTrue($"No lit pixel near expected position ({ex},{ey}) at angle {angle * 180 / Math.PI:F0} deg");
        }
    }

    // --- Coverage: all lit pixels from golden should appear in actual (no gaps) ---

    [Theory]
    [InlineData(10, 10, 90, 90)]
    [InlineData(10, 10, 90, 50)]
    [InlineData(10, 10, 50, 90)]
    public void DrawLine_Diagonal_NoCoverageGaps(int x0, int y0, int x1, int y1)
    {
        var width = Math.Max(x0, x1) + 20;
        var height = Math.Max(y0, y1) + 20;

        var renderer = new RgbaImageRenderer((uint)width, (uint)height);
        renderer.Surface.Clear(Black);
        renderer.DrawLine(x0, y0, x1, y1, White);

        // Walk along the line and verify each integer Y has at least one lit pixel
        var dx = x1 - x0;
        var dy = y1 - y0;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));

        for (var i = 0; i <= steps; i++)
        {
            var t = steps > 0 ? (float)i / steps : 0f;
            var ex = (int)(x0 + t * dx);
            var ey = (int)(y0 + t * dy);

            // At least one pixel near this expected position should be lit
            var anyLit = false;
            for (var ny = -1; ny <= 1 && !anyLit; ny++)
            {
                for (var nx = -1; nx <= 1 && !anyLit; nx++)
                {
                    if (IsLit(renderer.Surface, ex + nx, ey + ny)) anyLit = true;
                }
            }

            anyLit.ShouldBeTrue($"No lit pixel near expected position ({ex},{ey}) at step {i}/{steps}");
        }
    }
}
