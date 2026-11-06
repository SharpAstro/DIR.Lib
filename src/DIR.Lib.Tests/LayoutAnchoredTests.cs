using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Pins <see cref="Layout.Node.Anchored"/>: a floating child placed inside a rect rather than filling it
/// or taking a share of it.
/// <para>
/// Added because every consumer that floats a panel had written the same three things by hand — a switch
/// turning a dock side into a coordinate, the reader's offset along that edge, and a clamp keeping the
/// panel on screen when the window or a sidebar moves under it. The switch is where they drift, so each
/// side is pinned here separately rather than by one parameterised case.
/// </para>
/// </summary>
public class LayoutAnchoredTests
{
    private const float Area = 200f;

    /// <summary>A 40x30 panel floating in a 200x200 area, at whatever anchor is asked for.</summary>
    private static Rect<float> Placed(Layout.DockSide? side, float along = 0f, float across = 0f,
        float margin = 0f, bool clamp = true, float w = 40f, float h = 30f)
    {
        var panel = Layout.Builder.Spacer().WFixed(w).HFixed(h);
        var root = Layout.Builder.Anchored(panel, side, along, across, margin, clamp);

        // Depth 1 is the child: Arrange places the Anchored node itself at the bounds it was given.
        return Layout.Engine.Arrange(root, new Rect<float>(0, 0, Area, Area), new UnitContext())
            .First(a => a.Depth > 0)
            .Bounds;
    }

    [Fact]
    public void TheChildKeepsItsOwnMeasuredSize_RatherThanFillingTheArea()
    {
        var r = Placed(Layout.DockSide.Left);

        r.Width.ShouldBe(40f);
        r.Height.ShouldBe(30f);
    }

    /// <summary>
    /// Each side pins ONE coordinate and keeps the offset on the other. Asserted side by side rather than
    /// through a shared expression, because a switch that pins the wrong coordinate for one side is exactly
    /// the defect this primitive exists to stop, and a test built from the same switch would not see it.
    /// </summary>
    [Fact]
    public void EachSidePinsItsOwnCoordinateAndKeepsTheOffsetAlongTheEdge()
    {
        // Left/Right pin X at the margin and run the offset DOWN.
        Placed(Layout.DockSide.Left, along: 50f, margin: 8f).ShouldSatisfyAllConditions(
            () => Placed(Layout.DockSide.Left, along: 50f, margin: 8f).X.ShouldBe(8f),
            () => Placed(Layout.DockSide.Left, along: 50f, margin: 8f).Y.ShouldBe(50f));

        // Right sits its far edge a margin in, so its left edge is area - margin - width.
        Placed(Layout.DockSide.Right, along: 50f, margin: 8f).X.ShouldBe(Area - 8f - 40f);
        Placed(Layout.DockSide.Right, along: 50f, margin: 8f).Y.ShouldBe(50f);

        // Top/Bottom pin Y and run the offset ACROSS.
        Placed(Layout.DockSide.Top, along: 50f, margin: 8f).Y.ShouldBe(8f);
        Placed(Layout.DockSide.Top, along: 50f, margin: 8f).X.ShouldBe(50f);

        Placed(Layout.DockSide.Bottom, along: 50f, margin: 8f).Y.ShouldBe(Area - 8f - 30f);
        Placed(Layout.DockSide.Bottom, along: 50f, margin: 8f).X.ShouldBe(50f);
    }

    [Fact]
    public void WithNoSideBothOffsetsDecideThePosition()
    {
        var r = Placed(side: null, along: 30f, across: 70f);

        r.X.ShouldBe(30f);
        r.Y.ShouldBe(70f);
    }

    /// <summary>
    /// The clamp is what makes a floating panel survive the window narrowing or a sidebar opening under it,
    /// which is why a consumer doing this by hand re-clamps every frame.
    /// </summary>
    [Fact]
    public void AnOffsetPastTheEdgeIsClampedBackInsideTheMargin()
    {
        // Far past the bottom-right: pulled back so the panel's far edge sits a margin in.
        var r = Placed(side: null, along: 1000f, across: 1000f, margin: 8f);
        r.X.ShouldBe(Area - 8f - 40f);
        r.Y.ShouldBe(Area - 8f - 30f);

        // ...and past the top-left, back to the margin.
        var t = Placed(side: null, along: -1000f, across: -1000f, margin: 8f);
        t.X.ShouldBe(8f);
        t.Y.ShouldBe(8f);
    }

