using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <c>.WithScroll(controller)</c>: the list states its viewport ONCE, by being arranged.
///
/// <para>
/// The shape it removes is the wheel handler's opening line -- "is the pointer over my rect" -- written
/// once per scrollable, seventeen times across eleven files in one consumer, each of them a second
/// derivation of a rect the arrange pass already held. The engine knows which arranged rect is under the
/// pointer; nothing was asking it.
/// </para>
/// </summary>
public class LayoutScrollTargetTests
{
    private static (DeclarationWidget Widget, DeclarationStubRenderer Renderer) Fixture()
    {
        var renderer = new DeclarationStubRenderer(100, 100);
        return (new DeclarationWidget(renderer), renderer);
    }

    [Fact]
    public void ThePaintBindsTheArrangedRectAsTheViewport()
    {
        // The row sits under a header, so its top edge is nowhere near the card's -- which is exactly
        // the offset a consumer restating the rect beside its paint gets wrong.
        var (widget, _) = Fixture();
        var scroll = new ListScrollController();
        var tree = Layout.Builder.VStack(
                Layout.Builder.Spacer().RowH(10f),
                Layout.Builder.Spacer().RowH(30f).WithScroll(scroll))
            .Stretch();

        widget.Render(tree, new RectF32(0, 0, 100, 40));

        scroll.Viewport.ShouldBe(new RectF32(0, 10, 100, 30));
    }

    [Fact]
    public void WhatTheListCONTAINSStaysTheConsumersToState()
    {
        // BindViewport is deliberately only half of SetExtent: the engine cannot know how many rows
        // there are or how tall one is, so a consumer keeps stating them and the binding must not
        // trample that.
        var (widget, _) = Fixture();
        var scroll = new ListScrollController();
        scroll.SetExtent(new RectF32(0, 0, 10, 10), atomExtentPx: 10f, totalAtoms: 20, DesignScale.One);

        widget.Render(
            Layout.Builder.Spacer().Stretch().WithScroll(scroll),
            new RectF32(0, 0, 100, 50));

        scroll.TotalAtoms.ShouldBe(20);
        scroll.Viewport.ShouldBe(new RectF32(0, 0, 100, 50));
        scroll.VisibleAtoms.ShouldBe(5, "the new viewport shows five of the rows the consumer stated");
    }

    [Fact]
    public void TheWheelGoesToTheINNERMOSTScrollableUnderThePointer()
    {
        // A list inside a panel that also scrolls takes the wheel, as it should. Innermost = registered
        // last, the same top-most rule the hit test already uses.
        var (widget, _) = Fixture();
        var outer = new ListScrollController();
        var inner = new ListScrollController();
        var tree = Layout.Builder.VStack(
                Layout.Builder.Spacer().RowH(20f),
                Layout.Builder.Spacer().RowH(20f).WithScroll(inner))
            .Stretch().WithScroll(outer);

        widget.Render(tree, new RectF32(0, 0, 100, 40));

        widget.ScrollTargetAt(50f, 30f).ShouldBeSameAs(inner);
        widget.ScrollTargetAt(50f, 10f).ShouldBeSameAs(outer, "where only the outer one is under the pointer");
    }

    [Fact]
    public void NothingScrollableUnderThePointerIsNoTargetRatherThanTheNearestOne()
    {
        var (widget, _) = Fixture();
        var scroll = new ListScrollController();
        var tree = Layout.Builder.VStack(
                Layout.Builder.Spacer().RowH(20f),
                Layout.Builder.Spacer().RowH(20f).WithScroll(scroll))
            .Stretch();

        widget.Render(tree, new RectF32(0, 0, 100, 40));

        widget.ScrollTargetAt(50f, 5f).ShouldBeNull();
    }

    [Fact]
    public void AScrollableNodeWithNoHitStillRegistersARegionAndStaysInertToPresses()
    {
        var (widget, _) = Fixture();
        var scroll = new ListScrollController();

        widget.Render(Layout.Builder.Spacer().Stretch().WithScroll(scroll), new RectF32(0, 0, 100, 100));

        var region = widget.GetRegisteredRegions().Single();
        region.Result.ShouldBeOfType<HitResult.ChromeHit>();
        region.Scroll.ShouldBeSameAs(scroll);
        region.OnClick.ShouldBeNull();
    }

    [Fact]
    public void BindViewportLeavesTheTailPinAloneBecauseItDoesNotKnowTheRowCount()
    {
        // A Bottom-anchored list would otherwise latch to the end of a list of NOTHING on the frame
        // before its rows were stated, and never follow the tail again.
        var scroll = new ListScrollController { Anchor = ScrollAnchor.Bottom };
        scroll.BindViewport(new RectF32(0, 0, 100, 50));
        scroll.SetExtent(new RectF32(0, 0, 100, 50), atomExtentPx: 10f, totalAtoms: 20, DesignScale.One);

        scroll.Offset.ShouldBe(scroll.MaxOffset, "the tail pin established on the call that knew the count");
    }
}
