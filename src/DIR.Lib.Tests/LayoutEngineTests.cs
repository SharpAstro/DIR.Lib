using System;
using System.Collections.Immutable;
using System.Numerics;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Measure/Arrange unit tests for the surface-agnostic layout engine. A stub <see cref="Layout.IMeasureContext{T}"/>
/// stands in for the per-surface width oracle (fixed glyph metrics for pixels, char-count x 1 for cells), so
/// these run headless with no renderer.
/// </summary>
public class LayoutEngineTests
{
    // --- stub measure contexts ---

    private sealed class PixelCtx : Layout.IMeasureContext<float>
    {
        public float CharWidth = 7f;
        public float LineHeight = 16f;
        public float Scale = 1f;
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * CharWidth, LineHeight);
        public float ToSurface(float designUnits) => designUnits * Scale;
    }

    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
    }

    private static Rect<T> RectOf<T>(ImmutableArray<Layout.ArrangedNode<T>> arranged, Layout.Node node) where T : INumber<T>
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

    private static Layout.Node.Leaf Row(float fixedHeight) =>
        new(new Layout.Content.Box(0, 0)) { Height = Layout.Sizing.Fixed(fixedHeight), Width = Layout.Sizing.Star() };

    private static Layout.Node.Leaf StarRow(float weight = 1f) =>
        new(new Layout.Content.Box(0, 0)) { Height = Layout.Sizing.Star(weight), Width = Layout.Sizing.Star() };

    // --- Fixed sizing ---

    [Fact]
    public void VerticalStack_FixedRows_StackTopDownAtFullWidth()
    {
        var a = Row(10);
        var b = Row(10);
        var c = Row(10);
        var stack = new Layout.Node.Stack([a, b, c]);

        var arranged = Layout.Engine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, a).ShouldBe(new Rect<float>(0, 0, 100, 10));
        RectOf(arranged, b).ShouldBe(new Rect<float>(0, 10, 100, 10));
        RectOf(arranged, c).ShouldBe(new Rect<float>(0, 20, 100, 10));
    }

    [Fact]
    public void VerticalStack_WithGap_OffsetsByGap()
    {
        var a = Row(10);
        var b = Row(10);
        var stack = new Layout.Node.Stack([a, b], Layout.Axis.Vertical, Gap: 5f);

        var arranged = Layout.Engine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, a).Y.ShouldBe(0f);
        RectOf(arranged, b).Y.ShouldBe(15f); // 10 height + 5 gap
    }

    // --- Star sizing ---

    [Fact]
    public void VerticalStack_TwoEqualStars_SplitEvenly()
    {
        var a = StarRow();
        var b = StarRow();
        var stack = new Layout.Node.Stack([a, b]);

        var arranged = Layout.Engine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, a).Height.ShouldBe(50f, 0.001);
        RectOf(arranged, b).Height.ShouldBe(50f, 0.001);
        RectOf(arranged, b).Y.ShouldBe(50f, 0.001);
    }

    [Fact]
    public void VerticalStack_WeightedStars_SplitByWeight()
    {
        var a = StarRow(1f);
        var b = StarRow(3f);
        var stack = new Layout.Node.Stack([a, b]);

        var arranged = Layout.Engine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, a).Height.ShouldBe(25f, 0.001);
        RectOf(arranged, b).Height.ShouldBe(75f, 0.001);
    }

    [Fact]
    public void VerticalStack_FixedPlusStars_StarsSplitLeftover()
    {
        var fixedRow = Row(20);
        var s1 = StarRow();
        var s2 = StarRow();
        var stack = new Layout.Node.Stack([fixedRow, s1, s2]);

        var arranged = Layout.Engine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, fixedRow).Height.ShouldBe(20f, 0.001);
        RectOf(arranged, s1).Height.ShouldBe(40f, 0.001);
        RectOf(arranged, s2).Height.ShouldBe(40f, 0.001);
    }

    // --- Auto (intrinsic) sizing ---

    [Fact]
    public void VerticalStack_AutoText_SizesToGlyphMetrics()
    {
        var ctx = new PixelCtx { CharWidth = 7f, LineHeight = 16f };
        var text = new Layout.Node.Leaf(new Layout.Content.Text("hello")); // Auto x Auto
        var stack = new Layout.Node.Stack([text]);

        var arranged = Layout.Engine.Arrange(stack, new Rect<float>(0, 0, 100, 100), ctx);

        var r = RectOf(arranged, text);
        r.Height.ShouldBe(16f, 0.001);            // line height
        r.Width.ShouldBe(35f, 0.001);             // 5 chars x 7, clamped under crossAvail (100)
    }

    // --- cross-axis resolution ---

    [Fact]
    public void VerticalStack_CrossAxis_FixedStarAuto()
    {
        var ctx = new PixelCtx { CharWidth = 7f };
        var fixedW = new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Width = Layout.Sizing.Fixed(30), Height = Layout.Sizing.Fixed(10) };
        var starW = new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Width = Layout.Sizing.Star(), Height = Layout.Sizing.Fixed(10) };
        var autoW = new Layout.Node.Leaf(new Layout.Content.Text("ab")) { Width = Layout.Sizing.Auto, Height = Layout.Sizing.Fixed(10) };
        var stack = new Layout.Node.Stack([fixedW, starW, autoW]);

        var arranged = Layout.Engine.Arrange(stack, new Rect<float>(0, 0, 100, 100), ctx);

        RectOf(arranged, fixedW).Width.ShouldBe(30f, 0.001);
        RectOf(arranged, starW).Width.ShouldBe(100f, 0.001);  // stretch to full cross
        RectOf(arranged, autoW).Width.ShouldBe(14f, 0.001);   // 2 chars x 7
    }

    // --- padding ---

    [Fact]
    public void Padding_InsetsChildArea()
    {
        var child = Row(10);
        var stack = new Layout.Node.Stack([child]) { Padding = 10f };

        var arranged = Layout.Engine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

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
        var c1 = new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Width = Layout.Sizing.Star(), Height = Layout.Sizing.Star() };
        var c2 = new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Width = Layout.Sizing.Star(), Height = Layout.Sizing.Star() };
        var row = new Layout.Node.Stack([c1, c2], Layout.Axis.Horizontal) { Width = Layout.Sizing.Star(), Height = Layout.Sizing.Star() };
        var outer = new Layout.Node.Stack([top, row]);

        var arranged = Layout.Engine.Arrange(outer, new Rect<float>(0, 0, 100, 100), new PixelCtx());

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
        var top = new Layout.Node.Leaf(new Layout.Content.Box(0, 0));
        var fill = new Layout.Node.Leaf(new Layout.Content.Box(0, 0));
        var dock = new Layout.Node.Dock([new Layout.DockChild(Layout.DockSide.Top, top, Layout.Sizing.Fixed(20))], fill);

        var arranged = Layout.Engine.Arrange(dock, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, top).ShouldBe(new Rect<float>(0, 0, 100, 20));
        RectOf(arranged, fill).ShouldBe(new Rect<float>(0, 20, 100, 80));
    }

    // --- grid ---

    [Fact]
    public void Grid_TwoByTwo_TilesEvenly()
    {
        var cells = new Layout.Node[]
        {
            new Layout.Node.Leaf(new Layout.Content.Box(0, 0)),
            new Layout.Node.Leaf(new Layout.Content.Box(0, 0)),
            new Layout.Node.Leaf(new Layout.Content.Box(0, 0)),
            new Layout.Node.Leaf(new Layout.Content.Box(0, 0)),
        };
        var grid = new Layout.Node.Grid(2, [.. cells]);

        var arranged = Layout.Engine.Arrange(grid, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        RectOf(arranged, cells[0]).ShouldBe(new Rect<float>(0, 0, 50, 50));
        RectOf(arranged, cells[1]).ShouldBe(new Rect<float>(50, 0, 50, 50));
        RectOf(arranged, cells[2]).ShouldBe(new Rect<float>(0, 50, 50, 50));
        RectOf(arranged, cells[3]).ShouldBe(new Rect<float>(50, 50, 50, 50));
    }

    // --- overlay (z-order) ---

    [Fact]
    public void Overlay_EmitsBaseBeforeTop_BothFillBounds()
    {
        var baseNode = new Layout.Node.Leaf(new Layout.Content.Box(0, 0));
        var topNode = new Layout.Node.Leaf(new Layout.Content.Box(0, 0));
        var overlay = new Layout.Node.Overlay(baseNode, topNode);

        var arranged = Layout.Engine.Arrange(overlay, new Rect<float>(0, 0, 100, 100), new PixelCtx());

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
        var stack = new Layout.Node.Stack([child]);

        var arranged = Layout.Engine.Arrange(stack, new Rect<float>(0, 0, 100, 100), new PixelCtx());

        ReferenceEquals(arranged[0].Node, stack).ShouldBeTrue();
    }

    // --- integral (cell) coordinates: exact tiling, deterministic remainder ---

    [Fact]
    public void CellStack_StarSplit_RemainderToLaterItems_SumsExactly()
    {
        var a = new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Height = Layout.Sizing.Star(), Width = Layout.Sizing.Star() };
        var b = new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Height = Layout.Sizing.Star(), Width = Layout.Sizing.Star() };
        var stack = new Layout.Node.Stack([a, b]);

        var arranged = Layout.Engine.Arrange(stack, new Rect<int>(0, 0, 10, 3), new CellCtx());

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
        var cells = new Layout.Node[]
        {
            new Layout.Node.Leaf(new Layout.Content.Box(0, 0)),
            new Layout.Node.Leaf(new Layout.Content.Box(0, 0)),
            new Layout.Node.Leaf(new Layout.Content.Box(0, 0)),
        };
        var grid = new Layout.Node.Grid(3, [.. cells]);

        var arranged = Layout.Engine.Arrange(grid, new Rect<int>(0, 0, 10, 4), new CellCtx());

        var w0 = RectOf(arranged, cells[0]).Width;
        var w1 = RectOf(arranged, cells[1]).Width;
        var w2 = RectOf(arranged, cells[2]).Width;
        (w0 + w1 + w2).ShouldBe(10); // exact tile of a 10-wide strip across 3 columns
        RectOf(arranged, cells[2]).Right.ShouldBe(10);
    }

    // --- Split ---

    private sealed record SplitHit(string Id) : HitResult;

    private static Layout.Node.Leaf Pane() => new(new Layout.Content.Box(0, 0));

    private static Layout.ArrangedNode<float>? DividerOf(ImmutableArray<Layout.ArrangedNode<float>> arranged, HitResult hit)
    {
        foreach (var a in arranged)
        {
            if (ReferenceEquals(a.Node.Hit, hit))
            {
                return a;
            }
        }

        return null;
    }

    [Fact]
    public void HorizontalSplit_PlacesPanesAroundDivider()
    {
        var first = Pane();
        var second = Pane();
        var hit = new SplitHit("FileList");
        var split = new Layout.Node.Split(first, second, Layout.Axis.Horizontal,
            FirstExtent: 30f, DividerThickness: 6f, DividerHit: hit);

        var arranged = Layout.Engine.Arrange(split, new Rect<float>(0, 0, 100, 40), new PixelCtx());

        RectOf(arranged, first).ShouldBe(new Rect<float>(0, 0, 30, 40));
        DividerOf(arranged, hit)!.Value.Bounds.ShouldBe(new Rect<float>(30, 0, 6, 40));
        RectOf(arranged, second).ShouldBe(new Rect<float>(36, 0, 64, 40)); // 100 - 30 - 6
    }

    [Fact]
    public void VerticalSplit_PlacesPanesAroundDivider()
    {
        var first = Pane();
        var second = Pane();
        var hit = new SplitHit("Top");
        var split = new Layout.Node.Split(first, second, Layout.Axis.Vertical,
            FirstExtent: 20f, DividerThickness: 4f, DividerHit: hit);

        var arranged = Layout.Engine.Arrange(split, new Rect<float>(0, 0, 50, 100), new PixelCtx());

        RectOf(arranged, first).ShouldBe(new Rect<float>(0, 0, 50, 20));
        DividerOf(arranged, hit)!.Value.Bounds.ShouldBe(new Rect<float>(0, 20, 50, 4));
        RectOf(arranged, second).ShouldBe(new Rect<float>(0, 24, 50, 76)); // 100 - 20 - 4
    }

    [Fact]
    public void Split_Divider_IsDrawEqualsHit_CarriesColorAndHit()
    {
        var hit = new SplitHit("FileList");
        var color = new RGBAColor32(0x40, 0x40, 0x48, 0xff);
        var split = new Layout.Node.Split(Pane(), Pane(), Layout.Axis.Horizontal,
            FirstExtent: 30f, DividerThickness: 6f, DividerHit: hit, DividerColor: color);

        var arranged = Layout.Engine.Arrange(split, new Rect<float>(0, 0, 100, 40), new PixelCtx());

        var divider = DividerOf(arranged, hit);
        divider.ShouldNotBeNull();
        // The drawn bar (Background) and the grab region (Hit) are the SAME arranged rect.
        divider.Value.Node.Background.ShouldBe(color);
        divider.Value.Bounds.ShouldBe(new Rect<float>(30, 0, 6, 40));
    }

    [Fact]
    public void Split_ClampsFirstExtentToBounds()
    {
        var first = Pane();
        var second = Pane();
        var hit = new SplitHit("FileList");
        // FirstExtent (200) exceeds the 100-wide bounds: clamp so the divider + second pane still fit.
        var split = new Layout.Node.Split(first, second, Layout.Axis.Horizontal,
            FirstExtent: 200f, DividerThickness: 6f, DividerHit: hit);

        var arranged = Layout.Engine.Arrange(split, new Rect<float>(0, 0, 100, 40), new PixelCtx());

        RectOf(arranged, first).Width.ShouldBe(94f);              // 100 - 6 divider
        RectOf(arranged, second).Width.ShouldBe(0f);              // nothing left
        DividerOf(arranged, hit)!.Value.Bounds.X.ShouldBe(94f);
    }

    [Fact]
    public void Split_NoDividerStyling_ReservesGapButEmitsNoDividerNode()
    {
        var first = Pane();
        var second = Pane();
        var split = new Layout.Node.Split(first, second, Layout.Axis.Horizontal,
            FirstExtent: 30f, DividerThickness: 6f); // no DividerHit, no DividerColor

        var arranged = Layout.Engine.Arrange(split, new Rect<float>(0, 0, 100, 40), new PixelCtx());

        // The 6-unit divider gap is still reserved (second starts at 36)...
        RectOf(arranged, second).ShouldBe(new Rect<float>(36, 0, 64, 40));
        // ...but with nothing to draw or hit, no divider node is emitted (just the 2 panes + the Split root).
        arranged.Length.ShouldBe(3);
    }
}
