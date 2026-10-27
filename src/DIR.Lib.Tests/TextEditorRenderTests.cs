using System.IO;
using SharpAstro.Png;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="TextEditorRenderer"/> and <see cref="Layout.Content.TextEditor"/> painted through
/// <see cref="RgbaImageRenderer"/> with a real face: the picture is the test, because a property being set says
/// nothing about whether a wrapped line, a selection across lines or a scroll thumb LOOKS right.
/// <para>
/// Three states at two widths in two palettes, with a Markdown idea body that carries a long path (the case that
/// has to break at a separator, not run off the box). Regenerate with <c>DIR_LIB_UPDATE_BASELINES=1</c> after
/// looking at what changed; the contrast tests below pin the palettes the captures are taken in.
/// </para>
/// </summary>
public class TextEditorRenderTests
{
    private static readonly string TestFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";

    private const string IdeaBody =
        "## Sidecar for the flight logs\n\n"
        + "Keep the **exposure log** beside the session in "
        + @"C:\Users\seb\source\repos\sharpastro\tianwen\src\TianWen.Lib\Sequencing\ExposureLog.cs"
        + " so the notes pane can link to it.\n\n"
        + "- wire the path chip\n- dark-mode contrast pass\n- ask about https://example.org/notes/2026/flight-logs/sidecar-format";

    private sealed record Look(string Name, UiPalette Palette);

    private static readonly Look Dark = new("dark", new UiPalette
    {
        ContentBg = new(24, 26, 32, 255),
        PanelBg = new(36, 39, 48, 255),
        HeaderBg = new(44, 48, 58, 255),
        Separator = new(116, 122, 142, 255),
        BodyText = new(226, 228, 234, 255),
        DimText = new(160, 166, 182, 255),
        Accent = new(110, 170, 255, 255),
        Selection = new(60, 100, 170, 200),
        Info = new(110, 170, 255, 255),
        Warn = new(240, 190, 80, 255),
        Error = new(255, 120, 110, 255),
    });

