using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="PopoverState"/> and <see cref="Layout.Builder.Popover"/>: the five obligations a hand-written
/// popover carried, discharged by the engine.
/// <para>
/// Those five were a flag, a backdrop that dismisses, a placement, an Escape route and a line in the host's
/// dispatcher, and forgetting any one of them was SILENT: the overlay still opened, still drew and still
/// took clicks. So each is pinned separately here, against registered regions and window state rather than
/// against a picture, because a picture cannot tell a discharged obligation from a forgotten one that
/// happens to look right this frame.
/// </para>
/// </summary>
public class LayoutPopoverTests
{
    private static readonly RectF32 Anchor = new(40f, 20f, 20f, 10f);

    private static (DeclarationWidget Widget, DeclarationStubRenderer Renderer) Fixture()
    {
        var renderer = new DeclarationStubRenderer(200, 200);
        return (new DeclarationWidget(renderer), renderer);
    }

    /// <summary>Content carrying a hit nothing else uses, so its arranged rect is readable from the regions.</summary>
    private static Layout.Node Content() =>
        Layout.Builder.Box(50f, 30f).Clickable(new HitResult.LinkHit("content"), _ => { });

    private static ClickableRegion ContentRegion(DeclarationWidget widget)
        => widget.GetRegisteredRegions().Single(r => r.Result is HitResult.LinkHit { Url: "content" });

    private static ClickableRegion? Backdrop(DeclarationWidget widget)
        => widget.GetRegisteredRegions().Cast<ClickableRegion?>()
            .FirstOrDefault(r => r!.Value.Result is HitResult.ChromeHit);

    // ---- the state ------------------------------------------------------------------

