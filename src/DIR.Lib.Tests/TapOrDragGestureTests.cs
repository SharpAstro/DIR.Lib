using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Headless unit tests for <see cref="TapOrDragGesture"/> — the press → (tap | drag) discriminator.
/// Exercises the two host patterns (arm+release only, vs. arm+update+release), the DPI-scaled slop
/// radius, the drag latch, and the default-struct trap (a zeroed struct must not silently treat every
/// press as a drag once armed).
/// </summary>
public class TapOrDragGestureTests
{
    [Fact]
    public void ArmThenReleaseWithoutMoving_IsTap()
    {
        var g = new TapOrDragGesture();
        g.Arm(10f, 10f);
        g.Release(10f, 10f).ShouldBe(GestureOutcome.Tap);
        g.State.ShouldBe(GestureState.Idle);
    }

    [Fact]
    public void MoveWithinSlop_ReleaseIsStillTap()
    {
        var g = new TapOrDragGesture();
        g.Arm(10f, 10f, slopPx: 4f);
        g.Update(12f, 11f).ShouldBeFalse(); // (2,1) → dist^2 = 5 < 16, still armed
        g.IsArmed.ShouldBeTrue();
        g.Release(12f, 11f).ShouldBe(GestureOutcome.Tap);
    }

    [Fact]
    public void MovePastSlop_LatchesDrag()
    {
        var g = new TapOrDragGesture();
        g.Arm(10f, 10f, slopPx: 4f);
        g.Update(20f, 10f).ShouldBeTrue(); // 10px > 4px slop
        g.IsDragging.ShouldBeTrue();
        g.Release(20f, 10f).ShouldBe(GestureOutcome.Drag);
    }

    [Fact]
    public void DragThatWandersBackInsideSlop_StillCountsAsDrag()
    {
        // The latch: once past the slop radius, returning near the press point does not demote to a tap.
        var g = new TapOrDragGesture();
        g.Arm(10f, 10f, slopPx: 4f);
        g.Update(30f, 10f).ShouldBeTrue();
        g.Update(11f, 10f).ShouldBeTrue(); // back near the start, but already dragging
        g.Release(11f, 10f).ShouldBe(GestureOutcome.Drag);
    }

    [Fact]
    public void ReleaseReChecksSlop_WhenUpdateWasNeverPumped()
    {
        // A host that only calls Arm + Release (never Update) still classifies a far release as a drag.
        var g = new TapOrDragGesture();
        g.Arm(10f, 10f, slopPx: 4f);
        g.Release(40f, 40f).ShouldBe(GestureOutcome.Drag);
    }

    [Fact]
    public void DpiScale_WidensSlopRadius()
    {
        var g = new TapOrDragGesture();
        g.Arm(0f, 0f, dpiScale: 2f, slopPx: 4f); // effective slop = 8px
        g.Update(7f, 0f).ShouldBeFalse();          // within 8
        g.Update(9f, 0f).ShouldBeTrue();           // past 8
    }

    [Fact]
    public void DownModifiers_AreCapturedAtArmTime()
    {
        var g = new TapOrDragGesture();
        g.Arm(5f, 5f, InputModifier.Shift | InputModifier.Ctrl);
        g.DownModifiers.ShouldBe(InputModifier.Shift | InputModifier.Ctrl);
        g.DownPosition.ShouldBe((5f, 5f));
    }

    [Fact]
    public void DefaultStruct_NeverArmed_ReleaseIsNone()
    {
        // A `default` TapOrDragGesture (field with no explicit init) reports None, never a spurious drag.
        TapOrDragGesture g = default;
        g.State.ShouldBe(GestureState.Idle);
        g.Release(100f, 100f).ShouldBe(GestureOutcome.None);
    }

    [Fact]
    public void Cancel_ResetsToIdle()
    {
        var g = new TapOrDragGesture();
        g.Arm(1f, 1f);
        g.Cancel();
        g.State.ShouldBe(GestureState.Idle);
        g.Release(1f, 1f).ShouldBe(GestureOutcome.None);
    }
}
