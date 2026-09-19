using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A stack declared <see cref="Layout.Node.WithScroll"/> is a scroll container: its children are laid out
/// at their full extent and slid by the controller's offset, the painter clips its subtree to the node's
/// own rect, and what cannot be seen is not registered.
/// </summary>
/// <remarks>
/// <para>
/// Before this, <c>.WithScroll</c> bound the viewport and routed the wheel, and that was all: the rows
/// were arranged at the node's clamped height as if it were the room, the controller was never told how
/// much content there was (so <c>MaxOffset</c> stayed 0 and the wheel was declined), and nothing slid or
/// clipped. A declared menu longer than its <c>maxHeight</c> therefore overflowed rather than scrolled --
/// with every overflowing row still REGISTERED, taking the presses aimed at whatever it hung over.
/// </para>
/// <para>
/// Pinned against arranged rects and registered regions, since a picture of a clipped list looks right
/// whether or not the region under the clipped part is live.
/// </para>
/// </remarks>
public class LayoutScrollTests
{
    private const float RowH = 20f;
    private const int Rows = 10;
    private const float ViewportH = 50f;

    private static (DeclarationWidget Widget, ListScrollController Scroll) Fixture()
        => (new DeclarationWidget(new DeclarationStubRenderer(200, 200)), new ListScrollController { Mode = ScrollBarMode.None });

    /// <summary>Ten fixed rows, each a hit target, in a stack clamped to two and a half of them.</summary>
    private static Layout.Node List(ListScrollController scroll)
    {
        var rows = new Layout.Node[Rows];
        for (var i = 0; i < Rows; i++)
        {
            rows[i] = Layout.Builder.Spacer().RowH(RowH).Clickable(new HitResult.ListItemHit("rows", i), _ => { });
        }

        return Layout.Builder.VStack(rows).WStar().HClamp(0f, ViewportH).WithScroll(scroll);
    }

    /// <summary>The list in a column with a button below it, so what the rows would otherwise cover is a
    /// region of its own.</summary>
    private static Layout.Node Page(ListScrollController scroll)
        => Layout.Builder.VStack(
                List(scroll),
                Layout.Builder.Spacer().RowH(30f).Clickable(new HitResult.ButtonHit("below"), _ => { }))
            .Stretch();

    private static Layout.ArrangedNode<float>? Row(DeclarationWidget widget, int index)
        => widget.GetCapturedLayout().Cast<Layout.ArrangedNode<float>?>()
            .FirstOrDefault(n => n!.Value.Node.Hit is HitResult.ListItemHit { Index: var i } && i == index);

    private static ClickableRegion? Region(DeclarationWidget widget, int index)
        => widget.GetRegisteredRegions().Cast<ClickableRegion?>()
            .FirstOrDefault(r => r!.Value.Result is HitResult.ListItemHit { Index: var i } && i == index);

    [Fact]
    public void TheListIsMeasuredAtItsClampNotItsContent()
    {
        var (widget, scroll) = Fixture();
        widget.Render(Page(scroll), new RectF32(0, 0, 200, 200));

        var list = widget.GetCapturedLayout().Single(n => n.Node.Scroll is not null);
        list.Bounds.Height.ShouldBe(ViewportH);
    }

    [Fact]
    public void TheArrangeTellsTheControllerWhatItScrollsOver()
    {
        var (widget, scroll) = Fixture();
        widget.Render(Page(scroll), new RectF32(0, 0, 200, 200));

        scroll.TotalAtoms.ShouldBe((int)(Rows * RowH), "one surface unit per atom, so the total is the content extent");
        scroll.Viewport.ShouldBe(new RectF32(0, 0, 200, ViewportH));
        scroll.MaxOffset.ShouldBe(Rows * RowH - ViewportH);
    }