    [Fact]
    public void TheThreeVerbsAreTheWholeSurface()
    {
        var state = new PopoverState();
        state.IsOpen.ShouldBeFalse("a popover starts closed");

        state.Open();
        state.Open();
        state.IsOpen.ShouldBeTrue("Open is idempotent");

        state.Toggle();
        state.IsOpen.ShouldBeFalse();
        state.Toggle();
        state.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void ClosedFiresOnlyOnATransition()
    {
        var state = new PopoverState();
        var closed = 0;
        state.Closed += () => closed++;

        state.Close();
        closed.ShouldBe(0, "closing an already-closed popover is not a transition");

        state.Open();
        state.Close();
        closed.ShouldBe(1);

        state.Close();
        closed.ShouldBe(1);
    }

    [Fact]
    public void EscapeClosesItAndEveryOtherKeyIsDeclined()
    {
        var state = new PopoverState();
        state.Open();

        state.HandleKeyDown(InputKey.Enter).ShouldBeFalse("anything but Escape passes through");
        state.IsOpen.ShouldBeTrue();
        state.HandleKeyDown(InputKey.Escape).ShouldBeTrue();
        state.IsOpen.ShouldBeFalse();
    }

    /// <summary>
    /// The claimant slot is set by being painted and never cleared, so a popover left in it after closing
    /// must decline. Without this, opening a popover once would eat every Escape in the window afterwards.
    /// </summary>
    [Fact]
    public void AClosedPopoverDeclinesEscapeSoAStaleClaimIsHarmless()
        => new PopoverState().HandleKeyDown(InputKey.Escape).ShouldBeFalse();

    // ---- the declaration ------------------------------------------------------------

    [Fact]
    public void TheBuilderPutsTheStateOnTheOverlayRoot()
    {
        var state = new PopoverState();
        var node = Layout.Builder.Popover(Anchor, Content(), state);

        node.ShouldBeOfType<Layout.Node.Overlay>();
        node.Popover.ShouldBeSameAs(state);
    }

    [Fact]
    public void AClosedPopoverPaintsNothingAtAll()
    {
        var (widget, _) = Fixture();
        var state = new PopoverState();

        widget.Render(Layout.Builder.Popover(Anchor, Content(), state), new RectF32(0, 0, 200, 200));

        widget.GetRegisteredRegions().ShouldBeEmpty(
            "a closed popover skips its whole subtree, so neither the content nor the backdrop registers");
    }

    [Fact]
    public void AnOpenPopoverPaintsItsContentAndItsBackdrop()
    {
        var (widget, _) = Fixture();
        var state = new PopoverState();
        state.Open();

        widget.Render(Layout.Builder.Popover(Anchor, Content(), state), new RectF32(0, 0, 200, 200));

        ContentRegion(widget).Width.ShouldBe(50f, 0.01f);
        Backdrop(widget).ShouldNotBeNull("the backdrop is a real node, so it consumes the dismissing click");
    }

    [Fact]
    public void TheBackdropCloses()
    {
        var (widget, _) = Fixture();
        var state = new PopoverState();
        state.Open();

        widget.Render(Layout.Builder.Popover(Anchor, Content(), state), new RectF32(0, 0, 200, 200));
        Backdrop(widget)!.Value.OnClick!(default);

        state.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void PaintingAnOpenPopoverClaimsTheKeyboard()
    {
        var (widget, _) = Fixture();
        var state = new PopoverState();
        state.Open();

        widget.Render(Layout.Builder.Popover(Anchor, Content(), state), new RectF32(0, 0, 200, 200));

        widget.Ui.PaintedPopovers.ShouldBe([state], "the claim is made BY BEING PAINTED");
        Routing.Key(widget, InputKey.Escape).ShouldBeTrue();
        state.IsOpen.ShouldBeFalse();
    }

    /// <summary>
    /// A popover that CLOSED is not on the stack, with nothing having released anything. That is the whole
    /// of why 9.x's <c>IKeyboardClaimant</c> could be left stale and why implementers carried an obligation
    /// to decline once off screen: the single slot was never cleared, so the contract had to live in every
    /// implementer instead of in the mechanism.
    /// </summary>
    [Fact]
    public void AClosedPopoverIsSimplyNotOnTheStack()
    {
        var (widget, _) = Fixture();
        var state = new PopoverState();
        state.Open();

        var tree = Layout.Builder.Popover(Anchor, Content(), state);
        widget.Render(tree, new RectF32(0, 0, 200, 200));
        widget.Ui.PaintedPopovers.ShouldBe([state]);

        state.Close();
        widget.Render(tree, new RectF32(0, 0, 200, 200));
        widget.Ui.PaintedPopovers.ShouldBeEmpty();
        Routing.Key(widget, InputKey.Escape).ShouldBeFalse("nothing on screen owns Escape any more");
    }

    /// <summary>
    /// The nesting 9.x could not express. One claimant slot meant the last painter won and nothing
    /// restored, so a popover raised over another took the slot outright: dismissing the inner one left the
    /// outer one on screen with Escape reaching nothing, for the rest of the window's life. Topmost-first
    /// over a stack is that bug's absence.
    /// </summary>
    [Fact]
    public void AnInnerPopoverTakesTheKeysAndHandsThemBackWhenItCloses()
    {
        var (widget, _) = Fixture();
        var outer = new PopoverState();
        var inner = new PopoverState();
        outer.Open();
        inner.Open();

        // The inner one is painted INSIDE the outer one's content, so it is on top by paint order alone.
        var tree = Layout.Builder.Popover(
            Anchor,
            Layout.Builder.Popover(new RectF32(60f, 40f, 20f, 10f), Content(), inner),
            outer);

        widget.Render(tree, new RectF32(0, 0, 200, 200));
        widget.Ui.PaintedPopovers.ShouldBe([outer, inner], "paint order is z-order, bottom first");

        Routing.Key(widget, InputKey.Escape).ShouldBeTrue();
        inner.IsOpen.ShouldBeFalse();
        outer.IsOpen.ShouldBeTrue("the key reached the topmost popover and stopped there");

        // The next frame paints only the outer one, which is what hands the keyboard back.
        widget.Render(tree, new RectF32(0, 0, 200, 200));
        widget.Ui.PaintedPopovers.ShouldBe([outer]);

        Routing.Key(widget, InputKey.Escape).ShouldBeTrue();
        outer.IsOpen.ShouldBeFalse();
    }

    // ---- placement --------------------------------------------------------------------

    [Fact]
    public void TheContentSitsOutsideTheAnchorsNamedEdge()
    {
        var (widget, _) = Fixture();
        var state = new PopoverState();
        state.Open();

        widget.Render(Layout.Builder.Popover(Anchor, Content(), state), new RectF32(0, 0, 200, 200));

        var content = ContentRegion(widget);
        content.Y.ShouldBe(Anchor.Y + Anchor.Height, 0.01f, "Bottom means BELOW the anchor, not inside it");
        content.X.ShouldBe(Anchor.X, 0.01f, "and it lines up with the anchor's leading edge");
    }

    /// <summary>
    /// The case the hand-written placement got wrong twice: an anchor near an edge whose content is wider
    /// than the room left beside it. The clamp is against the PANE, not the anchor.
    /// </summary>
    [Fact]
    public void TheContentIsClampedIntoThePaneNotTheAnchor()
    {
        var (widget, _) = Fixture();
        var state = new PopoverState();
        state.Open();

        var anchor = new RectF32(180f, 20f, 15f, 10f);
        widget.Render(Layout.Builder.Popover(anchor, Content(), state), new RectF32(0, 0, 200, 200));

        var content = ContentRegion(widget);
        (content.X + content.Width).ShouldBeLessThanOrEqualTo(200.01f);
        content.X.ShouldBeGreaterThanOrEqualTo(-0.01f);
    }

    /// <summary>
    /// A side means the OPPOSITE thing without an anchor, and that older meaning has callers. Pinned so the
    /// anchor overload cannot quietly redefine the one that shipped in 9.1.
    /// </summary>
    [Fact]
    public void WithoutAnAnchorASidePinsInsideTheParentAsBefore()
    {
        var (widget, _) = Fixture();

        widget.Render(
            Layout.Builder.Anchored(Content(), Layout.DockSide.Bottom).Stretch(),
            new RectF32(0, 0, 200, 200));

        var content = ContentRegion(widget);
        (content.Y + content.Height).ShouldBe(200f, 0.01f, "pinned INSIDE the bottom edge");
    }

    // ---- the pointer ------------------------------------------------------------------

    [Fact]
    public void AnOpenPopoverOwnsThePointerForItsContentRect()
    {
        var (widget, _) = Fixture();
        var state = new PopoverState();
        state.Open();

        widget.Render(Layout.Builder.Popover(Anchor, Content(), state), new RectF32(0, 0, 200, 200));

        var owner = widget.Ui.PointerOwner.ShouldNotBeNull();
        var content = ContentRegion(widget);
        owner.X.ShouldBe(content.X, 0.01f);
        owner.Width.ShouldBe(content.Width, 0.01f,
            "the CONTENT rect, not the overlay root's, which is the whole pane and would confine nothing");
    }

    [Fact]
    public void AClosedPopoverOwnsNothing()
    {
        var (widget, _) = Fixture();

        widget.Render(Layout.Builder.Popover(Anchor, Content(), new PopoverState()), new RectF32(0, 0, 200, 200));

        widget.Ui.PointerOwner.ShouldBeNull();
    }

    /// <summary>
    /// Closing a popover releases the pointer on the NEXT paint, on a host that never touches
    /// <see cref="WindowUiSettings.FrameId"/>. That host is the default and the common case, and the first
    /// shipped version of this failed it.
    /// </summary>
    /// <remarks>
    /// The clear was keyed on the frame id CHANGING, which is right only for a host that advances it.
    /// <see cref="WindowUiSettings.FrameId"/> is documented as optional and starts at 0, so on a host that
    /// leaves it there the first paint cleared and every later one returned early: the popover kept the
    /// pointer for the life of the process and every hover in the window died the first time one closed. It
    /// took a consumer adopting popovers to find it, because every test here painted once.
    /// <para>
    /// So this paints REPEATEDLY and never moves the frame id, which is the shape that was missing.
    /// </para>
    /// </remarks>
    [Fact]
    public void AClosedPopoverStopsOwningThePointerOnAHostThatNeverMovesTheFrameId()
    {
        var (widget, _) = Fixture();
        var state = new PopoverState();
        state.Open();
        var tree = Layout.Builder.Popover(Anchor, Content(), state);

        widget.Render(tree, new RectF32(0, 0, 200, 200));
        widget.Ui.PointerOwner.ShouldNotBeNull("an open popover owns the pointer");
        widget.Ui.FrameId.ShouldBe(0, "and the host has not touched the frame id");

        // Painting again while it is still open keeps the claim, because the popover re-makes it.
        widget.Render(tree, new RectF32(0, 0, 200, 200));
        widget.Ui.PointerOwner.ShouldNotBeNull("still open, still owned");

        state.Close();
        widget.Render(tree, new RectF32(0, 0, 200, 200));

        widget.Ui.PointerOwner.ShouldBeNull("a closed popover paints nothing, so nothing re-claims the pointer");
    }

    /// <summary>
    /// The multi-widget case the per-frame clear exists for. Clearing in each widget's BeginFrame would let
    /// a second widget wipe the owner a first had just painted, so the confinement would hold only when the
    /// popover happened to belong to the last widget drawn.
    /// </summary>
    [Fact]
    public void ASecondWidgetBeginningTheSameFrameDoesNotWipeTheOwner()
    {
        var renderer = new DeclarationStubRenderer(200, 200);
        var withPopover = new DeclarationWidget(renderer);
        var plain = new DeclarationWidget(renderer);
        withPopover.ShareWith(plain);
        var state = new PopoverState();
        state.Open();

        withPopover.Render(Layout.Builder.Popover(Anchor, Content(), state), new RectF32(0, 0, 200, 200));
        var owned = withPopover.Ui.PointerOwner;

        plain.Render(Layout.Builder.Box(10f, 10f), new RectF32(0, 0, 200, 200));

        withPopover.Ui.PointerOwner.ShouldBe(owned, "same frame, so the owner survives the second BeginFrame");
    }
}
