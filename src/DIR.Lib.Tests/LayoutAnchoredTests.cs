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
}
