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

    /// <summary>The structures document with its design width set to <paramref name="width"/>.</summary>
    private static string StructuresAt(uint width) => StructuresJson.Replace("\"width\": 480", $"\"width\": {width}");

    private static readonly UiTheme LightTheme = new()
    {
        Palette = NotificationsRenderTests.Light,
        Metrics = Prototype.DefaultTheme.Metrics,
    };

    /// <summary>
    /// Draws the structures screen with row "a" selected and table row 1 picked; with <paramref name="hoverRowB"/> the
    /// pointer sits on tree row "b", which lights it and shows its hover-revealed button, as a host's paint, settle,
    /// move, paint cycle does.
    /// </summary>
    private static RgbaImage DrawStructures(UiTheme theme, bool hoverRowB, uint width = 480)
        => DrawStructures(new RgbaImageRenderer(width, 360), theme, hoverRowB);

    private static RgbaImage DrawStructures(RgbaImageRenderer renderer, UiTheme theme, bool hoverRowB)
    {
        var width = renderer.Surface.Width;
        // The document is as wide as the surface: the player fits a design to its surface by scaling it, so a 480 design
        // on a 240 surface would be the same layout at half size, never a narrow one.
        var doc = Prototype.Load(StructuresAt(width));
        doc.Problems.ShouldBeEmpty();
        var player = new PrototypePlayer<RgbaImage>(renderer, doc.Document!, theme, FontPath);
        player.State.Set("sel", "a");
        player.State.Set("pick", "1");
        var router = new InputRouter(player.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [player] };
        void Paint()
        {
            renderer.Surface.Clear(new RGBAColor32(0, 0, 0, 255));
            player.Render(new RectF32(0, 0, width, 360));
            router.AfterPaint();
        }
        Paint();
        if (hoverRowB)
        {
            // Row b is the third row of the tree; the rows are 24 tall and its middle is at y 107.
            router.Handle(new InputEvent.MouseMove(60f, 107f));
            Paint();
        }
        return renderer.Surface;
    }

    [Fact]
    public void TreeTabsAndTableMatchTheirBaseline()
        => CompareBaseline(DrawStructures(Prototype.DefaultTheme, false), "prototype_structures_480x360.png");

    [Fact]
    public void TreeRowHoverMatchesItsBaseline()
        => CompareBaseline(DrawStructures(Prototype.DefaultTheme, true), "prototype_structures_hover_480x360.png");

    [Fact]
    public void TreeTabsAndTableInTheLightPaletteMatchTheirBaseline()
        => CompareBaseline(DrawStructures(LightTheme, false), "prototype_structures_light_480x360.png");

    [Fact]
    public void TreeRowHoverInTheLightPaletteMatchesItsBaseline()
        => CompareBaseline(DrawStructures(LightTheme, true), "prototype_structures_light_hover_480x360.png");

    [Fact]
    public void TreeTabsAndTableAtANarrowWidthMatchTheirBaseline()
        => CompareBaseline(DrawStructures(Prototype.DefaultTheme, false, 240), "prototype_structures_240x360.png");

    /// <summary>
    /// At 240 wide the long step name does not fit its row: it is drawn ending in an ellipsis, never cut, and the row's
    /// tooltip holds the whole name.
    /// </summary>
    [Fact]
    public void ALongTreeRowNameEndsInAnEllipsisWithTheWholeNameOnHover()
    {
        const string name = "Remove stars with a very long step name";
        var renderer = new TextRecordingRenderer(240, 360);
        DrawStructures(renderer, Prototype.DefaultTheme, false);

        renderer.Texts.ShouldNotContain(name);
        var drawn = renderer.Texts.Single(t => t.StartsWith("Remove", StringComparison.Ordinal));
        drawn.ShouldEndWith("…");
        name.ShouldStartWith(drawn[..^1]);

        var root = new PrototypeTreeBuilder(Prototype.DefaultTheme, new PrototypeState())
            .Build(Prototype.Load(StructuresAt(240)).Document!.Screens[0]);
        PrototypeKit.Walk(root).ShouldContain(n => n.Tooltip == name && n.Hit is HitResult.ListItemHit);
    }

    /// <summary>An <see cref="RgbaImageRenderer"/> that also keeps every string it was told to draw.</summary>
    private sealed class TextRecordingRenderer(uint width, uint height) : RgbaImageRenderer(width, height)
    {
        public List<string> Texts { get; } = [];

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout,
            TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
        {
            Texts.Add(text.ToString());
            base.DrawText(text, fontFamily, fontSize, fontColor, layout, horizAlignment, vertAlignment);
        }
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
