using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A cursor that can walk a list PAST its own viewport, and an Enter that can differ from a click.
///
/// <para>
/// These are the two things a real adoption sweep ran into. <c>MoveListCursor</c> steps only over rows
/// the last paint REGISTERED, which is the right rule for a list that paints all of itself and the wrong
/// one for a list that paints only its window: a twenty-row list showing five could not be walked past
/// the fifth row, so the arrows stopped dead at the bottom of the viewport and two lists had to keep
/// their hand-written Up/Down blocks. And <c>ActivateListCursor</c> was all-or-nothing on the click
/// handler, so a list whose Enter pins while its click selects kept Enter by hand too.
/// </para>
///
/// <para>
/// The count does NOT weaken the reachability rule where the paint is evidence: inside the span the
/// paint covered, an unregistered or disabled row is still skipped. Past that span there is no evidence
/// at all, and the count is what says the row is there.
/// </para>
/// </summary>
public class LayoutListCursorCountTests
{
    private const int RowH = 10;
    private const int Rows = 20;
    private const int Viewport = 5;

    private static readonly RGBAColor32 Rest = new(0x20, 0x40, 0x60, 0xff);

    private static (DeclarationWidget Widget, DeclarationStubRenderer Renderer) Fixture()
    {
        var renderer = new DeclarationStubRenderer(100, 100);
        return (new DeclarationWidget(renderer), renderer);
    }

    /// <summary>
    /// The virtualised list: twenty rows of which only <paramref name="first"/>..+5 are painted, exactly
    /// as a real one does -- off-screen rows register no clickable, so the cursor cannot see them.
    /// </summary>
    private static Layout.Node Window(int first, List<int>? chosen = null, List<int>? pinned = null)
    {
        var rows = new Layout.Node[Viewport];
        for (var i = 0; i < Viewport; i++)
        {
            var index = first + i;
            var row = Layout.Builder.Spacer().RowH(RowH).Bg(Rest)
                .Clickable(new HitResult.ListItemHit("targets", index), _ => chosen?.Add(index));
            rows[i] = pinned is null ? row : row.Activatable(_ => pinned.Add(index));
        }
        return Layout.Builder.VStack(rows).Stretch();
    }

    [Fact]
    public void ACountedCursorWalksTheWholeListThroughAFiveRowWindow()
    {
        // The acceptance case: twenty rows, five painted, and the arrows reach the twentieth. The
        // consumer scrolls on Moved, so each step paints the next window -- which is what a list
        // whose scroll controller is hung off the event does.
        var (widget, _) = Fixture();
        var first = 0;
        widget.ListCursor.Open("targets", 0, Rows);
        widget.ListCursor.Moved += index =>
        {
            if (index >= first + Viewport) first = index - Viewport + 1;
            else if (index < first) first = index;
        };

        widget.Render(Window(first), new RectF32(0, 0, 100, Viewport * RowH));

        for (var expected = 1; expected < Rows; expected++)
        {
            widget.MoveListCursor(1).ShouldBeTrue($"row {expected} is reachable");
            widget.ListCursor.Index.ShouldBe(expected);
            widget.Render(Window(first), new RectF32(0, 0, 100, Viewport * RowH));
        }

        first.ShouldBe(Rows - Viewport, "the window followed the cursor to the end");
        widget.MoveListCursor(1).ShouldBeFalse("the end of the list is an end, not a wrap");
        widget.ListCursor.Index.ShouldBe(Rows - 1);
    }

    [Fact]
    public void AndBackUpAgain()
    {
        var (widget, _) = Fixture();
        var first = Rows - Viewport;
        widget.ListCursor.Open("targets", Rows - 1, Rows);
        widget.ListCursor.Moved += index =>
        {
            if (index >= first + Viewport) first = index - Viewport + 1;
            else if (index < first) first = index;
        };

        widget.Render(Window(first), new RectF32(0, 0, 100, Viewport * RowH));

        for (var expected = Rows - 2; expected >= 0; expected--)
        {
            widget.MoveListCursor(-1).ShouldBeTrue($"row {expected} is reachable");
            widget.ListCursor.Index.ShouldBe(expected);
            widget.Render(Window(first), new RectF32(0, 0, 100, Viewport * RowH));
        }

        first.ShouldBe(0);
        widget.MoveListCursor(-1).ShouldBeFalse();
    }