    [Fact]
    public void TheRowsAreLaidOutAtFullExtentAndSlidByTheOffset()
    {
        var (widget, scroll) = Fixture();
        widget.Render(Page(scroll), new RectF32(0, 0, 200, 200));
        Row(widget, 0)!.Value.Bounds.Y.ShouldBe(0f);
        Row(widget, 1)!.Value.Bounds.Y.ShouldBe(RowH, "not squeezed into the clamp");

        scroll.AtomOffset = 30;
        widget.Render(Page(scroll), new RectF32(0, 0, 200, 200));

        Row(widget, 1)!.Value.Bounds.Y.ShouldBe(RowH - 30f);
        Row(widget, 2)!.Value.Bounds.Y.ShouldBe(2 * RowH - 30f);
    }

    /// <summary>The one that matters: a row arranged past the viewport's edge is not a press target.</summary>
    [Fact]
    public void ARowOutsideTheViewportIsNeitherCapturedNorRegistered()
    {
        var (widget, scroll) = Fixture();
        widget.Render(Page(scroll), new RectF32(0, 0, 200, 200));

        Row(widget, 5).ShouldBeNull("arranged at y=100, wholly below a 50-unit viewport");
        Region(widget, 5).ShouldBeNull();

        // The button below the list is what a press there reaches, not a row hanging under the clip.
        widget.HitTest(10f, ViewportH + 10f).ShouldBeOfType<HitResult.ButtonHit>().Action.ShouldBe("below");
    }

    [Fact]
    public void ARowStraddlingTheEdgeRegistersOnlyThePartThatShows()
    {
        var (widget, scroll) = Fixture();
        widget.Render(Page(scroll), new RectF32(0, 0, 200, 200));

        // Row 2 spans 40..60 against a viewport ending at 50.
        var region = Region(widget, 2)!.Value;
        region.Y.ShouldBe(2 * RowH);
        region.Height.ShouldBe(ViewportH - 2 * RowH);
        widget.HitTest(10f, 45f).ShouldBeOfType<HitResult.ListItemHit>().Index.ShouldBe(2);
    }

    [Fact]
    public void AWheelOverTheListReachesItsController()
    {
        var (widget, scroll) = Fixture();
        widget.Render(Page(scroll), new RectF32(0, 0, 200, 200));

        var router = Routing.Over(widget);
        router.Handle(new InputEvent.Scroll(-1f, 10f, 10f, InputModifier.None)).ShouldBeTrue();

        scroll.Offset.ShouldBe(scroll.WheelStepAtoms, "a notch scrolls WheelStepAtoms surface units");
    }

    /// <summary>A list of uniform rows counts its offset in rows, so "scroll one" is one row and the
    /// keyboard's EnsureVisible means the row it names.</summary>
    [Fact]
    public void AListThatStatesItsRowHeightCountsInRows()
    {
        var (widget, scroll) = Fixture();
        scroll.AtomDesignUnits = RowH;
        widget.Render(Page(scroll), new RectF32(0, 0, 200, 200));

        scroll.TotalAtoms.ShouldBe(Rows);
        scroll.MaxOffset.ShouldBe(Rows - 2, "two whole rows fit a 50-unit viewport");

        scroll.AtomOffset = 1;
        widget.Render(Page(scroll), new RectF32(0, 0, 200, 200));
        Row(widget, 1)!.Value.Bounds.Y.ShouldBe(0f, "one atom is one row");
    }

    [Fact]
    public void AListThatFitsArrangesExactlyAsAnUnscrolledOne()
    {
        var (widget, scroll) = Fixture();
        var few = Layout.Builder.VStack(
                Layout.Builder.Spacer().RowH(RowH).Clickable(new HitResult.ListItemHit("rows", 0), _ => { }),
                Layout.Builder.Spacer().RowH(RowH).Clickable(new HitResult.ListItemHit("rows", 1), _ => { }))
            .WStar().HClamp(0f, ViewportH).WithScroll(scroll);
        widget.Render(few, new RectF32(0, 0, 200, 200));

        scroll.MaxOffset.ShouldBe(0f);
        Row(widget, 0)!.Value.Bounds.Y.ShouldBe(0f);
        Row(widget, 1)!.Value.Bounds.Y.ShouldBe(RowH);
        Region(widget, 1)!.Value.Height.ShouldBe(RowH);
    }
}
