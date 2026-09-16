using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Content.Slider"/>: the node IS the control, the same precedent
/// <see cref="Layout.Content.TextInput"/> set for a field.
/// <para>
/// Before this a slider could not be declared at all; every one of them opted out of the region model and
/// re-derived its own track rect beside the paint, arming a drag flag from the host's dispatcher (six such
/// caches counted in one consumer). What is worth pinning here is the same thing <c>LayoutPressTests</c>
/// pins for <c>OnPress</c> in general, specialised to the one control it was written for: the drag reads
/// the RECT THE ENGINE ARRANGED, not a cached copy, and a plain click jumps the handle rather than only a
/// drag doing anything.
/// </para>
/// </summary>
public class LayoutSliderTests
{
    private static (DeclarationWidget Widget, DeclarationStubRenderer Renderer) Fixture()
    {
        var renderer = new DeclarationStubRenderer(100, 100);
        return (new DeclarationWidget(renderer), renderer);
    }

    private static HitResult.SliderStateHit RegisteredSlider(DeclarationWidget widget)
        => widget.GetRegisteredRegions().Select(r => r.Result).OfType<HitResult.SliderStateHit>().Single();

    // ---- Declaration ----

    [Fact]
    public void BuilderSliderIsStarWidthByDefault()
    {
        // Unlike TextInput, which shrinks to its placeholder under Auto: a slider has no content-shaped
        // width to shrink to and almost always fills the row it sits in.
        var state = new SliderState();

        Layout.Builder.Slider(state).Width.IsStar.ShouldBeTrue();
    }

    [Fact]
    public void PaintingASliderRegistersItsStateAtTheArrangedRect()
    {
        var (widget, _) = Fixture();
        var state = new SliderState { Min = 0f, Max = 10f, Value = 5f };

        widget.Render(
            Layout.Builder.VStack(Layout.Builder.Slider(state).RowH(20f)).Stretch(),
            new RectF32(0, 0, 100, 40));

        var region = widget.GetRegisteredRegions()
            .Single(r => r.Result is HitResult.SliderStateHit);
        region.Result.ShouldBe(new HitResult.SliderStateHit(state));
        region.Y.ShouldBe(0f);
        region.Height.ShouldBe(20f);
        region.OnPress.ShouldNotBeNull();
    }

    // ---- Intrinsic size ----

    [Fact]
    public void AutoHeightIsTheTrackHeightNothingElseStatesOne()
    {
        var widget = Fixture().Widget;
        var state = new SliderState();

        var size = widget.Measure(Layout.Builder.Slider(state), new Layout.Size<float>(200f, 200f));

        size.Height.ShouldBe(Layout.Content.Slider.TrackHeight);
    }

    // ---- Drag arithmetic: press jumps, move tracks, release lands ----

    [Fact]
    public void APressJumpsTheValueToWhereItLanded()
    {
        // Applying on the press itself, not only on a subsequent move, is what makes a plain click useful.
        var (widget, _) = Fixture();
        var state = new SliderState { Min = 0f, Max = 100f, Value = 0f };
        widget.Render(Layout.Builder.Slider(state).Stretch(), new RectF32(0, 0, 100, 10));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.SliderStateHit);
        var capture = region.OnPress!(new PointerPress(25f, 5f, MouseButton.Left, InputModifier.None, 1));

