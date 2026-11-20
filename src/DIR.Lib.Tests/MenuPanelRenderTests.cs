using System;
using System.IO;
using DIR.Lib;
using SharpAstro.Png;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The menu panel drawn for real, at a wide and a narrow surface, with content shorter than the card: the case where
/// the card is wider than what it holds (clamped up to <see cref="MenuPanelStyle.MinWidth"/> when wide, full width
/// when portrait). The title is centred in the card and Back and close sit in its two top corners. Until 11.87 the
/// content sat at the card's left edge here, which no test drew. Pixel baselines live in <c>Baselines/</c>;
/// regenerate with <c>DIR_LIB_UPDATE_BASELINES=1</c>.
/// </summary>
public class MenuPanelRenderTests
{
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    [Theory]
    [InlineData(1200u, 800u, "menu_panel_short_wide.png")]
    [InlineData(360u, 640u, "menu_panel_short_narrow.png")]
    public void AShortMenuIsCentredInItsCard_WithBackAndCloseInTheCorners(uint width, uint height, string baseline)
    {
        var renderer = new RgbaImageRenderer(width, height);
        // The page behind the scrim, so the card's place on the surface shows.
        renderer.Surface.Clear(new RGBAColor32(0x30, 0x4a, 0x6a, 0xff));
        var menu = new PixelMenuWidget<RgbaImage>(renderer, FontPath);
        menu.Reset("Paused", "Pick one", ["Resume", "Quit"]);
        menu.Back = static () => { };
        menu.Close = static () => { };

        menu.RenderPanel();

        CompareBaseline(renderer.Surface, baseline);
    }

    private static void CompareBaseline(RgbaImage img, string name)
    {
        var baselinePath = Path.Combine(BaselineDir, name);
        if (UpdateBaselines)
        {
            Directory.CreateDirectory(SourceBaselineDir);
            Directory.CreateDirectory(BaselineDir);
            var png = PngWriter.Encode(img.Pixels, img.Width, img.Height);
            File.WriteAllBytes(Path.Combine(SourceBaselineDir, name), png);
            File.WriteAllBytes(baselinePath, png);
            return;
        }

        File.Exists(baselinePath).ShouldBeTrue($"Baseline '{name}' is missing; generate it with DIR_LIB_UPDATE_BASELINES=1.");
        var baseline = PngReader.Decode(File.ReadAllBytes(baselinePath));
        baseline.Width.ShouldBe(img.Width);
        baseline.Height.ShouldBe(img.Height);

        var maxDiff = 0;
        var diffCount = 0;
        for (var i = 0; i < baseline.Pixels.Length; i++)
        {
            var diff = Math.Abs(img.Pixels[i] - baseline.Pixels[i]);
            if (diff > 0) diffCount++;
            if (diff > maxDiff) maxDiff = diff;
        }

        if (maxDiff > 2)
        {
            var actualPath = Path.ChangeExtension(baselinePath, ".actual.png");
            PngWriter.Save(actualPath, img.Pixels, img.Width, img.Height);
            Assert.Fail($"Baseline mismatch for '{name}': {diffCount} pixels differ, max diff={maxDiff}. Actual saved to '{actualPath}'.");
        }
    }
}
