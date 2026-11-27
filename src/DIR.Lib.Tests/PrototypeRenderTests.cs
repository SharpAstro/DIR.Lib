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

    private const string StructuresJson = """
        { "width": 480, "height": 360, "entry": "s", "screens": [ { "id": "s", "root": {
          "kind": "vstack", "gap": 12, "padding": 12, "background": "ContentBg", "children": [
            { "kind": "tabs", "options": ["Project", "Frames", "Processes"], "bind": "tab" },
            { "kind": "tree", "bind": "sel", "children": [
              { "kind": "treeRow", "key": "g", "text": "Calibrate", "mark": "CaretDown", "note": "2 steps" },
              { "kind": "treeRow", "key": "a", "text": "Remove stars with a very long step name", "depth": 1, "note": "ready",
                "trailingIcon": "Plus", "trailingAction": "set:ran=a" },
              { "kind": "treeRow", "key": "b", "text": "Stack", "depth": 1, "trailingIcon": "Plus" } ] },
            { "kind": "table", "columns": ["File", "Kind", "Exposure"], "columnWidths": [140, "star", 80], "children": [
              { "kind": "tableRow", "selectedWhen": "pick=1", "children": [
                { "kind": "text", "text": "frame_0001.fits" }, { "kind": "text", "text": "light" }, { "kind": "text", "text": "300 s" } ] },
              { "kind": "tableRow", "children": [
                { "kind": "text", "text": "dark_0001.fits" }, { "kind": "text", "text": "dark" }, { "kind": "text", "text": "300 s" } ] } ] }
          ] } } ] }
        """;

    [Fact]
    public void TreeTabsAndTableMatchTheirBaseline()
    {
        var doc = Prototype.Load(StructuresJson);
        doc.Problems.ShouldBeEmpty();
        var image = Prototype.Render(doc.Document!, (480u, 360u), Prototype.DefaultTheme, FontPath).Single().Image;
        CompareBaseline(image, "prototype_structures_480x360.png");
    }

    [Fact]
    public void RenderReturnsOneImagePerScreenInDocumentOrder()
    {
        var images = Prototype.Render(PrototypeKit.Sample(), (360u, 640u), Prototype.DefaultTheme, FontPath);
        images.Select(i => i.Screen).ShouldBe(["home", "form", "about"]);
        images.ShouldAllBe(i => i.Image.Width == 360 && i.Image.Height == 640);
    }

    [Fact]
    public void RenderAtTargetsReturnsOneNamedImageSizedByWidthTimesScalePerTargetAndScreen()
    {
        var doc = Prototype.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Prototype", "targets.json"))).Document!;
        var images = Prototype.Render(doc, Prototype.DefaultTheme, FontPath);

        images.Select(i => (i.Target, i.Screen)).ShouldBe([("phone", "home"), ("desktop", "home")]);
        var phone = images.Single(i => i.Target == "phone").Image;
        (phone.Width, phone.Height).ShouldBe((780, 1688));
        var desktop = images.Single(i => i.Target == "desktop").Image;
        (desktop.Width, desktop.Height).ShouldBe((1440, 900));
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