    private static readonly Look Light = new("light", new UiPalette
    {
        ContentBg = new(255, 255, 255, 255),
        PanelBg = new(238, 240, 245, 255),
        HeaderBg = new(228, 231, 238, 255),
        Separator = new(128, 134, 150, 255),
        BodyText = new(28, 30, 36, 255),
        DimText = new(92, 98, 114, 255),
        Accent = new(0, 84, 190, 255),
        Selection = new(0, 84, 190, 70),
        Info = new(0, 84, 190, 255),
        Warn = new(150, 90, 0, 255),
        Error = new(180, 30, 30, 255),
    });

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public Layout.Size<float> Measure(Layout.Node root, float width)
            => MeasureLayout(root, new Layout.Size<float>(width, 10_000f), TestFont, DesignScale.One);

        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: TestFont, scale: DesignScale.One);
        }

        public void Activate(TextInputState state) => Ui.Focus.Focus(state);
    }

    /// <summary>The editor on a panel of its palette, measured by the engine at <paramref name="width"/> and painted there.</summary>
    private static RgbaImage Capture(Look look, int width, TextEditorState state, bool focused, int minLines, int maxLines)
    {
        const int margin = 8;
        var colors = TextInputColors.FromPalette(look.Palette);
        var probe = new Widget(new RgbaImageRenderer(1, 1));
        var tree = Layout.Builder.TextEditor(state, 14f, minLines, maxLines, colors).Stretch();
        var size = probe.Measure(tree, width - margin * 2);

        var height = (int)MathF.Ceiling(size.Height) + margin * 2;
        var renderer = new RgbaImageRenderer((uint)width, (uint)height);
        var widget = new Widget(renderer);
        renderer.FillRectangle(new RectInt(new PointInt(width, height), new PointInt(0, 0)), look.Palette.PanelBg);
        if (focused)
        {
            widget.Activate(state);
        }

        var bounds = new RectF32(margin, margin, width - margin * 2, MathF.Ceiling(size.Height));
        widget.Render(tree, bounds);
        // The second frame is the first one's layout applied: the state knows its wrap after the first paint.
        renderer.FillRectangle(new RectInt(new PointInt(width, height), new PointInt(0, 0)), look.Palette.PanelBg);
        widget.Render(tree, bounds);
        return renderer.Surface;
    }

    private static TextEditorState Empty() => new() { Placeholder = "Capture an idea, a path or a link…" };

    private static TextEditorState Selected(int width)
    {
        var state = new TextEditorState { Text = IdeaBody };
        // From inside the first paragraph to inside the long path: the selection has to cross wrapped lines.
        var from = IdeaBody.IndexOf("exposure", StringComparison.Ordinal);
        var to = IdeaBody.IndexOf("Sequencing", StringComparison.Ordinal) + 4;
        state.MoveCaretTo(from);
        state.MoveCaretTo(to, extend: true);
        _ = width;
        return state;
    }

    private static TextEditorState Scrolled()
    {
        var state = new TextEditorState { Text = IdeaBody + "\n\n" + IdeaBody };
        state.MoveCaretTo(state.Text.Length);
        return state;
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var look in new[] { Light, Dark })
        {
            foreach (var width in new[] { 200, 600 })
            {
                yield return [look.Name, width];
            }
        }
    }

    private static Look LookNamed(string name) => name == "dark" ? Dark : Light;

    [Theory]
    [MemberData(nameof(Cases))]
    public void EmptyEditorShowsItsWrappedPlaceholder(string look, int width)
    {
        var img = Capture(LookNamed(look), width, Empty(), focused: false, minLines: 3, maxLines: 6);
        CompareBaseline(img, $"text_editor_empty_{look}_{width}.png");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void WrappedTextWithASelectionAcrossLines(string look, int width)
    {
        var img = Capture(LookNamed(look), width, Selected(width), focused: true, minLines: 3, maxLines: 40);
        CompareBaseline(img, $"text_editor_selection_{look}_{width}.png");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ABoxAtItsMaxHeightScrollsToTheCaret(string look, int width)
    {
        var state = Scrolled();
        var img = Capture(LookNamed(look), width, state, focused: true, minLines: 2, maxLines: 5);
        state.ScrollOffsetY.ShouldBeGreaterThan(0f, "the caret is at the end of a text taller than the box");
        CompareBaseline(img, $"text_editor_scrolled_{look}_{width}.png");
    }

    // ---- what the engine and the paint agree on --------------------------------------------------

    [Fact]
    public void TheBoxGrowsWithItsContentUpToTheMaximumAndThenStops()
    {
        var widget = new Widget(new RgbaImageRenderer(1, 1));
        float HeightOf(string text)
        {
            var state = new TextEditorState { Text = text };
            return widget.Measure(Layout.Builder.TextEditor(state, 14f, 2, 4).Stretch(), 200f).Height;
        }

        var one = HeightOf("a");
        var two = HeightOf("a\nb");
        var three = HeightOf("a\nb\nc");
        var four = HeightOf("a\nb\nc\nd");
        var ten = HeightOf("a\nb\nc\nd\ne\nf\ng\nh\ni\nj");

        two.ShouldBe(one, "a minimum of two lines is two lines tall however little is in it");
        three.ShouldBeGreaterThan(two);
        four.ShouldBeGreaterThan(three);
        ten.ShouldBe(four, "past the maximum the box stops growing and scrolls");
    }

    [Fact]
    public void WrappedTextMakesTheBoxTallerAtANarrowWidth()
    {
        var widget = new Widget(new RgbaImageRenderer(1, 1));
        var state = new TextEditorState { Text = IdeaBody };
        var tree = Layout.Builder.TextEditor(state, 14f, 1, 100).Stretch();

        widget.Measure(tree, 180f).Height.ShouldBeGreaterThan(widget.Measure(tree, 600f).Height);
    }

    [Fact]
    public void FocusingAnEmptyEditorDoesNotMoveItsBottomEdge()
    {
        var widget = new Widget(new RgbaImageRenderer(1, 1));
        var state = Empty();
        var tree = Layout.Builder.TextEditor(state, 14f, 1, 6).Stretch();

        var before = widget.Measure(tree, 200f).Height;
        widget.Activate(state);
        widget.Measure(tree, 200f).Height.ShouldBe(before, "the placeholder is measured, so hiding it moves nothing");
    }

    [Fact]
    public void PaintingTheEditorHandsTheStateItsLayoutAndKeepsTheCaretInView()
    {
        var state = Scrolled();
        _ = Capture(Dark, 200, state, focused: true, minLines: 2, maxLines: 5);

        state.VisualLayout.ShouldNotBeNull();
        var layout = state.VisualLayout!;
        var caretTop = layout.YOf(layout.LineOf(state.CursorPos));
        caretTop.ShouldBeGreaterThanOrEqualTo(state.ScrollOffsetY);
        (caretTop + layout.LineHeight).ShouldBeLessThanOrEqualTo(state.ScrollOffsetY + state.ViewportHeight + 0.5f);
    }

    [Fact]
    public void TheRendererReportsTheCaretRectOfAFocusedEditorAndNothingForAnUnfocusedOne()
    {
        var renderer = new RgbaImageRenderer(300, 120);
        var state = new TextEditorState { Text = "hello\nworld" };
        state.MoveCaretTo(8);

        var idle = TextEditorRenderer.Render(renderer, state, 4, 4, 280, 80, TestFont, 14f, 18.2f);
        idle.ShouldBe(default);

        new TextInputFocus().Focus(state);
        var caret = TextEditorRenderer.Render(renderer, state, 4, 4, 280, 80, TestFont, 14f, 18.2f);
        caret.ShouldNotBe(default);
        caret.UpperLeft.Y.ShouldBeGreaterThan(4 + 18, "the caret is on the second line");
    }

    // ---- contrast of the palettes the captures use ----------------------------------------------

    private static double Lum(RGBAColor32 c)
    {
        static double Lin(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(c.Red) + 0.7152 * Lin(c.Green) + 0.0722 * Lin(c.Blue);
    }

    private static double Contrast(RGBAColor32 a, RGBAColor32 b)
    {
        var (hi, lo) = (Math.Max(Lum(a), Lum(b)), Math.Min(Lum(a), Lum(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    private static RGBAColor32 Over(RGBAColor32 top, RGBAColor32 bottom)
    {
        var a = top.Alpha / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return new RGBAColor32(Mix(top.Red, bottom.Red), Mix(top.Green, bottom.Green), Mix(top.Blue, bottom.Blue), 255);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void ThePalettesTheCapturesAreTakenInMeetWcagAA(string name)
    {
        var colors = TextInputColors.FromPalette(LookNamed(name).Palette);

        Contrast(colors.Text, colors.Background).ShouldBeGreaterThanOrEqualTo(4.5, "body text on the idle box");
        Contrast(colors.Text, colors.BackgroundActive).ShouldBeGreaterThanOrEqualTo(4.5, "body text on the focused box");
        Contrast(colors.Placeholder, colors.Background).ShouldBeGreaterThanOrEqualTo(4.5, "the placeholder is text too");
        Contrast(colors.Text, Over(colors.Selection, colors.BackgroundActive)).ShouldBeGreaterThanOrEqualTo(4.5, "selected text on its highlight");
        Contrast(colors.Cursor, colors.BackgroundActive).ShouldBeGreaterThanOrEqualTo(3.0, "the caret");
        Contrast(colors.BorderActive, colors.BackgroundActive).ShouldBeGreaterThanOrEqualTo(3.0, "the focused edge");
        Contrast(colors.Border, LookNamed(name).Palette.PanelBg).ShouldBeGreaterThanOrEqualTo(3.0, "the idle edge against the panel it sits on");
        Contrast(colors.Placeholder, colors.Background).ShouldBeGreaterThanOrEqualTo(3.0, "the scroll thumb, drawn in the placeholder colour");
    }

    private static void CompareBaseline(RgbaImage img, string name)
    {
        var baselinePath = Path.Combine(BaselineDir, name);
        var sourceBaselinePath = Path.Combine(SourceBaselineDir, name);

        if (UpdateBaselines)
        {
            var pngBytes = PngWriter.Encode(img.Pixels, img.Width, img.Height);
            Directory.CreateDirectory(SourceBaselineDir);
            Directory.CreateDirectory(BaselineDir);
            File.WriteAllBytes(sourceBaselinePath, pngBytes);
            File.WriteAllBytes(baselinePath, pngBytes);
            return;
        }

        if (!File.Exists(baselinePath))
        {
            Assert.Fail($"Baseline '{name}' does not exist. Set DIR_LIB_UPDATE_BASELINES=1 once the render has been looked at.");
        }

        var baseline = PngReader.Decode(File.ReadAllBytes(baselinePath));
        baseline.Width.ShouldBe(img.Width, $"Width mismatch for '{name}'");
        baseline.Height.ShouldBe(img.Height, $"Height mismatch for '{name}'");

        var maxDiff = 0;
        for (var i = 0; i < baseline.Pixels.Length; i++)
        {
            maxDiff = Math.Max(maxDiff, Math.Abs(img.Pixels[i] - baseline.Pixels[i]));
        }

        if (maxDiff > 2)
        {
            var actualPath = Path.ChangeExtension(baselinePath, ".actual.png");
            PngWriter.Save(actualPath, img.Pixels, img.Width, img.Height);
            Assert.Fail($"Baseline mismatch for '{name}': max diff={maxDiff}. Actual saved to '{actualPath}'; look at it before regenerating.");
        }
    }
}
