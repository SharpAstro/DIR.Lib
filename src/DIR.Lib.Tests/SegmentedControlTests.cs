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
/// <see cref="Builder.Segmented{T}"/>: icon and word per segment, every word dropping at once when the control does not
/// fit (measured, never a host constant), Left/Right and press choosing, disabled segments, and the contrast of its
/// ink in a dark, a light and a one-hue palette. Pixel baselines segmented_wide.png and segmented_narrow.png.
/// </summary>
public class SegmentedControlTests
{
    private enum Mode { Single, Side, Overlay, Slider }

    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static UiTheme ThemeOf(UiPalette p) => new() { Palette = p, Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f) };

    private static readonly UiTheme Dark = ThemeOf(NotificationsRenderTests.Dark);

    private static ButtonGroupOption<Mode>[] Options(string? disabledReason = null) =>
    [
        new(Mode.Single, "Single", IconKind.OpenInPage),
        new(Mode.Side, "Side by side", IconKind.CompareSideBySide),
        new(Mode.Overlay, "Overlay", IconKind.CompareOverlay) { DisabledReason = disabledReason },
        new(Mode.Slider, "Slider with a long word", IconKind.CompareSlider),
    ];

    private static Node Build(UiTheme theme, Mode selected, Action<Mode>? onSelect, ListCursor? cursor = null,
        ButtonGroupOption<Mode>[]? options = null)
        => Builder.Segmented<Mode>(options ?? Options(), selected, onSelect, theme, cursor ?? new ListCursor(), "mode");

    // ---- measured switch ----

    private sealed class FloatCtx : IMeasureContext<float>
    {
        public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 8f, 14f);
        public float ToSurface(float designUnits) => designUnits;
        public float ToSurfaceX(float designUnits) => designUnits;
        public float ToSurfaceY(float designUnits) => designUnits;
    }

    private static List<ArrangedNode<float>> Arrange(Node root, float width)
        => [.. Engine.Arrange(root, new Rect<float>(0, 0, width, 40f), new FloatCtx())];

    private static int Words(List<ArrangedNode<float>> a)
        => a.Count(n => n.Node is Node.Leaf { Content: Content.Text });

    [Fact]
    public void Every_word_drops_together_at_the_measured_width_and_each_segment_keeps_its_tooltip()
    {
        var tree = Build(Dark, Mode.Single, _ => { });
        var full = Engine.Measure(tree, new Size<float>(100_000f, 40f), new FloatCtx()).Width;

        var wide = Arrange(tree, full);
        Words(wide).ShouldBe(4);

        var narrow = Arrange(tree, full - 1f);
        Words(narrow).ShouldBe(0);
        var tips = narrow.Where(n => n.Node.Hit is HitResult.ListItemHit).Select(n => n.Node.Tooltip).ToArray();
        tips.Length.ShouldBe(4);
        tips[0].ShouldBe("Single");
        tips[1].ShouldBe("Side by side");
        tips[3].ShouldBe("Slider with a long word");
    }

    [Fact]
    public void The_narrow_form_is_narrower_than_the_full_one()
    {
        var tree = Build(Dark, Mode.Single, _ => { });
        var full = Engine.Measure(tree, new Size<float>(100_000f, 40f), new FloatCtx()).Width;
        var narrow = Engine.Measure(tree, new Size<float>(full - 1f, 40f), new FloatCtx()).Width;
        narrow.ShouldBeLessThan(full);
    }

    [Fact]
    public void The_control_is_a_button_tall_with_a_declared_horizontal_list()
    {
        var tree = Build(Dark, Mode.Single, _ => { });
        Engine.Measure(tree, new Size<float>(100_000f, 100f), new FloatCtx()).Height.ShouldBe(28f);
        tree.DeclaredList.ShouldNotBeNull().Horizontal.ShouldBeTrue();
        tree.NarrowForm!.DeclaredList.ShouldNotBeNull().Horizontal.ShouldBeTrue();
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
        public Mode Selected { get; private set; } = Mode.Single;
        public List<Mode> Chosen { get; } = [];
        public ListCursor Cursor { get; } = new();
        public Widget Widget { get; }
        public InputRouter Router { get; }
        private readonly float _width;
        private readonly ButtonGroupOption<Mode>[] _options;

        public Rig(float width = 900f, ButtonGroupOption<Mode>[]? options = null)
        {
            _width = width;
            _options = options ?? Options();
            var renderer = new RgbaImageRenderer((uint)width, 60u);
            Widget = new Widget(renderer) { FontPath = FontPath };
            Router = new InputRouter(Widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [Widget] };
            Paint();
        }

        public void Paint()
        {
            Widget.Tree = Builder.Segmented<Mode>(_options, Selected, m => { Chosen.Add(m); Selected = m; }, Dark, Cursor, "mode");
            Widget.Render(_width, 60f);
            Router.AfterPaint();
        }

        public ClickableRegion Region(int index)
        {
            var regions = new List<ClickableRegion>();
            Widget.CollectPaintedRegions(regions);
            return regions.First(r => r.Result is HitResult.ListItemHit h && h.Index == index);
        }

        public void Click(int index)
        {
            var r = Region(index);
            var x = r.X + r.Width / 2f;
            var y = r.Y + r.Height / 2f;
            Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
            Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left, InputModifier.None));
            Paint();
        }

        public bool Key(InputKey key)
        {
            var handled = Router.Handle(new InputEvent.KeyDown(key, InputModifier.None));
            Paint();
            return handled;
        }
    }

    [Fact]
    public void A_press_on_another_segment_chooses_it_and_a_press_on_the_chosen_one_does_not()
    {
        var rig = new Rig();
        rig.Click(1);
        rig.Chosen.ShouldBe([Mode.Side]);
        rig.Click(1);
        rig.Chosen.ShouldBe([Mode.Side]);
    }

    [Fact]
    public void Right_and_Left_move_the_choice_and_skip_the_disabled_segment()
    {
        var rig = new Rig(options: Options("Needs two images"));
        rig.Key(InputKey.Right).ShouldBeTrue();
        rig.Selected.ShouldBe(Mode.Side);
        rig.Key(InputKey.Right).ShouldBeTrue();
        rig.Selected.ShouldBe(Mode.Slider); // Overlay (2) is disabled and stepped over
        rig.Key(InputKey.Right).ShouldBeFalse(); // the end reads as an end
        rig.Selected.ShouldBe(Mode.Slider);
        rig.Key(InputKey.Left).ShouldBeTrue();
        rig.Selected.ShouldBe(Mode.Side);
        rig.Chosen.ShouldBe([Mode.Side, Mode.Slider, Mode.Side]);
    }

    [Fact]
    public void Up_and_Down_are_not_this_lists_keys()
    {
        var rig = new Rig();
        rig.Key(InputKey.Down).ShouldBeFalse();
        rig.Chosen.ShouldBeEmpty();
    }

    [Fact]
    public void A_disabled_segment_takes_no_press_and_shows_its_reason()
    {
        var rig = new Rig(options: Options("Needs two images"));
        var region = rig.Region(2);
        region.IsDisabled.ShouldBeTrue();
        region.Tooltip.ShouldBe("Needs two images");
        rig.Click(2);
        rig.Chosen.ShouldBeEmpty();
    }

    [Fact]
    public void A_segment_shortcut_chooses_it()
    {
        ButtonGroupOption<Mode>[] options =
        [
            new(Mode.Single, "Single", IconKind.OpenInPage),
            new(Mode.Side, "Side", IconKind.CompareSideBySide) { Shortcut = new KeyChord(InputKey.D2) },
        ];
        var rig = new Rig(options: options);
        rig.Key(InputKey.D2).ShouldBeTrue();
        rig.Selected.ShouldBe(Mode.Side);
    }

    [Fact]
    public void A_null_onSelect_is_a_display_that_registers_nothing()
    {
        var rig = new Rig();
        rig.Widget.Tree = Build(Dark, Mode.Side, null);
        rig.Widget.Render(900f, 60f);
        var regions = new List<ClickableRegion>();
        rig.Widget.CollectPaintedRegions(regions);
        regions.Count(r => r.Result is HitResult.ListItemHit).ShouldBe(0);
    }

    // ---- own hit ----

    [Fact]
    public void A_selected_option_with_its_own_hit_leaves_the_cursor_closed()
    {
        var own = new HitResult.ButtonHit("mode-side");
        var options = Options();
        options[1] = new ButtonGroupOption<Mode>(Mode.Side, "Side by side", IconKind.CompareSideBySide) { Hit = own };

        var cursor = new ListCursor();
        var tree = Build(Dark, Mode.Side, _ => { }, cursor, options);
        cursor.IsOpen.ShouldBeFalse();
        var arranged = Arrange(tree, 900f);
        arranged.Any(n => Equals(n.Node.Hit, own)).ShouldBeTrue();
        arranged.Any(n => n.Node.Hit is HitResult.ListItemHit { Index: 1 }).ShouldBeFalse();

        var plain = new ListCursor();
        var plainTree = Build(Dark, Mode.Side, _ => { }, plain);
        plain.IsOpen.ShouldBeTrue();
        plain.ListId.ShouldBe("mode");
        plain.Index.ShouldBe(1);
        Arrange(plainTree, 900f).Any(n => n.Node.Hit is HitResult.ListItemHit { Index: 1 }).ShouldBeTrue();
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
    public void The_chosen_ink_reads_on_the_accent_and_icons_read_on_their_ground(string name)
    {
        var p = PaletteNamed(name);
        RGBAColor32.ContrastRatio(p.InkOn(p.Accent), p.Accent).ShouldBeGreaterThanOrEqualTo(4.5f);
        // The unchosen icon sits on the container's RaisedBg, and on the hover fill under the pointer.
        RGBAColor32.ContrastRatio(p.BodyText, p.RaisedBg).ShouldBeGreaterThanOrEqualTo(3f);
        RGBAColor32.ContrastRatio(p.BodyText, p.Selection).ShouldBeGreaterThanOrEqualTo(3f);
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void The_painted_chosen_segment_uses_the_ink_the_palette_names(string name)
    {
        var p = PaletteNamed(name);
        var arranged = Arrange(Build(ThemeOf(p), Mode.Side, _ => { }), 2000f);
        var chosen = arranged.First(n => n.Node.Hit is HitResult.ListItemHit { Index: 1 });
        chosen.Node.Background.ShouldBe(p.Accent);
        var ink = arranged.Where(n => n.Node is Node.Leaf { Content: Content.Text } && n.Bounds.X >= chosen.Bounds.X
            && n.Bounds.X < chosen.Bounds.X + chosen.Bounds.Width).Select(n => ((Content.Text)((Node.Leaf)n.Node).Content).Color).ToArray();
        ink.Length.ShouldBe(1);
        ink[0].ShouldBe(p.InkOn(p.Accent));
    }

    // ---- baselines ----

    private static RgbaImage Draw(UiTheme theme, float width)
    {
        var renderer = new RgbaImageRenderer((uint)width, 56u);
        renderer.Surface.Clear(theme.Palette.ContentBg);
        var widget = new Widget(renderer) { FontPath = FontPath };
        widget.Tree = Builder.VStack(
            Builder.Spacer().HFixed(12f),
            Builder.HStack(Builder.Spacer().WFixed(12f), Build(theme, Mode.Side, _ => { }), Builder.Spacer().WFixed(12f)).HFixed(theme.Metrics.ButtonHeight)
        ).Stretch();
        widget.Render(width, 56f);
        return renderer.Surface;
    }

    [Fact] public void Wide() => CompareBaseline(Draw(Dark, 640f), "segmented_wide.png");

    [Fact] public void Narrow() => CompareBaseline(Draw(Dark, 232f), "segmented_narrow.png");

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
