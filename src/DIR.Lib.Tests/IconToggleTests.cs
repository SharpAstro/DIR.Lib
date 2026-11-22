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
/// <see cref="Builder.IconToggle"/> and <see cref="Builder.IconAction"/>: the look follows the value the caller gives,
/// the words are the tooltip, the optional word drops at the measured width, a disabled button takes no press and says
/// why, and the icon's contrast on its ground in a dark, a light and a one-hue palette. Baselines icontoggle_off.png,
/// icontoggle_on.png and icontoggle_word.png.
/// </summary>
public class IconToggleTests
{
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static UiTheme ThemeOf(UiPalette p) => new() { Palette = p, Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f) };

    private static readonly UiTheme Dark = ThemeOf(NotificationsRenderTests.Dark);

    private sealed class FloatCtx : IMeasureContext<float>
    {
        public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 8f, 14f);
        public float ToSurface(float designUnits) => designUnits;
        public float ToSurfaceX(float designUnits) => designUnits;
        public float ToSurfaceY(float designUnits) => designUnits;
    }

    private static List<ArrangedNode<float>> Arrange(Node root, float width)
        => [.. Engine.Arrange(root, new Rect<float>(0, 0, width, 40f), new FloatCtx())];

    private static IconKind[] IconsOf(IEnumerable<ArrangedNode<float>> a)
        => [.. a.Where(n => n.Node is Node.Leaf { Content: Content.Icon }).Select(n => ((Content.Icon)((Node.Leaf)n.Node).Content).Kind)];

    // ---- filled form ----

    [Fact]
    public void Pin_takes_PinFilled_and_a_kind_without_a_filled_form_keeps_its_outline()
    {
        IconSet.FilledForm(IconKind.Pin).ShouldBe(IconKind.PinFilled);
        IconSet.FilledForm(IconKind.OpenInPage).ShouldBe(IconKind.OpenInPage);
        IconSet.FilledForm(IconKind.PinFilled).ShouldBe(IconKind.PinFilled);
    }

    [Fact]
    public void Pressed_fills_with_the_accent_and_takes_the_filled_icon()
    {
        var off = Arrange(Builder.IconToggle(IconKind.Pin, false, _ => { }, Dark, "Pin"), 200f);
        var on = Arrange(Builder.IconToggle(IconKind.Pin, true, _ => { }, Dark, "Pin"), 200f);
        IconsOf(off).ShouldBe([IconKind.Pin]);
        IconsOf(on).ShouldBe([IconKind.PinFilled]);
        on[0].Node.Background.ShouldBe(Dark.Palette.Accent);
        off[0].Node.Background.ShouldNotBe(Dark.Palette.Accent);
    }

    [Fact]
    public void An_action_is_never_pressed()
    {
        var a = Arrange(Builder.IconAction(IconKind.OpenInPage, () => { }, Dark, "Open in page"), 200f);
        IconsOf(a).ShouldBe([IconKind.OpenInPage]);
        a[0].Node.Background.ShouldNotBe(Dark.Palette.Accent);
    }

    [Fact]
    public void The_button_is_square_at_the_button_height()
    {
        var size = Engine.Measure(Builder.IconToggle(IconKind.Pin, false, _ => { }, Dark, "Pin"), new Size<float>(1000f, 100f), new FloatCtx());
        size.Width.ShouldBe(28f);
        size.Height.ShouldBe(28f);
    }

    // ---- word drops ----

    [Fact]
    public void The_word_drops_at_the_measured_width_and_the_words_stay_the_tooltip()
    {
        var tree = Builder.IconToggle(IconKind.Pin, false, _ => { }, Dark, "Pin this page", word: "Pin");
        var full = Engine.Measure(tree, new Size<float>(100_000f, 40f), new FloatCtx()).Width;
        full.ShouldBeGreaterThan(28f);

        Arrange(tree, full).Count(n => n.Node is Node.Leaf { Content: Content.Text }).ShouldBe(1);
        var narrow = Arrange(tree, full - 1f);
        narrow.Count(n => n.Node is Node.Leaf { Content: Content.Text }).ShouldBe(0);
        narrow.First(n => n.Node.Hit is not null).Node.Tooltip.ShouldBe("Pin this page");
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
        public bool Value { get; set; }
        public List<bool> Asked { get; } = [];
        public string? DisabledReason { get; }
        public Widget Widget { get; }
        public InputRouter Router { get; }

        public Rig(string? disabledReason = null)
        {
            DisabledReason = disabledReason;
            var renderer = new RgbaImageRenderer(200u, 60u);
            Widget = new Widget(renderer) { FontPath = FontPath };
            Router = new InputRouter(Widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [Widget] };
            Paint();
        }

        public void Paint()
        {
            Widget.Tree = Builder.HStack(Builder.IconToggle(IconKind.Pin, Value, v => Asked.Add(v), Dark, "Pin", disabledReason: DisabledReason)).HFixed(28f);
            Widget.Render(200f, 60f);
            Router.AfterPaint();
        }

        public ClickableRegion Region()
        {
            var regions = new List<ClickableRegion>();
            Widget.CollectPaintedRegions(regions);
            return regions.First(r => r.Result is HitResult.ButtonHit);
        }

        public void Click()
        {
            var r = Region();
            var x = r.X + r.Width / 2f;
            var y = r.Y + r.Height / 2f;
            Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
            Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left, InputModifier.None));
            Paint();
        }

        public bool Pressed()
        {
            return Arrange(Widget.Tree, 200f).First(n => n.Node.Hit is HitResult.ButtonHit).Node.Background == Dark.Palette.Accent;
        }
    }

    [Fact]
    public void The_look_follows_the_value_the_caller_gives_not_the_click()
    {
        var rig = new Rig();
        rig.Pressed().ShouldBeFalse();

        rig.Click();
        rig.Asked.ShouldBe([true]); // handed the other state
        rig.Pressed().ShouldBeFalse(); // the caller has not passed it yet

        rig.Value = true;
        rig.Paint();
        rig.Pressed().ShouldBeTrue();

        rig.Click();
        rig.Asked.ShouldBe([true, false]);
        rig.Pressed().ShouldBeTrue();
    }

    [Fact]
    public void Tooltip_carries_the_words()
    {
        new Rig().Region().Tooltip.ShouldBe("Pin");
    }

    [Fact]
    public void A_disabled_toggle_takes_no_press_and_says_why()
    {
        var rig = new Rig("Sign in to pin");
        var region = rig.Region();
        region.IsDisabled.ShouldBeTrue();
        region.Tooltip.ShouldBe("Sign in to pin");
        rig.Click();
        rig.Asked.ShouldBeEmpty();
    }

    // ---- hit identity ----

    private static (Widget Widget, InputRouter Router) Paint(Node tree)
    {
        var widget = new Widget(new RgbaImageRenderer(200u, 60u)) { FontPath = FontPath, Tree = tree };
        var router = new InputRouter(widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [widget] };
        widget.Render(200f, 60f);
        router.AfterPaint();
        return (widget, router);
    }

    private static ClickableRegion RegionOf(Widget widget, HitResult hit)
    {
        var regions = new List<ClickableRegion>();
        widget.CollectPaintedRegions(regions);
        return regions.First(r => r.Result == hit);
    }

    private static void ClickOn(InputRouter router, ClickableRegion r)
    {
        var x = r.X + r.Width / 2f;
        var y = r.Y + r.Height / 2f;
        router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
        router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left, InputModifier.None));
    }

    [Fact]
    public void An_IconToggle_with_a_hit_registers_that_hit()
    {
        var hit = new HitResult.ButtonHit("pin-row-3");
        var tree = Builder.HStack(Builder.IconToggle(IconKind.Pin, false, _ => { }, Dark, "Pin", hit: hit)).HFixed(28f);
        Arrange(tree, 200f).Any(n => Equals(n.Node.Hit, hit)).ShouldBeTrue();
        var asked = new List<bool>();
        var pressTree = Builder.HStack(Builder.IconToggle(IconKind.Pin, false, asked.Add, Dark, "Pin", hit: hit)).HFixed(28f);
        var (w, router) = Paint(pressTree);
        RegionOf(w, hit).Result.ShouldBe(hit);
        ClickOn(router, RegionOf(w, hit));
        asked.ShouldBe([true]);
    }

    [Fact]
    public void An_IconAction_with_a_hit_registers_that_hit()
    {
        var hit = new HitResult.ButtonHit("open-row-3");
        var pressed = 0;
        var tree = Builder.HStack(Builder.IconAction(IconKind.OpenInPage, () => pressed++, Dark, "Open", hit: hit)).HFixed(28f);
        Arrange(tree, 200f).Any(n => Equals(n.Node.Hit, hit)).ShouldBeTrue();
        var (w, router) = Paint(tree);
        ClickOn(router, RegionOf(w, hit));
        pressed.ShouldBe(1);
    }

    [Fact]
    public void Two_Pin_toggles_with_distinct_hits_are_told_apart()
    {
        var hitA = new HitResult.ButtonHit("pin-a");
        var hitB = new HitResult.ButtonHit("pin-b");
        var asked = new List<string>();
        var tree = Builder.HStack(
            Builder.IconToggle(IconKind.Pin, false, _ => asked.Add("a"), Dark, "Pin", hit: hitA).WFixed(28f),
            Builder.IconToggle(IconKind.Pin, false, _ => asked.Add("b"), Dark, "Pin", hit: hitB).WFixed(28f)).HFixed(28f);
        var (w, router) = Paint(tree);
        var a = RegionOf(w, hitA);
        var b = RegionOf(w, hitB);
        a.X.ShouldNotBe(b.X);
        ClickOn(router, b);
        asked.ShouldBe(["b"]);
        ClickOn(router, a);
        asked.ShouldBe(["b", "a"]);
    }

    [Fact]
    public void Without_a_hit_the_identity_is_the_icon_toggle_kind()
    {
        var expected = new HitResult.ButtonHit("icon-toggle:" + IconKind.Pin);
        var tree = Builder.HStack(Builder.IconToggle(IconKind.Pin, false, _ => { }, Dark, "Pin")).HFixed(28f);
        Arrange(tree, 200f).Any(n => Equals(n.Node.Hit, expected)).ShouldBeTrue();
        var (w, _) = Paint(tree);
        RegionOf(w, expected).Result.ShouldBe(expected);
    }

    // ---- contrast ----

    public static TheoryData<string> Palettes => new() { "dark", "light", "onehue" };

    private static UiPalette PaletteNamed(string name) => name switch
    {
        "dark" => NotificationsRenderTests.Dark,
        "light" => NotificationsRenderTests.Light,
        _ => NotificationsRenderTests.OneHue,
    };

    [Theory]
    [MemberData(nameof(Palettes))]
    public void The_icon_reads_on_its_ground_off_and_on(string name)
    {
        var p = PaletteNamed(name);
        RGBAColor32.ContrastRatio(p.BodyText, p.ContentBg).ShouldBeGreaterThanOrEqualTo(3f);
        RGBAColor32.ContrastRatio(p.BodyText, p.Selection).ShouldBeGreaterThanOrEqualTo(3f);
        RGBAColor32.ContrastRatio(p.InkOn(p.Accent), p.Accent).ShouldBeGreaterThanOrEqualTo(3f);
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void The_painted_pressed_icon_uses_the_ink_the_palette_names(string name)
    {
        var p = PaletteNamed(name);
        var arranged = Arrange(Builder.IconToggle(IconKind.Pin, true, _ => { }, ThemeOf(p), "Pin"), 200f);
        var ink = ((Content.Icon)((Node.Leaf)arranged.First(n => n.Node is Node.Leaf { Content: Content.Icon }).Node).Content).Color;
        ink.ShouldBe(p.InkOn(p.Accent));
    }

    // ---- baselines ----

    private static RgbaImage Draw(UiTheme theme, float width, params Node[] buttons)
    {
        var renderer = new RgbaImageRenderer((uint)width, 56u);
        renderer.Surface.Clear(theme.Palette.ContentBg);
        var widget = new Widget(renderer) { FontPath = FontPath };
        var row = new List<Node> { Builder.Spacer().WFixed(12f) };
        foreach (var b in buttons)
        {
            row.Add(b);
            row.Add(Builder.Spacer().WFixed(8f));
        }

        widget.Tree = Builder.VStack(Builder.Spacer().HFixed(12f), Builder.HStack([.. row]).HFixed(theme.Metrics.ButtonHeight)).Stretch();
        widget.Render(width, 56f);
        return renderer.Surface;
    }

    [Fact]
    public void Off() => CompareBaseline(Draw(Dark, 140f,
        Builder.IconToggle(IconKind.Pin, false, _ => { }, Dark, "Pin"),
        Builder.IconAction(IconKind.OpenInPage, () => { }, Dark, "Open in page")), "icontoggle_off.png");

    [Fact]
    public void On() => CompareBaseline(Draw(Dark, 140f,
        Builder.IconToggle(IconKind.Pin, true, _ => { }, Dark, "Pin"),
        Builder.IconAction(IconKind.OpenInPage, () => { }, Dark, "Open in page")), "icontoggle_on.png");

    [Fact]
    public void Word() => CompareBaseline(Draw(Dark, 220f,
        Builder.IconToggle(IconKind.Pin, true, _ => { }, Dark, "Pin this page", word: "Pinned"),
        Builder.IconToggle(IconKind.Pin, false, _ => { }, Dark, "Pin this page", word: "Pin")), "icontoggle_word.png");

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
