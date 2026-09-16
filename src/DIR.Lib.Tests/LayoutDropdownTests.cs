using System.Collections.Immutable;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Builder.Dropdown"/>: the menu as a declared popover rather than a second popup
/// mechanism beside one.
/// <para>
/// The menu predates the popover -- <c>RenderDropdownMenu</c> already implemented the backdrop, the Escape
/// and the swallowed disabled row, as a ten-parameter method that had to be called last in the render pass.
/// So what is pinned here is not that a menu works, which it always did, but that it is now the SAME
/// mechanism: one <see cref="PopoverState"/>, one Escape rule, one backdrop, and a disabled row that still
/// swallows its press once the row is a node instead of a painted rect.
/// </para>
/// </summary>
public class LayoutDropdownTests
{
    private static readonly RectF32 Anchor = new(40f, 20f, 60f, 10f);

    private static (DeclarationWidget Widget, DeclarationStubRenderer Renderer) Fixture()
    {
        var renderer = new DeclarationStubRenderer(200, 200);
        return (new DeclarationWidget(renderer), renderer);
    }

    private static DropdownMenuState<string> Menu(params DropdownItem<string>[] items)
    {
        var state = new DropdownMenuState<string>();
        state.Open(Anchor.Position.X, Anchor.Position.Y, Anchor.Size.X, [.. items]);
        return state;
    }

    private static ImmutableArray<ClickableRegion> Rows(DeclarationWidget widget)
        => [.. widget.GetRegisteredRegions()
            .Where(r => r.Result is HitResult.ListItemHit { ListId: DropdownMenuState<string>.ListId })
            .OrderBy(r => ((HitResult.ListItemHit)r.Result!).Index)];

    private static ClickableRegion? Backdrop(DeclarationWidget widget)
        => widget.GetRegisteredRegions().Cast<ClickableRegion?>()
            .FirstOrDefault(r => r!.Value.Result is HitResult.ChromeHit);

    // ---- the state IS a popover ------------------------------------------------------

    [Fact]
    public void TheMenuIsOpenExactlyWhenItsPopoverIs()
    {
        var state = Menu(DropdownItem.Text("One"));

        state.IsOpen.ShouldBeTrue();
        state.Popover.IsOpen.ShouldBeTrue("one flag, not two kept in step by hand");

        state.Close();
        state.IsOpen.ShouldBeFalse();
        state.Popover.IsOpen.ShouldBeFalse();
    }

    /// <summary>
    /// The old <c>Close()</c> set a flag and cleared the highlight inline, because calling it was the only
    /// way to close. A declared menu closes through the backdrop or Escape without going near it, so the
    /// reset hangs off the transition instead -- and every spelling of "close" has to reach it.
    /// </summary>
    [Fact]
    public void EveryWayOfClosingClearsTheHighlight()
    {
        foreach (var close in new System.Action<DropdownMenuState<string>>[]
        {
            s => s.Close(),
            s => s.IsOpen = false,
            s => s.Popover.Close(),
            s => s.HandleKeyDown(InputKey.Escape),
        })
        {
            var state = Menu(DropdownItem.Text("One"), DropdownItem.Text("Two"));
            state.HighlightIndex = 1;

            close(state);

            state.HighlightIndex.ShouldBe(-1);
        }
    }

    [Fact]
    public void EscapeIsThePopoversOwnRuleRatherThanASecondCopyOfIt()
    {
        var state = Menu(DropdownItem.Text("One"));
        var closedRaised = 0;
        state.Popover.Closed += () => closedRaised++;

        state.HandleKeyDown(InputKey.Escape).ShouldBeTrue();

        state.IsOpen.ShouldBeFalse();
        closedRaised.ShouldBe(1, "closing runs the popover's transition, so a consumer observing it sees this");
    }

    // ---- the declaration -------------------------------------------------------------

    [Fact]
    public void TheBuilderHandsTheMenusOwnStateToThePopover()
    {
        var state = Menu(DropdownItem.Text("One"));

        var node = Layout.Builder.Dropdown(Anchor, state);

        node.Popover.ShouldBeSameAs(state.Popover,
            "the backdrop, the Escape claim and IsOpen must be the same object or they can disagree");
    }

    [Fact]
    public void AClosedMenuPaintsNothing()
    {
        var (widget, _) = Fixture();
        var state = Menu(DropdownItem.Text("One"));
        state.Close();

        widget.Render(Layout.Builder.Dropdown(Anchor, state), new RectF32(0, 0, 200, 200));

        widget.GetRegisteredRegions().ShouldBeEmpty();
    }

