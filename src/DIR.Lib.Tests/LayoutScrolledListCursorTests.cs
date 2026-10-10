using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The keyboard in a list declared inside a scroll container: the arrows reach every row, and the row
/// they land on is brought into view, with nothing for the host to wire.
/// </summary>
/// <remarks>
/// <para>
/// The painter registers only what its viewport shows, so a row clipped out of view registered nothing,
/// and <c>MoveListCursor</c>, which walks the registered rows, stopped dead at the last one the viewport
/// showed. That row was often half under the viewport's edge, and with nothing scrolling it into view it
/// stayed that way: a long list read as one whose last row was cut off for good. A host could hang
/// <see cref="ListScrollController.EnsureVisible"/> off <see cref="ListCursor.Moved"/> and state a row
/// count, but only by keeping a second copy of what the tree already declares, and an index is not a
/// position in a list of rows of mixed height or under a header.
/// </para>
/// <para>
/// A clipped row is still a row the paint MET, declared, which is why it can be stepped onto without a
/// count; it still answers no press, which is what keeping it unregistered is for.
/// </para>
/// </remarks>
public class LayoutScrolledListCursorTests
{
    private const float RowH = 20f;
    private const int Rows = 10;
    private const float ViewportH = 50f;
    private const float Pad = 5f;

    private static (DeclarationWidget Widget, ListScrollController Scroll) Fixture()
        => (new DeclarationWidget(new DeclarationStubRenderer(200, 200)), new ListScrollController { Mode = ScrollBarMode.None });

    /// <summary>A header and ten rows, in a padded card clamped to two and a half rows: the header puts
    /// every row off the atom grid, which is what an index-based reveal gets wrong.</summary>
    private static Layout.Node Card(ListScrollController scroll, int disabledRow = -1)
    {
        var children = new Layout.Node[Rows + 1];
        children[0] = Layout.Builder.Spacer().RowH(13f);
        for (var i = 0; i < Rows; i++)
        {
            var row = Layout.Builder.Spacer().RowH(RowH).Clickable(new HitResult.ListItemHit("rows", i), _ => { });
            children[i + 1] = i == disabledRow ? row.Disabled("not now") : row;
        }

        // In a column with something below it: the root takes the whole bounds whatever its clamp says.
        return Layout.Builder.VStack(
                Layout.Builder.VStack(children).WStar().HClamp(0f, ViewportH).Pad(Pad).WithScroll(scroll),
                Layout.Builder.Spacer().RowH(30f))
            .Stretch();
    }

    private static readonly RectF32 Bounds = new(0, 0, 200, 200);

    private static ClickableRegion? Region(DeclarationWidget widget, int index)
        => widget.GetRegisteredRegions().Cast<ClickableRegion?>()
            .FirstOrDefault(r => r!.Value.Result is HitResult.ListItemHit { Index: var i } && i == index);

    /// <summary>The row is on screen whole, inside the padding: every pixel of it answers a press.</summary>
    private static void ShouldShowWhole(DeclarationWidget widget, int index)
    {
        var region = Region(widget, index);
        region.ShouldNotBeNull($"row {index} is on screen");
        region.Value.Height.ShouldBe(RowH, $"all of row {index} is on screen");
        region.Value.Y.ShouldBeGreaterThanOrEqualTo(Pad, $"row {index} clears the top padding");
        (region.Value.Y + region.Value.Height).ShouldBeLessThanOrEqualTo(ViewportH - Pad, $"row {index} clears the bottom padding");
    }

    [Fact]
    public void TheArrowsWalkToTheEndOfTheList_AndEachRowComesIntoView()
    {
        var (widget, scroll) = Fixture();
        widget.ListCursor.Open("rows", 0);
        widget.Render(Card(scroll), Bounds);

        for (var expected = 1; expected < Rows; expected++)
        {
            widget.MoveListCursor(1).ShouldBeTrue($"row {expected} is reachable");
            widget.ListCursor.Index.ShouldBe(expected);
            widget.Render(Card(scroll), Bounds);
            ShouldShowWhole(widget, expected);
        }

        widget.MoveListCursor(1).ShouldBeFalse("the end of the list is an end");
        scroll.Offset.ShouldBe(scroll.MaxOffset, "and the list is scrolled to it");
    }

    [Fact]
    public void AndBackToTheTop()
    {
        var (widget, scroll) = Fixture();
        widget.ListCursor.Open("rows", Rows - 1);
        widget.Render(Card(scroll), Bounds);
        scroll.AtomOffset = 10_000;
        widget.Render(Card(scroll), Bounds);

        for (var expected = Rows - 2; expected >= 0; expected--)
        {
            widget.MoveListCursor(-1).ShouldBeTrue($"row {expected} is reachable");
            widget.Render(Card(scroll), Bounds);
            ShouldShowWhole(widget, expected);
        }

        widget.MoveListCursor(-1).ShouldBeFalse();
    }

    [Fact]
    public void AStepToARowAlreadyInViewDoesNotScroll()
    {
        // Minimal, so the wheel and the keyboard can share a list without the arrows snapping it about.
        var (widget, scroll) = Fixture();
        widget.ListCursor.Open("rows", 1);
        widget.Render(Card(scroll), Bounds);

        widget.MoveListCursor(-1).ShouldBeTrue();

        scroll.Offset.ShouldBe(0f, "row 0 was already on screen whole");
    }

    [Fact]
    public void ADisabledRowOutOfViewIsSteppedOverLikeOneInView()
    {
        var (widget, scroll) = Fixture();
        widget.ListCursor.Open("rows", 2);
        widget.Render(Card(scroll, disabledRow: 3), Bounds);
        Region(widget, 3).ShouldBeNull("row 3 starts below the viewport");

        widget.MoveListCursor(1).ShouldBeTrue();

        widget.ListCursor.Index.ShouldBe(4, "straight past the row Enter would refuse");
    }

    [Fact]
    public void AClippedRowStillAnswersNoPress()
    {
        // Reachable by the keyboard is not the same as on screen: the region rule is untouched.
        var (widget, scroll) = Fixture();
        widget.ListCursor.Open("rows", 0);
        widget.Render(Card(scroll), Bounds);

        Region(widget, Rows - 1).ShouldBeNull();
        widget.HitTest(10f, ViewportH + 5f).ShouldBeNull();
    }
}
