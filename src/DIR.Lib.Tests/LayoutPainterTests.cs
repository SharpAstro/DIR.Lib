using System;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Tests the pixel painter on <see cref="PixelWidgetBase{TSurface}"/>: it arranges a <see cref="Layout.Node"/>
/// tree and binds each leaf's click region to its arranged rect, so draw-position and hit-region cannot drift.
/// Uses the CPU <see cref="RgbaImageRenderer"/> (no GPU, no font needed for region binding).
/// </summary>
public class LayoutPainterTests
{
    /// <summary>
    /// Records every <c>DrawText</c> the painter issues, with the font it chose, and answers MeasureText
    /// itself so no real font file is ever loaded.
    /// </summary>
    private sealed class RecordingRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public List<(string Text, string Font)> Runs { get; } = [];

        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
            => Runs.Add((text.ToString(), fontFamily));
    }

    private sealed class FontWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, dpiScale: 1f);
        }
    }

    private sealed class TestWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public ClickableRegion[] Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: string.Empty, dpiScale: 1f);
            return GetRegisteredRegions();
        }

        /// <summary>Renders WITHOUT a dpiScale argument, so the widget's <see cref="PixelWidgetBase{T}.DpiScale"/> applies.</summary>
        public ClickableRegion[] RenderDefaultDpi(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: string.Empty);
            return GetRegisteredRegions();
        }

        /// <summary>Renders WITHOUT a fontPath argument, so the widget's <see cref="PixelWidgetBase{T}.FontPath"/> applies.</summary>
        public void RenderDefaultFont(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds);
        }

        /// <summary>Renders with an explicit fontPath, which must override the widget's <see cref="PixelWidgetBase{T}.FontPath"/>.</summary>
        public void RenderWithFont(Layout.Node root, RectF32 bounds, string fontPath)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: fontPath);
        }

        /// <summary>Arranges without painting, so a test can compare geometry alone.</summary>
        public System.Collections.Immutable.ImmutableArray<Layout.ArrangedNode<float>> Arrange(Layout.Node root, RectF32 bounds)
            => ArrangeLayout(root, bounds, fontPath: string.Empty, dpiScale: 1f);

        public HitResult? DispatchAt(float x, float y) => HitTestAndDispatch(x, y);
    }

    /// <summary>Captures the font family each <see cref="DrawText"/> resolves to (draw is skipped, so no
    /// real font file is needed) -- proves the layout painter fed through the widget-owned FontPath.</summary>
    private sealed class FontSpyRenderer(uint width, uint height) : RgbaImageRenderer(width, height)
    {
        public string? LastTextFont { get; private set; }

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize, RGBAColor32 fontColor,
            in RectInt layout, TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
            => LastTextFont = fontFamily;
    }

    private static Layout.Node.Leaf HitRow(string action, float height, Action<InputModifier>? onClick = null) =>
        new(new Layout.Content.Box(0, 0))
        {
            Hit = new HitResult.ButtonHit(action),
            OnClick = onClick,
            Height = Layout.Sizing.Fixed(height),
            Width = Layout.Sizing.Star(),
        };

    [Fact]
    public void PaintLayout_BindsClickRegionsToArrangedRects()
    {
        using var renderer = new RgbaImageRenderer(100, 100);
        var widget = new TestWidget(renderer);

        var a = HitRow("A", 10);
        var b = HitRow("B", 10);
        var stack = new Layout.Node.Stack([a, b]);

        var regions = widget.Render(stack, new RectF32(0, 0, 100, 100));

        regions.Length.ShouldBe(2);

        var ra = regions.First(r => r.Result is HitResult.ButtonHit { Action: "A" });
        ra.X.ShouldBe(0f);
        ra.Y.ShouldBe(0f);
        ra.Width.ShouldBe(100f);   // Star cross stretches to full width
        ra.Height.ShouldBe(10f);

        var rb = regions.First(r => r.Result is HitResult.ButtonHit { Action: "B" });
        rb.Y.ShouldBe(10f);        // second row directly below the first
        rb.Height.ShouldBe(10f);
    }

    [Fact]
    public void PaintLayout_OnClick_DispatchesInsideArrangedRect()
    {
        using var renderer = new RgbaImageRenderer(100, 100);
        var widget = new TestWidget(renderer);

        var clicks = 0;
        var leaf = HitRow("X", 20, _ => clicks++);
        var stack = new Layout.Node.Stack([leaf]);

        widget.Render(stack, new RectF32(0, 0, 100, 100));

        var hit = widget.DispatchAt(50, 10); // inside the 0..100 x 0..20 row
        hit.ShouldBeOfType<HitResult.ButtonHit>().Action.ShouldBe("X");
        clicks.ShouldBe(1);

        widget.DispatchAt(50, 50); // below the row -> no hit
        clicks.ShouldBe(1);
    }

    [Fact]
    public void RenderLayout_OmittedDpiScale_UsesWidgetDpiScaleProperty()
    {
        using var renderer = new RgbaImageRenderer(200, 200);
        var widget = new TestWidget(renderer) { DpiScale = 2f };

        // A design-unit Fixed(10) row must arrange to 20 device px when the widget-owned
        // DpiScale (2) applies -- no dpiScale argument at the call site.
        var row = HitRow("A", 10);
        var regions = widget.RenderDefaultDpi(new Layout.Node.Stack([row]), new RectF32(0, 0, 200, 200));

        var ra = regions.First(r => r.Result is HitResult.ButtonHit { Action: "A" });
        ra.Height.ShouldBe(20f);
    }

    [Fact]
    public void RenderLayout_ExplicitDpiScale_OverridesWidgetProperty()
    {
        using var renderer = new RgbaImageRenderer(200, 200);
        var widget = new TestWidget(renderer) { DpiScale = 2f };

        // The device-px escape hatch: an explicit dpiScale: 1f wins over the property, so a tree
        // holding already-scaled pixel sizes is not scaled twice.
        var row = HitRow("A", 10);
        var regions = widget.Render(new Layout.Node.Stack([row]), new RectF32(0, 0, 200, 200));

        var ra = regions.First(r => r.Result is HitResult.ButtonHit { Action: "A" });
        ra.Height.ShouldBe(10f);
    }

    [Fact]
    public void RenderLayout_OmittedFontPath_UsesWidgetFontPathProperty()
    {
        using var renderer = new FontSpyRenderer(200, 200);
        var widget = new TestWidget(renderer) { FontPath = "widget-font" };

        // A Text leaf with no fontPath argument must paint through the widget-owned FontPath -- the
        // font analogue of the DpiScale-property test above.
        var text = Layout.Builder.Text("Hi").RowH(10);
        widget.RenderDefaultFont(new Layout.Node.Stack([text]), new RectF32(0, 0, 200, 200));

        renderer.LastTextFont.ShouldBe("widget-font");
    }

    [Fact]
    public void RenderLayout_ExplicitFontPath_OverridesWidgetProperty()
    {
        using var renderer = new FontSpyRenderer(200, 200);
        var widget = new TestWidget(renderer) { FontPath = "widget-font" };

        // An explicit fontPath wins over the property (the override escape hatch, e.g. an emoji run).
        widget.RenderWithFont(new Layout.Node.Stack([Layout.Builder.Text("Hi").RowH(10)]), new RectF32(0, 0, 200, 200), "call-font");

        renderer.LastTextFont.ShouldBe("call-font");
    }

    [Fact]
    public void PaintLayout_NonClickableLeaves_RegisterNoRegions()
    {
        using var renderer = new RgbaImageRenderer(100, 100);
        var widget = new TestWidget(renderer);

        // A panel background + a plain box, neither carrying a Hit.
        var stack = new Layout.Node.Stack([new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Height = Layout.Sizing.Fixed(10), Width = Layout.Sizing.Star() }])
        {
            Background = new RGBAColor32(0x10, 0x10, 0x18, 0xff),
        };

        var regions = widget.Render(stack, new RectF32(0, 0, 100, 100));

        regions.Length.ShouldBe(0);
    }

    // ---- Node.Radius ----

    private static readonly RGBAColor32 Backdrop = new RGBAColor32(0, 0, 0, 255);
    private static readonly RGBAColor32 Fill = new RGBAColor32(255, 255, 255, 255);

    private static RGBAColor32 PixelAt(RgbaImage image, int x, int y)
    {
        var at = (y * image.Width + x) * 4;
        return new RGBAColor32(image.Pixels[at], image.Pixels[at + 1], image.Pixels[at + 2], image.Pixels[at + 3]);
    }

    /// <summary>
    /// The load-bearing invariant: Radius is chrome, so arrange must not see it. A rounded panel has to
    /// occupy and inset exactly the rect a square one would, or every layout downstream of it shifts the
    /// moment someone rounds a corner.
    /// </summary>
    [Fact]
    public void Radius_DoesNotChangeArrangement()
    {
        Layout.Node Tree(float radius) => Layout.Builder.VStack(
            Layout.Builder.Text("header", 12f).RowH(20),
            Layout.Builder.Box(0, 0).Stretch().Bg(Fill).Radius(radius),
            Layout.Builder.Text("footer", 12f).RowH(20)).Pad(4).Bg(Backdrop).Radius(radius);

        using var renderer = new RgbaImageRenderer(120, 120);
        var widget = new TestWidget(renderer);

        var square = widget.Arrange(Tree(0f), new RectF32(0, 0, 120, 120));
        var rounded = widget.Arrange(Tree(8f), new RectF32(0, 0, 120, 120));

        rounded.Length.ShouldBe(square.Length);
        for (var i = 0; i < square.Length; i++)
        {
            rounded[i].Bounds.ShouldBe(square[i].Bounds, $"node {i} moved when only its corner radius changed");
        }
    }

    [Fact]
    public void Radius_CutsTheCornersOfABackground()
    {
        using var renderer = new RgbaImageRenderer(40, 40);
        renderer.Surface.Clear(Backdrop);
        var widget = new TestWidget(renderer);

        widget.Render(Layout.Builder.Box(0, 0).Stretch().Bg(Fill).Radius(10f), new RectF32(0, 0, 40, 40));

        PixelAt(renderer.Surface, 0, 0).ShouldBe(Backdrop, "the corner is outside the arc");
        PixelAt(renderer.Surface, 39, 39).ShouldBe(Backdrop);
        PixelAt(renderer.Surface, 20, 20).ShouldBe(Fill, "the middle is filled");
        PixelAt(renderer.Surface, 20, 0).ShouldBe(Fill, "the edge between the arcs is straight");
    }

    /// <summary>
    /// A zero radius has to take the plain <c>FillRectangle</c> path, so every existing tree paints exactly
    /// as it did before this feature existed.
    /// </summary>
    [Fact]
    public void Radius_Zero_PaintsExactlyTheSquarePath()
    {
        using var roundedRenderer = new RgbaImageRenderer(40, 40);
        using var squareRenderer = new RgbaImageRenderer(40, 40);
        roundedRenderer.Surface.Clear(Backdrop);
        squareRenderer.Surface.Clear(Backdrop);

        new TestWidget(roundedRenderer).Render(
            Layout.Builder.Box(0, 0).Stretch().Bg(Fill).Radius(0f), new RectF32(0, 0, 40, 40));
        new TestWidget(squareRenderer).Render(
            Layout.Builder.Box(0, 0).Stretch().Bg(Fill), new RectF32(0, 0, 40, 40));

        roundedRenderer.Surface.Pixels.ShouldBe(squareRenderer.Surface.Pixels);
    }

    /// <summary>
    /// Radius is a design unit like every other chrome measure, so the same tree must round harder on a
    /// HiDPI surface. Measured as the run of backdrop pixels along the top edge, which is the arc's bite.
    /// </summary>
    [Fact]
    public void Radius_IsADesignUnit_SoItScalesWithDpi()
    {
        static int TopEdgeCut(float dpiScale)
        {
            using var renderer = new RgbaImageRenderer(80, 80);
            renderer.Surface.Clear(Backdrop);
            var widget = new TestWidget(renderer) { DpiScale = dpiScale };
            widget.RenderDefaultDpi(Layout.Builder.Box(0, 0).Stretch().Bg(Fill).Radius(8f), new RectF32(0, 0, 80, 80));

            var cut = 0;
            while (cut < 80 && PixelAt(renderer.Surface, cut, 0) == Backdrop)
            {
                cut++;
            }
            return cut;
        }

        var at1x = TopEdgeCut(1f);
        var at2x = TopEdgeCut(2f);

        at1x.ShouldBeGreaterThan(0, "a radius of 8 must bite into the top edge at all");
        at2x.ShouldBeGreaterThan(at1x, "the same design-unit radius must round harder at 2x");
    }

    /// <summary>A Box leaf paints its own fill, so it has to honour the radius too -- otherwise
    /// <c>Box(...).Radius(n)</c> silently does nothing while <c>Box(...).Bg(c).Radius(n)</c> works.</summary>
    [Fact]
    public void Radius_AppliesToABoxLeafsOwnFill()
    {
        using var renderer = new RgbaImageRenderer(40, 40);
        renderer.Surface.Clear(Backdrop);
        var widget = new TestWidget(renderer);

        var box = new Layout.Node.Leaf(new Layout.Content.Box(0, 0) { Color = Fill })
        {
            Width = Layout.Sizing.Star(),
            Height = Layout.Sizing.Star(),
            CornerRadius = 10f,
        };
        widget.Render(box, new RectF32(0, 0, 40, 40));

        PixelAt(renderer.Surface, 0, 0).ShouldBe(Backdrop, "the Box leaf's own fill is rounded too");
        PixelAt(renderer.Surface, 20, 20).ShouldBe(Fill);
    }

    // --- emoji font fallback ---

    [Fact]
    public void MixedTextAndEmoji_SplitsIntoRunsWithTheRightFontEach()
    {
        // A run is drawn with exactly ONE font, so without a split the socket renders as blank space in a
        // text font. This is what lets a glyph and its label live in the same string.
        var renderer = new RecordingRenderer(400, 100);
        var widget = new FontWidget(renderer) { FontPath = "text.ttf", EmojiFontPath = "emoji.ttf" };

        widget.Render(Layout.Builder.Text("🔌 4 of 6", 12f, new RGBAColor32(0xff, 0xff, 0xff, 0xff)),
            new RectF32(0, 0, 400, 100));

        renderer.Runs.Count.ShouldBe(2);
        renderer.Runs[0].Font.ShouldBe("emoji.ttf");
        renderer.Runs[0].Text.ShouldBe("🔌");
        renderer.Runs[1].Font.ShouldBe("text.ttf");
        renderer.Runs[1].Text.ShouldBe(" 4 of 6");
    }

    [Fact]
    public void PlainText_StaysASingleDrawWithTheTextFont()
    {
        // The untouched path: no surrogate, no split, no extra measuring.
        var renderer = new RecordingRenderer(400, 100);
        var widget = new FontWidget(renderer) { FontPath = "text.ttf", EmojiFontPath = "emoji.ttf" };

        widget.Render(Layout.Builder.Text("6 of 6", 12f, new RGBAColor32(0xff, 0xff, 0xff, 0xff)),
            new RectF32(0, 0, 400, 100));

        renderer.Runs.ShouldHaveSingleItem().Font.ShouldBe("text.ttf");
    }

    [Fact]
    public void WithNoEmojiFont_TextIsDrawnAsOneRunEvenIfItHoldsAnEmoji()
    {
        // No fallback configured means no split: one draw with whatever font there is, exactly as before.
        var renderer = new RecordingRenderer(400, 100);
        var widget = new FontWidget(renderer) { FontPath = "text.ttf" };

        widget.Render(Layout.Builder.Text("🔌 4 of 6", 12f, new RGBAColor32(0xff, 0xff, 0xff, 0xff)),
            new RectF32(0, 0, 400, 100));

        renderer.Runs.ShouldHaveSingleItem().Font.ShouldBe("text.ttf");
    }

    [Fact]
    public void AVariationSelectorStaysWithTheGlyphItModifies()
    {
        // VS16 attaches to the emoji before it; splitting there would strand it in the text run and could
        // change how the sequence renders.
        var renderer = new RecordingRenderer(400, 100);
        var widget = new FontWidget(renderer) { FontPath = "text.ttf", EmojiFontPath = "emoji.ttf" };

        widget.Render(Layout.Builder.Text("🔌️ ok", 12f, new RGBAColor32(0xff, 0xff, 0xff, 0xff)),
            new RectF32(0, 0, 400, 100));

        renderer.Runs.Count.ShouldBe(2);
        renderer.Runs[0].Font.ShouldBe("emoji.ttf");
        renderer.Runs[0].Text.ShouldBe("🔌️");
        renderer.Runs[1].Text.ShouldBe(" ok");
    }

    // ---- Coverage-driven fallback in the declarative painter -------------------------------
    //
    // Real fixture files, because the split is decided by reading each candidate's cmap. The
    // renderer is still the recording stub, so nothing is rasterized.

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fonts", name);

    /// <summary>
    /// The gap this closes: a text leaf was drawn whole in one font, so a symbol the primary lacked
    /// came out as .notdef with no way for the caller to intervene short of an escape-hatch leaf.
    /// </summary>
    [Fact]
    public void PaintLayout_SplitsATextLeafAcrossCoveringFonts()
    {
        var dejavu = Fixture("DejaVuSans.ttf");
        var emoji = Fixture("NotoColorEmoji.ttf");
        var renderer = new RecordingRenderer(400, 100);
        var widget = new FontWidget(renderer)
        {
            FontPath = dejavu,
            FontFallback = new FontFallbackResolver(dejavu, [emoji]),
        };

        // The rocket is the only part DejaVu can't draw.
        widget.Render(Layout.Builder.Text("hi\U0001F680!", 12f), new RectF32(0, 0, 400, 100));

        renderer.Runs.Select(r => r.Text).ShouldBe(["hi", "\U0001F680", "!"]);
        renderer.Runs[0].Font.ShouldBe(dejavu);
        renderer.Runs[1].Font.ShouldBe(emoji);
        renderer.Runs[2].Font.ShouldBe(dejavu);
    }

    /// <summary>Text the primary covers is still one draw — the split must not cost anything.</summary>
    [Fact]
    public void PaintLayout_CoveredText_StaysASingleRun()
    {
        var dejavu = Fixture("DejaVuSans.ttf");
        var renderer = new RecordingRenderer(400, 100);
        var widget = new FontWidget(renderer)
        {
            FontPath = dejavu,
            FontFallback = new FontFallbackResolver(dejavu, [Fixture("NotoColorEmoji.ttf")]),
        };

        widget.Render(Layout.Builder.Text("hello", 12f), new RectF32(0, 0, 400, 100));

        renderer.Runs.ShouldHaveSingleItem().Text.ShouldBe("hello");
    }

    /// <summary>Without a resolver the painter behaves exactly as it always did.</summary>
    [Fact]
    public void PaintLayout_WithoutAFallback_DrawsTheWholeLeafInTheOneFont()
    {
        var dejavu = Fixture("DejaVuSans.ttf");
        var renderer = new RecordingRenderer(400, 100);
        var widget = new FontWidget(renderer) { FontPath = dejavu };

        widget.Render(Layout.Builder.Text("hi\U0001F680!", 12f), new RectF32(0, 0, 400, 100));

        var run = renderer.Runs.ShouldHaveSingleItem();
        run.Text.ShouldBe("hi\U0001F680!");
        run.Font.ShouldBe(dejavu);
    }

    /// <summary>
    /// Measure has to split on the same boundaries as paint, or the arranged rect won't fit what
    /// lands in it. Measured with a renderer whose widths differ per font, so a measure that ignored
    /// the fallback would produce a visibly different number.
    /// </summary>
    [Fact]
    public void MeasureText_SplitsOnTheSameCoverageRunsAsThePainter()
    {
        var dejavu = Fixture("DejaVuSans.ttf");
        var emoji = Fixture("NotoColorEmoji.ttf");
        var renderer = new PerFontWidthRenderer(400, 100, wideFont: emoji);
        const string Text = "hi\U0001F680!"; // 5 UTF-16 units; the rocket is 2 of them

        var plain = new PixelMeasureContext<RgbaImage>(renderer, dejavu, 1f);
        var withFallback = new PixelMeasureContext<RgbaImage>(renderer, dejavu, 1f)
        {
            Fallback = new FontFallbackResolver(dejavu, [emoji]),
        };

        // Measured whole in the primary: 5 units x 6px. Measured per run, the rocket's 2 units come
        // from the emoji face at 10px: 2*6 + 2*10 + 1*6 = 38.
        plain.MeasureText(Text, 12f).Width.ShouldBe(30f);
        withFallback.MeasureText(Text, 12f).Width.ShouldBe(38f);
    }

    /// <summary>Widths depend on the font, so a measure that used the wrong one is visible.</summary>
    private sealed class PerFontWidthRenderer(uint w, uint h, string wideFont) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * (fontFamily == wideFont ? 10f : 6f), fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        {
        }
    }
}

