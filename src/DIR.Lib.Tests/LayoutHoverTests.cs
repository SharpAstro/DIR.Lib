using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A node's hover fill, resolved at paint time against the rect the engine arranged it into.
///
/// <para>
/// The bug this exists to prevent: a consumer that wants a control to light under the pointer has to know
/// where that control IS, and the layout tree hands its rects back only after it has painted them — too
/// late to choose the fill. So every consumer computed the rect a second time by hand, and every one of
/// them drifted, because the arithmetic silently depends on the card's own bounds, its padding, the
/// spacers between rows and how many rows there happen to be. The symptom is a button that lights while
/// the pointer is a pad above it. Resolving the fill where the background is already painted removes the
/// class: it is the same rect, so it cannot disagree.
/// </para>
///
/// <para>
/// Inert by construction. A node with no <see cref="Layout.Node.HoverBackground"/>, or a host that sets no
/// <see cref="PixelWidgetBase{TSurface}.Pointer"/>, paints exactly what it painted before.
/// </para>
/// </summary>
public class LayoutHoverTests
{
    private static readonly RGBAColor32 Rest = new(0x20, 0x40, 0x60, 0xff);
    private static readonly RGBAColor32 Hot = new(0xc0, 0x30, 0x10, 0xff);

    /// <summary>Answers MeasureText itself so no real font file is loaded.</summary>
    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    private sealed class HoverWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds, (float X, float Y)? pointer)
        {
            Pointer = pointer;
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "stub.ttf", dpiScale: 1f);
        }
    }

    private static (HoverWidget Widget, StubRenderer Renderer) Fixture()
    {
        var renderer = new StubRenderer(100, 100);
        return (new HoverWidget(renderer), renderer);
    }

    private static RGBAColor32 PixelAt(StubRenderer r, int x, int y)
    {
        var img = r.Surface;
        var i = (y * img.Width + x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    private static Layout.Node Box() => Layout.Builder.Spacer().Stretch().Bg(Rest).BgHover(Hot);

    [Fact]
    public void ThePointerInsideTheArrangedRectPaintsTheHoverFill()
    {
        var (widget, renderer) = Fixture();

        widget.Render(Box(), new RectF32(10, 10, 40, 40), pointer: (30, 30));

        PixelAt(renderer, 30, 30).ShouldBe(Hot);
    }

    [Fact]
    public void ThePointerOutsideItPaintsTheOrdinaryBackground()
    {
        var (widget, renderer) = Fixture();

        widget.Render(Box(), new RectF32(10, 10, 40, 40), pointer: (80, 80));

        PixelAt(renderer, 30, 30).ShouldBe(Rest);
    }

    [Fact]
    public void NoPointerAtAllIsNotHovered()
    {
        // A host that never sets one -- every consumer written before this existed -- must be unaffected.
        var (widget, renderer) = Fixture();

        widget.Render(Box(), new RectF32(10, 10, 40, 40), pointer: null);

        PixelAt(renderer, 30, 30).ShouldBe(Rest);
    }

    [Fact]
    public void ANodeThatStatesNoHoverFillIgnoresThePointer()
    {
        var (widget, renderer) = Fixture();

        widget.Render(Layout.Builder.Spacer().Stretch().Bg(Rest), new RectF32(10, 10, 40, 40),
            pointer: (30, 30));

        PixelAt(renderer, 30, 30).ShouldBe(Rest);
    }

    [Fact]
    public void TheRectIsTopLeftInclusiveAndBottomRightExclusive()
    {
        // Two rows sharing an edge must not both claim the pointer on it, or a list lights two rows at
        // once wherever a boundary falls under the cursor.
        var (widget, renderer) = Fixture();

        widget.Render(Box(), new RectF32(10, 10, 40, 40), pointer: (10, 10));   // top-left corner: inside
        PixelAt(renderer, 30, 30).ShouldBe(Hot);

        widget.Render(Box(), new RectF32(10, 10, 40, 40), pointer: (50, 50));   // bottom-right: outside
        PixelAt(renderer, 30, 30).ShouldBe(Rest);
    }

    [Fact]
    public void HoverFollowsTheRectTheEngineArranged_NotOneTheCallerComputed()
    {
        // The regression that motivated this. The row sits under a header and a gap, so its top edge is
        // nowhere near the bounds' top -- exactly the offset a hand-computed rect gets wrong. The pointer
        // is placed at the row's real centre and must light THAT row and not its neighbour.
        var (widget, renderer) = Fixture();

        var tree = Layout.Builder.VStack(
                Layout.Builder.Spacer().RowH(10f).Bg(Rest),
                Layout.Builder.Spacer().RowH(10f).Bg(Rest),
                Box().RowH(10f))
            .Stretch();

        widget.Render(tree, new RectF32(0, 0, 100, 30), pointer: (50, 25));

        PixelAt(renderer, 50, 25).ShouldBe(Hot);    // the third row, where the pointer is
        PixelAt(renderer, 50, 5).ShouldBe(Rest);    // the first, which must not have lit
    }
}
