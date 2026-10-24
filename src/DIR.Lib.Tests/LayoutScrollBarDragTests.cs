using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The interactive scrollbar of a <see cref="Layout.Node.WithScroll"/> stack takes thumb drags and track clicks
/// through <see cref="InputRouter"/> alone (11.36).
/// <para>
/// <c>ListScrollController.HandleInput</c> always did the thumb and the paging, but for a layout-declared scroll the
/// router forwarded only the wheel, so a press on the bar, the moves and the release never reached it and the bar was
/// drawn and dead. The painter now registers the bar's column as a press region over the rows, and a press there
/// arms a capture that feeds the controller.
/// </para>
/// </summary>
public class LayoutScrollBarDragTests
{
    private const float RowH = 20f;
    private const float ViewportH = 100f;
    private const float PageW = 200f;

    private static Layout.Node List(ListScrollController scroll, int rows, List<int> clicked)
    {
        var nodes = new Layout.Node[rows];
        for (var i = 0; i < rows; i++)
        {
            var index = i;
            nodes[i] = Layout.Builder.Spacer().RowH(RowH).Clickable(new HitResult.ListItemHit("rows", i), _ => clicked.Add(index));
        }

        return Layout.Builder.VStack(
                Layout.Builder.VStack(nodes).WStar().HClamp(0f, ViewportH).WithScroll(scroll),
                Layout.Builder.Spacer().RowH(30f))
            .Stretch();
    }

    private static (DeclarationWidget Widget, ListScrollController Scroll, InputRouter Router, List<int> Clicked) Fixture(
        ScrollBarMode mode, int rows = 10)
    {
        var widget = new DeclarationWidget(new DeclarationStubRenderer(200, 300));
        var scroll = new ListScrollController { Mode = mode };
        var clicked = new List<int>();
        widget.Render(List(scroll, rows, clicked), new RectF32(0, 0, PageW, 300));
        return (widget, scroll, Routing.Over(widget), clicked);
    }

    private static int BarRegions(DeclarationWidget widget)
        => widget.GetRegisteredRegions().Count(r => r.Result is HitResult.ChromeHit && r.OnPress is not null);

    private static bool Down(InputRouter router, float x, float y) => router.Handle(new InputEvent.MouseDown(x, y));

    private static bool Move(InputRouter router, float x, float y)
        => router.Handle(new InputEvent.MouseMove(x, y, MouseButton.Left));

    private static bool Up(InputRouter router, float x, float y) => router.Handle(new InputEvent.MouseUp(x, y));

    [Fact]
    public void APressOnTheThumbThenAMoveScrollsProportionally()
    {
        var (_, scroll, router, _) = Fixture(ScrollBarMode.Interactive);
        var bar = scroll.InteractiveScrollBarRect.ShouldNotBeNull();
        var (thumbStart, thumbExtent) = scroll.DebugThumbGeometry();
        var x = bar.X + bar.Width / 2f;

        Down(router, x, thumbStart + 5f).ShouldBeTrue();
        scroll.Offset.ShouldBe(0, "the press alone grips the thumb and moves nothing");

        // Content 200 over a viewport of 100: the thumb is half the track, so the usable track is 50 for an offset of 100.
        Move(router, x, thumbStart + 5f + 25f).ShouldBeTrue();

        scroll.Offset.ShouldBe(50);
        thumbExtent.ShouldBe(50f);
    }

    [Fact]
    public void TheDragContinuesPastTheBarsEdgeAndTheReleaseEndsIt()
    {
        var (_, scroll, router, _) = Fixture(ScrollBarMode.Interactive);
        var bar = scroll.InteractiveScrollBarRect.ShouldNotBeNull();
        var x = bar.X + bar.Width / 2f;

        Down(router, x, 5f);
        // Far left of the bar and well below the viewport: still the drag's, and clamped to the end.
        Move(router, 5f, 250f).ShouldBeTrue();
        scroll.Offset.ShouldBe((int)scroll.MaxOffset);

        Move(router, 5f, 30f);
        scroll.Offset.ShouldBeLessThan((int)scroll.MaxOffset);

        Up(router, 5f, 30f).ShouldBeTrue();
        var released = scroll.Offset;
        Move(router, 5f, 200f);
        scroll.Offset.ShouldBe(released, "a move after the release belongs to nobody");
    }

    [Fact]
    public void ATrackClickPagesByAViewportTowardTheClick()
    {
        var (_, scroll, router, clicked) = Fixture(ScrollBarMode.Interactive, rows: 15);
        var bar = scroll.InteractiveScrollBarRect.ShouldNotBeNull();
        var x = bar.X + bar.Width / 2f;
        var (thumbStart, thumbExtent) = scroll.DebugThumbGeometry();

        Down(router, x, thumbStart + thumbExtent + 10f).ShouldBeTrue();
        Up(router, x, thumbStart + thumbExtent + 10f);
        scroll.Offset.ShouldBe((int)ViewportH, "below the thumb pages one viewport toward the end");

        var (thumbAfter, _) = scroll.DebugThumbGeometry();
        Down(router, x, thumbAfter - 5f).ShouldBeTrue();
        Up(router, x, thumbAfter - 5f);
        scroll.Offset.ShouldBe(0, "above the thumb pages back");
        clicked.ShouldBeEmpty("a track click is not a click on the row under it");
    }

    [Fact]
    public void APressOnARowBesideTheBarStillClicksTheRow()
    {
        var (_, scroll, router, clicked) = Fixture(ScrollBarMode.Interactive);

        Down(router, 10f, 50f).ShouldBeTrue();
        Up(router, 10f, 50f);

        clicked.ShouldBe([2]);
        scroll.Offset.ShouldBe(0);
    }

    [Theory]
    [InlineData(ScrollBarMode.Decorative)]
    [InlineData(ScrollBarMode.None)]
    public void ANonInteractiveBarRegistersNothingSoTheRowUnderItKeepsThePress(ScrollBarMode mode)
    {
        var (widget, scroll, router, clicked) = Fixture(mode);

        scroll.InteractiveScrollBarRect.ShouldBeNull();
        BarRegions(widget).ShouldBe(0);

        Down(router, PageW - 2f, 50f);
        Up(router, PageW - 2f, 50f);

        clicked.ShouldBe([2]);
    }

    [Fact]
    public void AnInteractiveBarRegistersOneRegionOverTheRows()
    {
        var (widget, scroll, _, _) = Fixture(ScrollBarMode.Interactive);
        var bar = scroll.InteractiveScrollBarRect.ShouldNotBeNull();

        BarRegions(widget).ShouldBe(1);
        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.ChromeHit && r.OnPress is not null);
        (region.X, region.Y, region.Width, region.Height).ShouldBe((bar.X, bar.Y, bar.Width, bar.Height));
        widget.HitTest(bar.X + 1f, 10f).ShouldBeOfType<HitResult.ChromeHit>();
    }

    [Fact]
    public void AListThatFitsRegistersNoBar()
    {
        var (widget, scroll, _, _) = Fixture(ScrollBarMode.Interactive, rows: 3);

        scroll.InteractiveScrollBarRect.ShouldBeNull();
        BarRegions(widget).ShouldBe(0);
    }
}
