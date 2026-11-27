using DIR.Lib.Prototyping;
using SharpAstro.Png;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The #330 vocabulary end to end: one workbench-like prototype (tree, tabs, table, buttons of every style, a switch,
/// hover, selectedWhen, bold, mono and label text, a doubleAction) that loads clean, checks clean at every target and
/// renders, plus a baseline of each state it can reach on a dark and a light palette. States are driven in memory
/// through the player and an <see cref="InputRouter"/>, never a window.
/// Set DIR_LIB_UPDATE_BASELINES=1 (with a filter on this class) to write the baselines.
/// </summary>
public sealed class PrototypeShowcaseTests
{
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string MonoFontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSansMono.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";

    private static readonly UiTheme LightTheme = new()
    {
        Palette = NotificationsRenderTests.Light,
        Metrics = Prototype.DefaultTheme.Metrics,
    };

    private static PrototypeDocument Showcase()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Prototype", "showcase.json");
        var result = Prototype.Load(File.ReadAllText(path));
        result.Problems.ShouldBeEmpty();
        return result.Document!;
    }

    [Fact]
    public void ItLoadsWithNoProblems() => Showcase().Screens.Length.ShouldBe(2);

    [Fact]
    public void ItChecksCleanAtEveryTarget()
        => Prototype.Check(Showcase(), Prototype.DefaultTheme, fontPath: FontPath).ShouldBeEmpty();

    [Fact]
    public void ItChecksCleanInTheLightPalette()
        => Prototype.Check(Showcase(), LightTheme, fontPath: FontPath).ShouldBeEmpty();

    [Fact]
    public void EachScreenRenders()
    {
        var images = Prototype.Render(Showcase(), Prototype.DefaultTheme, FontPath);
        images.Select(i => i.Screen).ShouldBe(["workbench", "detail"]);
        images.ShouldAllBe(i => i.Image.Width == 560 && i.Image.Height == 480);
    }

    private sealed class Session
    {
        public readonly RgbaImageRenderer Renderer = new(560, 480);
        public readonly PrototypePlayer<RgbaImage> Player;
        public readonly InputRouter Router;

        public Session(UiTheme theme)
        {
            Player = new PrototypePlayer<RgbaImage>(Renderer, Showcase(), theme, FontPath);
            Player.Ui.MonoFontPath = MonoFontPath;
            Player.State.Set("sel", "a");
            Player.State.Set("pick", "1");
            Player.State.Set("live", true);
            Router = new InputRouter(Player.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [Player] };
            Paint();
        }

        public void Paint()
        {
            Renderer.Surface.Clear(new RGBAColor32(0, 0, 0, 255));
            Player.Render(new RectF32(0, 0, 560, 480));
            Router.AfterPaint();
        }

        public void Move(float x, float y) { Router.Handle(new InputEvent.MouseMove(x, y)); Paint(); }
        public void Down(float x, float y) { Router.Handle(new InputEvent.MouseDown(x, y)); Paint(); }
        public void Up(float x, float y) { Router.Handle(new InputEvent.MouseUp(x, y)); Paint(); }
    }

    public static TheoryData<string> Palettes => ["dark", "light"];

    private static UiTheme Pick(string p) => p == "light" ? LightTheme : Prototype.DefaultTheme;

    // Design-unit points on the workbench screen (read off the rest capture).
    private const float RowBX = 60f, RowBY = 161f;       // tree row "b"
    private const float RunX = 31f, RunY = 279f;         // the accent Run button
    private const float CancelX = 89f, CancelY = 279f;   // the standard Cancel button

    [Theory, MemberData(nameof(Palettes))]
    public void RestMatchesItsBaseline(string palette)
        => CompareBaseline(new Session(Pick(palette)).Renderer.Surface, $"prototype_showcase_{palette}_rest_560x480.png");

    [Theory, MemberData(nameof(Palettes))]
    public void HoverMatchesItsBaseline(string palette)
    {
        var s = new Session(Pick(palette));
        s.Move(CancelX, CancelY);
        s.Paint();
        CompareBaseline(s.Renderer.Surface, $"prototype_showcase_{palette}_hover_560x480.png");
    }

    [Theory, MemberData(nameof(Palettes))]
    public void TreeRowHoverRevealsItsButton(string palette)
    {
        var s = new Session(Pick(palette));
        s.Move(RowBX, RowBY);
        CompareBaseline(s.Renderer.Surface, $"prototype_showcase_{palette}_rowhover_560x480.png");
    }

    [Theory, MemberData(nameof(Palettes))]
    public void PressedMatchesItsBaseline(string palette)
    {
        var s = new Session(Pick(palette));
        // A press that runs its action on the way down has already left the pressed look, so the held press is set
        // the way the router records it, on the window's settings.
        s.Move(RunX, RunY);
        s.Player.Ui.PressOrigin = (RunX, RunY);
        s.Paint();
        CompareBaseline(s.Renderer.Surface, $"prototype_showcase_{palette}_pressed_560x480.png");
    }

    [Theory, MemberData(nameof(Palettes))]
    public void SelectedAndOnMatchItsBaseline(string palette)
    {
        var s = new Session(Pick(palette));
        s.Player.State.Set("ran", "all");
        s.Player.State.Set("linked", true);
        s.Paint();
        CompareBaseline(s.Renderer.Surface, $"prototype_showcase_{palette}_selected_560x480.png");
    }

    [Fact]
    public void APressOnATableRowPicksIt()
    {
        var s = new Session(Prototype.DefaultTheme);
        s.Player.State.Set("pick", "");
        s.Paint();
        s.Down(60f, 243f);
        s.Up(60f, 243f);
        s.Player.State.Get("pick").ShouldBe("2");
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
            var d = Math.Abs(baseline.Pixels[i] - img.Pixels[i]);
            if (d > 0) diffCount++;
            if (d > maxDiff) maxDiff = d;
        }
        maxDiff.ShouldBeLessThanOrEqualTo(2, $"{name}: {diffCount} bytes differ");
    }
}
