using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Acceptance tests that render text onto a grid background and compare against baseline images.
/// Set environment variable DIR_LIB_UPDATE_BASELINES=1 to regenerate baselines.
/// </summary>
public class RenderAcceptanceTests : IDisposable
{
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";

    private readonly FreeTypeGlyphRasterizer _rasterizer = new();

    [Fact]
    public void RenderGrid_WithCenterlines()
    {
        var img = CreateGridImage(200, 200, gridSpacing: 20);
        CompareBaseline(img, "grid_200x200.bmp");
    }

    [Fact]
    public void RenderText_HelloWorld()
    {
        var img = CreateGridImage(300, 60, gridSpacing: 20);
        RenderText(img, "Hello, World!", FontPath, 24f, new RGBAColor32(255, 255, 255, 255), 10, 10);
        CompareBaseline(img, "text_hello_world.bmp");
    }

    [Fact]
    public void RenderText_BaselineAlignment()
    {
        // Renders "Agp" — characters with ascenders, x-height, and descenders
        var img = CreateGridImage(200, 80, gridSpacing: 20);
        RenderText(img, "Agp", FontPath, 36f, new RGBAColor32(255, 200, 0, 255), 10, 10);
        CompareBaseline(img, "text_baseline_agp.bmp");
    }

    [Fact]
    public void RenderText_MultipleLines()
    {
        var img = CreateGridImage(250, 120, gridSpacing: 20);
        RenderText(img, "Line 1", FontPath, 20f, new RGBAColor32(255, 255, 255, 255), 10, 5);
        RenderText(img, "Line 2", FontPath, 20f, new RGBAColor32(200, 200, 100, 255), 10, 35);
        RenderText(img, "Sizes!", FontPath, 32f, new RGBAColor32(100, 200, 255, 255), 10, 65);
        CompareBaseline(img, "text_multiline.bmp");
    }

    [Fact]
    public void RenderMixedContent_RectAndText()
    {
        var img = CreateGridImage(200, 200, gridSpacing: 20);

        // Draw a filled rectangle
        img.FillRect(20, 20, 180, 60, new RGBAColor32(60, 60, 120, 255));
        // Draw text on top
        RenderText(img, "Box", FontPath, 28f, new RGBAColor32(255, 255, 255, 255), 60, 22);

        // Draw a semi-transparent overlay
        img.FillRect(40, 80, 160, 160, new RGBAColor32(200, 50, 50, 128));
        RenderText(img, "Alpha", FontPath, 24f, new RGBAColor32(255, 255, 255, 255), 50, 100);

        CompareBaseline(img, "mixed_rect_text.bmp");
    }

    public void Dispose() => _rasterizer.Dispose();

    /// <summary>
    /// Creates an image with a dark background, light grid lines, and brighter center crosshairs.
    /// </summary>
    private static RgbaImage CreateGridImage(int width, int height, int gridSpacing)
    {
        var img = new RgbaImage(width, height);
        var bg = new RGBAColor32(30, 30, 40, 255);
        var gridColor = new RGBAColor32(50, 50, 70, 255);
        var centerColor = new RGBAColor32(80, 80, 120, 255);

        img.Clear(bg);

        // Grid lines
        for (var x = 0; x < width; x += gridSpacing)
            img.DrawVLine(x, 0, height, gridColor);
        for (var y = 0; y < height; y += gridSpacing)
            img.DrawHLine(0, width, y, gridColor);

        // Center crosshairs (brighter)
        var cx = width / 2;
        var cy = height / 2;
        img.DrawHLine(0, width, cy, centerColor);
        img.DrawVLine(cx, 0, height, centerColor);

        return img;
    }

    private void RenderText(RgbaImage img, string text, string fontPath, float fontSize, RGBAColor32 color, int x, int y)
    {
        var penX = (float)x;
        fontSize = MathF.Round(fontSize);

        // First pass: measure max ascent for baseline
        var maxAscent = 0;
        foreach (var ch in text)
        {
            var g = _rasterizer.RasterizeGlyph(fontPath, fontSize, ch);
            if (g.BearingY > maxAscent) maxAscent = g.BearingY;
        }

        var baseline = y + maxAscent;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                var space = _rasterizer.RasterizeGlyph(fontPath, fontSize, 'n');
                penX += space.AdvanceX;
                continue;
            }

