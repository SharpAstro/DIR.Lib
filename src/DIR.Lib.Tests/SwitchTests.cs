using System;
using System.Collections.Generic;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Builder.Switch"/> on the four themes of <see cref="ChoiceControlsTests"/>: the click asks for the opposite value
/// and changes nothing itself, a disabled switch takes no press and says why, Enter and Space are declared, the geometry
/// follows the metrics, and the knob and edge keep 3:1 against what they are drawn on. In memory, with a stub renderer.
/// </summary>
public class SwitchTests
{
    public static IEnumerable<object[]> Themes() => ChoiceControlsTests.Themes();

    private static UiTheme ThemeOf(UiPalette p) => new() { Palette = p, Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f) };

    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * 8f, 14f);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    private sealed class TestWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Node root)
        {
            BeginFrame();
            RenderLayout(root, new RectF32(0, 0, 300, 60), fontPath: "stub.ttf", scale: DesignScale.One);
        }
    }

    private sealed class Rig
    {
        private readonly StubRenderer _renderer = new(300, 60);
        private readonly TestWidget _widget;
        private readonly InputRouter _router;
        private readonly UiTheme _theme;
        public Node Root { get; }
        public List<bool> Asked { get; } = [];

        public Rig(UiTheme theme, bool on = false, string? disabledReason = null, string? label = null)
        {
            _theme = theme;
            Root = Builder.Switch(on, Asked.Add, theme, label, disabledReason: disabledReason);
            _widget = new TestWidget(_renderer);
            _router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [_widget] };
            Paint();
        }

        public Node Track => ((Node.Stack)Root).Children[0];

        public void Paint()
        {
            _renderer.Surface.Clear(_theme.Palette.PanelBg);
            _widget.Render(Root);
            _router.AfterPaint();
        }

        public RGBAColor32 At(int x, int y)
        {
            var img = _renderer.Surface;
            var i = (y * img.Width + x) * 4;
            return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
        }

        // Past the track's end, the knob's corner cut and clear of the label (the stub draws none).
        public RGBAColor32 Rest() => At(30, 14);
        public RGBAColor32 StartCell() => At(8, 14);

        public void Move() => _router.Handle(new InputEvent.MouseMove(30, 14));
        public void Click(InputModifier mod = InputModifier.None)
        {
            _router.Handle(new InputEvent.MouseDown(30, 14, MouseButton.Left, mod, 1));
            _router.Handle(new InputEvent.MouseUp(30, 14, MouseButton.Left));
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void A_click_asks_for_the_opposite_and_does_not_flip_itself(UiTheme theme, string name)
    {
        var off = new Rig(theme);
        var offLook = off.Rest();
        off.Click();
        off.Asked.ShouldBe([true], name);
        off.Paint();
        off.Rest().ShouldBe(offLook, name + " the look follows the value the caller passes, not the click");

        var on = new Rig(theme, on: true);
        on.Click();
        on.Asked.ShouldBe([false], name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void A_disabled_switch_takes_no_press_and_has_its_reason_as_tooltip(UiTheme theme, string name)
    {
        var rig = new Rig(theme, on: true, disabledReason: "Needs a camera", label: "Cooling");
        rig.Click();
        rig.Asked.ShouldBeEmpty(name);
        rig.Track.DisabledReason.ShouldBe("Needs a camera", name);
        rig.Track.OnActivate.ShouldBeNull(name);
        ((Node.Stack)rig.Root).Children[1].DisabledReason.ShouldBe("Needs a camera", name + " label");
    }

    [Fact]
    public void Enter_and_Space_are_declared_through_Activatable_and_ask_for_the_opposite()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var rig = new Rig(theme, on: true, label: "Cooling");
        rig.Track.OnActivate.ShouldNotBeNull();
        ((Node.Stack)rig.Root).Children[1].OnActivate.ShouldNotBeNull();
        rig.Track.OnActivate!(InputModifier.None);
        rig.Asked.ShouldBe([false]);
        rig.Track.Cursor.ShouldBe(CursorKind.Pointer);
    }

    [Fact]
    public void The_label_goes_before_the_track_when_asked_and_shares_its_hit()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var before = (Node.Stack)Builder.Switch(false, null, theme, "Cooling", labelBefore: true);
        before.Children[1].Width.ShouldBe(Sizing.Fixed(theme.Metrics.ButtonHeight * theme.Controls.SwitchOrDerived.HeightScale * theme.Controls.SwitchOrDerived.WidthScale));
        before.Children[0].Hit.ShouldBe(before.Children[1].Hit);
        var after = (Node.Stack)Builder.Switch(false, null, theme, "Cooling");
        after.Children[0].Width.ShouldBe(before.Children[1].Width);
    }

    [Fact]
    public void A_tooltip_and_a_hit_id_are_kept()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var id = new HitResult.ListItemHit("settings", 2);
        var node = (Node.Stack)Builder.Switch(true, null, theme, tooltip: "Cooling on or off", hit: id);
        node.Children[0].Tooltip.ShouldBe("Cooling on or off");
        node.Children[0].Hit.ShouldBe(id);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_geometry_follows_the_metrics_and_the_knob_sits_at_the_start_off_and_the_end_on(UiTheme theme, string name)
    {
        var s = theme.Controls.SwitchOrDerived;
        var height = theme.Metrics.ButtonHeight * s.HeightScale;
        var width = height * s.WidthScale;
        var off = new Rig(theme);
        var bg = theme.Palette.PanelBg;
        off.Track.Height.ShouldBe(Sizing.Fixed(height), name);
        off.Track.Width.ShouldBe(Sizing.Fixed(width), name);
        off.Rest().ShouldBe(s.OffFill, name + " off track at the end, knob at the start");
        off.StartCell().ShouldBe(s.OffKnob, name);
        off.At((int)width + 6, 14).ShouldBe(bg, name + " nothing past the track");
        // Centred in the button's row: the track's top and bottom rows are clear of the row's edge.
        off.At(18, 1).ShouldBe(bg, name + " above the track");

        var on = new Rig(theme, on: true);
        on.StartCell().ShouldBe(s.OnFill, name + " on track at the start, knob at the end");
        on.At((int)(width - 3 - 7), 14).ShouldBe(s.OnKnob, name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Hover_and_pressed_are_the_declared_fills(UiTheme theme, string name)
    {
        var s = theme.Controls.SwitchOrDerived;
        var rig = new Rig(theme);
        rig.Move();
        rig.Paint();
        rig.Rest().ShouldBe(s.OffHoverFill, name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_knob_reads_on_its_track_and_the_edge_on_the_panel_at_3_to_1(UiTheme theme, string name)
    {
        var s = theme.Controls.SwitchOrDerived;
        foreach (var track in new[] { s.OnFill, s.OnHoverFill, s.OnPressedFill })
        {
            RGBAColor32.ContrastRatio(s.OnKnob, track).ShouldBeGreaterThanOrEqualTo(3f, name + " on knob");
        }
        foreach (var track in new[] { s.OffFill, s.OffHoverFill, s.OffPressedFill })
        {
            RGBAColor32.ContrastRatio(s.OffKnob, track).ShouldBeGreaterThanOrEqualTo(3f, name + " off knob");
        }
        RGBAColor32.ContrastRatio(s.OffEdge!.Value, theme.Palette.PanelBg).ShouldBeGreaterThanOrEqualTo(3f, name + " edge");
    }

    [Fact]
    public void A_style_set_on_the_theme_replaces_the_derived_one()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var custom = theme.Controls.SwitchOrDerived with { HeightScale = 1f };
        var node = Builder.Switch(false, null, theme with { Controls = theme.Controls with { Switch = custom } });
        ((Node.Stack)node).Children[0].Height.ShouldBe(Sizing.Fixed(theme.Metrics.ButtonHeight));
        (theme.Controls with { Switch = null }).SwitchOrDerived.OnFill.ShouldBe(theme.Controls.Accent.RestFill!.Value);
    }
}
