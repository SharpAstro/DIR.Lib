using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Builder.BusyButton"/>: the busy part keeps the rest button's exact rect, Stop grows out of its left edge and is
/// the only part that takes a press, no Stop without an onStop or while stopping, the estimate's DimText and Warn inks, done
/// and failed. Pixel baseline busy_states_dark.png (the screen's States panel at a fixed motion time).
/// </summary>
public class BusyButtonTests
{
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static RGBAColor32 Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xff);

    private static readonly UiPalette Palette = new()
    {
        ContentBg = Hex(0x101318), PanelBg = Hex(0x171b22), HeaderBg = Hex(0x1e232c), Separator = Hex(0x2a3039),
        SeparatorStrong = Hex(0x3c444f), BodyText = Hex(0xe2e6ec), DimText = Hex(0x8b939f), Accent = Hex(0xff5e00),
        Selection = Hex(0x302820), Info = Hex(0x5aa0ff), Warn = Hex(0xe6b800), Error = Hex(0xff7a70), Success = Hex(0x3ddc97), Focus = Hex(0xff5e00),
    };

    private static readonly UiTheme Theme = new() { Palette = Palette, Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f) { ControlRadius = 8f } };

    private sealed class FloatCtx : IMeasureContext<float>
    {
        public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 8f, 14f);
        public float ToSurface(float designUnits) => designUnits;
        public float ToSurfaceX(float designUnits) => designUnits;
        public float ToSurfaceY(float designUnits) => designUnits;
    }

    private static List<ArrangedNode<float>> Arrange(Node row)
        => [.. Engine.Arrange(Builder.HStack(row).Stretch(), new Rect<float>(0, 0, 400f, 60f), new FloatCtx())];

    private static Node Busy(BusyState state, string? estimate = "about 15 seconds", Action? onStop = null, int[]? pressed = null)
        => Builder.BusyButton("Plate solve", "Solving…", state, estimate, onStop, () => { if (pressed is not null) pressed[0]++; }, Theme);

    private static ArrangedNode<float> RestButton(List<ArrangedNode<float>> a) => a.Single(n => n.Node.Hit is HitResult.ButtonHit { Action: "Plate solve" });
    private static ArrangedNode<float> BusyPart(List<ArrangedNode<float>> a) => a.Single(n => n.Node.Hit is HitResult.ChromeHit);
    private static ArrangedNode<float>? Stop(List<ArrangedNode<float>> a)
        => a.Where(n => n.Node.Hit is HitResult.ButtonHit { Action: "Plate solve/stop" }).Select(n => (ArrangedNode<float>?)n).SingleOrDefault();

    private static RGBAColor32? InkOf(List<ArrangedNode<float>> a, string text)
        => a.Select(n => n.Node).OfType<Node.Leaf>().Select(l => l.Content).OfType<Content.Text>().SingleOrDefault(t => t.Value == text)?.Color;

    [Fact]
    public void The_busy_part_keeps_the_rest_buttons_exact_rect()
    {
        var rest = RestButton(Arrange(Busy(BusyState.Rest))).Bounds;
        foreach (var state in new[] { BusyState.Busy, BusyState.Overdue, BusyState.Stopping })
        {
            var arranged = Arrange(Busy(state, onStop: () => { }));
            BusyPart(arranged).Bounds.ShouldBe(rest);
        }
        BusyPart(Arrange(Busy(BusyState.Busy))).Bounds.ShouldBe(rest, "without Stop as well");
    }

    [Fact]
    public void Stop_grows_out_of_the_left_edge_and_lies_wholly_left_of_the_busy_part()
    {
        var arranged = Arrange(Busy(BusyState.Busy, onStop: () => { }));
        var stop = Stop(arranged).ShouldNotBeNull().Bounds;
        var part = BusyPart(arranged).Bounds;
        (stop.X + stop.Width).ShouldBeLessThanOrEqualTo(part.X);
        stop.Y.ShouldBe(part.Y);
        stop.Height.ShouldBe(part.Height);
    }

    [Fact]
    public void Only_stop_takes_a_press()
    {
        var stops = new[] { 0 };
        var arranged = Arrange(Busy(BusyState.Busy, onStop: () => stops[0]++));
        BusyPart(arranged).Node.OnClick.ShouldBeNull("the busy part takes no press");
        var stop = Stop(arranged).ShouldNotBeNull().Node;
        stop.OnClick.ShouldNotBeNull();
        stop.OnClick!(default);
        stops[0].ShouldBe(1);
        stop.HoverBackground.ShouldNotBeNull();
    }

    [Fact]
    public void The_stop_square_turns_Error_with_the_segment_under_the_pointer()
    {
        var arranged = Arrange(Busy(BusyState.Busy, onStop: () => { }));
        var stop = Stop(arranged).ShouldNotBeNull();
        var square = arranged.Single(n => n.Node.HitHoverBackground is not null);
        square.Node.HitHoverBackground.ShouldBe(Palette.Error);
        square.Depth.ShouldBeGreaterThan(stop.Depth);
    }

    [Fact]
    public void Two_buttons_with_one_label_and_their_own_hits_have_their_own_stop()
    {
        HitResult StopOf(Node n) => Arrange(n).Single(a => a.Node.OnClick is not null).Node.Hit!; // busy: only Stop takes a press
        var a = StopOf(Builder.BusyButton("Solve", "Solving…", BusyState.Busy, null, () => { }, () => { }, Theme, hit: new HitResult.ButtonHit("solve:main")));
        var b = StopOf(Builder.BusyButton("Solve", "Solving…", BusyState.Busy, null, () => { }, () => { }, Theme, hit: new HitResult.ButtonHit("solve:guide")));
        a.ShouldBe(new HitResult.ButtonHit("solve:main/stop"));
        b.ShouldBe(new HitResult.ButtonHit("solve:guide/stop"));
        StopOf(Builder.BusyButton("Solve", "Solving…", BusyState.Busy, null, () => { }, () => { }, Theme, stopHit: new HitResult.ButtonHit("halt")))
            .ShouldBe(new HitResult.ButtonHit("halt"));
    }

    [Fact]
    public void No_stop_without_onStop_or_while_stopping()
    {
        Stop(Arrange(Busy(BusyState.Busy))).ShouldBeNull();
        Stop(Arrange(Busy(BusyState.Stopping, onStop: () => { }))).ShouldBeNull();
        Stop(Arrange(Busy(BusyState.Rest, onStop: () => { }))).ShouldBeNull();
    }

    [Fact]
    public void The_rest_button_starts_the_task()
    {
        var presses = new[] { 0 };
        RestButton(Arrange(Busy(BusyState.Rest, pressed: presses))).Node.OnClick!(default);
        presses[0].ShouldBe(1);
    }

    [Fact]
    public void The_estimate_is_DimText_and_turns_Warn_past_it()
    {
        InkOf(Arrange(Busy(BusyState.Busy)), "about 15 seconds").ShouldBe(Palette.DimText);
        InkOf(Arrange(Busy(BusyState.Overdue, "longer than usual (45 s)")), "longer than usual (45 s)").ShouldBe(Palette.Warn);
        InkOf(Arrange(Busy(BusyState.Busy)), "Solving…").ShouldBe(Palette.DimText);
    }

    [Fact]
    public void Done_is_a_Success_tick_and_edge_with_the_result_beside_it()
    {
        var arranged = Arrange(Busy(BusyState.Done("RA 5h 35m"), onStop: () => { }));
        InkOf(arranged, "Done").ShouldBe(Palette.Success);
        InkOf(arranged, "RA 5h 35m").ShouldBe(Palette.BodyText);
        arranged.Any(n => n.Node.BorderStyle?.Color == Palette.Success).ShouldBeTrue();
        arranged.Any(n => n.Node is Node.Leaf { Content: Content.Icon { Kind: IconKind.Check } }).ShouldBeTrue();
        Stop(arranged).ShouldBeNull();
        arranged.Any(n => n.Node is Node.Leaf { Content: Content.Icon { Kind: IconKind.Spinner } }).ShouldBeFalse();
    }

    [Fact]
    public void Failed_is_the_rest_button_with_the_reason_in_Error()
    {
        var failed = Arrange(Busy(BusyState.Failed("No stars found")));
        InkOf(failed, "No stars found").ShouldBe(Palette.Error);
        RestButton(failed).Bounds.ShouldBe(RestButton(Arrange(Busy(BusyState.Rest))).Bounds);
        failed.Any(n => n.Node.Hit is HitResult.ChromeHit).ShouldBeFalse();
    }

    [Fact]
    public void The_done_hold_is_three_seconds_on_the_callers_clock()
    {
        BusyState.HoldsDone(1000, 3999).ShouldBeTrue();
        BusyState.HoldsDone(1000, 4000).ShouldBeFalse();
    }

    // ---- baseline: the screen's States panel at a fixed motion time ----

    private static Node Card(Node control, string caption)
        => Builder.VStack(control.HFixed(30f), Builder.Text(caption, 12f, Palette.DimText, TextAlign.Center).WStar())
            .WithGap(10f).Pad(12f).WFixed(380f).HFixed(84f).Bg(Palette.PanelBg).Radius(8f);

    private static Node StatesPanel()
    {
        Node B(BusyState s, string? est, bool stop = true, string label = "Solving…")
            => Builder.BusyButton("Plate solve", label, s, est, stop ? () => { } : null, () => { }, Theme, doneLabel: "Solved");
        return Builder.VStack(
            Builder.HStack(
                Card(B(BusyState.Rest, null), "Rest"),
                Card(B(BusyState.Busy, "about 15 seconds"), "Busy: Stop joins it on the left"),
                Card(B(BusyState.Busy, "about 15 seconds"), "Hover on Stop")).WithGap(10f).HFixed(84f),
            Builder.HStack(
                Card(B(BusyState.Busy, "step 4 of 9", label: "Focusing…"), "Busy, counted in steps"),
                Card(B(BusyState.Overdue, "longer than usual (45 s)"), "Past its estimate"),
                Card(B(BusyState.Stopping, null), "Stopping: Stop folds away")).WithGap(10f).HFixed(84f),
            Builder.HStack(
                Card(B(BusyState.Busy, "about 40 seconds", stop: false, label: "Warming…"), "Cannot be stopped: no Stop"),
                Card(B(BusyState.Done("RA 5h 35m · Dec −5° 23′"), null), "Done: holds 3 s, then rests"),
                Card(B(BusyState.Failed("No stars found"), null), "Failed: rests, says why")).WithGap(10f).HFixed(84f))
            .WithGap(10f).Pad(20f);
    }

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public Node Tree { get; set; } = Builder.Box(0, 0);
        public void Render(float w, float h)
        {
            BeginFrame();
            RenderLayout(Tree, new RectF32(0, 0, w, h), FontPath, DesignScale.One);
        }
    }

    [Fact]
    public void States_dark()
    {
        var renderer = new RgbaImageRenderer(1220u, 340u);
        renderer.Surface.Clear(Palette.ContentBg);
        var widget = new Widget(renderer) { FontPath = FontPath, Tree = StatesPanel() };
        widget.Ui.MotionMilliseconds = 250;
        // Over the Stop segment of the third card (right-aligned control: card x 800, inner pad 12, row 356 wide, Stop 34 wide).
        widget.Pointer = (1010f, 47f);
        widget.Render(1220f, 340f);
        widget.Ui.IsMoving.ShouldBeTrue("the spinners move");
        CompareBaseline(renderer.Surface, "busy_states_dark.png");
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
        for (var i = 0; i < baseline.Pixels.Length; i++)
        {
            maxDiff = Math.Max(maxDiff, Math.Abs(img.Pixels[i] - baseline.Pixels[i]));
        }

        if (maxDiff > 2)
        {
            var actualPath = Path.ChangeExtension(baselinePath, ".actual.png");
            PngWriter.Save(actualPath, img.Pixels, img.Width, img.Height);
            Assert.Fail($"Baseline mismatch for '{name}': max diff={maxDiff}. Actual saved to '{actualPath}'.");
        }
    }
}