            var glyph = _rasterizer.RasterizeGlyph(fontPath, fontSize, ch);
            if (glyph.Width == 0) continue;

            var gx = (int)(penX + glyph.BearingX);
            var gy = baseline - glyph.BearingY;

            // Tint glyph with color
            BlitGlyphTinted(img, gx, gy, glyph, color);
            penX += glyph.AdvanceX;
        }
    }

    private static void BlitGlyphTinted(RgbaImage img, int dstX, int dstY, GlyphBitmap glyph, RGBAColor32 color)
    {
        var src = glyph.Rgba;
        var w = glyph.Width;
        var h = glyph.Height;
        var pixels = img.Pixels;
        var surfW = img.Width;
        var surfH = img.Height;

        for (var sy = 0; sy < h; sy++)
        {
            var dy = dstY + sy;
            if (dy < 0 || dy >= surfH) continue;
            var srcRow = sy * w * 4;
            var dstRow = dy * surfW * 4;
            for (var sx = 0; sx < w; sx++)
            {
                var dx = dstX + sx;
                if (dx < 0 || dx >= surfW) continue;
                var alpha = src[srcRow + sx * 4 + 3];
                if (alpha == 0) continue;
                var di = dstRow + dx * 4;
                if (alpha == 255)
                {
                    pixels[di] = color.Red;
                    pixels[di + 1] = color.Green;
                    pixels[di + 2] = color.Blue;
                    pixels[di + 3] = 255;
                }
                else
                {
                    var inv = 256 - alpha;
                    var a = alpha + 1;
                    pixels[di] = (byte)((color.Red * a + pixels[di] * inv) >> 8);
                    pixels[di + 1] = (byte)((color.Green * a + pixels[di + 1] * inv) >> 8);
                    pixels[di + 2] = (byte)((color.Blue * a + pixels[di + 2] * inv) >> 8);
                    pixels[di + 3] = (byte)Math.Min(255, pixels[di + 3] + alpha - (pixels[di + 3] * alpha >> 8));
                }
            }
        }
    }

    private static void CompareBaseline(RgbaImage img, string name)
    {
        var baselinePath = Path.Combine(BaselineDir, name);
        var sourceBaselinePath = Path.Combine(SourceBaselineDir, name);

        if (UpdateBaselines)
        {
            Directory.CreateDirectory(SourceBaselineDir);
            BmpWriter.Save(sourceBaselinePath, img.Pixels, img.Width, img.Height);
            return;
        }

        if (!File.Exists(baselinePath))
        {
            Directory.CreateDirectory(BaselineDir);
            BmpWriter.Save(baselinePath, img.Pixels, img.Width, img.Height);
            Assert.Fail($"Baseline '{name}' did not exist — generated. Re-run tests or set DIR_LIB_UPDATE_BASELINES=1.");
            return;
        }

        var (baseline, bw, bh) = BmpReader.Load(baselinePath);
        bw.ShouldBe(img.Width, $"Width mismatch for '{name}'");
        bh.ShouldBe(img.Height, $"Height mismatch for '{name}'");

        // Allow small per-pixel differences (anti-aliasing may vary slightly)
        var maxDiff = 0;
        var diffCount = 0;
        for (var i = 0; i < baseline.Length; i++)
        {
            var diff = Math.Abs(img.Pixels[i] - baseline[i]);
            if (diff > 0) diffCount++;
            if (diff > maxDiff) maxDiff = diff;
        }

        if (maxDiff > 2)
        {
            var actualPath = Path.ChangeExtension(baselinePath, ".actual.bmp");
            BmpWriter.Save(actualPath, img.Pixels, img.Width, img.Height);
            Assert.Fail($"Baseline mismatch for '{name}': {diffCount} pixels differ, max diff={maxDiff}. Actual saved to '{actualPath}'.");
        }
    }
}
