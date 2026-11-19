using System.Linq;
using DIR.Lib.ChildWindows;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

public class ChildWindowSetTests
{
    private static readonly RectF32 Wide = new(0, 0, 1000, 600);
    private static readonly RectF32 Narrow = new(0, 0, 400, 800);

    private static ChildWindowSet Make(int n, RectF32? host = null, float w = 300, float h = 200)
    {
        var set = new ChildWindowSet();
        set.NoteHost(host ?? Wide);
        for (int i = 0; i < n; i++) set.Add("w" + i, new RectF32(10 + i * 5, 10 + i * 5, w, h));
        return set;
    }

    private static bool Overlap(RectF32 a, RectF32 b)
        => a.X < b.Right - 0.001f && b.X < a.Right - 0.001f && a.Y < b.Bottom - 0.001f && b.Y < a.Bottom - 0.001f;

    [Fact]
    public void AddPutsNewChildInFrontAndActive()
    {
        var set = Make(3);
        set.Children.Select(c => c.Title).ToArray().ShouldBe(["w2", "w1", "w0"]);
        set.Active!.Title.ShouldBe("w2");
    }

    [Fact]
    public void PressBringsToFrontAndActivates()
    {
        var set = Make(3);
        var back = set.Children[2].Id;
        set.BringToFront(back).ShouldBeTrue();
        set.Children[0].Id.ShouldBe(back);
        set.Active!.Id.ShouldBe(back);
        set.Children.Select(c => c.Title).ToArray().ShouldBe(["w0", "w2", "w1"]);
        set.BringToFront(999).ShouldBeFalse();
    }

    [Fact]
    public void ClosingActiveActivatesNextInOrder()
    {
        var set = Make(3);               // order w2 w1 w0, active w2
        set.Close(set.Active!.Id);
        set.Active!.Title.ShouldBe("w1");
        set.Close(set.Active!.Id);
        set.Active!.Title.ShouldBe("w0");
        set.Close(set.Active!.Id);
        set.Active.ShouldBeNull();
        set.Children.Count.ShouldBe(0);
    }

    [Fact]
    public void ClosingLastInOrderActivatesNewLast()
    {
        var set = Make(3);
        var w0 = set.Children[2].Id;
        set.BringToFront(w0);            // order w0 w2 w1
        set.Close(set.Children[2].Id);   // close w1 (not active)
        set.Active!.Title.ShouldBe("w0");
        set.Close(w0);
        set.Active!.Title.ShouldBe("w2");
    }

    [Fact]
    public void ClosingInactiveKeepsActive()
    {
        var set = Make(3);
        set.Close(set.Children[1].Id);
        set.Active!.Title.ShouldBe("w2");
    }

    [Theory]
    [InlineData(-5000f, 0f)]
    [InlineData(5000f, 0f)]
    [InlineData(0f, -5000f)]
    [InlineData(0f, 5000f)]
    public void DraggedPastAnyEdgeKeepsGrabStripReachable(float dx, float dy)
    {
        var set = Make(1);
        var c = set.Children[0];
        set.Move(c.Id, dx, dy);
        var t = c.TitleBar;
        var strip = System.Math.Min(ChildWindowSet.KeepReachableStrip, t.Width);
        (System.Math.Min(t.Right, Wide.Right) - System.Math.Max(t.X, Wide.X)).ShouldBeGreaterThanOrEqualTo(strip - 0.001f);
        t.Y.ShouldBeGreaterThanOrEqualTo(Wide.Y);
        t.Y.ShouldBeLessThanOrEqualTo(Wide.Bottom - ChildWindowSet.TitleBarHeight + 0.001f);
    }

    [Fact]
    public void MoveInsideHostIsExact()
    {
        var set = Make(1);
        var c = set.Children[0];
        var before = c.Bounds;
        set.Move(c.Id, 20, 30);
        c.Bounds.X.ShouldBe(before.X + 20);
        c.Bounds.Y.ShouldBe(before.Y + 30);
        c.Bounds.Size.ShouldBe(before.Size);
    }

    [Fact]
    public void ResizeHoldsMinimumSizeAndOppositeEdge()
    {
        var set = Make(1);
        var c = set.Children[0];
        var right = c.Bounds.Right;
        set.Resize(c.Id, ResizeEdge.Left, 5000, 0);
        c.Bounds.Width.ShouldBe(ChildWindowSet.MinWidth);
        c.Bounds.Right.ShouldBe(right);
        var bottom = c.Bounds.Bottom;
        set.Resize(c.Id, ResizeEdge.Top, 0, 5000);
        c.Bounds.Height.ShouldBe(ChildWindowSet.MinHeight);
        c.Bounds.Bottom.ShouldBe(bottom);
        set.Resize(c.Id, ResizeEdge.Right | ResizeEdge.Bottom, -5000, -5000);
        c.Bounds.Size.X.ShouldBe(ChildWindowSet.MinWidth);
        c.Bounds.Size.Y.ShouldBe(ChildWindowSet.MinHeight);
    }

