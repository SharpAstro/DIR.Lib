using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Builder.Button"/> (Accent, Standard, Subtle) and <see cref="Builder.Hyperlink"/> on four themes (a light and
/// a dark palette, each with the accents #00e5d5 and #0063b1): text on every fill reads at 4.5:1, the control edge at 3:1
/// against the panel, Accent's ink is <see cref="UiPalette.InkOn"/>, the five states paint the declared fills through real
/// <see cref="InputRouter"/> events (no font, no clock; the stub renderer draws no glyphs so a pixel read is the fill), and
/// the chevron and the north-east arrow are present only when asked for. No pixel baselines.
/// </summary>
public class ButtonKindsTests
{
    private static readonly RGBAColor32 Teal = new(0x00, 0xe5, 0xd5, 0xff);
    private static readonly RGBAColor32 Blue = new(0x00, 0x63, 0xb1, 0xff);

    public static IEnumerable<object[]> Themes()
    {
        foreach (var accent in new[] { Teal, Blue })
        {
            yield return [ThemeOf(NotificationsRenderTests.Dark with { Accent = accent }), "dark"];
            yield return [ThemeOf(NotificationsRenderTests.Light with { Accent = accent }), "light"];
        }
    }

    public static IEnumerable<object[]> ThemesAndKinds()
    {
        foreach (var t in Themes())
        {
            foreach (var kind in Enum.GetValues<ButtonKind>()) yield return [t[0], t[1], kind];
        }
    }

