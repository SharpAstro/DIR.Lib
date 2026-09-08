using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Content.TextInput"/> -- the leaf that makes a text box a declaration rather than a
/// keyed <see cref="Layout.Content.Fill"/> plus a painter dictionary entry.
/// <para>
/// What is worth pinning here is not that the field draws (the pixel renderer was already doing that from
/// every call site) but that <b>declaring it is sufficient</b>: the painter registers the hit, the cursor and
/// the tab-order entry itself, so none of them can be forgotten. Those three registrations ARE the WinForms
/// fidelity -- click-to-focus, the I-beam and Tab cycling all derive from them -- and every one of them was
/// previously a separate thing a call site had to remember.
/// </para>
/// </summary>
public class LayoutTextInputTests
{
    /// <summary>Answers MeasureText itself, so no font file is needed: one char is half the font size wide.</summary>
    private sealed class MetricsRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);
    }

    private sealed class TestWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public ClickableRegion[] Render(Layout.Node root, RectF32 bounds, float dpiScale = 1f, string fontPath = "font.ttf")
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: fontPath, dpiScale: dpiScale);
            return GetRegisteredRegions();
        }

        public System.Collections.Generic.List<TextInputState> TextInputsAfter(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "font.ttf", dpiScale: 1f);
            return GetRegisteredTextInputs();
        }

        public HitResult? DispatchAt(float x, float y) => HitTestAndDispatch(x, y);
    }

    private static TestWidget Widget() => new(new MetricsRenderer(400, 200));

    // ---- The three registrations that make declaration sufficient ----

    [Fact]
    public void PaintingAFieldRegistersItsHit_OverTheArrangedRect()
    {
        var state = new TextInputState();
        var widget = Widget();

        var regions = widget.Render(
            Layout.Builder.TextInput(state, 14f).Stretch(),
            new RectF32(10f, 20f, 120f, 30f));

        var region = regions.ShouldHaveSingleItem();
        region.Result.ShouldBeOfType<HitResult.TextInputHit>().Input.ShouldBeSameAs(state);
        region.X.ShouldBe(10f);
        region.Y.ShouldBe(20f);
        region.Width.ShouldBe(120f);
        region.Height.ShouldBe(30f);
    }

    /// <summary>
    /// The I-beam is part of being a field, not something the enclosing panel arranges for. Before the leaf,
    /// this came from <c>RenderTextInput</c> -- so it was present exactly where a call site remembered to
    /// call it and absent everywhere a field was drawn some other way.
    /// </summary>
    [Fact]
    public void AFieldStatesTheTextCursor()
    {
        var regions = Widget().Render(
            Layout.Builder.TextInput(new TextInputState(), 14f).Stretch(),
            new RectF32(0f, 0f, 100f, 24f));

        regions.ShouldHaveSingleItem().Cursor.ShouldBe(CursorKind.Text);
    }

    /// <summary>
    /// Tab order is derived from region paint order, so it is the visual order automatically -- but only
    /// because the field registers. This is the assertion that "declare it and Tab works" is true.
    /// </summary>
    [Fact]
    public void FieldsAreTabReachableInPaintOrder_WithNoPerFieldWiring()
    {
        TextInputState first = new(), second = new(), third = new();

        var inputs = Widget().TextInputsAfter(
            Layout.Builder.VStack(
                Layout.Builder.TextInput(first, 14f).RowH(20f),
                Layout.Builder.TextInput(second, 14f).RowH(20f),
                Layout.Builder.TextInput(third, 14f).RowH(20f)),
            new RectF32(0f, 0f, 200f, 60f));

        inputs.ShouldBe([first, second, third]);
    }

    /// <summary>
    /// A click on the field must focus the FIELD even when a row around it carries its own hit. The field
    /// registers during the leaf's content pass, after the enclosing node's hit, so it is on top by paint
    /// order -- the same rule that makes an inner button beat the card behind it.
    /// </summary>
    [Fact]
    public void AFieldInsideAClickableRow_WinsTheClick()
    {
        var state = new TextInputState();
        var widget = Widget();

        widget.Render(
            Layout.Builder.HStack(Layout.Builder.TextInput(state, 14f).Stretch())
                .Clickable(new HitResult.ButtonHit("row")),
            new RectF32(0f, 0f, 100f, 24f));

        widget.DispatchAt(50f, 12f).ShouldBeOfType<HitResult.TextInputHit>().Input.ShouldBeSameAs(state);
    }

    // ---- Measure ----

    /// <summary>
    /// A box that resizes while you type is a bug, so the intrinsic width comes from the placeholder (or an
    /// explicit sample) and never from the live text.
    /// </summary>
    [Fact]
    public void IntrinsicWidth_ComesFromThePlaceholder_NotTheLiveText()
    {
        var state = new TextInputState { Placeholder = "1234", Text = "a much longer typed value" };
        var ctx = new PixelMeasureContext<RgbaImage>(new MetricsRenderer(10, 10), "font.ttf", 1f);

        var size = Layout.Engine.Measure(
            Layout.Builder.TextInput(state, 10f),
            new Layout.Size<float>(1000f, 1000f), ctx);

        // 4 chars at half the 10-unit font, plus the renderer's inset on both sides.
        size.Width.ShouldBe(4f * 5f + TextInputRenderer.HorizontalPadding(10f) * 2f);
    }

    [Fact]
    public void AWidthSample_OverridesThePlaceholder()
    {
        var state = new TextInputState { Placeholder = "much longer placeholder" };
        var ctx = new PixelMeasureContext<RgbaImage>(new MetricsRenderer(10, 10), "font.ttf", 1f);

        var size = Layout.Engine.Measure(
            Layout.Builder.TextInput(state, 10f, widthSample: "00"),
            new Layout.Size<float>(1000f, 1000f), ctx);

        size.Width.ShouldBe(2f * 5f + TextInputRenderer.HorizontalPadding(10f) * 2f);
    }

    /// <summary>
    /// The inset is reserved rather than ignored, which is the point of
    /// <see cref="TextInputRenderer.HorizontalPadding"/> being stated once: an Auto-sized field must be wide
    /// enough for the sample to fit BETWEEN the insets, not under them.
    /// </summary>
    [Fact]
    public void IntrinsicWidth_ReservesTheRenderersOwnInset()
    {
        var ctx = new PixelMeasureContext<RgbaImage>(new MetricsRenderer(10, 10), "font.ttf", 1f);
        var sample = "abcd";

        var field = Layout.Engine.Measure(
            Layout.Builder.TextInput(new TextInputState(), 10f, widthSample: sample),
            new Layout.Size<float>(1000f, 1000f), ctx);
        var bareText = Layout.Engine.Measure(
            Layout.Builder.Text(sample, 10f),
            new Layout.Size<float>(1000f, 1000f), ctx);

        (field.Width - bareText.Width).ShouldBe(TextInputRenderer.HorizontalPadding(10f) * 2f);
    }

    // ---- DPI ----

    /// <summary>
    /// A field and the label beside it must be one size at any DPI, which they are only because the painter
    /// crosses the field's font size through the SAME context scale a text run crosses. It is asserted
    /// through the arranged rect rather than the drawn glyphs because the rect is what the engine and the
    /// painter have to agree about.
    /// </summary>
    [Fact]
    public void AFieldsIntrinsicSizeScalesWithDpi_LikeTheTextBesideIt()
    {
        var renderer = new MetricsRenderer(10, 10);
        var state = new TextInputState { Placeholder = "abcd" };

        var at1 = Layout.Engine.Measure(Layout.Builder.TextInput(state, 10f),
            new Layout.Size<float>(1000f, 1000f), new PixelMeasureContext<RgbaImage>(renderer, "font.ttf", 1f));
        var at2 = Layout.Engine.Measure(Layout.Builder.TextInput(state, 10f),
            new Layout.Size<float>(1000f, 1000f), new PixelMeasureContext<RgbaImage>(renderer, "font.ttf", 2f));

        at2.Width.ShouldBe(at1.Width * 2f);
        at2.Height.ShouldBe(at1.Height * 2f);
    }

    // ---- Per-frame collections ----

    /// <summary>
    /// Fields that appear as hardware does (one per camera, one per OTA) can never be statically declared
    /// controls, so a leaf in a per-frame tree has to make them an ordinary loop. This is the case that would
    /// rule out a design where a field is registered once at construction.
    /// </summary>
    [Fact]
    public void FieldsBuiltInALoop_EachRegisterTheirOwnState()
    {
        var states = Enumerable.Range(0, 4).Select(_ => new TextInputState()).ToArray();
        var rows = states.Select(s => Layout.Builder.TextInput(s, 14f).RowH(20f)).ToArray();

        var inputs = Widget().TextInputsAfter(Layout.Builder.VStack(rows), new RectF32(0f, 0f, 200f, 80f));

        inputs.ShouldBe(states);
    }
    // ---- The selection highlight sits UNDER the glyphs ----

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fonts", name);

    /// <summary>
    /// One field rendered with a real face, so glyph ink actually lands on the surface. The caret is
    /// suppressed by frame count (it blinks off on the second 30-frame half), because it is drawn in its
    /// own colour and would otherwise count as ink at whichever column the cursor happens to sit.
    /// </summary>
    private static RgbaImageRenderer RenderField(TextInputState state)
    {
        var renderer = new RgbaImageRenderer(220, 44);
        renderer.Surface.Clear(new RGBAColor32(0, 0, 0, 255));
        var widget = new TestWidget(renderer) { FrameCount = 30 };
        widget.Render(
            Layout.Builder.TextInput(state, 20f).Stretch(),
            new RectF32(0f, 0f, 220f, 44f),
            fontPath: Fixture("DejaVuSans.ttf"));
        return renderer;
    }

    private static double Lum(RgbaImageRenderer r, int x, int y)
    {
        var i = ((y * r.Surface.Width) + x) * 4;
        var p = r.Surface.Pixels;
        return (0.299 * p[i]) + (0.587 * p[i + 1]) + (0.114 * p[i + 2]);
    }

    /// <summary>
    /// Selected text has to stay readable, and that is a statement about paint ORDER. The highlight is a
    /// translucent fill (alpha 180 by default), so painted on TOP of the run it leaves a fixed 29% of the
    /// glyph's contrast: on screen, a coloured block with no text in it. Reported 2026-09-07 against the
    /// sky atlas F3 box, whose OpenSearch selects the whole query, so every open showed it.
    /// <para>
    /// Measured on this fixture with the default palette, as mean ink contrast against its own local
    /// background: 125.3 unselected, 106.5 with the highlight underneath (85% of it), 37.1 with the
    /// highlight over the top (29.6%, the alpha residual exactly, and no colour choice can raise it). The
    /// bound is 60%, which only the right order reaches; seen to fail at 37.1 with the fill moved back
    /// after the run.
    /// </para>
    /// </summary>
    [Fact]
    public void SelectedText_KeepsItsContrast_BecauseTheHighlightIsPaintedUnderTheGlyphs()
    {
        const string Text = "C30";

        var plainState = new TextInputState { Text = Text };
        plainState.Activate();

        var selectedState = new TextInputState { Text = Text };
        selectedState.Activate();
        selectedState.SelectAll();

        var plain = RenderField(plainState);
        var selected = RenderField(selectedState);

        // Ink is where the unselected field drew away from its own background, sampled to the right of
        // the text: inside the box, past the last glyph, and outside any selection.
        var plainBg = Lum(plain, 200, 22);
        var ink = new List<(int X, int Y)>();
        for (var y = 6; y < 38; y++)
        {
            for (var x = 6; x < 190; x++)
            {
                if (Math.Abs(Lum(plain, x, y) - plainBg) > 20d)
                {
                    ink.Add((x, y));
                }
            }
        }

        ink.Count.ShouldBeGreaterThan(40, "the fixture has to draw real glyphs for this to measure anything");

        // The selected render's local background is the highlight itself, taken as the most common
        // luminance over the ink's bounding box rather than guessed from a pixel that might be a glyph.
        var x0 = ink.Min(p => p.X);
        var x1 = ink.Max(p => p.X);
        var y0 = ink.Min(p => p.Y);
        var y1 = ink.Max(p => p.Y);
        var histogram = new Dictionary<int, int>();
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var bucket = (int)Math.Round(Lum(selected, x, y));
                histogram[bucket] = histogram.GetValueOrDefault(bucket) + 1;
            }
        }

        var selectedBg = histogram.OrderByDescending(kv => kv.Value).First().Key;
        var plainContrast = ink.Average(p => Math.Abs(Lum(plain, p.X, p.Y) - plainBg));
        var selectedContrast = ink.Average(p => Math.Abs(Lum(selected, p.X, p.Y) - selectedBg));

        selectedContrast.ShouldBeGreaterThan(
            plainContrast * 0.6d,
            $"selected ink contrast {selectedContrast:F1} against unselected {plainContrast:F1} "
            + $"(highlight background {selectedBg}, field background {plainBg:F1})");
    }
}