    /// <summary>
    /// A panel LARGER than the space left for it inverts the clamp's range — a window dragged narrow, or a
    /// sidebar opening under a wide palette. The upper bound is guarded against its own lower bound, so the
    /// panel parks at the leading margin instead of jumping to a negative one.
    /// </summary>
    [Fact]
    public void APanelTooBigForTheAreaParksAtTheLeadingMargin_RatherThanInvertingTheClamp()
    {
        var r = Placed(side: null, along: 500f, across: 500f, margin: 8f, w: 400f, h: 400f);

        r.X.ShouldBe(8f);
        r.Y.ShouldBe(8f);
    }

    [Fact]
    public void ClampCanBeTurnedOff_ForAChildThatMayOverhang()
    {
        // A drag chip tracking a pointer past an edge is the case: it is meant to hang out.
        var r = Placed(side: null, along: 1000f, across: 1000f, margin: 8f, clamp: false);

        r.X.ShouldBe(1000f);
        r.Y.ShouldBe(1000f);
    }

    /// <summary>
    /// Measured to its CHILD, not to the space it floats in — so nesting one inside a stack reserves the
    /// panel rather than the canvas. Without this an Anchored in a column would swallow the whole column.
    /// </summary>
    [Fact]
    public void ItMeasuresToItsChild_NotToTheAvailableSpace()
    {
        var measured = Layout.Engine.Measure(
            Layout.Builder.Anchored(Layout.Builder.Spacer().WFixed(40f).HFixed(30f), Layout.DockSide.Left),
            new Layout.Size<float>(Area, Area),
            new UnitContext());

        measured.Width.ShouldBe(40f);
        measured.Height.ShouldBe(30f);
    }

    /// <summary>
    /// The composition it exists for: a panel floating over content that fills the same rect. The base is
    /// arranged first (so it paints under), and the floating child keeps its own size.
    /// </summary>
    [Fact]
    public void OverlayPlusAnchoredFloatsAPanelOverFullBleedContent()
    {
        var page = Layout.Builder.Spacer().Stretch();
        var palette = Layout.Builder.Spacer().WFixed(40f).HFixed(30f);
        var root = Layout.Builder.Overlay(page, Layout.Builder.Anchored(palette, Layout.DockSide.Right, margin: 8f));

        var arranged = Layout.Engine.Arrange(root, new Rect<float>(0, 0, Area, Area), new UnitContext());
        var leaves = arranged.Where(a => a.Node is Layout.Node.Leaf).Select(a => a.Bounds).ToArray();

        leaves.Length.ShouldBe(2);
        // The page fills the rect...
        leaves[0].Width.ShouldBe(Area);
        leaves[0].Height.ShouldBe(Area);
        // ...and the palette floats at the right edge, at its own size, painted after it.
        leaves[1].X.ShouldBe(Area - 8f - 40f);
        leaves[1].Width.ShouldBe(40f);
    }

