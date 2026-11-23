using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The themed choice controls (<see cref="Builder.Chip"/> now; split button and toggle tile in the sections below as they
/// land) on the four themes of <see cref="ButtonKindsTests"/>: ink at 4.5:1 on the fill painted, the ring at 3:1 against the
/// panel, the states painted through real <see cref="InputRouter"/> events with a stub renderer (no font, no clock).
/// </summary>
public class ChoiceControlsTests
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
        public List<InputModifier> Clicks { get; } = [];
        public ListCursor Cursor { get; } = new();

        public Rig(UiTheme theme, bool on = false, string? disabledReason = null, bool row = false)
        {
            _theme = theme;
            Root = row
                ? Builder.ChipRow(Cursor, "chips", theme,
                    Builder.Chip("One", true, Clicks.Add, theme, hit: new HitResult.ListItemHit("chips", 0)),
                    Builder.Chip("Two", false, Clicks.Add, theme, hit: new HitResult.ListItemHit("chips", 1)),
                    Builder.Chip("Three", false, Clicks.Add, theme, hit: new HitResult.ListItemHit("chips", 2))).HFixed(28f)
                : Builder.HStack(Builder.Chip("Filter", on, Clicks.Add, theme, disabledReason: disabledReason)).HFixed(28f);
            if (row) Cursor.Open("chips", 0);
            _widget = new TestWidget(_renderer);
            _router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [_widget] };
            Paint();
        }

        public void Paint()
        {
            _renderer.Surface.Clear(_theme.Palette.PanelBg);
            _widget.Render(Root);
            _router.AfterPaint();
        }

        public void Dump(string path, int scale = 4)
        {
            var img = _renderer.Surface; int w=(int)img.Width, h=(int)img.Height; var o = new byte[w*scale*h*scale*4];
            for (int y=0;y<h*scale;y++) for (int x=0;x<w*scale;x++) Array.Copy(img.Pixels, ((y/scale)*w+x/scale)*4, o, (y*w*scale+x)*4, 4);
            System.IO.File.WriteAllBytes(path, SharpAstro.Png.PngWriter.Encode(o, (uint)(w*scale), (uint)(h*scale)));
        }

        public RGBAColor32 At(int x, int y)
        {
            var img = _renderer.Surface;
            var i = (y * img.Width + x) * 4;
            return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
        }

        // Past the ring and the corner cut, clear of the text (the stub draws none).
        public RGBAColor32 Fill() => At(14, 14);

        public void Move() => _router.Handle(new InputEvent.MouseMove(14, 14));
        public void Down(InputModifier mod = InputModifier.None)
            => _router.Handle(new InputEvent.MouseDown(14, 14, MouseButton.Left, mod, 1));
        public void Up() => _router.Handle(new InputEvent.MouseUp(14, 14, MouseButton.Left));
        public bool Key(InputKey key)
        {
            var handled = _router.Handle(new InputEvent.KeyDown(key, InputModifier.None));
            Paint();
            return handled;
        }
    }

    // ================= Chip =================

    [Theory]
    [MemberData(nameof(Themes))]
    public void Chip_ink_reads_on_every_fill_at_4_5_to_1(UiTheme theme, string name)
    {
        var s = theme.Controls.ChipOrDerived;
        foreach (var fill in new[] { s.OnFill, s.OnHoverFill, s.OnPressedFill })
        {
            RGBAColor32.ContrastRatio(s.OnInk, fill).ShouldBeGreaterThanOrEqualTo(4.5f, $"{name} on {fill}");
        }
        foreach (var fill in new[] { s.OffFill, s.OffHoverFill, s.OffPressedFill })
        {
            RGBAColor32.ContrastRatio(s.OffInk, fill).ShouldBeGreaterThanOrEqualTo(4.5f, $"{name} off {fill}");
        }
        s.OnInk.ShouldBe(theme.Palette.InkOn(theme.Palette.Accent), name);
        s.OnFill.ShouldBe(theme.Palette.Accent, name);
        s.OnHoverFill.ShouldNotBe(s.OnFill, name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Chip_ring_is_seen_against_the_panel_and_on_has_none(UiTheme theme, string name)
    {
        var s = theme.Controls.ChipOrDerived;
        s.OffEdge.ShouldNotBeNull();
        RGBAColor32.ContrastRatio(s.OffEdge!.Value, theme.Palette.PanelBg).ShouldBeGreaterThanOrEqualTo(3f, name);

        Builder.Chip("A", true, _ => { }, theme).BorderStyle.ShouldBeNull(name);
        Builder.Chip("A", false, _ => { }, theme).BorderStyle.ShouldNotBeNull(name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Chip_disabled_ink_reads_on_its_fill_at_4_5_to_1(UiTheme theme, string name)
    {
        var s = theme.Controls.ChipOrDerived;
        s.DisabledInk.ShouldNotBeNull();
        RGBAColor32.ContrastRatio(s.DisabledInk!.Value, s.OffFill).ShouldBeGreaterThanOrEqualTo(4.5f, name + " off");
        // A disabled chip that is on is painted with the off fill and ring too, so the same pair is what is read.
        var disabledOn = new Rig(theme, on: true, disabledReason: "Nothing to filter");
        disabledOn.Fill().ShouldBe(s.OffFill, name + " on, disabled");
        disabledOn.At(0, 14).ShouldBe(s.OffEdge!.Value, name + " on, disabled ring");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Chip_is_fully_round_and_the_ring_and_fill_are_painted(UiTheme theme, string name)
    {
        var s = theme.Controls.ChipOrDerived;
        var off = new Rig(theme);
        off.At(0, 0).ShouldBe(theme.Palette.PanelBg, $"{name} corner is the ground");
        off.At(0, 14).ShouldBe(s.OffEdge!.Value, $"{name} ring");
        off.Fill().ShouldBe(s.OffFill, name);

        var on = new Rig(theme, on: true);
        on.At(0, 0).ShouldBe(theme.Palette.PanelBg, $"{name} on corner is the ground");
        on.At(0, 14).ShouldBe(s.OnFill, $"{name} on has no ring");
        on.Fill().ShouldBe(s.OnFill, name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Chip_rest_hover_and_pressed_paint_the_declared_fills_on_and_off(UiTheme theme, string name)
    {
        var s = theme.Controls.ChipOrDerived;
        foreach (var on in new[] { false, true })
        {
            var rig = new Rig(theme, on);
            rig.Fill().ShouldBe(on ? s.OnFill : s.OffFill, $"{name} on={on} rest");
            rig.Move();
            rig.Paint();
            rig.Fill().ShouldBe(on ? s.OnHoverFill : s.OffHoverFill, $"{name} on={on} hover");
            rig.Down();
            rig.Paint();
            rig.Fill().ShouldBe(on ? s.OnPressedFill : s.OffPressedFill, $"{name} on={on} pressed");
            rig.Up();
            rig.Clicks.Count.ShouldBe(1);
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Chip_disabled_is_flat_takes_no_press_and_says_why(UiTheme theme, string name)
    {
        var s = theme.Controls.ChipOrDerived;
        var rig = new Rig(theme, disabledReason: "Nothing to filter");
        rig.Fill().ShouldBe(s.OffFill, name);
        rig.Move();
        rig.Down();
        rig.Paint();
        rig.Fill().ShouldBe(s.OffFill, $"{name} no hover or pressed");
        rig.Up();
        rig.Clicks.ShouldBeEmpty();
        Builder.Chip("A", false, _ => { }, theme, disabledReason: "Nothing to filter").DisabledReason.ShouldBe("Nothing to filter");
    }

    [Fact]
    public void Chip_click_passes_the_modifier_of_the_press()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var rig = new Rig(theme);
        rig.Down(InputModifier.Shift);
        rig.Up();
        rig.Down();
        rig.Up();
        rig.Clicks.ShouldBe([InputModifier.Shift, InputModifier.None]);
    }

    [Fact]
    public void Chip_height_is_the_button_height_and_the_hit_defaults_to_a_ButtonHit()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var chip = Builder.Chip("Filter", false, _ => { }, theme);
        chip.Hit.ShouldBe(new HitResult.ButtonHit("Filter"));
        chip.CornerRadius.ShouldBe(theme.Metrics.ButtonHeight / 2f);
    }

    [Fact]
    public void A_ControlStyles_written_without_a_chip_still_derives_one()
    {
        var d = ThemeOf(NotificationsRenderTests.Dark).Controls;
        var old = new ControlStyles { Accent = d.Accent, Standard = d.Standard, Subtle = d.Subtle, Hyperlink = d.Hyperlink };
        old.Chip.ShouldBeNull();
        old.ChipOrDerived.OnFill.ShouldBe(d.Accent.RestFill!.Value);
        old.ChipOrDerived.OffEdge.ShouldBe(d.Standard.Edge);
    }

    [Fact]
    public void A_chip_row_declares_a_horizontal_list_that_Right_steps_without_pressing()
    {
        var rig = new Rig(ThemeOf(NotificationsRenderTests.Dark), row: true);
        rig.Cursor.Index.ShouldBe(0);
        rig.Key(InputKey.Right).ShouldBeTrue();
        rig.Cursor.Index.ShouldBe(1);
        rig.Clicks.ShouldBeEmpty();
    }

    // ================= SplitButton =================

    private sealed class SplitRig
    {
        private readonly StubRenderer _renderer = new(300, 60);
        private readonly TestWidget _widget;
        private readonly InputRouter _router;
        private readonly UiTheme _theme;
        public Node Root { get; }
        public List<InputModifier> Primary { get; } = [];
        public int Details { get; private set; }
        public int LeftX { get; }
        public int RightX { get; }
        public int DividerX { get; }

        public SplitRig(UiTheme theme, bool on = false, string? disabledReason = null, bool focused = false)
        {
            _theme = theme;
            var split = Builder.SplitButton("Run", on, Primary.Add, () => Details++, theme,
                tooltip: "Run it", detailsTooltip: "Run options", disabledReason: disabledReason,
                hit: focused ? new HitResult.ListItemHit("split", 0) : null);
            Root = Builder.HStack(split).HFixed(28f);
            if (focused)
            {
                var cursor = new ListCursor();
                cursor.Open("split", 0);
                Root = Root.ListOf(cursor, "split");
            }
            var pad = theme.Controls.Standard.PaddingX;
            var leftWidth = 2 * pad + 24f;
            LeftX = (int)(pad + 12f);
            DividerX = (int)leftWidth;
            RightX = (int)(leftWidth + theme.Controls.Standard.EdgeWidth + 3f);
            _widget = new TestWidget(_renderer);
            _router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [_widget] };
            Paint();
        }

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

        public void Move(int x) => _router.Handle(new InputEvent.MouseMove(x, 14));
        public void Down(int x) => _router.Handle(new InputEvent.MouseDown(x, 14, MouseButton.Left, InputModifier.None, 1));
        public void Up(int x) => _router.Handle(new InputEvent.MouseUp(x, 14, MouseButton.Left));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void SplitButton_ink_reads_on_every_fill_and_the_ring_is_seen(UiTheme theme, string name)
    {
        var c = theme.Controls;
        foreach (var s in new[] { c.Accent, c.Standard })
        {
            foreach (var fill in new[] { s.RestFill ?? s.HoverFill, s.HoverFill, s.PressedFill })
            {
                RGBAColor32.ContrastRatio(s.Ink, fill).ShouldBeGreaterThanOrEqualTo(4.5f, $"{name} {fill}");
            }
        }
        RGBAColor32.ContrastRatio(c.Standard.DisabledInk!.Value, c.Standard.RestFill ?? c.Standard.HoverFill)
            .ShouldBeGreaterThanOrEqualTo(4.5f, name + " disabled");
        RGBAColor32.ContrastRatio(c.Standard.Edge!.Value, theme.Palette.PanelBg).ShouldBeGreaterThanOrEqualTo(3f, name + " ring");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void SplitButton_paints_rest_on_and_disabled_fills_a_divider_and_a_ring(UiTheme theme, string name)
    {
        var c = theme.Controls;
        var off = new SplitRig(theme);
        off.At(off.LeftX, 14).ShouldBe(c.Standard.RestFill ?? c.Standard.HoverFill, name + " left");
        off.At(off.RightX, 14).ShouldBe(c.Standard.RestFill ?? c.Standard.HoverFill, name + " right");
        off.At(0, 14).ShouldBe(c.Standard.Edge!.Value, name + " ring");
        off.At(off.DividerX, 14).ShouldBe(c.Standard.Edge.Value, name + " divider");

        var on = new SplitRig(theme, on: true);
        on.At(on.LeftX, 14).ShouldBe(c.Accent.RestFill ?? c.Accent.HoverFill, name + " on");
        on.At(on.DividerX, 14).ShouldBe(c.Accent.Ink, name + " on divider");

        var dis = new SplitRig(theme, on: true, disabledReason: "Nothing to run");
        dis.At(dis.LeftX, 14).ShouldBe(c.Standard.RestFill ?? c.Standard.HoverFill, name + " disabled is flat");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void SplitButton_hover_and_pressed_light_one_part_and_leave_the_other_at_rest(UiTheme theme, string name)
    {
        var s = theme.Controls.Standard;
        var rest = s.RestFill ?? s.HoverFill;
        var rig = new SplitRig(theme);
        rig.Move(rig.LeftX);
        rig.Paint();
        rig.At(rig.LeftX, 14).ShouldBe(s.HoverFill, name + " left hover");
        rig.At(rig.RightX, 14).ShouldBe(rest, name + " right stays");
        rig.Move(rig.RightX);
        rig.Paint();
        rig.At(rig.LeftX, 14).ShouldBe(rest, name + " left stays");
        rig.At(rig.RightX, 14).ShouldBe(s.HoverFill, name + " right hover");
        rig.Down(rig.RightX);
        rig.Paint();
        rig.At(rig.RightX, 14).ShouldBe(s.PressedFill, name + " right pressed");
        rig.At(rig.LeftX, 14).ShouldBe(rest, name + " left unpressed");
        rig.Up(rig.RightX);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void SplitButton_a_press_reaches_only_its_own_callback(UiTheme theme, string name)
    {
        var rig = new SplitRig(theme);
        rig.Down(rig.LeftX);
        rig.Up(rig.LeftX);
        rig.Primary.Count.ShouldBe(1, name);
        rig.Details.ShouldBe(0, name);
        rig.Down(rig.RightX);
        rig.Up(rig.RightX);
        rig.Primary.Count.ShouldBe(1, name);
        rig.Details.ShouldBe(1, name);

        var disabled = new SplitRig(theme, disabledReason: "No");
        disabled.Down(disabled.LeftX);
        disabled.Up(disabled.LeftX);
        disabled.Down(disabled.RightX);
        disabled.Up(disabled.RightX);
        disabled.Primary.ShouldBeEmpty(name);
        disabled.Details.ShouldBe(0, name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void SplitButton_shows_no_notch_beside_the_divider_at_rest(UiTheme theme, string name)
    {
        var s = theme.Controls.Standard;
        var rest = s.RestFill ?? s.HoverFill;
        var row = (int)Math.Ceiling(s.EdgeWidth);
        var rig = new SplitRig(theme);
        rig.At(rig.DividerX - 1, row).ShouldBe(rest, name + " left of divider");
        rig.At(rig.DividerX + (int)Math.Ceiling(s.EdgeWidth), row).ShouldBe(rest, name + " right of divider");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void SplitButton_on_hover_lights_one_part_and_disabled_says_why_on_both(UiTheme theme, string name)
    {
        var a = theme.Controls.Accent;
        var rig = new SplitRig(theme, on: true);
        rig.Move(rig.RightX);
        rig.Paint();
        rig.At(rig.RightX, 14).ShouldBe(a.HoverFill, name + " right hover on");
        rig.At(rig.LeftX, 14).ShouldBe(a.RestFill ?? a.HoverFill, name + " left stays on");

        var node = Builder.SplitButton("Run", true, _ => { }, () => { }, theme, disabledReason: "Nothing to run");
        var stack = (Node.Stack)((Node.Overlay)node).Base;
        stack.Children[0].DisabledReason.ShouldBe("Nothing to run", name);
        stack.Children[2].DisabledReason.ShouldBe("Nothing to run", name);
    }

    [Fact]
    public void SplitButton_caret_has_a_default_tooltip_and_a_callers_wins()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var plain = (Node.Stack)((Node.Overlay)Builder.SplitButton("Run", false, _ => { }, () => { }, theme)).Base;
        plain.Children[2].Tooltip.ShouldBe("Run options");
        var custom = (Node.Stack)((Node.Overlay)Builder.SplitButton("Run", false, _ => { }, () => { }, theme, detailsTooltip: "More")).Base;
        custom.Children[2].Tooltip.ShouldBe("More");
    }

    [Fact]
    public void SplitButton_parts_have_distinct_hits_tooltips_height_and_the_right_part_a_caret()
    {
        var theme = ThemeOf(NotificationsRenderTests.Dark);
        var node = Builder.SplitButton("Run", false, _ => { }, () => { }, theme, tooltip: "Run it", detailsTooltip: "Run options");
        var stack = (Node.Stack)((Node.Overlay)node).Base;
        stack.Children.Length.ShouldBe(3);
        var left = stack.Children[0];
        var right = (Node.Stack)stack.Children[2];
        left.Hit.ShouldBe(new HitResult.ButtonHit("Run"));
        right.Hit.ShouldBe(new HitResult.ButtonHit("Run/details"));
        left.Tooltip.ShouldNotBe(right.Tooltip);
        ((Node.Leaf)right.Children[0]).Content.ShouldBeOfType<Content.Icon>().Kind.ShouldBe(IconKind.CaretRight);
        stack.Height.ShouldBe(Sizing.Fixed(theme.Metrics.ButtonHeight));
    }

    // ================= ToggleTile =================

    private sealed class TileRig
    {
        private readonly StubRenderer _renderer = new(300, 120);
        private readonly TestWidget _widget;
        private readonly InputRouter _router;
        private readonly UiTheme _theme;
        public Node Root { get; }
        public List<InputModifier> Toggles { get; } = [];

        public TileRig(UiTheme theme, bool on = false, string? disabledReason = null, bool opens = false,
            string label = "Grid", bool focused = false)
        {
            _theme = theme;
            Root = Builder.HStack(Builder.ToggleTile(IconKind.Pin, label, on, Toggles.Add, theme, opens, disabledReason,
                hit: focused ? new HitResult.ListItemHit("tiles", 0) : null)).HFixed(110f);
            if (focused)
            {
                var cursor = new ListCursor();
                cursor.Open("tiles", 0);
                Root = Root.ListOf(cursor, "tiles");
            }
            _widget = new TestWidget(_renderer);
            _router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [_widget] };
            Paint();
        }

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

        // Inside the tile, clear of the ring, the corner cut and the centred icon.
        public RGBAColor32 Fill() => At(5, 5);
        public void Move(int x = 5, int y = 5) => _router.Handle(new InputEvent.MouseMove(x, y));
        public void Down(int x = 5, int y = 5, InputModifier mod = InputModifier.None)
            => _router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, mod, 1));
        public void Up(int x = 5, int y = 5) => _router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ToggleTile_ink_reads_on_every_fill_and_the_ring_is_seen(UiTheme theme, string name)
    {
        var s = theme.Controls.TileOrDerived;
        foreach (var fill in new[] { s.OnFill, s.OnHoverFill, s.OnPressedFill })
        {
            RGBAColor32.ContrastRatio(s.OnInk, fill).ShouldBeGreaterThanOrEqualTo(4.5f, $"{name} on {fill}");
        }
        foreach (var fill in new[] { s.OffFill, s.OffHoverFill, s.OffPressedFill })
        {
            RGBAColor32.ContrastRatio(s.OffInk, fill).ShouldBeGreaterThanOrEqualTo(4.5f, $"{name} off {fill}");
        }
        RGBAColor32.ContrastRatio(s.DisabledInk!.Value, s.OffFill).ShouldBeGreaterThanOrEqualTo(4.5f, name + " disabled");
        RGBAColor32.ContrastRatio(s.LabelInk, theme.Palette.PanelBg).ShouldBeGreaterThanOrEqualTo(4.5f, name + " label");
        RGBAColor32.ContrastRatio(s.DisabledInk.Value, theme.Palette.PanelBg).ShouldBeGreaterThanOrEqualTo(4.5f, name + " disabled label");
        RGBAColor32.ContrastRatio(s.OffEdge!.Value, theme.Palette.PanelBg).ShouldBeGreaterThanOrEqualTo(3f, name + " ring");
        s.OnInk.ShouldBe(theme.Palette.InkOn(theme.Palette.Accent), name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ToggleTile_paints_off_on_and_disabled_fills_and_a_ring_only_when_off(UiTheme theme, string name)
    {
        var s = theme.Controls.TileOrDerived;
        var off = new TileRig(theme);
        off.Fill().ShouldBe(s.OffFill, name + " off");
        off.At(0, 21).ShouldBe(s.OffEdge!.Value, name + " ring");

        var on = new TileRig(theme, on: true);
        on.Fill().ShouldBe(s.OnFill, name + " on");
        on.At(0, 21).ShouldBe(s.OnFill, name + " on has no ring");

        var dis = new TileRig(theme, on: true, disabledReason: "Not now");
        dis.Fill().ShouldBe(s.OffFill, name + " disabled is flat and neutral");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ToggleTile_hover_and_pressed_fill_the_tile(UiTheme theme, string name)
    {
        var s = theme.Controls.TileOrDerived;
        foreach (var on in new[] { false, true })
        {
            var rig = new TileRig(theme, on);
            rig.Move();
            rig.Paint();
            rig.Fill().ShouldBe(on ? s.OnHoverFill : s.OffHoverFill, $"{name} hover on={on}");
            rig.Down();
            rig.Paint();
            rig.Fill().ShouldBe(on ? s.OnPressedFill : s.OffPressedFill, $"{name} pressed on={on}");
            rig.Up();
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ToggleTile_a_press_toggles_through_the_router_and_passes_the_modifier(UiTheme theme, string name)
    {
        var rig = new TileRig(theme);
        rig.Down(mod: InputModifier.Shift);
        rig.Up();
        rig.Toggles.ShouldBe([InputModifier.Shift], name);
        // The label is part of the same hit.
        rig.Down(5, 48);
        rig.Up(5, 48);
        rig.Toggles.Count.ShouldBe(2, name + " label");

        var disabled = new TileRig(theme, disabledReason: "No");
        disabled.Down();
        disabled.Up();
        disabled.Toggles.ShouldBeEmpty(name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ToggleTile_a_very_long_label_keeps_the_tile_at_its_size_within_its_width_with_the_tooltip(UiTheme theme, string name)
    {
        const string Long = "A very long label that would squeeze the icon if it were allowed to push the tile wider";
        var node = Builder.ToggleTile(IconKind.Pin, Long, false, _ => { }, theme);
        var side = theme.Metrics.ButtonHeight * theme.Controls.TileOrDerived.SideScale;
        node.Width.ShouldBe(Sizing.Fixed(side), name);
        var stack = (Node.Stack)node;
        var tile = stack.Children[0];
        var caption = stack.Children[1];
        tile.Width.ShouldBe(Sizing.Fixed(side), name);
        tile.Height.ShouldBe(Sizing.Fixed(side), name);
        caption.Width.ShouldBe(Sizing.Fixed(side), name);
        caption.Tooltip.ShouldBe(Long, name);
        tile.Tooltip.ShouldBe(Long, name);

        var rig = new TileRig(theme, label: Long);
        rig.Fill().ShouldBe(theme.Controls.TileOrDerived.OffFill, name);
        // The stub renderer draws no glyphs, so the caption's painted extent is read from its hit region: a press on the
        // caption row inside the tile width toggles, and one beyond it does not (the label never widens its hit).
        var captionY = (int)(side + theme.Controls.TileOrDerived.LabelGap) + 3;
        rig.Down((int)side / 2, captionY);
        rig.Up((int)side / 2, captionY);
        rig.Toggles.Count.ShouldBe(1, name + " caption inside the tile width");
        rig.Down((int)Math.Ceiling(side) + 6, captionY);
        rig.Up((int)Math.Ceiling(side) + 6, captionY);
        rig.Toggles.Count.ShouldBe(1, name + " nothing beyond the tile width");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ToggleTile_a_very_long_label_ends_in_an_ellipsis_within_the_tile_width(UiTheme theme, string name)
    {
        const string Long = "A very long label that would squeeze the icon if it were allowed to push the tile wider";
        var stack = (Node.Stack)Builder.ToggleTile(IconKind.Pin, Long, false, _ => { }, theme);
        var caption = stack.Children[1].ShouldBeOfType<Node.Leaf>();
        var text = caption.Content.ShouldBeOfType<Content.Text>();
        text.Trim.ShouldBe(TextTrim.End, name);
        var side = theme.Metrics.ButtonHeight * theme.Controls.TileOrDerived.SideScale;

        // The pixel painter fits each run to its arranged rect through TextFit.ForWidth; with a real text face that is
        // the label cut short with a trailing ellipsis, measuring no wider than the tile.
        var font = System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
        using var renderer = new RgbaImageRenderer(64, 64);
        var (fitted, size) = TextFit.ForWidth(renderer, Long, font, null, text.FontSize, side, text.Trim);
        size.ShouldBe(text.FontSize, name);
        fitted.ShouldEndWith("…", Case.Sensitive, name);
        fitted.Length.ShouldBeLessThan(Long.Length, name);
        Long.ShouldStartWith(fitted[..^1], Case.Sensitive, name);
        renderer.MeasureText(fitted, font, size).Width.ShouldBeLessThanOrEqualTo(side, name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ToggleTile_a_callers_tooltip_describes_the_tile_and_the_caption_keeps_the_whole_label(UiTheme theme, string name)
    {
        var node = (Node.Stack)Builder.ToggleTile(IconKind.Pin, "A long label", false, _ => { }, theme, tooltip: "Show the grid");
        node.Children[0].Tooltip.ShouldBe("Show the grid", name);
        node.Children[1].Tooltip.ShouldBe("A long label", name);
        var disabled = (Node.Stack)Builder.ToggleTile(IconKind.Pin, "Grid", true, _ => { }, theme, disabledReason: "No image");
        disabled.Children[0].DisabledReason.ShouldBe("No image", name);
        disabled.Children[1].DisabledReason.ShouldBe("No image", name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ToggleTile_label_gap_is_the_same_set_or_derived(UiTheme theme, string name)
    {
        var set = theme.Controls.Tile!.LabelGap;
        (theme.Controls with { Tile = null }).TileOrDerived.LabelGap.ShouldBe(set, name);
        set.ShouldBe(theme.Controls.Standard.PaddingX / 2f, name);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ToggleTile_opens_draws_a_caret_in_the_tile(UiTheme theme, string name)
    {
        var plain = (Node.Stack)Builder.ToggleTile(IconKind.Pin, "Grid", false, _ => { }, theme);
        var opens = (Node.Stack)Builder.ToggleTile(IconKind.Pin, "Grid", false, _ => { }, theme, opens: true);
        var plainTile = (Node.Stack)plain.Children[0];
        var opensTile = (Node.Stack)opens.Children[0];
        opensTile.Children.Length.ShouldBe(plainTile.Children.Length + 1, name);
        opensTile.Children.OfType<Node.Leaf>().Select(l => l.Content).OfType<Content.Icon>()
            .Select(i => i.Kind).ShouldContain(IconKind.CaretRight, name);
    }

    // ================= Focus ring (design-language section 6) =================

    [Theory]
    [MemberData(nameof(Themes))]
    public void Chip_focus_keeps_the_rest_fill_and_draws_a_Focus_ring_outside_the_edge(UiTheme theme, string name)
    {
        var s = theme.Controls.ChipOrDerived;
        var rig = new Rig(theme, on: true, row: true);
        // A ChipRow lays its chips out 16 units in from the top left (a probe of the painted pixels showed the chip
        // spanning y 16..43 with the ring at 46..47), so probe relative to that origin: chip 0 is on and under the cursor.
        const int origin = 16;
        var x = origin + 8;
        var bottom = origin + 28;
        rig.At(x, origin + 14).ShouldBe(s.OnFill, name + " fill is the rest fill");
        var gap = (int)theme.Metrics.FocusRingGap;
        rig.At(x, bottom).ShouldNotBe(theme.Palette.Focus, name + " gap is not the ring");
        rig.At(x, bottom).ShouldNotBe(s.OnFill, name + " gap is not the fill");
        rig.At(x, bottom + gap).ShouldBe(theme.Palette.Focus, name + " ring below the gap");
        rig.At(x, bottom + gap + 1).ShouldBe(theme.Palette.Focus, name + " ring is two units wide");
        new Rig(theme, on: true).At(14, 28 + gap).ShouldNotBe(theme.Palette.Focus, name + " no ring unfocused");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void SplitButton_focus_keeps_the_rest_fill_and_draws_a_Focus_ring_outside_the_edge(UiTheme theme, string name)
    {
        var s = theme.Controls.Standard;
        var rig = new SplitRig(theme, focused: true);
        var gap = (int)theme.Metrics.FocusRingGap;
        rig.At(rig.LeftX, 14).ShouldBe(s.RestFill ?? s.HoverFill, name + " fill is the rest fill");
        rig.At(rig.LeftX, 28).ShouldNotBe(theme.Palette.Focus, name + " gap is not the ring");
        rig.At(rig.LeftX, 28 + gap).ShouldBe(theme.Palette.Focus, name + " ring below the gap");
        rig.At(rig.LeftX, 29 + gap).ShouldBe(theme.Palette.Focus, name + " ring is two units wide");
        var rest = new SplitRig(theme);
        rest.At(rest.LeftX, 28 + gap).ShouldNotBe(theme.Palette.Focus, name + " no ring unfocused");
        // The side facing the other part carries no ring: it never reaches into the details part.
        var inner = (int)(rig.DividerX + theme.Controls.Standard.EdgeWidth) + 1;
        rig.At(inner + 1, 14).ShouldNotBe(theme.Palette.Focus, name + " ring does not reach into the other part");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ToggleTile_focus_keeps_the_rest_fill_and_draws_a_Focus_ring_outside_the_edge(UiTheme theme, string name)
    {
        var side = (int)(theme.Metrics.ButtonHeight * theme.Controls.TileOrDerived.SideScale);
        var rig = new TileRig(theme, focused: true);
        rig.Fill().ShouldBe(theme.Controls.TileOrDerived.OffFill, name + " fill is the rest fill");
        var gap = (int)theme.Metrics.FocusRingGap;
        rig.At(side / 2, side).ShouldNotBe(theme.Palette.Focus, name + " gap is not the ring");
        rig.At(side / 2, side + gap).ShouldBe(theme.Palette.Focus, name + " ring below the gap");
        rig.At(side / 2, side + gap + 1).ShouldBe(theme.Palette.Focus, name + " ring is two units wide");
        new TileRig(theme).At(side / 2, side + gap).ShouldNotBe(theme.Palette.Focus, name + " no ring unfocused");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void TmpDump(UiTheme theme, string name)
    {
        new SplitRig(theme, focused: true).Dump("C:/Users/SebastianGodelet/.claude/jobs/319a50ad/tmp/split-" + name + "-" + theme.Palette.Accent.GetHashCode() + ".png");
    }
}
