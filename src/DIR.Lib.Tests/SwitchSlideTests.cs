using System;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The switch's knob slide: a switch whose side changed since it was last painted draws its knob part way for
/// <see cref="MotionClock.KnobSlideMilliseconds"/> of motion time and raises the moving flag; a first paint, a locked
/// switch and reduced motion land at once. The clock is set by the test, never read.
/// </summary>
public class SwitchSlideTests
{
    private static RGBAColor32 Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xff);

    private static readonly UiPalette Palette = new()
    {
        ContentBg = Hex(0x101318), PanelBg = Hex(0x171b22), HeaderBg = Hex(0x1e232c), Separator = Hex(0x2a3039),
        SeparatorStrong = Hex(0x3c444f), BodyText = Hex(0xffffff), DimText = Hex(0x8b939f), Accent = Hex(0xff5e00),
        Selection = Hex(0x302820), Info = Hex(0x5aa0ff), Warn = Hex(0xffb020), Error = Hex(0xff7a70), Focus = Hex(0xff5e00),
    };

    private static readonly UiTheme Theme = new() { Palette = Palette, Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f) };
    private const uint W = 80, H = 40;

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public RgbaImageRenderer Target { get; } = (RgbaImageRenderer)renderer;
        public Node Tree { get; set; } = Builder.Box(0, 0);
        public void Render()
        {
            BeginFrame();
            RenderLayout(Tree, new RectF32(0, 0, W, H), string.Empty, DesignScale.One);
        }
    }

    private static readonly HitResult Id = new HitResult.ButtonHit("sw");

    private static (int KnobLeft, bool Moving) Paint(Widget w, bool on, long ms, string? locked = null)
    {
        w.Ui.MotionMilliseconds = ms;
        w.Tree = Builder.HStack(Builder.Switch(on, _ => { }, Theme, SwitchSize.Regular, lockedReason: locked, hit: Id)).Stretch();
        var renderer = w.Target;
        renderer.Surface.Clear(new RGBAColor32(0, 0, 0, 255));
        w.Render();
        return (KnobLeft(renderer.Surface), w.Ui.IsMoving);
    }

    // The knob's left edge: the first pixel of the knob ink (the on-knob colour) along the track's middle row.
    private static int KnobLeft(RgbaImage img)
    {
        var knob = Theme.Controls.SwitchOrDerived;
        var row = (int)H / 2;
        for (var x = 0; x < (int)W; x++)
        {
            var i = (row * (int)W + x) * 4;
            var px = new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
            if (px == knob.OnKnob || px == knob.OffKnob) return x;
        }
        return -1;
    }

    private static Widget NewWidget() => new(new RgbaImageRenderer(W, H));

    [Fact]
    public void A_first_paint_lands_at_once_and_does_not_move()
    {
        var w = NewWidget();
        Paint(w, true, 0).Moving.ShouldBeFalse();
    }

    [Fact]
    public void A_changed_side_slides_part_way_then_arrives()
    {
        var w = NewWidget();
        var off = Paint(w, false, 1000).KnobLeft;
        var start = Paint(w, true, 1010);
        var mid = Paint(w, true, 1010 + MotionClock.KnobSlideMilliseconds / 2);
        var done = Paint(w, true, 1010 + MotionClock.KnobSlideMilliseconds);

        start.Moving.ShouldBeTrue();
        mid.Moving.ShouldBeTrue();
        mid.KnobLeft.ShouldBeGreaterThan(off);
        mid.KnobLeft.ShouldBeLessThan(done.KnobLeft);
        done.Moving.ShouldBeFalse();
    }

    [Fact]
    public void A_refused_switch_slides_back()
    {
        var w = NewWidget();
        Paint(w, false, 0);
        var pressed = Paint(w, true, 10);
        Paint(w, true, 10 + MotionClock.KnobSlideMilliseconds);
        var on = Paint(w, true, 400).KnobLeft;
        var back = Paint(w, false, 410);
        var mid = Paint(w, false, 410 + MotionClock.KnobSlideMilliseconds / 2);

        back.Moving.ShouldBeTrue("the refusal starts a slide back");
        mid.Moving.ShouldBeTrue();
        mid.KnobLeft.ShouldBeLessThan(on);
        mid.KnobLeft.ShouldBeGreaterThan(Paint(w, false, 410 + 1000).KnobLeft);
        pressed.Moving.ShouldBeTrue();
    }

    [Fact]
    public void Locked_and_reduced_motion_land_at_once()
    {
        var locked = NewWidget();
        Paint(locked, false, 0, "running");
        Paint(locked, true, 10, "running").Moving.ShouldBeFalse();

        var reduced = NewWidget();
        reduced.Ui.ReducedMotion = true;
        Paint(reduced, false, 0);
        Paint(reduced, true, 10).Moving.ShouldBeFalse();
    }

    [Fact]
    public void A_clock_that_never_runs_still_lands_the_knob_on_its_new_side()
    {
        var w = NewWidget();
        var off = Paint(w, false, 0).KnobLeft;
        Paint(w, true, 0);
        var second = Paint(w, true, 0);
        var third = Paint(w, true, 0);

        second.Moving.ShouldBeFalse("the clock stood still, so the slide lands instead of never finishing");
        third.Moving.ShouldBeFalse();
        second.KnobLeft.ShouldBeGreaterThan(off);
        third.KnobLeft.ShouldBe(second.KnobLeft);
    }

    [Fact]
    public void A_switch_no_longer_painted_is_forgotten_so_its_next_paint_is_a_first_paint()
    {
        var w = NewWidget();
        Paint(w, false, 0);
        // Two frames in which the switch is not painted (each Render begins a frame, which is a cycle boundary).
        w.Tree = Builder.Box(1, 1);
        w.Render();
        w.Render();
        Paint(w, true, 10).Moving.ShouldBeFalse();
    }
}
