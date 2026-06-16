using System;
using System.Collections.Immutable;
using System.Numerics;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Measure/Arrange unit tests for the surface-agnostic layout engine. A stub <see cref="IMeasureContext{T}"/>
/// stands in for the per-surface width oracle (fixed glyph metrics for pixels, char-count x 1 for cells), so
/// these run headless with no renderer.
/// </summary>
public class LayoutEngineTests
{
    // --- stub measure contexts ---

    private sealed class PixelCtx : IMeasureContext<float>
    {
        public float CharWidth = 7f;
        public float LineHeight = 16f;
        public float Scale = 1f;
        public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * CharWidth, LineHeight);
        public float ToSurface(float designUnits) => designUnits * Scale;
    }

    private sealed class CellCtx : IMeasureContext<int>
    {
        public Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
    }

    private static Rect<T> RectOf<T>(ImmutableArray<ArrangedNode<T>> arranged, LayoutNode node) where T : INumber<T>
    {
        foreach (var a in arranged)
        {
            if (ReferenceEquals(a.Node, node))
            {
                return a.Bounds;
            }
        }

        throw new InvalidOperationException("Node was not arranged.");
    }

    private static LayoutNode.Leaf Row(float fixedHeight) =>
        new(new LayoutContent.Box(0, 0)) { Height = Sizing.Fixed(fixedHeight), Width = Sizing.Star() };

    private static LayoutNode.Leaf StarRow(float weight = 1f) =>
        new(new LayoutContent.Box(0, 0)) { Height = Sizing.Star(weight), Width = Sizing.Star() };

    // --- Fixed sizing ---

    [Fact]
    public void VerticalStack_FixedRows_StackTopDownAtFullWidth()
    {
        var a = Row(10);
        var b = Row(10);
        var c = Row(10);
        var stack = new LayoutNode.Stack([a, b, c]);

        var arranged = LayoutEngine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, a).ShouldBe(new Rect<float>(0, 0, 100, 10));
        RectOf(arranged, b).ShouldBe(new Rect<float>(0, 10, 100, 10));
        RectOf(arranged, c).ShouldBe(new Rect<float>(0, 20, 100, 10));
    }

    [Fact]
    public void VerticalStack_WithGap_OffsetsByGap()
    {
        var a = Row(10);
        var b = Row(10);
        var stack = new LayoutNode.Stack([a, b], LayoutAxis.Vertical, Gap: 5f);

        var arranged = LayoutEngine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, a).Y.ShouldBe(0f);
        RectOf(arranged, b).Y.ShouldBe(15f); // 10 height + 5 gap
    }

    // --- Star sizing ---

    [Fact]
    public void VerticalStack_TwoEqualStars_SplitEvenly()
    {
        var a = StarRow();
        var b = StarRow();
        var stack = new LayoutNode.Stack([a, b]);

        var arranged = LayoutEngine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, a).Height.ShouldBe(50f, 0.001);
        RectOf(arranged, b).Height.ShouldBe(50f, 0.001);
        RectOf(arranged, b).Y.ShouldBe(50f, 0.001);
    }

    [Fact]
    public void VerticalStack_WeightedStars_SplitByWeight()
    {
        var a = StarRow(1f);
        var b = StarRow(3f);
        var stack = new LayoutNode.Stack([a, b]);

        var arranged = LayoutEngine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, a).Height.ShouldBe(25f, 0.001);
        RectOf(arranged, b).Height.ShouldBe(75f, 0.001);
    }

    [Fact]
    public void VerticalStack_FixedPlusStars_StarsSplitLeftover()
    {
        var fixedRow = Row(20);
        var s1 = StarRow();
        var s2 = StarRow();
        var stack = new LayoutNode.Stack([fixedRow, s1, s2]);

        var arranged = LayoutEngine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, fixedRow).Height.ShouldBe(20f, 0.001);
        RectOf(arranged, s1).Height.ShouldBe(40f, 0.001);
        RectOf(arranged, s2).Height.ShouldBe(40f, 0.001);
    }

    // --- Auto (intrinsic) sizing ---

    [Fact]
    public void VerticalStack_AutoText_SizesToGlyphMetrics()
    {
        var ctx = new PixelCtx { CharWidth = 7f, LineHeight = 16f };
        var text = new LayoutNode.Leaf(new LayoutContent.Text("hello")); // Auto x Auto
        var stack = new LayoutNode.Stack([text]);

        var arranged = LayoutEngine.Arrange(stack, new Rect<float>(0, 0, 100, 100), ctx);

        var r = RectOf(arranged, text);
        r.Height.ShouldBe(16f, 0.001);            // line height
        r.Width.ShouldBe(35f, 0.001);             // 5 chars x 7, clamped under crossAvail (100)
    }

    // --- cross-axis resolution ---

    [Fact]
    public void VerticalStack_CrossAxis_FixedStarAuto()
    {
        var ctx = new PixelCtx { CharWidth = 7f };
        var fixedW = new LayoutNode.Leaf(new LayoutContent.Box(0, 0)) { Width = Sizing.Fixed(30), Height = Sizing.Fixed(10) };
        var starW = new LayoutNode.Leaf(new LayoutContent.Box(0, 0)) { Width = Sizing.Star(), Height = Sizing.Fixed(10) };
        var autoW = new LayoutNode.Leaf(new LayoutContent.Text("ab")) { Width = Sizing.Auto, Height = Sizing.Fixed(10) };
        var stack = new LayoutNode.Stack([fixedW, starW, autoW]);

        var arranged = LayoutEngine.Arrange(stack, new Rect<float>(0, 0, 100, 100), ctx);

        RectOf(arranged, fixedW).Width.ShouldBe(30f, 0.001);
        RectOf(arranged, starW).Width.ShouldBe(100f, 0.001);  // stretch to full cross
        RectOf(arranged, autoW).Width.ShouldBe(14f, 0.001);   // 2 chars x 7
    }

    // --- padding ---

    [Fact]
    public void Padding_InsetsChildArea()
    {
        var child = Row(10);
        var stack = new LayoutNode.Stack([child]) { Padding = 10f };

        var arranged = LayoutEngine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        var r = RectOf(arranged, child);
        r.X.ShouldBe(10f, 0.001);
        r.Y.ShouldBe(10f, 0.001);
        r.Width.ShouldBe(80f, 0.001);  // 100 - 2*10
    }

    // --- nested + axis switch ---

    [Fact]
    public void NestedStack_HorizontalRowInsideVertical_SplitsWidth()
    {
        var top = Row(30);
        var c1 = new LayoutNode.Leaf(new LayoutContent.Box(0, 0)) { Width = Sizing.Star(), Height = Sizing.Star() };
        var c2 = new LayoutNode.Leaf(new LayoutContent.Box(0, 0)) { Width = Sizing.Star(), Height = Sizing.Star() };
        var row = new LayoutNode.Stack([c1, c2], LayoutAxis.Horizontal) { Width = Sizing.Star(), Height = Sizing.Star() };
        var outer = new LayoutNode.Stack([top, row]);

        var arranged = LayoutEngine.Arrange(outer, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, top).ShouldBe(new Rect<float>(0, 0, 100, 30));
        var rowRect = RectOf(arranged, row);
        rowRect.ShouldBe(new Rect<float>(0, 30, 100, 70));
        RectOf(arranged, c1).ShouldBe(new Rect<float>(0, 30, 50, 70));
        RectOf(arranged, c2).ShouldBe(new Rect<float>(50, 30, 50, 70));
    }

    // --- dock ---

    [Fact]
    public void Dock_TopStripPlusFill()
    {
        var top = new LayoutNode.Leaf(new LayoutContent.Box(0, 0));
        var fill = new LayoutNode.Leaf(new LayoutContent.Box(0, 0));
        var dock = new LayoutNode.Dock([new DockChild(DockSide.Top, top, Sizing.Fixed(20))], fill);

        var arranged = LayoutEngine.Arrange(dock, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, top).ShouldBe(new Rect<float>(0, 0, 100, 20));
        RectOf(arranged, fill).ShouldBe(new Rect<float>(0, 20, 100, 80));
    }

    // --- grid ---

    [Fact]
    public void Grid_TwoByTwo_TilesEvenly()
    {
        var cells = new LayoutNode[]
        {
            new LayoutNode.Leaf(new LayoutContent.Box(0, 0)),
            new LayoutNode.Leaf(new LayoutContent.Box(0, 0)),
            new LayoutNode.Leaf(new LayoutContent.Box(0, 0)),
            new LayoutNode.Leaf(new LayoutContent.Box(0, 0)),
        };
        var grid = new LayoutNode.Grid(2, [.. cells]);

        var arranged = LayoutEngine.Arrange(grid, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, cells[0]).ShouldBe(new Rect<float>(0, 0, 50, 50));
        RectOf(arranged, cells[1]).ShouldBe(new Rect<float>(50, 0, 50, 50));
        RectOf(arranged, cells[2]).ShouldBe(new Rect<float>(0, 50, 50, 50));
        RectOf(arranged, cells[3]).ShouldBe(new Rect<float>(50, 50, 50, 50));
    }

    // --- overlay (z-order) ---

    [Fact]
    public void Overlay_EmitsBaseBeforeTop_BothFillBounds()
    {
        var baseNode = new LayoutNode.Leaf(new LayoutContent.Box(0, 0));
        var topNode = new LayoutNode.Leaf(new LayoutContent.Box(0, 0));
        var overlay = new LayoutNode.Overlay(baseNode, topNode);

        var arranged = LayoutEngine.Arrange(overlay, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, baseNode).ShouldBe(new Rect<float>(0, 0, 100, 100));
        RectOf(arranged, topNode).ShouldBe(new Rect<float>(0, 0, 100, 100));

        // baseNode/topNode are value-equal records (same content + rect), so locate by reference identity.
        var baseIdx = -1;
        var topIdx = -1;
        for (var i = 0; i < arranged.Length; i++)
        {
            if (ReferenceEquals(arranged[i].Node, baseNode)) baseIdx = i;
            if (ReferenceEquals(arranged[i].Node, topNode)) topIdx = i;
        }

        baseIdx.ShouldBeLessThan(topIdx); // base painted first, top on top
    }

    [Fact]
    public void Arrange_EmitsRootFirst()
    {
        var child = Row(10);
        var stack = new LayoutNode.Stack([child]);

        var arranged = LayoutEngine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        ReferenceEquals(arranged[0].Node, stack).ShouldBeTrue();
    }

    // --- integral (cell) coordinates: exact tiling, deterministic remainder ---

    [Fact]
    public void CellStack_StarSplit_RemainderToLaterItems_SumsExactly()
    {
        var a = new LayoutNode.Leaf(new LayoutContent.Box(0, 0)) { Height = Sizing.Star(), Width = Sizing.Star() };
        var b = new LayoutNode.Leaf(new LayoutContent.Box(0, 0)) { Height = Sizing.Star(), Width = Sizing.Star() };
        var stack = new LayoutNode.Stack([a, b]);

        var arranged = LayoutEngine.Arrange(stack, new Rect<int>(0, 0, 10, 3), new CellCtx());

        var ra = RectOf(arranged, a);
        var rb = RectOf(arranged, b);
        ra.Height.ShouldBe(1);          // floor(1.5)
        rb.Height.ShouldBe(2);          // remainder
        (ra.Height + rb.Height).ShouldBe(3); // no cell lost or gained
        rb.Y.ShouldBe(1);
    }

    [Fact]
    public void CellGrid_ThreeColumns_TilesWithoutGaps()
    {
        var cells = new LayoutNode[]
        {
            new LayoutNode.Leaf(new LayoutContent.Box(0, 0)),
            new LayoutNode.Leaf(new LayoutContent.Box(0, 0)),
            new LayoutNode.Leaf(new LayoutContent.Box(0, 0)),
        };
        var grid = new LayoutNode.Grid(3, [.. cells]);

        var arranged = LayoutEngine.Arrange(grid, new Rect<int>(0, 0, 10, 4), new CellCtx());

        var w0 = RectOf(arranged, cells[0]).Width;
        var w1 = RectOf(arranged, cells[1]).Width;
        var w2 = RectOf(arranged, cells[2]).Width;
        (w0 + w1 + w2).ShouldBe(10); // exact tile of a 10-wide strip across 3 columns
        RectOf(arranged, cells[2]).Right.ShouldBe(10);
    }
}
