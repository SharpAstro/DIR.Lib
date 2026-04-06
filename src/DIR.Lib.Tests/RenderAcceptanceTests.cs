using System.Text;
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

    [Fact]
    public void RenderColorGlyphs_Xiangqi()
    {
        var xiangqiFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "BabelStoneXiangqiColour.ttf");
        if (!File.Exists(xiangqiFont))
        {
            // Skip if font not available
            return;
        }

        var img = CreateGridImage(300, 80, gridSpacing: 20);

        // Render Xiangqi pieces — these should be colored (COLR font)
        var pieces = "\U0001FA60\U0001FA61\U0001FA62\U0001FA63"; // first 4 Xiangqi pieces
        RenderColorText(img, pieces, xiangqiFont, 48f, 10, 5);

        CompareBaseline(img, "color_xiangqi.bmp");
    }

    [Fact]
    public void ColorGlyph_IsColored_Flag()
    {
        var xiangqiFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "BabelStoneXiangqiColour.ttf");
        if (!File.Exists(xiangqiFont))
            return;

        // U+1FA60 is a Xiangqi piece — should render as colored (COLR font → BGRA bitmap)
        var glyph = _rasterizer.RasterizeGlyph(xiangqiFont, 48f, new Rune(0x1FA60));

        glyph.Width.ShouldBeGreaterThan(0);
        glyph.IsColored.ShouldBeTrue("Xiangqi glyph should be a color glyph");
    }

    [Fact]
    public void RenderColorGlyphs_NotoEmoji()
    {
        var emojiFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "Noto-COLRv1.ttf");
        if (!File.Exists(emojiFont))
            return;

        var img = CreateGridImage(300, 80, gridSpacing: 20);

        // 😀🎉🌍🔥 — common emoji
        RenderColorText(img, "\U0001F600\U0001F389\U0001F30D\U0001F525", emojiFont, 48f, 10, 5);

        CompareBaseline(img, "color_noto_emoji.bmp");
    }

    [Fact]
    public void RenderColorGlyphs_NotoEmoji_Dice()
    {
        var emojiFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "Noto-COLRv1.ttf");
        if (!File.Exists(emojiFont))
            return;

        var img = CreateGridImage(100, 80, gridSpacing: 20);

        // 🎲 — game die
        RenderColorText(img, "\U0001F3B2", emojiFont, 48f, 10, 5);

        CompareBaseline(img, "color_noto_emoji_dice.bmp");
    }

    [Fact]
    public void ColorGlyph_NotoEmoji_IsColored()
    {
        var emojiFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "Noto-COLRv1.ttf");
        if (!File.Exists(emojiFont))
            return;

        var glyph = _rasterizer.RasterizeGlyph(emojiFont, 48f, new Rune(0x1F600)); // 😀
        glyph.Width.ShouldBeGreaterThan(0);
        glyph.IsColored.ShouldBeTrue("Noto COLRv1 emoji should be a color glyph");
    }

    [Theory]
    [InlineData(0x1F52D, "telescope")]
    [InlineData(0x1F4C5, "calendar")]
    [InlineData(0x1F30C, "milky_way")]
    [InlineData(0x1F3AF, "bullseye")]
    [InlineData(0x1F3B2, "game_die")]
    public void RenderColorGlyph_TianWenSidebarEmoji(int codepoint, string name)
    {
        var emojiFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "Noto-COLRv1.ttf");
        if (!File.Exists(emojiFont))
            return;

        var rune = new Rune(codepoint);
        var glyph = _rasterizer.RasterizeGlyph(emojiFont, 32f, rune);

        glyph.Width.ShouldBeGreaterThan(0, $"Emoji U+{codepoint:X4} ({name}) should have non-zero width");
        glyph.Height.ShouldBeGreaterThan(0, $"Emoji U+{codepoint:X4} ({name}) should have non-zero height");
        glyph.IsColored.ShouldBeTrue($"Emoji U+{codepoint:X4} ({name}) should be a color glyph");
        glyph.AdvanceX.ShouldBeGreaterThan(0, $"Emoji U+{codepoint:X4} ({name}) should have non-zero advance");
    }

    [Fact]
    public void RenderColorGlyphs_TianWenSidebar()
    {
        var emojiFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "Noto-COLRv1.ttf");
        if (!File.Exists(emojiFont))
            return;

        var img = CreateGridImage(300, 80, gridSpacing: 20);

        // TianWen GUI sidebar icons: 🔭📅🌌🎯
        RenderColorText(img, "\U0001F52D\U0001F4C5\U0001F30C\U0001F3AF", emojiFont, 48f, 10, 5);

        CompareBaseline(img, "color_tianwen_sidebar.bmp");
    }

    [Fact]
    public void RenderText_ChessPieces()
    {
        var meridaFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "Merida.ttf");
        if (!File.Exists(meridaFont))
            return;

        var img = CreateGridImage(400, 80, gridSpacing: 20);

        // Chess pieces: ♔♕♖♗♘♙ (white) ♚♛♜♝♞♟ (black)
        RenderText(img, "\u2654\u2655\u2656\u2657\u2658\u2659", meridaFont, 48f,
            new RGBAColor32(255, 255, 255, 255), 10, 5);
        RenderText(img, "\u265A\u265B\u265C\u265D\u265E\u265F", meridaFont, 48f,
            new RGBAColor32(40, 40, 40, 255), 10, 40);

        CompareBaseline(img, "text_chess_pieces.bmp");
    }

    [Theory]
    [InlineData(256, 2)]
    [InlineData(48, 2)]
    [InlineData(32, 1)]
    [InlineData(24, 1)]
    [InlineData(16, 1)]
    public void RenderChess_WhiteKnightOnCheckerboard(int size, int tiles)
    {
        var meridaFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "Merida.ttf");
        if (!File.Exists(meridaFont))
            return;

        var tileSize = size / tiles;
        var blackSquare = new RGBAColor32(0xD1, 0x8B, 0x47, 0xff);
        var whiteSquare = new RGBAColor32(0xFF, 0xCE, 0x9E, 0xff);

        var img = new RgbaImage(size, size);

        // Draw checkerboard
        for (var row = 0; row < tiles; row++)
        for (var col = 0; col < tiles; col++)
        {
            var isDark = (row + col) % 2 == 0;
            var fill = isDark ? blackSquare : whiteSquare;
            img.FillRect(col * tileSize, row * tileSize, (col + 1) * tileSize, (row + 1) * tileSize, fill);
        }

        // Draw white knight centered on the image,
        // same technique as GameUI.DrawPiece: outline glyph first, then fill glyph
        var fontColorWhite = new RGBAColor32(0xfd, 0xfd, 0xfd, 0xff);
        var fontColorBlack = new RGBAColor32(0, 0, 0, 0xff);
        var fontSize = size * 0.8f;

        RenderCenteredPiece(img, "\u265E", meridaFont, fontSize, fontColorWhite, 0, 0, size);
        RenderCenteredPiece(img, "\u2658", meridaFont, fontSize, fontColorBlack, 0, 0, size);

        CompareBaseline(img, $"chess_white_knight_{size}x{size}.bmp");
    }

    [Theory]
    [InlineData(0x1F327, "cloud_with_rain")]     // 🌧
    [InlineData(0x1F32B, "fog")]                  // 🌫
    [InlineData(0x1F319, "crescent_moon")]        // 🌙
    [InlineData(0x2601,  "cloud")]                // ☁
    [InlineData(0x26C5,  "sun_behind_cloud")]     // ⛅
    [InlineData(0x2600,  "sun")]                  // ☀
    [InlineData(0x26C8,  "thunder")]              // ⛈
    [InlineData(0x2744,  "snowflake")]            // ❄
    public void RenderColorGlyph_WeatherEmoji(int codepoint, string name)
    {
        var emojiFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "Noto-COLRv1.ttf");
        if (!File.Exists(emojiFont))
            return;

        var rune = new Rune(codepoint);
        var glyph = _rasterizer.RasterizeGlyph(emojiFont, 32f, rune);

        glyph.Width.ShouldBeGreaterThan(0, $"Weather emoji U+{codepoint:X4} ({name}) should have non-zero width");
        glyph.Height.ShouldBeGreaterThan(0, $"Weather emoji U+{codepoint:X4} ({name}) should have non-zero height");
        glyph.AdvanceX.ShouldBeGreaterThan(0, $"Weather emoji U+{codepoint:X4} ({name}) should have non-zero advance");
    }

    [Fact]
    public void RenderColorGlyphs_WeatherBand()
    {
        var emojiFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "Noto-COLRv1.ttf");
        if (!File.Exists(emojiFont))
            return;

        var img = CreateGridImage(800, 120, gridSpacing: 20);

        // All weather icons used in the planner: 🌧🌫☁⛅☀🌙⛈❄
        RenderColorText(img, "\U0001F327\U0001F32B\u2601\u26C5\u2600\U0001F319\u26C8\u2744", emojiFont, 80f, 10, 10);

        CompareBaseline(img, "color_weather_emoji.bmp");
    }

    [Fact]
    public void DrawText_SupplementaryPlaneEmoji_DoesNotCrash()
    {
        var emojiFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "Noto-COLRv1.ttf");
        if (!File.Exists(emojiFont))
            return;

        // Regression test: supplementary plane characters (U+10000+) previously crashed
        // RgbaImageRenderer.DrawText due to char-based iteration splitting surrogate pairs
        using var renderer = new RgbaImageRenderer(200, 60);
        var layout = new RectInt(new PointInt(200, 60), new PointInt(0, 0));

        // Should not throw ArgumentOutOfRangeException
        renderer.DrawText("\U0001F327\U0001F319\U0001F32B", emojiFont, 24f,
            new RGBAColor32(255, 255, 255, 255), layout, TextAlign.Center, TextAlign.Center);
    }

    [Fact]
    public void GrayscaleGlyph_IsNotColored()
    {
        var glyph = _rasterizer.RasterizeGlyph(FontPath, 24f, new Rune('A'));
        glyph.IsColored.ShouldBeFalse("DejaVuSans 'A' should be a grayscale glyph");
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

        var maxAscent = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var g = _rasterizer.RasterizeGlyph(fontPath, fontSize, rune);
            if (g.BearingY > maxAscent) maxAscent = g.BearingY;
        }

        var baseline = y + maxAscent;

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                var space = _rasterizer.RasterizeGlyph(fontPath, fontSize, new Rune('n'));
                penX += space.AdvanceX;
                continue;
            }

            var glyph = _rasterizer.RasterizeGlyph(fontPath, fontSize, rune);
            if (glyph.Width == 0) { penX += glyph.AdvanceX; continue; }

            var gx = (int)(penX + glyph.BearingX);
            var gy = baseline - glyph.BearingY;

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

    private void RenderCenteredPiece(RgbaImage img, string text, string fontPath, float fontSize, RGBAColor32 color, int cellX, int cellY, int cellSize)
    {
        fontSize = MathF.Round(fontSize);
        var rune = text.EnumerateRunes().First();
        var glyph = _rasterizer.RasterizeGlyph(fontPath, fontSize, rune);
        if (glyph.Width == 0) return;

        // Center the glyph within the cell
        var gx = cellX + (cellSize - glyph.Width) / 2;
        var gy = cellY + (cellSize - glyph.Height) / 2;

        BlitGlyphTinted(img, gx, gy, glyph, color);
    }

    private void RenderColorText(RgbaImage img, string text, string fontPath, float fontSize, int x, int y)
    {
        var penX = (float)x;
        fontSize = MathF.Round(fontSize);

        var maxAscent = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var g = _rasterizer.RasterizeGlyph(fontPath, fontSize, rune);
            if (g.BearingY > maxAscent) maxAscent = g.BearingY;
        }

        var baseline = y + maxAscent;

        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = _rasterizer.RasterizeGlyph(fontPath, fontSize, rune);
            if (glyph.Width == 0) { penX += glyph.AdvanceX; continue; }

            var gx = (int)(penX + glyph.BearingX);
            var gy = baseline - glyph.BearingY;

            if (glyph.IsColored)
                img.BlitRgba(gx, gy, glyph.Rgba, glyph.Width, glyph.Height);
            else
                BlitGlyphTinted(img, gx, gy, glyph, new RGBAColor32(255, 255, 255, 255));

            penX += glyph.AdvanceX;
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