        capture.ShouldNotBeNull();
        state.Value.ShouldBe(25f);
    }

    [Fact]
    public void EveryMoveUntilReleaseUpdatesTheValueThroughTrackFrac()
    {
        var (widget, _) = Fixture();
        var state = new SliderState { Min = 0f, Max = 100f, Value = 0f };
        widget.Render(Layout.Builder.Slider(state).Stretch(), new RectF32(0, 0, 100, 10));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.SliderStateHit);
        var capture = region.OnPress!(new PointerPress(0f, 5f, MouseButton.Left, InputModifier.None, 1))!;

        capture.Move(new PointerMove(50f, 5f, MouseButton.Left, InputModifier.None));
        state.Value.ShouldBe(50f);

        capture.Move(new PointerMove(90f, 5f, MouseButton.Left, InputModifier.None));
        state.Value.ShouldBe(90f);

        capture.Release(new PointerMove(100f, 5f, MouseButton.Left, InputModifier.None));
        state.Value.ShouldBe(100f);
    }

    [Fact]
    public void AMoveBeyondEitherEdgeClampsToTheRange()
    {
        var (widget, _) = Fixture();
        var state = new SliderState { Min = 10f, Max = 20f, Value = 15f };
        widget.Render(Layout.Builder.Slider(state).Stretch(), new RectF32(0, 0, 100, 10));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.SliderStateHit);
        var capture = region.OnPress!(new PointerPress(15f, 5f, MouseButton.Left, InputModifier.None, 1))!;

        capture.Move(new PointerMove(-1000f, 5f, MouseButton.Left, InputModifier.None));
        state.Value.ShouldBe(10f);

        capture.Move(new PointerMove(1000f, 5f, MouseButton.Left, InputModifier.None));
        state.Value.ShouldBe(20f);
    }

    [Fact]
    public void EveryApplicationCallsOnChangedWithTheNewValue()
    {
        var (widget, _) = Fixture();
        var seen = new System.Collections.Generic.List<float>();
        var state = new SliderState { Min = 0f, Max = 100f, Value = 0f, OnChanged = v => seen.Add(v) };
        widget.Render(Layout.Builder.Slider(state).Stretch(), new RectF32(0, 0, 100, 10));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.SliderStateHit);
        var capture = region.OnPress!(new PointerPress(20f, 5f, MouseButton.Left, InputModifier.None, 1))!;
        capture.Move(new PointerMove(40f, 5f, MouseButton.Left, InputModifier.None));
        capture.Release(new PointerMove(60f, 5f, MouseButton.Left, InputModifier.None));

        // Tolerance on each, not exact list equality: TrackFrac's division is float and 60/100*100 does
        // not round-trip to exactly 60f, which is a fact about IEEE 754 and not about the mapping.
        seen.Count.ShouldBe(3);
        seen[0].ShouldBe(20f, 0.001f);
        seen[1].ShouldBe(40f, 0.001f);
        seen[2].ShouldBe(60f, 0.001f);
    }

    [Fact]
    public void ANonZeroStepSnapsTheDraggedValueToItsNearestMultiple()
    {
        var (widget, _) = Fixture();
        // Track is 100 wide over [0, 10] with a step of 2: a press at x=53 is frac 0.53 -> raw 5.3 ->
        // nearest even multiple of 2 is 6.
        var state = new SliderState { Min = 0f, Max = 10f, Value = 0f, Step = 2f };
        widget.Render(Layout.Builder.Slider(state).Stretch(), new RectF32(0, 0, 100, 10));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.SliderStateHit);
        region.OnPress!(new PointerPress(53f, 5f, MouseButton.Left, InputModifier.None, 1));

        state.Value.ShouldBe(6f);
    }

    // ---- Disabled: the content's own flag, and the ambient node-level one ----

    [Fact]
    public void ADisabledSliderStateRegistersItsRegionWithNoPressSoAPressIsSwallowed()
    {
        var (widget, _) = Fixture();
        var state = new SliderState { Min = 0f, Max = 10f, Value = 5f, Enabled = false };

        widget.Render(Layout.Builder.Slider(state).Stretch(), new RectF32(0, 0, 100, 10));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.SliderStateHit);
        region.IsDisabled.ShouldBeTrue();
        region.OnPress.ShouldBeNull();
        region.Cursor.ShouldBe(CursorKind.NotAllowed);

        // Still HIT, which is what swallowing means: a press over it does not fall through.
        widget.HitTest(50f, 5f).ShouldBe(RegisteredSlider(widget));
        Routing.Press(widget, 50f, 5f).ShouldBeTrue();
        state.Value.ShouldBe(5f, "a disabled slider takes the press and does nothing with it");
    }

    [Fact]
    public void ANodeLevelDisabledSubtreeDisablesTheSliderInsideItToo()
    {
        var (widget, _) = Fixture();
        var state = new SliderState { Min = 0f, Max = 10f, Value = 5f };

        widget.Render(
            Layout.Builder.VStack(Layout.Builder.Slider(state).RowH(10f))
                .Stretch().Disabled("Not while a run is on"),
            new RectF32(0, 0, 100, 10));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.SliderStateHit);
        region.IsDisabled.ShouldBeTrue();
        region.OnPress.ShouldBeNull();
    }

    [Fact]
    public void AnEnabledSliderInAnOrdinaryTreeHasNoCursorOverride()
    {
        // DrawTrackSlider's own registration never set one, and the declared leaf matches it: the ambient
        // cursor (or none) shows through rather than the leaf asserting an opinion nobody asked it for.
        var (widget, _) = Fixture();
        var state = new SliderState();

        widget.Render(Layout.Builder.Slider(state).Stretch(), new RectF32(0, 0, 100, 10));

        widget.GetRegisteredRegions().Single(r => r.Result is HitResult.SliderStateHit).Cursor.ShouldBeNull();
    }
}
