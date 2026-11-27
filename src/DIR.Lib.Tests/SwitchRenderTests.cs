using System;
using System.IO;
using DIR.Lib.Layout;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Builder.Switch"/> drawn in pixels (<see cref="RgbaImageRenderer"/>, DejaVu Sans) on a light and a dark palette:
/// six rows (off, on, hover, keyboard focus, disabled off, disabled on), each with a label. Baselines live in
/// <c>Baselines/</c>; <c>DIR_LIB_UPDATE_BASELINES=1</c> with a filter on this class writes them; read each before keeping it.
/// </summary>
public class SwitchRenderTests
{
    private const float W = 240f;
    private const float H = 252f;
    private const float Pad = 12f;
    private const float Row = 28f;
    private const float RowGap = 8f;

    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static UiTheme ThemeOf(UiPalette p) => new() { Palette = p, Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f) };

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Node root)
        {
            BeginFrame();
            RenderLayout(root, new RectF32(0, 0, W, H), FontPath, DesignScale.One);
        }
    }

    private static RgbaImage Draw(UiTheme theme)
    {
        var cursor = new ListCursor();
        HitResult Id(int i) => new HitResult.ListItemHit("switches", i);
        var tree = Builder.VStack(
                Builder.Switch(false, _ => { }, theme, "Off", hit: Id(0)),
                Builder.Switch(true, _ => { }, theme, "On", hit: Id(1)),
                Builder.Switch(false, _ => { }, theme, "Hover", hit: Id(2)),
                Builder.Switch(true, _ => { }, theme, "Focus", hit: Id(3)),
                Builder.Switch(false, null, theme, "Disabled off", disabledReason: "Needs a camera", hit: Id(4)),
                Builder.Switch(true, null, theme, "Disabled on", disabledReason: "Needs a camera", hit: Id(5)))
            .WithGap(RowGap).Pad(Pad).Bg(theme.Palette.PanelBg)
            .ListOf(cursor, "switches");
        cursor.Open("switches", 3);

        var renderer = new RgbaImageRenderer((uint)W, (uint)H);
        renderer.Surface.Clear(theme.Palette.PanelBg);
        var widget = new Widget(renderer) { FontPath = FontPath };
        var router = new InputRouter(widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [widget] };
        widget.Render(tree);
        router.AfterPaint();
        // The pointer rests on the third row's track.
        router.Handle(new InputEvent.MouseMove(Pad + 10f, Pad + 2 * (Row + RowGap) + Row / 2f));
        renderer.Surface.Clear(theme.Palette.PanelBg);
        widget.Render(tree);
        router.AfterPaint();
        return renderer.Surface;
    }

    [Fact] public void LightPalette() => CompareBaseline(Draw(ThemeOf(NotificationsRenderTests.Light)), "switch_states_light.png");
    [Fact] public void DarkPalette() => CompareBaseline(Draw(ThemeOf(NotificationsRenderTests.Dark)), "switch_states_dark.png");

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
