using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Node.Wrap.FirstLineTrailing"/>: a measured node flush at the main end of the first
/// line, later lines using the full extent, and measure and arrange breaking lines identically.
/// </summary>
public class LayoutWrapFirstLineTrailingTests
{
    private static readonly RGBAColor32 Ink = new(0xf0, 0xf0, 0xf0, 0xff);

    /// <summary>Design units are surface units; text is 10 wide per char and 10 tall.</summary>
    private sealed class PxCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 10, 10);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits);
    }

    /// <summary>A cell surface: 8 x 16 design units per cell, text one cell per char and one row tall.</summary>
    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
    }

    private static Layout.Node Item(float w = 60f) => Layout.Builder.Box(w, 10f, Ink);

    private static Layout.Node Trailing(int chars) => Layout.Builder.Text(new string('t', chars));

    private static Layout.Node Items(int count, float w = 60f)
        => Layout.Builder.WrapH([.. Enumerable.Range(0, count).Select(_ => Item(w))]);

    private static Layout.ArrangedNode<int>[] Arrange(Layout.Node root, IMeasure ctx, int w, int h = 400)
        => ctx.Arrange(root, w, h);

    private interface IMeasure
    {
        Layout.ArrangedNode<int>[] Arrange(Layout.Node root, int w, int h);
        Layout.Size<int> Measure(Layout.Node root, int w, int h);
    }

    private sealed class M<TCtx>(TCtx ctx) : IMeasure where TCtx : Layout.IMeasureContext<int>
    {
        public Layout.ArrangedNode<int>[] Arrange(Layout.Node root, int w, int h)
            => [.. Layout.Engine.Arrange(root, new Rect<int>(0, 0, w, h), ctx)];

        public Layout.Size<int> Measure(Layout.Node root, int w, int h)
            => Layout.Engine.Measure(root, new Layout.Size<int>(w, h), ctx);
    }

    private static readonly IMeasure Px = new M<PxCtx>(new PxCtx());
    private static readonly IMeasure Cell = new M<CellCtx>(new CellCtx());

    private static bool IsTrailing(Layout.ArrangedNode<int> a)
        => a.Node is Layout.Node.Leaf { Content: Layout.Content.Text };

    /// <summary>The wrap's children (depth 1): items in order, and the trailing text node apart.</summary>
    private static (List<Rect<int>> Items, Rect<int>? Trailing) Split(Layout.ArrangedNode<int>[] a)
    {
        var items = new List<Rect<int>>();
        Rect<int>? trailing = null;
        foreach (var e in a.Where(e => e.Depth == 1))
        {
            if (IsTrailing(e))
            {
                trailing = e.Bounds;
            }
            else
            {
                items.Add(e.Bounds);
            }
        }

        return (items, trailing);
    }

    private static int LinesOf(IEnumerable<Rect<int>> items) => items.Select(r => r.Y).Distinct().Count();

    [Fact]
    public void FirstLineHoldsItemsInTheRoomLeftByTheTrailingNode()
    {
        var root = Items(12).WithGap(12f).WithFirstLineTrailing(Layout.Builder.Box(150f, 20f, Ink));
        var a = Px.Arrange(root, 667, 400);

        var items = a.Where(e => e.Depth == 1 && e.Node is Layout.Node.Leaf { Content: Layout.Content.Box { Width: 60f } }).Select(e => e.Bounds).ToList();
        var trailing = a.Single(e => e.Depth == 1 && e.Node is Layout.Node.Leaf { Content: Layout.Content.Box { Width: 150f } }).Bounds;

        // 667 - 150 - 12 = 505 holds seven 60-wide items with 12 gaps (492); an eighth would need 564.
        items.Count.ShouldBe(12);
        items.Take(7).ShouldAllBe(r => r.Y == 0);
        items.Skip(7).ShouldAllBe(r => r.Y == 20);
        items[7].X.ShouldBe(0);
        trailing.X.ShouldBe(517);
        trailing.Y.ShouldBe(0);
        trailing.Width.ShouldBe(150);
        // Line 2 uses the full extent: 5 items * 60 + 4 gaps = 348 fits all five.
        items.Skip(7).Count().ShouldBe(5);
    }

    [Fact]
    public void LaterLinesUseTheFullExtent()
    {
        // Line 1 holds seven (the trailing node takes the rest). Line 2 has the full 667 and holds nine:
        // 9 * 60 + 8 * 12 = 636 fits and a tenth would need 708.
        var root = Items(20).WithGap(12f).WithFirstLineTrailing(Trailing(15));
        var (items, _) = Split(Px.Arrange(root, 667, 400));
        items.Count(r => r.Y == 0).ShouldBe(7);
        items.Count(r => r.Y == items[7].Y).ShouldBe(9);
    }

    [Fact]
    public void CellContextMeasuresTheTrailingNodeItself()
    {
        // 667 / 8 = 83 cells. Items: Fixed 60 -> 8 cells, gap 12 -> 2 cells. Trailing: 19 chars = 19 cells,
        // measured by the context (a Fixed would have gone through ToSurfaceX).
        var root = Items(12).WithGap(12f).WithFirstLineTrailing(Trailing(19));
        var a = Cell.Arrange(root, 83, 40);
        var (items, trailing) = Split(a);

        // Room: 83 - 19 - 2 = 62 holds six (6*8 + 5*2 = 58); a seventh needs 68.
        items.Count(r => r.Y == 0).ShouldBe(6);
        trailing.ShouldNotBeNull();
        trailing!.Value.X.ShouldBe(83 - 19);
        trailing.Value.Y.ShouldBe(0);
        trailing.Value.Width.ShouldBe(19);
        // Line 2 holds the other six (full width 83).
        items.Skip(6).ShouldAllBe(r => r.Y == items[6].Y);
        items[6].Y.ShouldBeGreaterThan(0);
        items[6].X.ShouldBe(0);
    }

    [Fact]
    public void NarrowCaseLineOneHoldsOnlyTheTrailingNode()
    {
        // 100 wide, trailing 90: item room = 100 - 90 - 12 = -2 -> 0, so the first 60-wide item cannot sit beside it.
        var root = Items(3).WithGap(12f).WithFirstLineTrailing(Layout.Builder.Box(90f, 20f, Ink));
        var a = Px.Arrange(root, 100, 400);

        var items = a.Where(e => e.Depth == 1 && e.Node is Layout.Node.Leaf { Content: Layout.Content.Box { Width: 60f } }).Select(e => e.Bounds).ToList();
        var trailing = a.Single(e => e.Depth == 1 && e.Node is Layout.Node.Leaf { Content: Layout.Content.Box { Width: 90f } }).Bounds;

        trailing.X.ShouldBe(10);
        trailing.Y.ShouldBe(0);
        // Items start on line 2, below the trailing node's cross extent.
        items[0].Y.ShouldBe(20);
        items[0].X.ShouldBe(0);
        // 100 wide: one 60 item per line? 60 + 12 + 60 = 132 > 100, so one per line.
        items.Select(r => r.Y).ToArray().ShouldBe([20, 30, 40]);
    }

    [Fact]
    public void MeasureBreaksLinesExactlyAsArrangeDoes()
    {
        foreach (var (count, width, trailChars) in new[] { (12, 667, 15), (12, 400, 15), (5, 100, 9), (1, 300, 5), (20, 667, 15), (3, 60, 6) })
        {
            var root = Items(count).WithGap(12f).WithLineGap(4f).WithFirstLineTrailing(Trailing(trailChars));
            var a = Px.Arrange(root, width, 1000);
            var (items, trailing) = Split(a);
            var all = items.Append(trailing!.Value).ToList();
            var arrangedBottom = all.Max(r => r.Y + r.Height);

            var measured = Px.Measure(root, width, 1000);
            measured.Height.ShouldBe(arrangedBottom, $"count {count} width {width}");

            // Measured main extent: the longest line. Line 1 is its items, a gap and the trailing node.
            var trailW = trailing!.Value.Width;
            var firstItems = items.Where(r => r.Y == trailing.Value.Y).ToList();
            var expected = firstItems.Count == 0 ? trailW : firstItems.Max(r => r.X + r.Width) + 12 + trailW;
            foreach (var r in items.Where(r => r.Y != trailing.Value.Y))
            {
                expected = Math.Max(expected, r.X + r.Width);
            }

            measured.Width.ShouldBe(expected, $"main count {count} width {width}");
        }
    }

    [Fact]
    public void ZeroChildrenIsOneLineHoldingTheTrailingNode()
    {
        var root = Layout.Builder.WrapH([]).WithFirstLineTrailing(Trailing(15));
        var a = Px.Arrange(root, 300, 100);
        var (items, trailing) = Split(a);

        items.ShouldBeEmpty();
        trailing!.Value.X.ShouldBe(150);
        trailing.Value.Width.ShouldBe(150);
        trailing.Value.Y.ShouldBe(0);

        var size = Px.Measure(root, 300, 100);
        size.Width.ShouldBe(150);
        size.Height.ShouldBe(10);
    }

    [Fact]
    public void MaxLinesKeepsTheTrailingNodeAndMarksTruncation()
    {
        var root = Items(20).WithGap(12f).WithFirstLineTrailing(Trailing(15)).WithMaxLines(1);
        var a = Px.Arrange(root, 667, 400);
        var (items, trailing) = Split(a);

        items.Count.ShouldBe(7);
        trailing.ShouldNotBeNull();
        trailing!.Value.X.ShouldBe(517);
        a[0].Truncated.ShouldBeTrue();
        Px.Measure(root, 667, 400).Height.ShouldBe(10);
    }

    [Fact]
    public void MaxLinesOneWithNarrowCaseDropsEveryItem()
    {
        var root = Items(3).WithGap(12f).WithFirstLineTrailing(Layout.Builder.Box(90f, 20f, Ink)).WithMaxLines(1);
        var a = Px.Arrange(root, 100, 400);

        a.Count(e => e.Depth == 1).ShouldBe(1);
        a[0].Truncated.ShouldBeTrue();
        Px.Measure(root, 100, 400).Height.ShouldBe(20);
    }

    [Fact]
    public void MaxLinesTwoMatchesMeasureAndArrange()
    {
        var root = Items(30).WithGap(12f).WithLineGap(2f).WithFirstLineTrailing(Trailing(15)).WithMaxLines(2);
        var a = Px.Arrange(root, 667, 400);
        var (items, trailing) = Split(a);

        LinesOf(items).ShouldBe(2);
        var bottom = items.Append(trailing!.Value).Max(r => r.Y + r.Height);
        Px.Measure(root, 667, 400).Height.ShouldBe(bottom);
    }

    [Fact]
    public void StarCrossTrailingStretchesToTheLine()
    {
        var tall = Layout.Builder.Box(60f, 30f, Ink);
        var trailing = Layout.Builder.Box(50f, 10f, Ink).HStar();
        var root = Layout.Builder.WrapH(tall, tall).WithGap(0f).WithFirstLineTrailing(trailing);
        var a = Px.Arrange(root, 300, 400);

        a.Single(e => e.Depth == 1 && e.Node is Layout.Node.Leaf { Content: Layout.Content.Box { Width: 50f } }).Bounds.Height.ShouldBe(30);
    }

    [Fact]
    public void TrailingNodeWiderThanTheContainerStartsAtTheOrigin()
    {
        var root = Items(2).WithGap(12f).WithFirstLineTrailing(Layout.Builder.Box(150f, 20f, Ink));
        var a = Px.Arrange(root, 100, 400);
        var trailing = a.Single(e => e.Depth == 1 && e.Node is Layout.Node.Leaf { Content: Layout.Content.Box { Width: 150f } }).Bounds;

        trailing.X.ShouldBe(0);
        trailing.Width.ShouldBe(150);
    }

    [Fact]
    public void VerticalWrapFlowsDownColumnsAndPlacesTheTrailingNodeAtTheBottom()
    {
        var items = Enumerable.Range(0, 8).Select(_ => Layout.Builder.Box(10f, 60f, Ink)).ToArray();
        var root = Layout.Builder.WrapV(items).WithGap(12f).WithFirstLineTrailing(Layout.Builder.Box(20f, 150f, Ink));
        var a = Px.Arrange(root, 400, 667);

        var boxes = a.Where(e => e.Depth == 1 && e.Node is Layout.Node.Leaf { Content: Layout.Content.Box { Height: 60f } }).Select(e => e.Bounds).ToList();
        var trailing = a.Single(e => e.Depth == 1 && e.Node is Layout.Node.Leaf { Content: Layout.Content.Box { Height: 150f } }).Bounds;

        // 667 - 150 - 12 = 505 holds seven; the eighth starts column two, which has the full height.
        boxes.Count(r => r.X == 0).ShouldBe(7);
        boxes[7].X.ShouldBeGreaterThan(0);
        boxes[7].Y.ShouldBe(0);
        trailing.X.ShouldBe(0);
        trailing.Y.ShouldBe(517);
        Px.Measure(root, 400, 667).Height.ShouldBe(517 + 150);
    }

    [Fact]
    public void NoOpOnANonWrap()
    {
        var stack = Layout.Builder.VStack(Item());
        stack.WithFirstLineTrailing(Item()).ShouldBeSameAs(stack);
    }

    [Fact]
    public void WrapWithoutTrailingIsUnchanged()
    {
        var a = Px.Arrange(Items(12).WithGap(12f), 667, 400);
        var (items, trailing) = Split(a);
        trailing.ShouldBeNull();
        items.Count(r => r.Y == 0).ShouldBe(9);
    }
}