    [Fact]
    public void AnOpenMenuRegistersOneRowPerEntryAndABackdrop()
    {
        var (widget, _) = Fixture();
        var state = Menu(DropdownItem.Text("One"), DropdownItem.Text("Two"), DropdownItem.Text("Three"));

        widget.Render(Layout.Builder.Dropdown(Anchor, state), new RectF32(0, 0, 200, 200));

        Rows(widget).Length.ShouldBe(3);
        Backdrop(widget).ShouldNotBeNull();
    }

    [Fact]
    public void ClickingARowSelectsThatEntryAndCloses()
    {
        var (widget, _) = Fixture();
        DropdownItem<string>? chosen = null;
        var state = new DropdownMenuState<string>();
        state.Open(Anchor.Position.X, Anchor.Position.Y, Anchor.Size.X,
            [DropdownItem.Text("One"), DropdownItem.Text("Two")],
            onSelect: item => chosen = item);

        widget.Render(Layout.Builder.Dropdown(Anchor, state), new RectF32(0, 0, 200, 200));
        Rows(widget)[1].OnClick!(default);

        chosen!.Value.ShouldBe("Two", "the entry clicked IS the value, with no index mapping");
        state.IsOpen.ShouldBeFalse();
    }

    /// <summary>
    /// The one that is silent when it breaks. <c>.Disabled()</c> STRIPS a handler; it does not create a
    /// region. A disabled row declared without a hit registers nothing, so its press reaches the backdrop
    /// underneath and dismisses the menu -- the click appears to do nothing, which is the dead-end the
    /// disabled state exists to remove.
    /// </summary>
    [Fact]
    public void ADisabledRowSwallowsItsPressInsteadOfFallingThroughToTheBackdrop()
    {
        var (widget, _) = Fixture();
        var state = Menu(
            DropdownItem.Text("One"),
            DropdownItem<string>.Disabled("Two", "two", "No camera is connected"));

        widget.Render(Layout.Builder.Dropdown(Anchor, state), new RectF32(0, 0, 200, 200));

        var disabled = Rows(widget)[1];
        disabled.IsDisabled.ShouldBeTrue();
        disabled.OnClick.ShouldBeNull();
        disabled.Cursor.ShouldBe(CursorKind.NotAllowed);

        // The region exists, so the press stops here. Dispatching on it must leave the menu OPEN.
        widget.HitTestAndDispatch(disabled.X + 1f, disabled.Y + 1f)
            .ShouldBeOfType<HitResult.ListItemHit>();
        state.IsOpen.ShouldBeTrue("a refused click must not dismiss the menu");
    }

    [Fact]
    public void TheBackdropClosesTheMenu()
    {
        var (widget, _) = Fixture();
        var state = Menu(DropdownItem.Text("One"));

        widget.Render(Layout.Builder.Dropdown(Anchor, state), new RectF32(0, 0, 200, 200));
        Backdrop(widget)!.Value.OnClick!(default);

        state.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void TheMenuTakesItsWidthFromTheAnchorItWasPlacedAgainst()
    {
        var (widget, _) = Fixture();
        var state = Menu(DropdownItem.Text("One"));

        widget.Render(Layout.Builder.Dropdown(Anchor, state), new RectF32(0, 0, 200, 200));

        Rows(widget)[0].Width.ShouldBe(Anchor.Size.X, 0.01f);
    }

    [Fact]
    public void AnOverflowingMenuCarriesTheStatesScrollModel()
    {
        var state = Menu([.. Enumerable.Range(0, 40).Select(i => DropdownItem.Text($"Row {i}"))]);

        var node = Layout.Builder.Dropdown(Anchor, state, maxHeight: 50f);

        // The list node under the popover owns the scroll, and it is the state's own controller -- so a
        // wheel forwarded through HandleScrollInput and the painted bar move together.
        FindScroll(node).ShouldBeSameAs(state.Scroll);
    }

    private static ListScrollController? FindScroll(Layout.Node node) => node switch
    {
        _ when node.Scroll is { } s => s,
        Layout.Node.Anchored a => FindScroll(a.Child),
        Layout.Node.Overlay o => FindScroll(o.Top) ?? FindScroll(o.Base),
        _ => null,
    };
}
