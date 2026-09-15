using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The measure seam: a box can BE the measurement of its content rather than a sum of the constants its
/// body happens to draw with.
///
/// <para>
/// <c>Layout.Engine.Measure</c> has always been public, and the widget base had no seam for it -- so a
/// widget that wanted one built a <see cref="PixelMeasureContext{TSurface}"/> by hand, which is a second
/// statement of the font and the scale, free to disagree with the one arrange and paint share. One
/// consumer had 55 <c>MeasureText</c> call sites in 16 files and exactly one use of
/// <c>Engine.Measure</c>, in the single panel someone had bothered to wire up that way.
/// </para>
/// </summary>
public class PixelWidgetMeasureSeamTests
{
    private static DeclarationWidget Fixture() => new(new DeclarationStubRenderer(200, 200));

    [Fact]
    public void MeasureLayoutAnswersWithTheWidgetsOwnFontAndScale()
    {
        // Half the font size per character, from the widget's renderer: a 6-character run at 10 units is
        // 30 wide and 10 tall, and the padding is added on both axes.
        var widget = Fixture();

        var size = widget.Measure(
            Layout.Builder.VStack(Layout.Builder.Text("abcdef", 10f)).Pad(4f),
            new Layout.Size<float>(200f, 200f));

        size.Width.ShouldBe(38f);
        size.Height.ShouldBe(18f);
    }

    [Fact]
    public void ItIsTheSameAnswerTheArrangeWouldGiveBecauseItIsTheSameContext()
    {
        // The point of the seam. A measure through a context of its own can differ from the arrange in
        // font, in DPI, or in the fallback chain, and nothing says so -- the box is simply the wrong
        // size on one machine.
        var widget = Fixture();
        var tree = Layout.Builder.VStack(Layout.Builder.Text("abcdef", 10f)).Pad(4f);
        var ctx = widget.Context();

        var seam = widget.Measure(tree, new Layout.Size<float>(200f, 200f));
        var direct = Layout.Engine.Measure(tree, new Layout.Size<float>(200f, 200f), ctx);

        seam.ShouldBe(direct);
    }

    [Fact]
    public void MeasureContextCarriesTheWidgetsFontAndScale()
    {
        var widget = Fixture();
        var ctx = widget.Context();

        ctx.FontPath.ShouldBe("stub.ttf");
        ctx.Scale.ShouldBe(DesignScale.One);
        ctx.FontScale.ShouldBe(1f);
    }

    [Fact]
    public void ADeclaredControlCanBeMeasuredBeforeItIsPlaced()
    {
        // The use the seam exists for: state the widest thing a popover can hold, ask how big that is,
        // and place the box there -- instead of summing the reserved widths its body draws with.
        var widget = Fixture();

        var narrow = widget.Measure(Layout.Builder.Text("100", 10f), new Layout.Size<float>(200f, 200f));
        var wide = widget.Measure(Layout.Builder.Text("100 percent", 10f), new Layout.Size<float>(200f, 200f));

        wide.Width.ShouldBeGreaterThan(narrow.Width);
    }
}

/// <summary>
/// <c>CaretIndexAt</c> reaches a host that holds only the interface.
///
/// <para>
/// The base has implemented it since 9.1 and the interface did not declare it, so a host routing a press
/// over a field had no way to ask the widget that PRODUCED the hit -- and only that widget can answer,
/// the answer being measured through the renderer and fallback chain that drew the text. One consumer
/// wrote an interface of its own declaring exactly this one method, which is the shape that says it
/// belongs here.
/// </para>
/// </summary>
public class PixelWidgetCaretInterfaceTests
{
    private sealed class FieldWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "stub.ttf", scale: DesignScale.One);
        }
    }

    [Fact]
    public void AHostHoldingTheInterfaceCanPlaceACaret()
    {
        var state = new TextInputState();
        state.Activate("abcdef");
        var widget = new FieldWidget(new DeclarationStubRenderer(200, 60));
        widget.Render(Layout.Builder.TextInput(state, 10f).Stretch(), new RectF32(0, 0, 200, 20));

        var hit = widget.GetRegisteredRegions()
            .Select(r => r.Result)
            .OfType<HitResult.TextInputHit>()
            .Single();

        IPixelWidget asInterface = widget;

        // Right of the last glyph is the end of the value; left of the first is the start. The exact
        // index in between is TextInputPointerTests' subject -- what this pins is that the question can
        // be ASKED through the interface at all.
        asInterface.CaretIndexAt(hit, 1000f).ShouldBe(state.Text.Length);
        asInterface.CaretIndexAt(hit, -1000f).ShouldBe(0);
    }
}