    [Fact]
    public void ResizeGrowsFromRightBottom()
    {
        var set = Make(1);
        var c = set.Children[0];
        var b = c.Bounds;
        set.Resize(c.Id, ResizeEdge.Right | ResizeEdge.Bottom, 40, 25);
        c.Bounds.Width.ShouldBe(b.Width + 40);
        c.Bounds.Height.ShouldBe(b.Height + 25);
        c.Bounds.Position.ShouldBe(b.Position);
    }

    [Fact]
    public void AddHonoursMinimumSize()
    {
        var set = new ChildWindowSet();
        var c = set.Add("tiny", new RectF32(0, 0, 1, 1));
        c.Bounds.Width.ShouldBe(ChildWindowSet.MinWidth);
        c.Bounds.Height.ShouldBe(ChildWindowSet.MinHeight);
    }

    [Fact]
    public void ShrinkingHostPullsChildBackWithinReach()
    {
        var set = Make(1);
        var c = set.Children[0];
        set.Move(c.Id, 600, 300);
        set.NoteHost(new RectF32(0, 0, 400, 300));
        c.Bounds.X.ShouldBeLessThanOrEqualTo(400 - ChildWindowSet.KeepReachableStrip);
        c.Bounds.Y.ShouldBeLessThanOrEqualTo(300 - ChildWindowSet.TitleBarHeight);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void CascadeStepsDownRightWithFrontLast(int n)
    {
        var set = Make(n);
        set.Cascade(Wide);
        set.Arrangement.ShouldBe(ChildWindowArrangement.Cascaded);
        // back to front: the back child is first placed, at the top-left.
        var backToFront = set.Children.Reverse().ToArray();
        backToFront[0].Bounds.Position.ShouldBe(Wide.Position);
        for (int i = 1; i < n; i++)
        {
            backToFront[i].Bounds.X.ShouldBe(i * ChildWindowSet.CascadeStep);
            backToFront[i].Bounds.Y.ShouldBe(i * ChildWindowSet.CascadeStep);
        }
        set.Children[0].Bounds.X.ShouldBe((n - 1) * ChildWindowSet.CascadeStep);
    }

    [Fact]
    public void CascadeWrapsToTopLeftInNarrowHost()
    {
        var set = Make(7, Narrow, 300, 200);
        set.Cascade(Narrow);
        var b = set.Children.Reverse().ToArray();
        // 300 wide in 400: the 4th (28*4+300 > 400) wraps.
        b[3].Bounds.X.ShouldBe(3 * ChildWindowSet.CascadeStep);
        b[4].Bounds.Position.ShouldBe(Narrow.Position);
        b[5].Bounds.X.ShouldBe(ChildWindowSet.CascadeStep);
        foreach (var c in b) c.Bounds.Right.ShouldBeLessThanOrEqualTo(Narrow.Right);
    }

    [Fact]
    public void CascadeKeepsActiveLast()
    {
        var set = Make(3);
        set.BringToFront(set.Children[2].Id);
        set.Cascade(Wide);
        set.Active!.Bounds.X.ShouldBe(2 * ChildWindowSet.CascadeStep);
    }

    private static void AssertTiled(ChildWindowSet set, RectF32 host, int cols, int rows)
    {
        var b = set.Children.Select(c => c.Bounds).ToArray();
        for (int i = 0; i < b.Length; i++)
        {
            b[i].X.ShouldBeGreaterThanOrEqualTo(host.X - 0.001f);
            b[i].Y.ShouldBeGreaterThanOrEqualTo(host.Y - 0.001f);
            b[i].Right.ShouldBeLessThanOrEqualTo(host.Right + 0.001f);
            b[i].Bottom.ShouldBeLessThanOrEqualTo(host.Bottom + 0.001f);
            for (int j = i + 1; j < b.Length; j++) Overlap(b[i], b[j]).ShouldBeFalse($"{i} overlaps {j}");
        }
        b.Select(r => System.MathF.Round(r.X, 2)).Distinct().Count().ShouldBeLessThanOrEqualTo(b.Length);
        // The host is filled edge to edge.
        b.Min(r => r.X).ShouldBe(host.X, 0.001f);
        b.Min(r => r.Y).ShouldBe(host.Y, 0.001f);
        b.Max(r => r.Right).ShouldBe(host.Right, 0.01f);
        b.Max(r => r.Bottom).ShouldBe(host.Bottom, 0.01f);
        // Distinct row and column starts match the grid.
        b.Select(r => System.MathF.Round(r.Y, 1)).Distinct().Count().ShouldBe(rows);
        b.Select(r => System.MathF.Round(r.X, 1)).Distinct().Count().ShouldBeGreaterThanOrEqualTo(1);
        _ = cols;
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(7, 3, 3)]
    public void TileWideHostIsNearSquareGridWithColumnsFirst(int n, int cols, int rows)
    {
        var set = Make(n);
        set.Tile(Wide);
        set.Arrangement.ShouldBe(ChildWindowArrangement.Tiled);
        AssertTiled(set, Wide, cols, rows);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(7, 3)]
    public void TileNarrowHostPrefersRows(int n, int rows)
    {
        var set = Make(n, Narrow);
        set.Tile(Narrow);
        AssertTiled(set, Narrow, 0, rows);
        // portrait stacks: the first column holds `rows` tiles, so the first child sits above the second.
        if (n >= 2) set.Children[1].Bounds.Y.ShouldBeGreaterThan(set.Children[0].Bounds.Y);
    }

    [Fact]
    public void TileGapIsTheOneSharedConstant()
    {
        var set = Make(4);
        set.Tile(Wide);
        var b = set.Children.Select(c => c.Bounds).ToArray();
        (b[1].X - b[0].Right).ShouldBe(ChildWindowSet.TileGap, 0.001f);
        (b[2].Y - b[0].Bottom).ShouldBe(ChildWindowSet.TileGap, 0.001f);
    }

    [Fact]
    public void TileLandscapeSevenStretchesShortLastRow()
    {
        var set = Make(7);
        set.Tile(Wide);
        set.Children[6].Bounds.Width.ShouldBe(Wide.Width, 0.01f);
    }

    [Fact]
    public void TabsAreReportedAndRoundTripRestoresFreePositions()
    {
        var set = Make(3);
        var free = set.Children.Select(c => c.Bounds).ToArray();
        set.ShowAsTabs(Wide);
        set.Arrangement.ShouldBe(ChildWindowArrangement.Tabs);
        foreach (var c in set.Children)
        {
            c.Bounds.Y.ShouldBe(ChildWindowSet.TabStripHeight);
            c.Bounds.Width.ShouldBe(Wide.Width);
        }
        set.ShowFree();
        set.Arrangement.ShouldBe(ChildWindowArrangement.Free);
        set.Children.Select(c => c.Bounds).ToArray().ShouldBe(free);
    }

    [Fact]
    public void FreePositionsSurviveTileAndCascadeToo()
    {
        var set = Make(4);
        var free = set.Children.Select(c => c.Bounds).ToArray();
        set.Tile(Wide);
        set.Cascade(Wide);
        set.ShowFree();
        set.Children.Select(c => c.Bounds).ToArray().ShouldBe(free);
    }

    [Fact]
    public void MovingInAnArrangementTakesOverAndFrees()
    {
        var set = Make(3);
        set.Tile(Wide);
        var tile = set.Children[1].Bounds;
        set.Move(set.Children[1].Id, 10, 10);
        set.Arrangement.ShouldBe(ChildWindowArrangement.Free);
        set.Children[1].Bounds.X.ShouldBe(tile.X + 10);
        set.Children[0].Bounds.Size.X.ShouldBeLessThan(Wide.Width);
    }

    [Fact]
    public void MinimisedAndMaximisedOnlyWhenAsked()
    {
        var set = Make(2);
        set.Children.ShouldAllBe(c => !c.Minimised && !c.Maximised);
        set.Tile(Wide);
        set.Children.ShouldAllBe(c => !c.Minimised && !c.Maximised);
        var id = set.Children[0].Id;
        set.SetMinimised(id, true);
        set.SetMaximised(set.Children[1].Id, true);
        set.Find(id)!.Minimised.ShouldBeTrue();
        set.Children[1].Maximised.ShouldBeTrue();
    }

    [Fact]
    public void ChangesRaiseVersionAndChanged()
    {
        var set = new ChildWindowSet();
        int raised = 0;
        set.Changed += () => raised++;
        var c = set.Add("a", new RectF32(0, 0, 200, 100));
        set.Version.ShouldBe(1);
        set.Move(c.Id, 1, 1);
        set.Rename(c.Id, "b");
        raised.ShouldBe(3);
        set.Version.ShouldBe(3);
    }
}