    private static UiTheme ThemeOf(UiPalette p) => new() { Palette = p, Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f) };

    private static ButtonStyle StyleOf(UiTheme t, ButtonKind k)
        => k switch { ButtonKind.Accent => t.Controls.Accent, ButtonKind.Standard => t.Controls.Standard, _ => t.Controls.Subtle };

    // ---- contrast ----

    [Theory]
    [MemberData(nameof(ThemesAndKinds))]
    public void Text_reads_on_every_fill_at_4_5_to_1(UiTheme theme, string name, ButtonKind kind)
    {
        var s = StyleOf(theme, kind);
        var rest = s.RestFill ?? theme.Palette.PanelBg;
        foreach (var fill in new[] { rest, s.HoverFill, s.PressedFill })
        {
            RGBAColor32.ContrastRatio(s.Ink, fill).ShouldBeGreaterThanOrEqualTo(4.5f, $"{name} {kind} on {fill}");
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_link_reads_on_the_panel_and_the_content_and_the_edge_is_seen_against_the_panel(UiTheme theme, string name)
    {
        RGBAColor32.ContrastRatio(theme.Controls.Hyperlink.Color, theme.Palette.PanelBg).ShouldBeGreaterThanOrEqualTo(4.5f, name);
        RGBAColor32.ContrastRatio(theme.Controls.Hyperlink.Color, theme.Palette.ContentBg).ShouldBeGreaterThanOrEqualTo(4.5f, name);
        RGBAColor32.ContrastRatio(theme.Palette.ControlEdge, theme.Palette.PanelBg).ShouldBeGreaterThanOrEqualTo(3f, name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Accent_ink_is_InkOn_the_accent(UiTheme theme, string name)
    {
        theme.Controls.Accent.Ink.ShouldBe(theme.Palette.InkOn(theme.Palette.Accent), name);
        theme.Controls.Accent.RestFill.ShouldBe(theme.Palette.Accent);
    }

    // ---- shape ----

    private sealed class FloatCtx : IMeasureContext<float>
    {
        public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 8f, 14f);
        public float ToSurface(float designUnits) => designUnits;
        public float ToSurfaceX(float designUnits) => designUnits;
        public float ToSurfaceY(float designUnits) => designUnits;
    }

    private static List<ArrangedNode<float>> Arrange(Node root)
        => [.. Engine.Arrange(root, new Rect<float>(0, 0, 300f, 40f), new FloatCtx())];

    private static readonly UiTheme Dark = ThemeOf(NotificationsRenderTests.Dark);

    [Fact]
    public void A_button_is_the_button_height_and_draws_no_chevron_unless_it_opens()
    {
        var plain = Arrange(Builder.Button("Save", ButtonKind.Standard, () => { }, Dark));
        plain[0].Node.Hit.ShouldBe(new HitResult.ButtonHit("Save"));
        Engine.Measure(Builder.Button("Save", ButtonKind.Standard, () => { }, Dark), new Size<float>(1000f, 100f), new FloatCtx())
            .Height.ShouldBe(28f);
        plain.Any(n => n.Node is Node.Leaf { Content: Content.Icon }).ShouldBeFalse();

        var opens = Arrange(Builder.Button("Save", ButtonKind.Standard, () => { }, Dark, opens: true));
        opens.Count(n => n.Node is Node.Leaf { Content: Content.Icon { Kind: IconKind.CaretRight } }).ShouldBe(1);

        var withIcon = Arrange(Builder.Button("Pin", ButtonKind.Subtle, () => { }, Dark, icon: IconKind.Pin));
        withIcon.Any(n => n.Node is Node.Leaf { Content: Content.Icon { Kind: IconKind.Pin } }).ShouldBeTrue();
    }

    [Fact]
    public void External_adds_the_north_east_arrow_and_internal_does_not()
    {
        bool HasArrow(Node n) => Arrange(n).Any(a => a.Node is Node.Leaf { Content: Content.Text { Value: "↗" } });
        HasArrow(Builder.Hyperlink("Docs", () => { }, Dark, external: true)).ShouldBeTrue();
        HasArrow(Builder.Hyperlink("Docs", () => { }, Dark)).ShouldBeFalse();
    }

    [Fact]
    public void The_link_has_no_fill_the_theme_colour_and_underlines_on_hover()
    {
        var link = Arrange(Builder.Hyperlink("Docs", () => { }, Dark));
        var text = link.Select(a => a.Node).OfType<Node.Leaf>().Select(l => l.Content).OfType<Content.Text>().Single();
        text.Color.ShouldBe(Dark.Controls.Hyperlink.Color);
        text.UnderlineOnHover.ShouldBeTrue();
        link.Any(a => a.Node.Background is not null).ShouldBeFalse();

        var plain = ThemeOf(NotificationsRenderTests.Dark);
        plain = plain with { Controls = plain.Controls with { Hyperlink = plain.Controls.Hyperlink with { UnderlineOnHover = false } } };
        Arrange(Builder.Hyperlink("Docs", () => { }, plain)).Select(a => a.Node).OfType<Node.Leaf>()
            .Select(l => l.Content).OfType<Content.Text>().Single().UnderlineOnHover.ShouldBeFalse();
    }

    // ---- the five states, through real events ----

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
            RenderLayout(root, new RectF32(0, 0, 200, 60), fontPath: "stub.ttf", scale: DesignScale.One);
        }
    }

    private sealed class Rig
    {
        private readonly StubRenderer _renderer = new(200, 60);
        private readonly TestWidget _widget;
        private readonly InputRouter _router;
        public int Clicks;
        public Node Root { get; private set; }

        public Rig(UiTheme theme, ButtonKind kind, string? disabledReason = null, bool focused = false)
            : this(theme, (Node?)null)
        {
            Root = focused
                ? Builder.HStack(Builder.Button("Save", kind, () => Clicks++, theme, disabledReason: disabledReason,
                        hit: new HitResult.ListItemHit("btns", 0))).HFixed(28f).ListOf(Cursor("btns"), "btns")
                : Builder.HStack(Builder.Button("Save", kind, () => Clicks++, theme, disabledReason: disabledReason)).HFixed(28f);
            Paint();
        }

        private static ListCursor Cursor(string id)
        {
            var c = new ListCursor();
            c.Open(id, 0);
            return c;
        }

        public Rig(UiTheme theme, Node? root)
        {
            Root = root ?? Builder.Spacer();
            _renderer.Surface.Clear(theme.Palette.PanelBg);
            _widget = new TestWidget(_renderer);
            _router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [_widget] };
            Paint();
        }

        public void Paint()
        {
            _renderer.Surface.Clear(default);
            _widget.Render(Root);
            _router.AfterPaint();
        }

        // Two units inside the left edge: past the radius' corner cut at y mid, clear of the text.
        public RGBAColor32 Fill()
        {
            var img = _renderer.Surface;
            const int x = 4, y = 14;
            var i = (y * img.Width + x) * 4;
            return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
        }

        public RGBAColor32 Pixel(int x, int y)
        {
            var img = _renderer.Surface;
            var i = (y * img.Width + x) * 4;
            return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
        }

        public int Count(RGBAColor32 c)
        {
            var img = _renderer.Surface;
            var n = 0;
            for (var i = 0; i + 3 < img.Pixels.Length; i += 4)
            {
                if (img.Pixels[i] == c.Red && img.Pixels[i + 1] == c.Green && img.Pixels[i + 2] == c.Blue && img.Pixels[i + 3] == c.Alpha) n++;
            }
            return n;
        }

        public void Move() => MoveTo(5, 14);
        public void MoveTo(float x, float y) => _router.Handle(new InputEvent.MouseMove(x, y));
        public void Down() => _router.Handle(new InputEvent.MouseDown(5, 14, MouseButton.Left, InputModifier.None, 1));
        public void Up() => _router.Handle(new InputEvent.MouseUp(5, 14, MouseButton.Left));
    }

    [Theory]
    [MemberData(nameof(ThemesAndKinds))]
    public void Rest_hover_and_pressed_paint_the_declared_fills_and_a_click_runs(UiTheme theme, string name, ButtonKind kind)
    {
        var s = StyleOf(theme, kind);
        var rig = new Rig(theme, kind);
        var rest = s.RestFill ?? default;
        rig.Fill().ShouldBe(rest, $"{name} {kind} rest");

        rig.Move();
        rig.Paint();
        rig.Fill().ShouldBe(s.HoverFill, $"{name} {kind} hover");

        rig.Down();
        rig.Paint();
        rig.Fill().ShouldBe(s.PressedFill, $"{name} {kind} pressed");

        rig.Up();
        rig.Clicks.ShouldBe(1);
    }

    // Focus shows only for a button in a declared list whose hit is that list's ListItemHit, with the cursor on it.
    [Theory]
    [MemberData(nameof(ThemesAndKinds))]
    public void Focus_paints_the_rest_fill_and_a_Focus_ring_outside_the_edge_for_a_button_under_the_list_cursor(UiTheme theme, string name, ButtonKind kind)
    {
        var s = StyleOf(theme, kind);
        var focused = new Rig(theme, kind, focused: true);
        focused.Fill().ShouldBe(s.RestFill ?? default, $"{name} {kind} focus keeps the rest fill");
        // The button is 28 tall at the top left: the band just below its bottom edge holds the ring, clear of the corner cut.
        focused.Pixel(14, 28).ShouldBe(theme.Palette.Focus, $"{name} {kind} ring below the edge");
        focused.Pixel(14, 29).ShouldBe(theme.Palette.Focus, $"{name} {kind} ring is two units wide");
        var rest = new Rig(theme, kind);
        rest.Fill().ShouldBe(s.RestFill ?? default, $"{name} {kind} not focused");
        rest.Pixel(14, 28).ShouldNotBe(theme.Palette.Focus, $"{name} {kind} no ring unfocused");
    }

    // ---- the link, rendered ----

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_link_draws_no_underline_at_rest_and_one_in_its_colour_on_hover(UiTheme theme, string name)
    {
        var colour = theme.Controls.Hyperlink.Color;
        var rig = new Rig(theme, Builder.HStack(Builder.Hyperlink("Docs", () => { }, theme)).HFixed(28f));
        rig.Count(colour).ShouldBe(0, $"{name} rest");
        rig.Move();
        rig.Paint();
        rig.Count(colour).ShouldBeGreaterThan(0, $"{name} hover");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Hovering_an_external_links_arrow_underlines_its_text(UiTheme theme, string name)
    {
        var style = theme.Controls.Hyperlink;
        var rig = new Rig(theme, Builder.HStack(Builder.Hyperlink("Docs", () => { }, theme, external: true)).HFixed(28f));
        rig.Count(style.Color).ShouldBe(0, $"{name} rest");

        // The stub measures 8 units a character: "Docs" is 0..32, the gap follows, then the 8-unit arrow.
        rig.MoveTo(32f + style.ArrowGap + 4f, 14f);
        rig.Paint();
        rig.Count(style.Color).ShouldBe(32, $"{name} the text's underline, one row as wide as the run");
    }

    [Fact]
    public void Two_links_with_one_label_take_distinct_hits_when_asked()
    {
        var a = Builder.Hyperlink("Docs", () => { }, Dark, hit: new HitResult.ButtonHit("docs-a"));
        var b = Builder.Hyperlink("Docs", () => { }, Dark, hit: new HitResult.ButtonHit("docs-b"));
        a.Hit.ShouldNotBe(b.Hit);
        Builder.Hyperlink("Docs", () => { }, Dark).Hit.ShouldBe(new HitResult.ButtonHit("Docs"));
    }

    [Theory]
    [MemberData(nameof(ThemesAndKinds))]
    public void Disabled_is_flat_takes_no_press_and_says_why(UiTheme theme, string name, ButtonKind kind)
    {
        var s = StyleOf(theme, kind);
        var rig = new Rig(theme, kind, "Sign in first");
        var rest = s.RestFill ?? default;
        rig.Fill().ShouldBe(rest, $"{name} {kind} rest");
        rig.Move();
        rig.Down();
        rig.Paint();
        rig.Fill().ShouldBe(rest, $"{name} {kind} no hover or pressed");
        rig.Up();
        rig.Clicks.ShouldBe(0);

        var node = Builder.Button("Save", kind, () => { }, theme, disabledReason: "Sign in first");
        node.DisabledReason.ShouldBe("Sign in first");
        node.HoverBackground.ShouldBeNull();
        node.PressedBackground.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(ThemesAndKinds))]
    public void Disabled_ink_is_dimmer_than_the_ink(UiTheme theme, string name, ButtonKind kind)
    {
        var s = StyleOf(theme, kind);
        var ground = s.RestFill ?? theme.Palette.PanelBg;
        s.DisabledInk.ShouldNotBeNull();
        RGBAColor32.ContrastRatio(s.DisabledInk!.Value, ground).ShouldBeLessThan(RGBAColor32.ContrastRatio(s.Ink, ground), $"{name} {kind}");
    }
}
