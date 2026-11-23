using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Node.FocusRing"/>: a stroke outside the node's edge while the list cursor is on its hit.
/// Rows sit in a 100-wide card with a 20-unit margin so the ring has room outside the rect.
/// </summary>
public class FocusRingTests
{
    private static readonly RGBAColor32 Fill = new(0x20, 0x40, 0x60, 0xff);
    private static readonly RGBAColor32 Ring = new(0xff, 0xc0, 0x00, 0xff);

    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    private sealed class ListWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "stub.ttf", scale: DesignScale.One);
        }
    }

    private sealed class FloatCtx : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * fontSize * 0.5f, fontSize);
        public float ToSurface(float designUnits) => designUnits;
        public float ToSurfaceX(float designUnits) => designUnits;
        public float ToSurfaceY(float designUnits) => designUnits;
    }

    private static RGBAColor32 PixelAt(StubRenderer r, int x, int y)
    {
        var img = r.Surface;
        var i = (y * img.Width + x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    // Row i occupies x 20..80, y 20+40i .. 50+40i inside a 100 x 140 card, 10 unit gaps.
    private static Layout.Node Rows(float radius = 0f, bool disabled = false, bool ring = true)
    {
        var rows = new Layout.Node[2];
        for (var i = 0; i < 2; i++)
        {
            var row = Layout.Builder.Spacer().RowH(30f).Bg(Fill).Radius(radius)
                .Clickable(new HitResult.ListItemHit("views", i), _ => { });
            if (ring) row = row.WithFocusRing(Ring);
            if (disabled) row = row.Disabled("no");
            rows[i] = row;
        }
        return new Layout.Node.Stack(System.Collections.Immutable.ImmutableArray.Create(rows)) { Padding = 20f, Gap = 10f };
    }

    private static StubRenderer Draw(Layout.Node tree, int cursorRow)
    {
        var renderer = new StubRenderer(100, 140);
        var widget = new ListWidget(renderer);
        widget.ListCursor.Open("views", cursorRow);
        widget.Render(tree, new RectF32(0, 0, 100, 140));
        return renderer;
    }

    [Fact]
    public void TheRingPaintsOutsideTheRectAndNotInside()
    {
        var r = Draw(Rows(), 0);

        PixelAt(r, 50, 19).ShouldBe(Ring);      // band above the row
        PixelAt(r, 19, 35).ShouldBe(Ring);      // band left
        PixelAt(r, 80, 35).ShouldBe(Ring);      // band right
        PixelAt(r, 50, 50).ShouldBe(Ring);      // band below
        PixelAt(r, 50, 35).ShouldBe(Fill);      // inside the rect stays the fill
        PixelAt(r, 21, 21).ShouldBe(Fill);
    }

    [Fact]
    public void NoRingWhenTheCursorIsOnAnotherRow()
    {
        var r = Draw(Rows(), 1);

        PixelAt(r, 50, 19).ShouldNotBe(Ring);
        PixelAt(r, 19, 35).ShouldNotBe(Ring);
        PixelAt(r, 19, 75).ShouldBe(Ring);      // row 1 (y 60..90) is the one ringed
    }

    [Fact]
    public void NoRingWhenTheNodeDeclaresNone()
    {
        var r = Draw(Rows(ring: false), 0);

        PixelAt(r, 50, 19).ShouldNotBe(Ring);
    }

    [Fact]
    public void NoRingForADisabledNode()
    {
        var r = Draw(Rows(disabled: true), 0);

        PixelAt(r, 50, 19).ShouldNotBe(Ring);
    }

    [Fact]
    public void ARoundedNodeGetsRoundedRingCorners()
    {
        var r = Draw(Rows(radius: 10f), 0);

        PixelAt(r, 50, 19).ShouldBe(Ring);          // straight run is ringed
        PixelAt(r, 18, 18).ShouldNotBe(Ring);       // the corner of the outer square is cut away
    }

    [Fact]
    public void TheRingTakesNoLayoutSpace()
    {
        var rect = new Rect<float>(0, 0, 100, 140);
        var plain = Layout.Engine.Arrange(Rows(ring: false), rect, new FloatCtx());
        var ringed = Layout.Engine.Arrange(Rows(), rect, new FloatCtx());

        ringed.Length.ShouldBe(plain.Length);
        for (var i = 0; i < ringed.Length; i++)
        {
            ringed[i].Bounds.ShouldBe(plain[i].Bounds);
        }
    }

    [Fact]
    public void TheDamageRectGrowsByTheRingWidthPlusOne()
    {
        var node = Layout.Builder.Box(80, 40).WithFocusRing(Ring, 2f);
        var arranged = new Layout.ArrangedNode<float>(node, new Rect<float>(50, 50, 80, 40));

        var sig = LayoutDamage.Signature(arranged, null);
        sig.FocusRing.ShouldNotBeNull();
        var damage = LayoutDamage.DamageRect(sig);

        damage.X.ShouldBe(47f);
        damage.Y.ShouldBe(47f);
        damage.Width.ShouldBe(86f);
        damage.Height.ShouldBe(46f);
    }

    [Fact]
    public void TheOffsetLeavesAGapBetweenTheEdgeAndTheRing()
    {
        var rows = ((Layout.Node.Stack)Rows()).Children.Select(c => c.WithFocusRing(new Layout.FocusRingStyle { Color = Ring, Width = 2f, Offset = 3f }));
        var tree = new Layout.Node.Stack(System.Collections.Immutable.ImmutableArray.CreateRange(rows)) { Padding = 20f, Gap = 10f };
        var r = Draw(tree, 0);

        PixelAt(r, 50, 19).ShouldNotBe(Ring);   // gap: 17..19 shows the card, not the ring
        PixelAt(r, 50, 17).ShouldNotBe(Ring);
        PixelAt(r, 50, 16).ShouldBe(Ring);      // ring: 15..16
        PixelAt(r, 50, 15).ShouldBe(Ring);
        PixelAt(r, 50, 35).ShouldBe(Fill);
    }

    [Fact]
    public void TheDamageRectGrowsByTheOffsetToo()
    {
        var node = Layout.Builder.Box(80, 40).WithFocusRing(new Layout.FocusRingStyle { Color = Ring, Width = 2f, Offset = 3f });
        var arranged = new Layout.ArrangedNode<float>(node, new Rect<float>(50, 50, 80, 40));

        var damage = LayoutDamage.DamageRect(LayoutDamage.Signature(arranged, null));

        damage.X.ShouldBe(44f);     // 2 + 3 + 1
        damage.Width.ShouldBe(92f);
    }

    [Fact]
    public void ASideLeftOutPaintsNoRingThere()
    {
        var rows = ((Layout.Node.Stack)Rows()).Children.Select(c => c.WithFocusRing(new Layout.FocusRingStyle
            { Color = Ring, Width = 2f, Sides = Layout.BorderSides.Top | Layout.BorderSides.Left | Layout.BorderSides.Bottom }));
        var r = Draw(new Layout.Node.Stack(System.Collections.Immutable.ImmutableArray.CreateRange(rows)) { Padding = 20f, Gap = 10f }, 0);

        PixelAt(r, 50, 19).ShouldBe(Ring);
        PixelAt(r, 19, 35).ShouldBe(Ring);
        PixelAt(r, 80, 35).ShouldNotBe(Ring);
    }

    [Fact]
    public void ASideLeftOutKeepsTheRoundedCornersAndStopsAtTheNodeEdge()
    {
        var rows = ((Layout.Node.Stack)Rows(radius: 12f)).Children.Select(c => c.WithFocusRing(new Layout.FocusRingStyle
            { Color = Ring, Width = 2f, Sides = Layout.BorderSides.Top | Layout.BorderSides.Left | Layout.BorderSides.Bottom }));
        var r = Draw(new Layout.Node.Stack(System.Collections.Immutable.ImmutableArray.CreateRange(rows)) { Padding = 20f, Gap = 10f }, 0);

        PixelAt(r, 23, 23).ShouldBe(Ring);           // on the rounded corner's diagonal
        PixelAt(r, 18, 18).ShouldNotBe(Ring);        // the square corner stays empty
        PixelAt(r, 50, 19).ShouldBe(Ring);           // straight top band
        PixelAt(r, 82, 19).ShouldNotBe(Ring);        // no band past the open (right) edge
        PixelAt(r, 80, 35).ShouldNotBe(Ring);
    }
}