    /// <summary>Design units map 1:1 to surface units, so an assertion reads as the number it was authored as.</summary>
    private sealed class UnitContext : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) =>
            new(text.Length * fontSize, fontSize);

        public float ToSurface(float designUnits) => designUnits;
    }
    // Eighteen 10-wide words: one line of 180 in a wide window, two lines of ten at 100.
    private static Layout.Node Words() =>
        Layout.Builder.WrapH([.. Enumerable.Range(0, 18).Select(_ => Layout.Builder.Spacer().WFixed(10f).HFixed(10f))]).WStar();

    private static void BoxHoldsEveryWord(Layout.Node root, Rect<float> bounds, float minLines = 0f)
    {
        var arranged = Layout.Engine.Arrange(root, bounds, new UnitContext());
        var box = arranged.First(a => a.Depth > 0).Bounds;
        var words = arranged.Where(a => a.Node is Layout.Node.Leaf).Select(a => a.Bounds).ToArray();

        words.Length.ShouldBe(18);
        box.Height.ShouldBeGreaterThanOrEqualTo(minLines * 10f - 0.01f);
        words.Max(w => w.Y + w.Height).ShouldBeLessThanOrEqualTo(box.Y + box.Height + 0.01f);
        words.Max(w => w.X + w.Width).ShouldBeLessThanOrEqualTo(box.X + box.Width + 0.01f);
    }

    [Fact]
    public void AnAnchoredFixedWidthParagraphGetsABoxThatHoldsEveryWord()
    {
        var root = Layout.Builder.Anchored(Layout.Builder.VStack(Words()).WFixed(100f), Layout.DockSide.Left);

        BoxHoldsEveryWord(root, new Rect<float>(0, 0, 1000f, 1000f), minLines: 2f);
    }

    [Fact]
    public void AnAnchoredToFixedWidthParagraphGetsABoxThatHoldsEveryWord()
    {
        var root = Layout.Builder.AnchoredTo(new RectF32(100, 100, 20, 20), Layout.Builder.VStack(Words()).WFixed(100f));

        BoxHoldsEveryWord(root, new Rect<float>(0, 0, 1000f, 1000f), minLines: 2f);
    }

    [Fact]
    public void TheChildIsMeasuredAtTheWidthInsideTheMargins()
    {
        // 100 wide less two margins of 10 is 80: 8 words a line, 3 lines. Measured at the whole 100 it would count 10 a
        // line, 2 lines, and the arranged 80 would then hang the third line out underneath.
        var root = Layout.Builder.Anchored(Layout.Builder.VStack(Words()), Layout.DockSide.Left, margin: 10f);

        BoxHoldsEveryWord(root, new Rect<float>(0, 0, 100f, 1000f), minLines: 3f);
    }

    [Fact]
    public void AnchoredToASideMeasuresTheChildAtTheRoomOnThatSide()
    {
        // 60 of room right of the anchor (940 to the 1000 edge): measured at the whole
        // window the words would count one line, so the room must wrap them to several lines the box then holds.
        var root = Layout.Builder.AnchoredTo(new RectF32(920, 100, 20, 20), Layout.Builder.VStack(Words()), Layout.DockSide.Right);

        var arranged = Layout.Engine.Arrange(root, new Rect<float>(0, 0, 1000f, 1000f), new UnitContext());
        var box = arranged.First(a => a.Depth > 0).Bounds;

        box.Width.ShouldBeLessThanOrEqualTo(60f + 0.01f);
        box.Height.ShouldBeGreaterThanOrEqualTo(30f - 0.01f);
        arranged.Where(a => a.Node is Layout.Node.Leaf).Max(a => a.Bounds.Y + a.Bounds.Height)
            .ShouldBeLessThanOrEqualTo(box.Y + box.Height + 0.01f);
    }

    [Fact]
    public void MaxWidthCapsTheWidthTheChildIsMeasuredAndArrangedAt()
    {
        var root = new Layout.Node.Anchored(Layout.Builder.VStack(Words()), Layout.DockSide.Left) { MaxWidth = 100f };

        var arranged = Layout.Engine.Arrange(root, new Rect<float>(0, 0, 1000f, 1000f), new UnitContext());
        var box = arranged.First(a => a.Depth > 0).Bounds;

        box.Width.ShouldBeLessThanOrEqualTo(100f + 0.01f);
        BoxHoldsEveryWord(root, new Rect<float>(0, 0, 1000f, 1000f), minLines: 2f);
        Layout.Engine.Measure(root, new Layout.Size<float>(1000f, 1000f), new UnitContext()).Width
            .ShouldBeLessThanOrEqualTo(100f + 0.01f);
    }

    [Theory]
    [InlineData(Layout.DockSide.Right, false)]
    [InlineData(Layout.DockSide.Left, false)]
    [InlineData(Layout.DockSide.Right, true)]
    [InlineData(Layout.DockSide.Left, true)]
    public void AHorizontalSideWithNoRoomStaysVisibleAndOnScreen(Layout.DockSide side, bool flip)
    {
        // The anchor fills the width (or touches the edge), so there is no room beside it: the box keeps a width and the
        // clamp keeps it inside, rather than collapsing to nothing.
        var anchor = flip ? new RectF32(0, 100, 1000, 20) : side == Layout.DockSide.Right ? new RectF32(990, 100, 10, 20) : new RectF32(0, 100, 10, 20);
        var root = new Layout.Node.Anchored(Layout.Builder.Spacer().WFixed(40f).HFixed(30f), side, 0f, 0f, 4f, true, anchor) { FlipWhenCramped = flip };

        var box = Layout.Engine.Arrange(root, new Rect<float>(0, 0, 1000f, 1000f), new UnitContext()).First(a => a.Depth > 0).Bounds;

        box.Width.ShouldBeGreaterThan(0f);
        box.X.ShouldBeGreaterThanOrEqualTo(0f);
        (box.X + box.Width).ShouldBeLessThanOrEqualTo(1000f + 0.01f);
    }

    /// <summary>A 40x30 panel against a 20x20 anchor at (<paramref name="ax"/>, <paramref name="ay"/>) in the 200x200 area.</summary>
    private static Rect<float> AgainstAnchor(Layout.DockSide side, float ax, float ay, bool center = false, bool flip = false)
    {
        var panel = Layout.Builder.Spacer().WFixed(40f).HFixed(30f);
        var root = new Layout.Node.Anchored(panel, side, Margin: 2f, Anchor: new RectF32(ax, ay, 20f, 20f))
        {
            CenterAlong = center,
            FlipWhenCramped = flip,
        };

        return Layout.Engine.Arrange(root, new Rect<float>(0, 0, Area, Area), new UnitContext())
            .First(a => a.Depth > 0)
            .Bounds;
    }

    [Theory]
    [InlineData(Layout.DockSide.Bottom, 90f, 122f)]  // x = 100 + (20 - 40) / 2; y = 100 + 20 + 2
    [InlineData(Layout.DockSide.Top, 90f, 68f)]      // y = 100 - 2 - 30
    [InlineData(Layout.DockSide.Right, 122f, 95f)]   // x = 100 + 20 + 2; y = 100 + (20 - 30) / 2
    [InlineData(Layout.DockSide.Left, 58f, 95f)]     // x = 100 - 2 - 40
    public void CenteredAlongItsEdgeTheChildIsLevelWithTheAnchorsMiddle(Layout.DockSide side, float x, float y)
    {
        var r = AgainstAnchor(side, 100f, 100f, center: true);

        r.X.ShouldBe(x);
        r.Y.ShouldBe(y);
    }

    [Theory]
    [InlineData(Layout.DockSide.Bottom, 100f, 175f, 143f)]  // no room below 175 + 20; above at 175 - 2 - 30
    [InlineData(Layout.DockSide.Top, 100f, 5f, 27f)]        // no room above 5; below at 5 + 20 + 2
    public void ACrampedSideFlipsToTheOtherWhenAsked(Layout.DockSide side, float ax, float ay, float y)
    {
        AgainstAnchor(side, ax, ay, flip: true).Y.ShouldBe(y);
        AgainstAnchor(side, ax, ay, flip: false).Y.ShouldNotBe(y, "without the flag the clamp alone answers");
    }

    [Theory]
    [InlineData(Layout.DockSide.Right, 175f, 100f, 133f)]   // no room right of 195; left at 175 - 2 - 40
    [InlineData(Layout.DockSide.Left, 5f, 100f, 27f)]       // no room left of 5; right at 5 + 20 + 2
    public void ACrampedSideFlipsAcrossToo(Layout.DockSide side, float ax, float ay, float x)
        => AgainstAnchor(side, ax, ay, flip: true).X.ShouldBe(x);

    [Fact]
    public void ASideWithNoRoomOnEitherSideStaysAndIsClamped()
    {
        // A 30-tall panel beside a 20-tall anchor filling a 200-tall area's middle has room on neither side
        // when the anchor is 190 tall; it keeps Bottom and the clamp holds it inside the area.
        var panel = Layout.Builder.Spacer().WFixed(40f).HFixed(30f);
        var root = new Layout.Node.Anchored(panel, Layout.DockSide.Bottom, Margin: 2f, Anchor: new RectF32(80f, 5f, 20f, 190f))
        {
            FlipWhenCramped = true,
        };

        var r = Layout.Engine.Arrange(root, new Rect<float>(0, 0, Area, Area), new UnitContext()).First(a => a.Depth > 0).Bounds;

        r.Y.ShouldBe(Area - 2f - 30f, "clamped to the bottom, not flipped to a side with no more room");
    }
}
