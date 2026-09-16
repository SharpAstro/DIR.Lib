using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A press that knows WHERE it landed, and the capture it can return to own the drag that follows.
///
/// <para>
/// <c>OnClick</c> is <c>Action&lt;InputModifier&gt;</c> and carries no position, so a slider could not
/// arm its own drag from the node it was painted on. What every one of them did instead was cache the
/// track rect in a field beside the paint and arm a flag from the host's dispatcher -- six such caches
/// in one consumer, each written every frame and read on the next press, each free to disagree with the
/// rect the engine arranged.
/// </para>
/// </summary>
public class LayoutPressTests
{
    private static readonly RGBAColor32 Fill = new(0x20, 0x40, 0x60, 0xff);

    private static (DeclarationWidget Widget, DeclarationStubRenderer Renderer) Fixture()
    {
        var renderer = new DeclarationStubRenderer(100, 100);
        return (new DeclarationWidget(renderer), renderer);
    }

    [Fact]
    public void APressHandlerIsBoundToTheRectTheEngineArranged()
    {
        // The whole point: the track is where the paint put it, not where a cached rect says it was.
        // The row sits under a header, so its top edge is nowhere near the card's.
        var (widget, _) = Fixture();
        var tree = Layout.Builder.VStack(
                Layout.Builder.Spacer().RowH(10f).Bg(Fill),
                Layout.Builder.Spacer().RowH(10f).Bg(Fill)
                    .Pressable(new HitResult.ButtonHit("track"), _ => null))
            .Stretch();

        widget.Render(tree, new RectF32(0, 0, 100, 20));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.ButtonHit { Action: "track" });
        region.Y.ShouldBe(10f);
        region.Height.ShouldBe(10f);
        region.OnPress.ShouldNotBeNull();
    }

    [Fact]
    public void ThePressCarriesItsPositionButtonModifiersAndClickCount()
    {
        var (widget, _) = Fixture();
        PointerPress seen = default;
        var tree = Layout.Builder.Spacer().Stretch()
            .Pressable(new HitResult.ButtonHit("track"), press => { seen = press; return null; });

        widget.Render(tree, new RectF32(0, 0, 100, 100));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.ButtonHit);
        region.OnPress!(new PointerPress(37f, 12f, MouseButton.Right, InputModifier.Shift, 2));

        seen.ShouldBe(new PointerPress(37f, 12f, MouseButton.Right, InputModifier.Shift, 2));
    }

    [Fact]
    public void AClaimedGestureTakesEveryMoveAndTheRelease()
    {
        // The three branches a drag flag spread over a host's dispatcher, as one object.
        var (widget, _) = Fixture();
        var moves = new List<float>();
        float? released = null;
        var tree = Layout.Builder.Spacer().Stretch().Pressable(
            new HitResult.ButtonHit("track"),
            press => new DragCapture(m => moves.Add(m.X), m => released = m.X));

        widget.Render(tree, new RectF32(0, 0, 100, 100));
        var capture = widget.GetRegisteredRegions()
            .Single(r => r.Result is HitResult.ButtonHit).OnPress!(
                new PointerPress(10f, 10f, MouseButton.Left, InputModifier.None, 1));

        capture.ShouldNotBeNull();
        capture.Move(new PointerMove(20f, 10f, MouseButton.Left, InputModifier.None));
        capture.Move(new PointerMove(30f, 10f, MouseButton.Left, InputModifier.None));
        capture.Release(new PointerMove(40f, 10f, MouseButton.Left, InputModifier.None));

        moves.ShouldBe([20f, 30f]);
        released.ShouldBe(40f);
    }

    [Fact]
    public void ANodeMayCarryBothAndDecliningTheDragLeavesTheClick()
    {
        // "Not mine" is a real answer: a list row whose body must let the press through to the scroll
        // controller declares a press that returns null, and the click still fires on release.
        var (widget, _) = Fixture();
        var clicked = 0;
        var tree = Layout.Builder.Spacer().Stretch()
            .Clickable(new HitResult.ButtonHit("row"), _ => clicked++)
            .Pressable(new HitResult.ButtonHit("row"), _ => null);

        widget.Render(tree, new RectF32(0, 0, 100, 100));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.ButtonHit);
        region.OnPress!(new PointerPress(1f, 1f, MouseButton.Left, InputModifier.None, 1)).ShouldBeNull();
        region.OnClick.ShouldNotBeNull();

        // Routed rather than dispatched, because the fall-through IS the router's rule: a press handler
        // returning null declines the DRAG, and the click then fires as though the node carried only one.
        Routing.Press(widget, 1f, 1f).ShouldBeTrue();
        clicked.ShouldBe(1);
    }

    [Fact]
    public void ANodeThatDeclaresNoPressRegistersNone()
    {
        // Every tree written before this existed is in exactly this state and must be unaffected.
        var (widget, _) = Fixture();
        widget.Render(
            Layout.Builder.Spacer().Stretch().Clickable(new HitResult.ButtonHit("plain"), _ => { }),
            new RectF32(0, 0, 100, 100));

        widget.GetRegisteredRegions().Single(r => r.Result is HitResult.ButtonHit).OnPress.ShouldBeNull();
    }
}