    [Fact]
    public void MovedIsRaisedWithTheRowSoAConsumerCanBringItIntoView()
    {
        var (widget, _) = Fixture();
        var seen = new List<int>();
        widget.ListCursor.Open("targets", 0, Rows);
        widget.ListCursor.Moved += seen.Add;
        widget.Render(Window(0), new RectF32(0, 0, 100, Viewport * RowH));

        widget.MoveListCursor(1);   // within the window: the painted walk answers
        widget.MoveListCursor(3);   // past it: three steps, the last two unpainted

        seen.ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void AStepThatGoesNowhereRaisesNothing()
    {
        var (widget, _) = Fixture();
        var seen = new List<int>();
        widget.ListCursor.Open("targets", Rows - 1, Rows);
        widget.ListCursor.Moved += seen.Add;
        widget.Render(Window(Rows - Viewport), new RectF32(0, 0, 100, Viewport * RowH));

        widget.MoveListCursor(1).ShouldBeFalse();

        seen.ShouldBeEmpty();
    }

    [Fact]
    public void WithoutACountTheCursorStillStopsAtTheEdgeOfWhatWasPainted()
    {
        // Unchanged behaviour for every consumer that does not state a count -- which is all of them.
        var (widget, _) = Fixture();
        widget.ListCursor.Open("targets", 4);
        widget.Render(Window(0), new RectF32(0, 0, 100, Viewport * RowH));

        widget.MoveListCursor(1).ShouldBeFalse();
        widget.ListCursor.Index.ShouldBe(4);
        widget.ListCursor.RowCount.ShouldBeNull();
    }

    [Fact]
    public void ACountedListNothingPaintedIsStillUnnavigable()
    {
        // A card that is not on screen must not move its cursor over rows nobody can see, count or no
        // count: the regions are last frame's, and last frame did not draw this list.
        var (widget, _) = Fixture();
        widget.ListCursor.Open("targets", 0, Rows);
        widget.Render(Layout.Builder.Spacer().Stretch().Bg(Rest), new RectF32(0, 0, 100, 50));

        widget.MoveListCursor(1).ShouldBeFalse();
        widget.ActivateListCursor().ShouldBeFalse();
    }

    [Fact]
    public void ACountDoesNotReachOverTheTopOfAnUnreachableROWInsideTheWindow()
    {
        // The painted span stays the evidence where there is any: row 1 is not clickable, so it is not
        // a row -- and the count must not turn it back into one.
        var (widget, _) = Fixture();
        var rows = new Layout.Node[3];
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            rows[i] = i == 1
                ? Layout.Builder.Spacer().RowH(RowH).Bg(Rest)
                : Layout.Builder.Spacer().RowH(RowH).Bg(Rest)
                    .Clickable(new HitResult.ListItemHit("targets", index));
        }
        widget.ListCursor.Open("targets", 0, 3);
        widget.Render(Layout.Builder.VStack(rows).Stretch(), new RectF32(0, 0, 100, 30));

        widget.MoveListCursor(1).ShouldBeTrue();
        widget.ListCursor.Index.ShouldBe(2, "straight past the gap, not onto it");
    }

    [Fact]
    public void ReopeningWithoutACountForgetsTheOldOne()
    {
        var (widget, _) = Fixture();
        widget.ListCursor.Open("targets", 0, Rows);
        widget.ListCursor.RowCount.ShouldBe(Rows);

        widget.ListCursor.Open("targets", 0);
        widget.ListCursor.RowCount.ShouldBeNull();

        widget.ListCursor.Open("targets", 0, Rows);
        widget.ListCursor.Close();
        widget.ListCursor.RowCount.ShouldBeNull();
    }

    [Fact]
    public void EnterPrefersTheRowsOwnActivateHandlerOverItsClick()
    {
        // "Enter pins, a click selects", which is what a planner row means and what a list could not say
        // before: ActivateListCursor was all-or-nothing on the click handler.
        var (widget, _) = Fixture();
        var chosen = new List<int>();
        var pinned = new List<int>();
        widget.ListCursor.Open("targets", 2, Rows);
        widget.Render(Window(0, chosen, pinned), new RectF32(0, 0, 100, Viewport * RowH));

        widget.ActivateListCursor().ShouldBeTrue();

        pinned.ShouldBe([2]);
        chosen.ShouldBeEmpty("the click handler is the fallback, not the act");
    }

    [Fact]
    public void ARowWithNoActivateHandlerStillFallsBackToItsClick()
    {
        var (widget, _) = Fixture();
        var chosen = new List<int>();
        widget.ListCursor.Open("targets", 2, Rows);
        widget.Render(Window(0, chosen), new RectF32(0, 0, 100, Viewport * RowH));

        widget.ActivateListCursor().ShouldBeTrue();

        chosen.ShouldBe([2]);
    }
}
