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
/// the sized <c>Builder.Switch</c> (DSN-1 v4): the arranged geometry of both sizes and the knob's side, that only the track is the hit,
/// locked and changing taking no press, locked and refused showing their reason, a null onToggle registering nothing, and
/// the knob colours in tianwen's Dark and Light and a one-hue Night palette. Pixel baselines switch_v4_states_dark.png (the
/// screen's States panel) and switch_v4_sizes_themes.png (its Sizes and themes panel).
/// </summary>
public class SwitchControlTests
{
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static RGBAColor32 Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xff);

    private static UiTheme ThemeOf(UiPalette p) => new() { Palette = p, Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f) };

    // tianwen's three palettes as home.json draws them. Dark's BodyText is the screen's white knob, which the knob rule takes.
    private static readonly UiPalette DarkPalette = new()
    {
        ContentBg = Hex(0x101318), PanelBg = Hex(0x171b22), HeaderBg = Hex(0x1e232c), Separator = Hex(0x2a3039),
        SeparatorStrong = Hex(0x3c444f), BodyText = Hex(0xffffff), DimText = Hex(0x8b939f), Accent = Hex(0xff5e00),
        Selection = Hex(0x302820), Info = Hex(0x5aa0ff), Warn = Hex(0xffb020), Error = Hex(0xff7a70), Focus = Hex(0xff5e00),
    };

    private static readonly UiPalette LightPalette = new()
    {
        ContentBg = Hex(0xffffff), PanelBg = Hex(0xffffff), HeaderBg = Hex(0xe9edf1), Separator = Hex(0xd8dee5),
        SeparatorStrong = Hex(0xbcc5cf), BodyText = Hex(0x1c2026), DimText = Hex(0x5a626c), Accent = Hex(0xb83c00),
        Selection = Hex(0xf3e6de), Info = Hex(0x1464d2), Warn = Hex(0xaa6400), Error = Hex(0xc81e28), Focus = Hex(0xb83c00),
    };

    // No blue in any role: every colour is an orange or a red.
    private static readonly UiPalette NightPalette = new()
    {
        ContentBg = Hex(0x000000), PanelBg = Hex(0x0c0400), HeaderBg = Hex(0x180800), Separator = Hex(0x2e1200),
        SeparatorStrong = Hex(0x4d1e00), BodyText = Hex(0xff7a2e), DimText = Hex(0xb83c00), Accent = Hex(0xff6a00),
        Selection = Hex(0x2e1200), Info = Hex(0xff7a2e), Warn = Hex(0xff6a00), Error = Hex(0xff4020), Success = Hex(0xff8c00), Focus = Hex(0xff6a00),
    };

    private static readonly UiTheme Dark = ThemeOf(DarkPalette);
    private static readonly UiTheme Light = ThemeOf(LightPalette);
    private static readonly UiTheme Night = ThemeOf(NightPalette);

    // ---- arranged geometry ----

    private sealed class FloatCtx : IMeasureContext<float>
    {
        public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 8f, 14f);
        public float ToSurface(float designUnits) => designUnits;
        public float ToSurfaceX(float designUnits) => designUnits;
        public float ToSurfaceY(float designUnits) => designUnits;
    }

    private static List<ArrangedNode<float>> Arrange(Node root)
        => [.. Engine.Arrange(root, new Rect<float>(0, 0, 400f, 100f), new FloatCtx())];

    private static ArrangedNode<float> Track(List<ArrangedNode<float>> a)
        => a.Single(n => n.Node.Hit is HitResult.ButtonHit);

    private static ArrangedNode<float> Knob(List<ArrangedNode<float>> a, float diameter)
        => a.Single(n => n.Node is Node.Leaf { Content: Content.Box b } && b.Width == diameter && b.Color != default);

    [Theory]
    [InlineData(SwitchSize.Regular, 40f, 22f, 18f)]
    [InlineData(SwitchSize.Small, 30f, 16f, 12f)]
    public void Both_sizes_are_the_designed_track_with_the_knob_at_the_side_the_value_says(SwitchSize size, float w, float h, float d)
    {
        var off = Arrange(Builder.Switch(false, _ => { }, Dark, size));
        var offTrack = Track(off).Bounds;
        offTrack.Width.ShouldBe(w);
        offTrack.Height.ShouldBe(h);
        var offKnob = Knob(off, d).Bounds;
        offKnob.X.ShouldBe(offTrack.X + 2f);
        offKnob.Y.ShouldBe(offTrack.Y + 2f);

        var on = Arrange(Builder.Switch(true, _ => { }, Dark, size));
        var onTrack = Track(on).Bounds;
        Knob(on, d).Bounds.X.ShouldBe(onTrack.X + w - 2f - d);

        var changing = Arrange(Builder.Switch(true, _ => { }, Dark, size, changing: true));
        var mid = Track(changing).Bounds;
        Knob(changing, d).Bounds.X.ShouldBe(mid.X + (w - d) / 2f);
    }

    [Fact]
    public void The_track_is_fully_round_and_the_layout_does_not_move_when_asking()
    {
        var plain = Arrange(Builder.Switch(false, _ => { }, Dark, SwitchSize.Regular));
        Track(plain).Node.CornerRadius.ShouldBe(11f);
        var asking = Arrange(Builder.Switch(false, _ => { }, Dark, SwitchSize.Regular, asking: true));
        Track(asking).Bounds.ShouldBe(Track(plain).Bounds);
        asking[0].Node.BorderStyle.ShouldNotBeNull().Color.ShouldBe(DarkPalette.Warn);
        plain[0].Node.BorderStyle.ShouldBeNull();
    }

    // ---- input ----

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public Node Tree { get; set; } = Builder.Box(0, 0);

        public void Render(float width, float height)
        {
            BeginFrame();
            RenderLayout(Tree, new RectF32(0, 0, width, height), FontPath, DesignScale.One);
        }
    }

    private sealed class Rig
    {
        public List<bool> Toggles { get; } = [];
        public Widget Widget { get; }
        public InputRouter Router { get; }

        public Rig(Func<Action<bool>, Node> build)
        {
            var renderer = new RgbaImageRenderer(300u, 60u);
            Widget = new Widget(renderer) { FontPath = FontPath };
            Router = new InputRouter(Widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [Widget] };
            Widget.Tree = Builder.HStack(build(Toggles.Add)).Stretch();
            Widget.Render(300f, 60f);
            Router.AfterPaint();
        }

        public List<ClickableRegion> Regions()
        {
            var regions = new List<ClickableRegion>();
            Widget.CollectPaintedRegions(regions);
            return regions;
        }

        public void ClickAt(float x, float y)
        {
            Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
            Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left, InputModifier.None));
        }

        public void ClickCentre(ClickableRegion r) => ClickAt(r.X + r.Width / 2f, r.Y + r.Height / 2f);
    }

    [Fact]
    public void A_press_on_the_track_toggles_to_the_new_value()
    {
        var rig = new Rig(t => Builder.Switch(false, t, Dark, SwitchSize.Regular));
        rig.ClickCentre(rig.Regions().Single(r => r.Result is HitResult.ButtonHit));
        rig.Toggles.ShouldBe([true]);

        var on = new Rig(t => Builder.Switch(true, t, Dark, SwitchSize.Regular));
        on.ClickCentre(on.Regions().Single(r => r.Result is HitResult.ButtonHit));
        on.Toggles.ShouldBe([false]);
    }

    [Fact]
    public void Only_the_track_is_the_hit_a_press_on_the_state_label_does_nothing()
    {
        var rig = new Rig(t => Builder.Switch(false, t, Dark, SwitchSize.Regular, stateLabel: "One per pool"));
        var track = rig.Regions().Single(r => r.Result is HitResult.ButtonHit);
        rig.Regions().Count.ShouldBe(1);
        // The label sits right of the track: a press well beyond the track's right edge.
        rig.ClickAt(track.X + track.Width + 40f, track.Y + track.Height / 2f);
        rig.Toggles.ShouldBeEmpty();
    }

    [Fact]
    public void Locked_takes_no_press_and_shows_its_reason()
    {
        var rig = new Rig(t => Builder.Switch(true, t, Dark, SwitchSize.Regular, lockedReason: "A session is running"));
        var region = rig.Regions().Single(r => r.Result is HitResult.ButtonHit);
        region.IsDisabled.ShouldBeTrue();
        region.Tooltip.ShouldBe("A session is running");
        rig.ClickCentre(region);
        rig.Toggles.ShouldBeEmpty();
    }

    [Fact]
    public void Changing_takes_no_press()
    {
        var rig = new Rig(t => Builder.Switch(false, t, Dark, SwitchSize.Regular, changing: true));
        var region = rig.Regions().Single(r => r.Result is HitResult.ButtonHit);
        region.IsDisabled.ShouldBeTrue();
        rig.ClickCentre(region);
        rig.Toggles.ShouldBeEmpty();
    }

    [Fact]
    public void Refused_keeps_the_press_draws_an_error_edge_and_shows_its_reason()
    {
        var rig = new Rig(t => Builder.Switch(false, t, Dark, SwitchSize.Regular, refusedReason: "The focuser said no"));
        var region = rig.Regions().Single(r => r.Result is HitResult.ButtonHit);
        region.Tooltip.ShouldBe("The focuser said no");
        region.IsDisabled.ShouldBeFalse();
        Track(Arrange(Builder.Switch(false, _ => { }, Dark, SwitchSize.Regular, refusedReason: "x"))).Node.BorderStyle.ShouldNotBeNull().Color.ShouldBe(DarkPalette.Error);
        rig.ClickCentre(region);
        rig.Toggles.ShouldBe([true]);
    }

    [Fact]
    public void A_null_onToggle_is_a_display_that_registers_nothing()
    {
        var rig = new Rig(_ => Builder.Switch(true, null, Dark, SwitchSize.Regular, stateLabel: "On"));
        rig.Regions().ShouldBeEmpty();
    }

    // ---- colours ----

    [Fact]
    public void Night_is_a_one_hue_palette_and_Dark_and_Light_are_not()
    {
        NightPalette.IsOneHue.ShouldBeTrue();
        DarkPalette.IsOneHue.ShouldBeFalse();
        LightPalette.IsOneHue.ShouldBeFalse();
    }

    [Theory]
    [InlineData("dark", 0xffffff, 0xff5e00)]
    [InlineData("light", 0xffffff, 0xb83c00)]
    [InlineData("night", 0x000000, 0xff6a00)]
    public void The_on_knob_is_white_on_Dark_and_Light_and_black_on_the_one_hue_Night(string name, int knob, int track)
    {
        var theme = name switch { "dark" => Dark, "light" => Light, _ => Night };
        var style = theme.Controls.SwitchOrDerived;
        style.OnKnob.ShouldBe(Hex(knob));
        style.OnFill.ShouldBe(Hex(track));
        var arranged = Arrange(Builder.Switch(true, _ => { }, theme, SwitchSize.Regular));
        ((Content.Box)((Node.Leaf)Knob(arranged, 18f).Node).Content).Color.ShouldBe(Hex(knob));
        Track(arranged).Node.Background.ShouldBe(Hex(track));
    }

    [Fact]
    public void The_off_knob_is_dim_on_a_dark_palette_and_an_outlined_on_knob_on_a_light_one()
    {
        Dark.Controls.SwitchOrDerived.OffKnob.ShouldBe(DarkPalette.DimText);
        Dark.Controls.SwitchOrDerived.OffKnobEdge.ShouldBeNull();
        Light.Controls.SwitchOrDerived.OffKnob.ShouldBe(Light.Controls.SwitchOrDerived.OnKnob);
        Light.Controls.SwitchOrDerived.OffKnobEdge.ShouldBe(LightPalette.SeparatorStrong);
        Dark.Controls.SwitchOrDerived.OffFill.ShouldBe(DarkPalette.HeaderBg);
        Dark.Controls.SwitchOrDerived.OffEdge.ShouldBe(DarkPalette.SeparatorStrong);
        Dark.Controls.SwitchOrDerived.RefusedEdge.ShouldBe(DarkPalette.Error);
        Dark.Controls.SwitchOrDerived.AskingRing.ShouldBe(DarkPalette.Warn);
    }

    [Fact]
    public void A_ControlStyles_written_without_a_switch_still_derives_one()
    {
        var styles = Dark.Controls with { Switch = null };
        styles.SwitchOrDerived.ShouldNotBeNull();
        var theme = Dark with { Controls = styles };
        Arrange(Builder.Switch(true, _ => { }, theme, SwitchSize.Regular)).Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_off_knob_lightens_with_its_track_under_the_pointer_and_a_locked_one_does_not()
    {
        var style = Dark.Controls.SwitchOrDerived;
        Arrange(Builder.Switch(false, _ => { }, Dark, SwitchSize.Regular)).Single(n => n.Node.HitHoverBackground is not null)
            .Node.HitHoverBackground.ShouldBe(style.OffHoverKnobOrDerived);
        Arrange(Builder.Switch(false, _ => { }, Dark, SwitchSize.Regular, lockedReason: "Locked")).Any(n => n.Node.HitHoverBackground is not null).ShouldBeFalse();
        Arrange(Builder.Switch(true, _ => { }, Dark, SwitchSize.Regular)).Any(n => n.Node.HitHoverBackground is not null).ShouldBeFalse();
    }

    // ---- baselines ----

    private static UiTheme WithTrackFills(UiTheme theme)
    {
        var s = theme.Controls.SwitchOrDerived;
        return theme with { Controls = theme.Controls with { Switch = s with { OnFill = s.OnHoverFill, OffFill = s.OffHoverFill, OffKnob = s.OffHoverKnobOrDerived } } };
    }

    private static Node Card(UiTheme theme, Node sw, string caption, float width = 120f)
        => Builder.VStack(
                Builder.HStack(Builder.Spacer().Stretch(), sw, Builder.Spacer().Stretch()).CrossCenter().WStar().HFixed(30f),
                Builder.Text(caption, 12f, theme.Palette.DimText, TextAlign.Center).WStar())
            .WithGap(10f).Pad(10f).WFixed(width).HFixed(80f).Bg(theme.Palette.PanelBg).Radius(8f);

    private static Node StatesPanel(UiTheme t)
    {
        const string list = "states";
        Node Sw(bool on, Action<bool>? f = null, SwitchSize size = SwitchSize.Regular, string? label = null, string? locked = null,
            bool changing = false, string? refused = null, HitResult? hit = null)
            => Builder.Switch(on, f ?? (_ => { }), t, size, label, locked, changing, refused, hit: hit);
        var hover = WithTrackFills(t);
        return Builder.VStack(
            Builder.HStack(
                Card(t, Sw(false), "Off"),
                Card(t, Sw(true), "On"),
                Card(t, Builder.Switch(false, _ => { }, hover, SwitchSize.Regular), "Off, hover"),
                Card(t, Builder.Switch(true, _ => { }, hover, SwitchSize.Regular), "On, hover"),
                Card(t, Sw(true, hit: new HitResult.ListItemHit(list, 0)), "Keyboard focus")).WithGap(10f).HFixed(80f),
            Builder.HStack(
                Card(t, Sw(false, locked: "A session is running"), "Off, locked"),
                Card(t, Sw(true, locked: "A session is running"), "On, locked"),
                Card(t, Sw(false, changing: true), "Changing…"),
                Card(t, Sw(false, refused: "The far end said no"), "Refused"),
                Card(t, Sw(true, label: "One per pool"), "With a state label", 250f)).WithGap(10f).HFixed(80f))
            .WithGap(10f).Pad(20f);
    }

    private static Node ThemeCard(UiTheme t, string name, float width)
        => Builder.HStack(
                Builder.Switch(true, _ => { }, t, SwitchSize.Regular),
                Builder.Switch(false, _ => { }, t, SwitchSize.Regular),
                Builder.Text(name, t.Metrics.BaseFontSize, t.Palette.DimText, TextAlign.Far).Stretch())
            .WithGap(8f).CrossCenter().Pad(14f, 0f).WFixed(width).HFixed(52f)
            .Bg(t.Palette.PanelBg).Border(t.Palette.Separator, 1f, 8f).Radius(8f);

    private static Node SizesThemesPanel(UiTheme t)
        => Builder.VStack(
            Builder.HStack(
                Builder.HStack(Builder.Switch(true, _ => { }, t, SwitchSize.Regular), Builder.Text("Regular 40 × 22: settings", t.Metrics.BaseFontSize, t.Palette.BodyText).Stretch())
                    .WithGap(12f).CrossCenter().Pad(14f, 0f).WFixed(280f).HFixed(52f).Bg(t.Palette.PanelBg).Radius(8f),
                Builder.HStack(Builder.Switch(true, _ => { }, t, SwitchSize.Small), Builder.Text("Small 30 × 16: dense rows", t.Metrics.BaseFontSize, t.Palette.BodyText).Stretch())
                    .WithGap(12f).CrossCenter().Pad(14f, 0f).WFixed(280f).HFixed(52f).Bg(t.Palette.PanelBg).Radius(8f)).WithGap(10f).HFixed(52f),
            Builder.HStack(ThemeCard(Light, "Light", 280f), ThemeCard(Night, "Night", 280f)).WithGap(10f).HFixed(52f))
            .WithGap(10f).Pad(20f);

    private static RgbaImage Draw(UiTheme theme, Node tree, int width, int height, string? focusList = null)
    {
        var renderer = new RgbaImageRenderer((uint)width, (uint)height);
        renderer.Surface.Clear(theme.Palette.ContentBg);
        var widget = new Widget(renderer) { FontPath = FontPath };
        if (focusList is not null) widget.ListCursor.Open(focusList, 0);
        widget.Tree = tree;
        widget.Render(width, height);
        return renderer.Surface;
    }

    [Fact] public void States_dark() => CompareBaseline(Draw(Dark, StatesPanel(Dark), 820, 230, "states"), "switch_v4_states_dark.png");

    [Fact] public void Sizes_and_themes() => CompareBaseline(Draw(Dark, SizesThemesPanel(Dark), 620, 164), "switch_v4_sizes_themes.png");

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
