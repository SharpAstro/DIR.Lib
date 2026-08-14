using System;
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
        public ClickableRegion[] Render(Layout.Node root, RectF32 bounds, float dpiScale = 1f)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "font.ttf", dpiScale: dpiScale);
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
}
