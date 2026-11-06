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
        // A viewport of nine rows: a notch is capped at a third of the viewport, and three rows are
        // exactly that. (The 2.5-row ViewportH the other tests use is smaller than a notch of three rows.)
        var rows = new Layout.Node[30];
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = Layout.Builder.Spacer().RowH(RowH);
        }

        var list = Layout.Builder.VStack(
                Layout.Builder.VStack(rows).WStar().HClamp(0f, 9 * RowH).WithScroll(scroll),
                Layout.Builder.Spacer().RowH(30f))
            .Stretch();
        widget.Render(list, new RectF32(0, 0, 200, 300));

        var router = Routing.Over(widget);
        router.Handle(new InputEvent.Scroll(-1f, 10f, 10f, InputModifier.None)).ShouldBeTrue();

        scroll.Offset.ShouldBe(scroll.WheelStepAtoms * RowH,
            "a notch scrolls WheelStepAtoms ROWS: on a list counted in surface units it was three pixels");
    }

    /// <summary>
    /// A notch over a stack with one child three viewports tall moves a readable line, never more than a
    /// third of the viewport: the mean pitch of ONE child is the whole content, so a notch jumped from the
    /// top to the bottom (#146).
    /// </summary>
    [Fact]
    public void ANotchOverOneTallChildNeverMovesMoreThanAThirdOfTheViewport()
    {
        var (widget, scroll) = Fixture();
        var tall = Layout.Builder.VStack(Layout.Builder.Spacer().RowH(3 * ViewportH))
            .WStar().HClamp(0f, ViewportH).WithScroll(scroll);
        var page = Layout.Builder.VStack(tall, Layout.Builder.Spacer().RowH(30f)).Stretch();
        widget.Render(page, new RectF32(0, 0, 200, 200));
        scroll.MaxOffset.ShouldBe(2 * ViewportH);

        var router = Routing.Over(widget);
        var notches = 0;
        while (scroll.Offset < scroll.MaxOffset && notches < 100)
        {
            var before = scroll.Offset;
            router.Handle(new InputEvent.Scroll(-1f, 10f, 10f, InputModifier.None)).ShouldBeTrue();
            notches++;

            var step = scroll.Offset - before;
            step.ShouldBeGreaterThan(0f);
            step.ShouldBeLessThanOrEqualTo(ViewportH / 3f + 0.001f);
        }

        scroll.Offset.ShouldBe(scroll.MaxOffset);
        notches.ShouldBeGreaterThan(3, "crossing three viewports takes several notches, not one");
    }

    /// <summary>The cap leaves ordinary rows alone: with a viewport well over three rows, a notch is still
    /// WheelStepAtoms rows.</summary>
    [Fact]
    public void AListOfOrdinaryRowsInATallViewportStillMovesThreeRowsANotch()
    {
        const float row = 36f;
        const float viewport = 360f;
        var (widget, scroll) = Fixture();
        var rows = new Layout.Node[30];
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = Layout.Builder.Spacer().RowH(row);
        }

        var list = Layout.Builder.VStack(
                Layout.Builder.VStack(rows).WStar().HClamp(0f, viewport).WithScroll(scroll),
                Layout.Builder.Spacer().RowH(30f))
            .Stretch();
        widget.Render(list, new RectF32(0, 0, 200, 500));

        Routing.Over(widget).Handle(new InputEvent.Scroll(-1f, 10f, 10f, InputModifier.None)).ShouldBeTrue();

        scroll.Offset.ShouldBe(scroll.WheelStepAtoms * row);
    }

    /// <summary>
    /// A padded list scrolls far enough to show its last row whole, clear of the padding: the viewport is
    /// the node's own rect and the padding is part of what it scrolls over, as CSS has it.
    /// </summary>
    /// <remarks>
    /// The arrange used to state the INNER rect as the viewport while the painter bound the arranged one,
    /// so the paint's clamp held the offset a whole padding short of the end, and with the rows slid a
    /// padding below the clip, the last half-row of a padded card was out of reach whatever scrolled it.
    /// </remarks>
    [Fact]
    public void APaddedListScrollsFarEnoughToShowItsLastRowWhole()
    {
        const float pad = 5f;
        var (widget, scroll) = Fixture();
        var rows = new Layout.Node[Rows];
        for (var i = 0; i < Rows; i++)
        {
            rows[i] = Layout.Builder.Spacer().RowH(RowH).Clickable(new HitResult.ListItemHit("rows", i), _ => { });
        }
        // In a column, as Page has it: the root takes the whole bounds whatever its clamp says.
        var list = Layout.Builder.VStack(
                Layout.Builder.VStack(rows).WStar().HClamp(0f, ViewportH).Pad(pad).WithScroll(scroll),
                Layout.Builder.Spacer().RowH(30f))
            .Stretch();

        widget.Render(list, new RectF32(0, 0, 200, 200));
        scroll.AtomOffset = 10_000;
        widget.Render(list, new RectF32(0, 0, 200, 200));

        var last = Row(widget, Rows - 1)!.Value.Bounds;
        (last.Y + last.Height).ShouldBe(ViewportH - pad, "the last row ends where the padding begins");
        Region(widget, Rows - 1)!.Value.Height.ShouldBe(RowH, "and all of it answers a press");
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

    /// <summary>
    /// The bar the ENGINE draws for a declared list takes the controller's colours. It passes none of its
    /// own, so the controller is the only place a host can state them: a night palette holding blue at zero
    /// had a blue-grey bar on every list.
    /// </summary>
    [Fact]
    public void TheEnginePaintsTheBarInTheControllersColours()
    {
        var track = new RGBAColor32(0x30, 0x00, 0x00, 0xff);
        var thumb = new RGBAColor32(0x80, 0x10, 0x00, 0xff);
        var renderer = new DeclarationStubRenderer(200, 200);
        var widget = new DeclarationWidget(renderer);
        var scroll = new ListScrollController { TrackColor = track, ThumbColor = thumb };

        widget.Render(Page(scroll), new RectF32(0, 0, 200, 200));

        // The bar is the right 6 units of the 50-unit viewport. The thumb is 20 tall at offset 0 (the
        // minimum: 50 of 200 would be 12.5) and inset a unit either side of the column.
        PixelAt(renderer, 197, 10).ShouldBe(thumb);
        PixelAt(renderer, 197, 40).ShouldBe(track);
        PixelAt(renderer, 194, 10).ShouldBe(track);
    }

    [Fact]
    public void TheEnginePaintsTheDefaultColoursWhenTheControllerStatesNone()
    {
        var renderer = new DeclarationStubRenderer(200, 200);
        var widget = new DeclarationWidget(renderer);

        widget.Render(Page(new ListScrollController()), new RectF32(0, 0, 200, 200));

        PixelAt(renderer, 197, 10).ShouldBe(ListScrollController.DefaultThumbColor);
        PixelAt(renderer, 197, 40).ShouldBe(ListScrollController.DefaultTrackColor);
    }

    private static RGBAColor32 PixelAt(DeclarationStubRenderer r, int x, int y)
    {
        var img = r.Surface;
        var i = (y * img.Width + x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    private const float LeafW = 30f;
    private const float ViewportW = 200f;

    /// <summary>Rows of fixed-width leaves pushed to the row's end, in a scrolled stack that overflows.</summary>
    private static Layout.Node EndAligned(ListScrollController scroll, int rows)
    {
        var leaves = new Layout.Node[rows];
        for (var i = 0; i < rows; i++)
        {
            leaves[i] = Layout.Builder.Spacer().WFixed(LeafW).HFixed(RowH)
                .Clickable(new HitResult.ListItemHit("rows", i), _ => { });
        }

        return Layout.Builder.VStack(leaves).Align(Layout.CrossAlign.End).WStar().HClamp(0f, ViewportH).WithScroll(scroll);
    }

    /// <summary>A scrolled stack as a child, so its HClamp is the viewport (a root takes the whole rect).</summary>
    private static Layout.Node Wrap(Layout.Node list)
        => Layout.Builder.VStack(list, Layout.Builder.Spacer().RowH(30f)).Stretch();

    [Fact]
    public void AnEndAlignedLeafEndsLeftOfTheScrollbarColumn()
    {
        var widget = new DeclarationWidget(new DeclarationStubRenderer(200, 200));
        var scroll = new ListScrollController { Mode = ScrollBarMode.Interactive };
        widget.Render(Wrap(EndAligned(scroll, Rows)), new RectF32(0, 0, ViewportW, 200));

        scroll.MaxOffset.ShouldBeGreaterThan(0f);
        var row = Row(widget, 0)!.Value.Bounds;
        (row.X + row.Width).ShouldBeLessThanOrEqualTo(ViewportW - ListScrollController.ScrollBarBaseWidthPx);
    }

    [Fact]
    public void AStarRowStopsAtTheScrollbarColumn()
    {
        var widget = new DeclarationWidget(new DeclarationStubRenderer(200, 200));
        var scroll = new ListScrollController { Mode = ScrollBarMode.Interactive };
        widget.Render(Page(scroll), new RectF32(0, 0, ViewportW, 200));

        var row = Row(widget, 0)!.Value.Bounds;
        (row.X + row.Width).ShouldBe(ViewportW - ListScrollController.ScrollBarBaseWidthPx);
    }

    /// <summary>Ten 20-unit words (the stub measures half the font size per character) in a wrap: exactly
    /// one line at the full 200 units, two in the 194 left beside the 6-unit bar.</summary>
    private static Layout.Node WordRow(int index)
    {
        var words = new Layout.Node[10];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = Layout.Builder.Text("abcd", 10f);
        }

        return Layout.Builder.WrapH(words).Clickable(new HitResult.ListItemHit("rows", index), _ => { });
    }

    private static Layout.Node WordRows(ListScrollController scroll, int rows)
        => Wrap(Layout.Builder.VStack(Enumerable.Range(0, rows).Select(WordRow).ToArray())
            .WStar().HClamp(0f, ViewportH).WithScroll(scroll));

    [Fact]
    public void AWrappingRowBesideTheBarKeepsItsLinesInsideItsOwnHeight()
    {
        // 11.44 reserved the bar column at arrange only: the row was measured at the full width (one
        // line), laid out beside the bar (two lines), and its second line was drawn over the next row.
        var widget = new DeclarationWidget(new DeclarationStubRenderer(200, 200));
        var scroll = new ListScrollController { Mode = ScrollBarMode.Interactive };
        widget.Render(WordRows(scroll, 8), new RectF32(0, 0, ViewportW, 200));

        scroll.MaxOffset.ShouldBeGreaterThan(0f);
        var row0 = Row(widget, 0)!.Value.Bounds;
        var row1 = Row(widget, 1)!.Value.Bounds;
        var words = widget.GetCapturedLayout().Cast<Layout.ArrangedNode<float>>()
            .Where(n => n.Node is Layout.Node.Leaf { Content: Layout.Content.Text })
            .Take(10).Select(n => n.Bounds).ToList();

        words.Max(w => w.Y + w.Height).ShouldBeLessThanOrEqualTo(row0.Y + row0.Height);
        row1.Y.ShouldBeGreaterThanOrEqualTo(row0.Y + row0.Height);
        (row0.X + row0.Width).ShouldBeLessThanOrEqualTo(ViewportW - ListScrollController.ScrollBarBaseWidthPx);
    }

    [Fact]
    public void TheScrollExtentCountsTheLinesTheBarsReserveAdds()
    {
        // The same rows at the full width (no bar: ScrollBarMode.None) take one line each; beside the bar
        // they take two, and the controller must scroll over the taller total, or the last row is cut off.
        var (w1, none) = Fixture();
        w1.Render(WordRows(none, 8), new RectF32(0, 0, ViewportW, 200));
        var w2 = new DeclarationWidget(new DeclarationStubRenderer(200, 200));
        var barred = new ListScrollController { Mode = ScrollBarMode.Interactive };
        w2.Render(WordRows(barred, 8), new RectF32(0, 0, ViewportW, 200));

        var oneLine = Row(w1, 0)!.Value.Bounds.Height;
        Row(w2, 0)!.Value.Bounds.Height.ShouldBe(oneLine * 2f);
        barred.MaxOffset.ShouldBeGreaterThan(none.MaxOffset);
    }

    [Fact]
    public void AControllerOnTheOtherAxisReservesNothingFromTheStack()
    {
        // A Horizontal controller on a VStack would take its cross extent from the wrong axis; the
        // reserve is skipped, so the rows keep the full width.
        var widget = new DeclarationWidget(new DeclarationStubRenderer(200, 200));
        var scroll = new ListScrollController { Mode = ScrollBarMode.Interactive, Axis = ScrollAxis.Horizontal };
        widget.Render(Wrap(EndAligned(scroll, Rows)), new RectF32(0, 0, ViewportW, 200));

        var row = Row(widget, 0)!.Value.Bounds;
        (row.X + row.Width).ShouldBe(ViewportW);
    }

    [Fact]
    public void AStackWithoutABarKeepsTheFullWidth()
    {
        // Fits: no bar shows.
        var widget = new DeclarationWidget(new DeclarationStubRenderer(200, 200));
        var scroll = new ListScrollController { Mode = ScrollBarMode.Interactive };
        widget.Render(Wrap(EndAligned(scroll, 2)), new RectF32(0, 0, ViewportW, 200));
        scroll.MaxOffset.ShouldBe(0f);
        var fit = Row(widget, 0)!.Value.Bounds;
        (fit.X + fit.Width).ShouldBe(ViewportW);

        // Overflows but ScrollBarMode.None: no bar either.
        var (w2, none) = Fixture();
        w2.Render(Wrap(EndAligned(none, Rows)), new RectF32(0, 0, ViewportW, 200));
        none.MaxOffset.ShouldBeGreaterThan(0f);
        var over = Row(w2, 0)!.Value.Bounds;
        (over.X + over.Width).ShouldBe(ViewportW);
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
