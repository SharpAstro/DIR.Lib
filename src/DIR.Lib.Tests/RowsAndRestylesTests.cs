using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The themed <see cref="Builder.Slider(SliderState, UiTheme, RGBAColor32?, RGBAColor32?, RGBAColor32?)"/>, the themed
/// <c>Builder.Dropdown</c> overload and <see cref="Builder.DropdownBox{T}"/> on four themes (a light and a dark palette, each
/// with the accents #00e5d5 and #0063b1): the slider's fill is the accent over a control-fill track and its thumb is round
/// with a ring at 3:1 against the panel; the box's text reads at 4.5:1 on its fill and its edge at 3:1; the caret turns while
/// the menu is open; menu rows read at 4.5:1 on the raised surface and on the hover fill; an explicit colour still wins. CPU
/// rendered through a stub renderer (no font, no clock, no pixel baselines).
/// </summary>
public class RowsAndRestylesTests
{
    private static readonly RGBAColor32 Teal = new(0x00, 0xe5, 0xd5, 0xff);
    private static readonly RGBAColor32 Blue = new(0x00, 0x63, 0xb1, 0xff);
    private static readonly RGBAColor32 Pink = new(0xff, 0x00, 0x80, 0xff);

    public static IEnumerable<object[]> Themes()
    {
        foreach (var accent in new[] { Teal, Blue })
        {
            yield return [ThemeOf(NotificationsRenderTests.Dark with { Accent = accent }), "dark"];
            yield return [ThemeOf(NotificationsRenderTests.Light with { Accent = accent }), "light"];
        }
    }

