using System.Collections.Generic;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="ListCursor.Step"/> with no widget in sight -- which is the point of it being here.
///
/// <para>
/// The walk used to live on <see cref="PixelWidgetBase{TSurface}"/>, reading the regions its tracker had
/// registered, so a cell surface wanting the same arrow behaviour had to write it again: Console.Lib's
/// <c>ScrollableList</c> was that second implementation, and "the same list behaves differently under the
/// arrows depending which surface it is on" is a difference nothing would have reported.
/// </para>
/// <para>
/// Reduced to a list of <see cref="ListCursor.PaintedRow"/>, the rule is one rule. What stays per-surface
/// is only where that list comes from -- registered regions on pixels, the drawn window on cells -- which
/// is genuinely different knowledge. The widget-driven cases are still pinned through the widget in
/// <see cref="LayoutListCursorTests"/> and <see cref="LayoutListCursorCountTests"/>; these pin the rule
/// itself.
/// </para>
/// </summary>
public class ListCursorStepTests
{
    private static List<ListCursor.PaintedRow> Rows(params int[] indices)
    {
        var rows = new List<ListCursor.PaintedRow>();
        foreach (var i in indices)
        {
            rows.Add(new ListCursor.PaintedRow(i));
        }

        return rows;
    }

    private static ListCursor Open(int index = -1, int? count = null)
    {
        var cursor = new ListCursor();
        if (count is { } n)
        {
            cursor.Open("rows", index, n);
        }
        else
        {
            cursor.Open("rows", index);
        }

        return cursor;
    }

    [Fact]
    public void AListTheReaderHasNotMovedInLandsOnTheFirstRowGoingDownAndTheLastGoingUp()
    {
        var down = Open();
        down.Step(1, Rows(0, 1, 2)).ShouldBeTrue();
        down.Index.ShouldBe(0);

        var up = Open();
        up.Step(-1, Rows(0, 1, 2)).ShouldBeTrue();
        up.Index.ShouldBe(2);
    }

    [Fact]
    public void AStepOffTheEndLeavesTheCursorWhereItWas()
    {
        var cursor = Open(2);

        cursor.Step(1, Rows(0, 1, 2)).ShouldBeFalse("stopping at the end reads as an end");
        cursor.Index.ShouldBe(2, "and moving to nothing would read as a broken control");
    }

    /// <summary>
    /// Nearest painted row in the direction of travel, NOT index plus one: the indices are the list's and
    /// need not be dense. A list that leaves out the rows a reader cannot act on has gaps exactly where a
    /// step must not stop.
    /// </summary>
    [Fact]
    public void AStepGoesToTheNearestPaintedRowAcrossAGap()
    {
        var cursor = Open(0);

        cursor.Step(1, Rows(0, 4, 9)).ShouldBeTrue();
        cursor.Index.ShouldBe(4);
    }

    [Fact]
    public void ADisabledRowIsSteppedOverRatherThanLandedOn()
    {
        // Registered, so its press is swallowed -- and skipped, because a highlight parked where Enter
        // will refuse reads as a stuck key.
        var painted = new List<ListCursor.PaintedRow>
        {
            new(0),
            new(1, IsDisabled: true),
            new(2),
        };

        var cursor = Open(0);
        cursor.Step(1, painted).ShouldBeTrue();
        cursor.Index.ShouldBe(2);
    }

    [Fact]
    public void AMultiRowStepMovesAsFarAsItCanAndSaysItMoved()
    {
        var cursor = Open(0);

        cursor.Step(5, Rows(0, 1, 2)).ShouldBeTrue();
        cursor.Index.ShouldBe(2, "a page step past the end lands on the last row rather than refusing");
    }

    /// <summary>
    /// The virtualised case: a list showing five of twenty steps onto row five, which the paint never
    /// drew. Without the count the arrows stop dead at the bottom of the window and the list can only be
    /// scrolled with the mouse.
    /// </summary>
    [Fact]
    public void ACountedListStepsPastTheRowsThePaintDrew()
    {
        var cursor = Open(4, count: 20);

        cursor.Step(1, Rows(0, 1, 2, 3, 4)).ShouldBeTrue();
        cursor.Index.ShouldBe(5);
    }

    [Fact]
    public void ACountedListStillStopsAtItsLastRow()
    {
        var cursor = Open(19, count: 20);

        cursor.Step(1, Rows(15, 16, 17, 18, 19)).ShouldBeFalse();
        cursor.Index.ShouldBe(19);
    }

    /// <summary>
    /// A card that is not on screen must not move its cursor over rows nobody can see -- counted or not.
    /// The count says the rows EXIST; the paint is what says they are reachable.
    /// </summary>
    [Fact]
    public void AListNothingOfWhichWasPaintedIsUnnavigableEvenWhenCounted()
    {
        var cursor = Open(0, count: 20);

        cursor.Step(1, Rows()).ShouldBeFalse();
        cursor.Index.ShouldBe(0);
    }

    [Fact]
    public void AStepRaisesMovedSoAConsumerCanBringTheRowIntoView()
    {
        var seen = new List<int>();
        var cursor = Open(count: 20);
        cursor.Moved += seen.Add;

        cursor.Step(2, Rows(0, 1, 2, 3, 4));

        seen.ShouldBe([0, 1], "once per row actually stepped onto, which is what EnsureVisible hangs off");
    }

    [Fact]
    public void ACursorInNoListDoesNotMove()
    {
        var cursor = new ListCursor();

        cursor.Step(1, Rows(0, 1, 2)).ShouldBeFalse();
        cursor.Index.ShouldBe(-1);
    }
}
