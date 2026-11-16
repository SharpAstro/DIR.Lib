using DIR.Lib.Prototyping;
using SharpAstro.Png;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Prototype.Render"/> of the sample, one image per screen, against baseline PNGs.
/// Set DIR_LIB_UPDATE_BASELINES=1 (with a filter on this class) to write them.
/// </summary>
public sealed class PrototypeRenderTests
{
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";

    [Theory]
    [InlineData("home")]
    [InlineData("form")]
    [InlineData("about")]
    public void SampleScreenMatchesItsBaseline(string screenId)
    {
        var images = Prototype.Render(PrototypeKit.Sample(), (360u, 640u), Prototype.DefaultTheme, FontPath);
        var image = images.Single(i => i.Screen == screenId).Image;
        CompareBaseline(image, $"prototype_sample_{screenId}_360x640.png");
    }

    [Fact]
    public void RenderReturnsOneImagePerScreenInDocumentOrder()
    {
        var images = Prototype.Render(PrototypeKit.Sample(), (360u, 640u), Prototype.DefaultTheme, FontPath);
        images.Select(i => i.Screen).ShouldBe(["home", "form", "about"]);
        images.ShouldAllBe(i => i.Image.Width == 360 && i.Image.Height == 640);
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