    private static UiTheme ThemeOf(UiPalette p) => new() { Palette = p, Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f) };

    private sealed class FloatCtx : IMeasureContext<float>
    {
        public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 8f, 14f);
        public float ToSurface(float designUnits) => designUnits;
        public float ToSurfaceX(float designUnits) => designUnits;
        public float ToSurfaceY(float designUnits) => designUnits;
    }

    private static List<ArrangedNode<float>> Arrange(Node root)
        => [.. Engine.Arrange(root, new Rect<float>(0, 0, 200f, 60f), new FloatCtx())];

    private static SliderState HalfSlider() => new() { Min = 0f, Max = 1f, Value = 0.5f };

    // ---- the slider ----

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_slider_fills_with_the_accent_over_a_control_fill_track_with_a_round_ringed_thumb(UiTheme theme, string name)
    {
        var leaf = (Node.Leaf)Builder.Slider(HalfSlider(), theme);
        var slider = leaf.Content.ShouldBeOfType<Content.Slider>();
        slider.FillColor.ShouldBe(theme.Palette.Accent, name);
        slider.Chrome.TrackBackground.ShouldBe(theme.Palette.ControlFill, name);
        slider.Chrome.Handle.ShouldBe(theme.Palette.Accent, name);
        slider.RoundThumb.ShouldBeTrue();
        slider.ThumbRing.ShouldBe(theme.Palette.ControlEdge, name);
        RGBAColor32.ContrastRatio(slider.ThumbRing, theme.Palette.PanelBg).ShouldBeGreaterThanOrEqualTo(3f, name + " ring on the panel");
    }

    [Fact]
    public void An_explicit_slider_colour_wins_and_the_plain_slider_keeps_its_look()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var slider = (Content.Slider)((Node.Leaf)Builder.Slider(HalfSlider(), theme, fillColor: Pink, trackColor: Blue, thumbColor: Teal)).Content;
        slider.FillColor.ShouldBe(Pink);
        slider.Chrome.TrackBackground.ShouldBe(Blue);
        slider.Chrome.Handle.ShouldBe(Teal);

        var plain = (Content.Slider)((Node.Leaf)Builder.Slider(HalfSlider())).Content;
        plain.RoundThumb.ShouldBeFalse();
        plain.FillColor.ShouldBe(new RGBAColor32(0xff, 0xff, 0xff, 0xff));
        plain.Chrome.ShouldBe(new TrackSliderChrome(new RGBAColor32(0x40, 0x40, 0x40, 0xff), new RGBAColor32(0xff, 0xff, 0xff, 0xff)));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_slider_paints_the_accent_up_to_the_thumb_and_the_control_fill_after_it(UiTheme theme, string name)
    {
        var renderer = new DeclarationStubRenderer(200, 60);
        renderer.Surface.Clear(theme.Palette.PanelBg);
        var widget = new DeclarationWidget(renderer);
        widget.Render(Builder.Slider(HalfSlider(), theme), new RectF32(0, 0, 200, theme.Controls.SliderOrDerived.ThumbSize));

        RGBAColor32 At(int x, int y)
        {
            var i = (y * renderer.Surface.Width + x) * 4;
            var p = renderer.Surface.Pixels;
            return new RGBAColor32(p[i], p[i + 1], p[i + 2], p[i + 3]);
        }
        var mid = (int)(theme.Controls.SliderOrDerived.ThumbSize / 2f);
        At(20, mid).ShouldBe(theme.Palette.Accent, name + " fill");
        At(180, mid).ShouldBe(theme.Palette.ControlFill, name + " track");
        At(100, mid).ShouldBe(theme.Palette.Accent, name + " thumb centre");
        At(100 - 7, mid).ShouldBe(theme.Palette.ControlEdge, name + " thumb ring");
    }

    // ---- the dropdown box ----

    private static DropdownMenuState<string> State(bool open)
    {
        var state = new DropdownMenuState<string>();
        if (open) state.Open(0f, 0f, 100f, [DropdownItem.Text("One"), DropdownItem.Text("Two")]);
        return state;
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_box_text_reads_on_its_fill_and_its_edge_on_the_panel(UiTheme theme, string name)
    {
        var s = theme.Controls.DropdownOrDerived;
        RGBAColor32.ContrastRatio(s.BoxInk, s.BoxFill).ShouldBeGreaterThanOrEqualTo(4.5f, name + " rest");
        RGBAColor32.ContrastRatio(s.BoxInk, s.BoxHoverFill).ShouldBeGreaterThanOrEqualTo(4.5f, name + " hover");
        RGBAColor32.ContrastRatio(s.BoxEdge, theme.Palette.PanelBg).ShouldBeGreaterThanOrEqualTo(3f, name + " edge");

        var box = Arrange(Builder.DropdownBox("Pick", State(false), theme));
        box[0].Node.Background.ShouldBe(theme.Palette.ControlFill, name);
        box[0].Node.BorderStyle!.Color.ShouldBe(theme.Palette.ControlEdge, name);
        box[0].Node.CornerRadius.ShouldBe(theme.Metrics.ControlRadius, name);
        box[0].Node.Hit.ShouldBe(new HitResult.ButtonHit("Pick"));
        Engine.Measure(Builder.DropdownBox("Pick", State(false), theme), new Size<float>(1000f, 100f), new FloatCtx()).Height.ShouldBe(theme.Metrics.ButtonHeight, name);
    }

    private static IconKind[] Carets(Node n)
        => [.. Arrange(n).Select(a => a.Node).OfType<Node.Leaf>().Select(l => l.Content).OfType<Content.Icon>().Select(i => i.Kind)];

    [Fact]
    public void The_caret_is_down_closed_and_up_open()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        Carets(Builder.DropdownBox("Pick", State(false), theme)).ShouldBe([IconKind.CaretDown]);
        Carets(Builder.DropdownBox("Pick", State(true), theme)).ShouldBe([IconKind.CaretUp]);
    }

    [Fact]
    public void A_press_on_the_box_toggles_the_menu()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var state = State(false);
        var renderer = new DeclarationStubRenderer(200, 60);
        var widget = new DeclarationWidget(renderer);
        var router = new InputRouter(widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [widget] };
        widget.Render(Builder.DropdownBox("Pick", state, theme), new RectF32(0, 0, 200, 60));
        router.AfterPaint();
        router.Handle(new InputEvent.MouseDown(20, 10, MouseButton.Left, InputModifier.None, 1));
        router.Handle(new InputEvent.MouseUp(20, 10, MouseButton.Left));
        state.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void An_explicit_box_fill_or_text_colour_wins()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var box = Arrange(Builder.DropdownBox("Pick", State(false), theme, fillColor: Pink, textColor: Teal));
        box[0].Node.Background.ShouldBe(Pink);
        box.Select(a => a.Node).OfType<Node.Leaf>().Select(l => l.Content).OfType<Content.Text>().Single().Color.ShouldBe(Teal);
    }

    // ---- the menu ----

    [Theory]
    [MemberData(nameof(Themes))]
    public void Menu_rows_read_on_the_raised_surface_and_on_the_hover_fill(UiTheme theme, string name)
    {
        var s = theme.Controls.DropdownOrDerived;
        s.MenuBg.ShouldBe(theme.Palette.RaisedBg);
        RGBAColor32.ContrastRatio(s.RowInk, s.MenuBg).ShouldBeGreaterThanOrEqualTo(4.5f, name + " rest");
        RGBAColor32.ContrastRatio(s.RowInk, s.RowHover).ShouldBeGreaterThanOrEqualTo(4.5f, name + " hover");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_menu_is_a_raised_surface_with_hover_rows(UiTheme theme, string name)
    {
        var menu = Builder.Dropdown(new RectF32(0, 0, 100, 28), State(true), theme);
        var nodes = Arrange(menu).Select(a => a.Node).ToList();
        var card = nodes.Single(n => n.IsSurface);
        card.Background.ShouldBe(theme.Palette.RaisedBg, name);
        card.CornerRadius.ShouldBe(theme.Metrics.CornerRadius, name);
        card.ShadowStyle.ShouldBe(theme.Metrics.Shadow, name);
        var rows = nodes.Where(n => n.Hit is HitResult.ListItemHit).ToList();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r.HoverBackground == theme.Palette.ControlFillHover);
        nodes.OfType<Node.Leaf>().Select(l => l.Content).OfType<Content.Text>().ShouldAllBe(t => t.Color == theme.Palette.BodyText);
    }

    [Fact]
    public void An_explicit_menu_colour_wins_and_the_plain_menu_is_not_a_surface()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var nodes = Arrange(Builder.Dropdown(new RectF32(0, 0, 100, 28), State(true), theme, background: Pink, highlight: Teal, textColor: Blue))
            .Select(a => a.Node).ToList();
        nodes.Single(n => n.IsSurface).Background.ShouldBe(Pink);
        nodes.Where(n => n.Hit is HitResult.ListItemHit).ShouldAllBe(r => r.HoverBackground == Teal);

        Arrange(Builder.Dropdown(new RectF32(0, 0, 100, 28), State(true))).Any(a => a.Node.IsSurface).ShouldBeFalse();
    }

    // ---- the Setting row ----

    private static List<Node> NodesOf(Node n) => [.. Arrange(n).Select(a => a.Node)];

    private static IEnumerable<Content.Icon> IconsOf(Node n) => NodesOf(n).OfType<Node.Leaf>().Select(l => l.Content).OfType<Content.Icon>();

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_row_title_reads_at_4_5_and_the_subtitle_is_legible_on_rest_and_hover(UiTheme theme, string name)
    {
        var s = theme.Controls.SettingRowOrDerived;
        s.RestFill.ShouldBe(theme.Palette.PanelBg, name);
        RGBAColor32.ContrastRatio(s.TitleInk, s.RestFill).ShouldBeGreaterThanOrEqualTo(4.5f, name + " title rest");
        RGBAColor32.ContrastRatio(s.TitleInk, s.HoverFill).ShouldBeGreaterThanOrEqualTo(4.5f, name + " title hover");
        RGBAColor32.ContrastRatio(s.SubtitleInk, s.RestFill).ShouldBeGreaterThanOrEqualTo(3f, name + " subtitle rest");
        RGBAColor32.ContrastRatio(s.SubtitleInk, s.HoverFill).ShouldBeGreaterThanOrEqualTo(3f, name + " subtitle hover");

        var row = Arrange(Builder.SettingRow("Title", "Subtitle", SettingTrailing.Chevron, theme))[0].Node;
        row.Background.ShouldBe(theme.Palette.PanelBg, name);
        row.HoverBackground.ShouldBe(s.HoverFill, name);
        row.Hit.ShouldBe(new HitResult.ButtonHit("Title"));
    }

    [Fact]
    public void The_row_corner_radius_is_the_themes_control_radius_and_zero_gives_a_square_row()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        Arrange(Builder.SettingRow("T", null, SettingTrailing.Chevron, theme))[0].Node.CornerRadius.ShouldBe(theme.Metrics.ControlRadius);
        var square = theme with { Metrics = theme.Metrics with { ControlRadius = 0f } };
        Arrange(Builder.SettingRow("T", null, SettingTrailing.Chevron, square))[0].Node.CornerRadius.ShouldBe(0f);
    }

    [Fact]
    public void Each_trailing_kind_draws_its_mark_and_expand_turns_when_open()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        IconsOf(Builder.SettingRow("T", "S", SettingTrailing.Chevron, theme)).Select(i => i.Kind).ShouldBe([IconKind.CaretRight]);
        IconsOf(Builder.SettingRow("T", "S", SettingTrailing.Expand(false), theme)).Select(i => i.Kind).ShouldBe([IconKind.CaretDown]);
        IconsOf(Builder.SettingRow("T", "S", SettingTrailing.Expand(true), theme)).Select(i => i.Kind).ShouldBe([IconKind.CaretUp]);

        NodesOf(Builder.SettingRow("T", "S", SettingTrailing.External, theme)).OfType<Node.Leaf>().Select(l => l.Content)
            .OfType<Content.Text>().Select(t => t.Value).ShouldContain("↗");

        var button = Builder.Button("Go", ButtonKind.Standard, () => { }, theme);
        NodesOf(Builder.SettingRow("T", "S", SettingTrailing.Of(button), theme)).OfType<Node.Leaf>().Select(l => l.Content)
            .OfType<Content.Text>().Select(t => t.Value).ShouldContain("Go");
    }

    [Fact]
    public void A_disabled_row_takes_no_press_and_says_why()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var row = Arrange(Builder.SettingRow("T", null, SettingTrailing.Chevron, theme, () => { }, disabledReason: "Not now"))[0].Node;
        row.OnClick.ShouldBeNull();
        row.HoverBackground.ShouldBeNull();
        row.DisabledReason.ShouldBe("Not now");
    }

    private static (InputRouter router, DeclarationWidget widget) Paint(Node tree)
    {
        var renderer = new DeclarationStubRenderer(300, 80);
        var widget = new DeclarationWidget(renderer);
        var router = new InputRouter(widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [widget] };
        widget.Render(tree, new RectF32(0, 0, 300, 80));
        router.AfterPaint();
        return (router, widget);
    }

    private static void Press(InputRouter router, float x, float y)
    {
        router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
        router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left));
    }

    [Fact]
    public void A_press_on_the_rows_left_middle_and_subtitle_runs_onClick_once_each()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var clicks = 0;
        var (router, _) = Paint(Builder.SettingRow("Title", "Subtitle", SettingTrailing.Chevron, theme, () => clicks++));
        Press(router, 3f, 10f);
        clicks.ShouldBe(1);
        Press(router, 150f, 10f);
        clicks.ShouldBe(2);
        var rowHeight = Arrange(Builder.SettingRow("Title", "Subtitle", SettingTrailing.Chevron, theme))[0].Bounds.Height;
        Press(router, 40f, rowHeight - 8f);
        clicks.ShouldBe(3);
    }

    [Fact]
    public void A_press_on_a_trailing_button_runs_the_buttons_handler_and_not_the_rows()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        int row = 0, button = 0;
        var trailing = Builder.Button("Go", ButtonKind.Standard, () => button++, theme).WFixed(60f);
        var (router, _) = Paint(Builder.SettingRow("Title", "Subtitle", SettingTrailing.Of(trailing), theme, () => row++));
        var arranged = Engine.Arrange(Builder.SettingRow("Title", "Subtitle", SettingTrailing.Of(trailing), theme, () => { }), new Rect<float>(0, 0, 300f, 80f), new FloatCtx());
        var b = arranged.Single(a => a.Node.Hit is HitResult.ButtonHit { } h && h.Equals(new HitResult.ButtonHit("Go"))).Bounds;
        Press(router, b.X + b.Width / 2f, b.Y + b.Height / 2f);
        button.ShouldBe(1);
        row.ShouldBe(0);
    }
}
