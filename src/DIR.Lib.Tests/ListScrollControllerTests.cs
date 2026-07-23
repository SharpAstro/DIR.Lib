using System.Collections.Generic;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Headless unit tests for <see cref="ListScrollController"/> — the atom-model scroll controller.
/// A 100x100 viewport with a 10px atom extent means 10 visible atoms; 30 total atoms give a max
/// offset of 20. Input is fed as <see cref="InputEvent"/>s, exactly as a widget would forward them.
/// </summary>
public class ListScrollControllerTests
{
    private static ListScrollController Make(int total = 30, float atomPx = 10f, float w = 100f, float h = 100f,
        ScrollAnchor anchor = ScrollAnchor.Top, ScrollBarMode mode = ScrollBarMode.Interactive, bool snap = false)
    {
        var c = new ListScrollController { Anchor = anchor, Mode = mode, SnapToAtom = snap };
        c.SetExtent(new RectF32(0f, 0f, w, h), atomPx, total, 1f);
        return c;
    }

    [Fact]
    public void InitialTopAnchor_IsAtStart()
    {
        var c = Make();
        c.Offset.ShouldBe(0f);
        c.FirstVisibleAtom.ShouldBe(0);
        c.VisibleAtoms.ShouldBe(10);
        c.MaxOffset.ShouldBe(20f);
    }

    [Fact]
    public void VisibleAtoms_IsFloatRobust_ForExactMultipleViewport()
    {
        // A menu sizes its viewport as N * rowH by multiplication, then the controller divides by rowH.
        // With a rowH whose N*rowH/rowH lands a hair under N in float, a bare floor would report N-1
        // visible atoms -> MaxOffset 1 -> the last row scrolls off a menu that actually fits. The
        // AtomFitEpsilon keeps VisibleAtoms == N, so such a menu stays clip-only (MaxOffset 0).
        for (var n = 1; n <= 64; n++)
        {
            var rowH = 25.2f; // a fractional extent that makes N*rowH/rowH prone to the 1-ulp shortfall
            var c = new ListScrollController();
            c.SetExtent(new RectF32(0f, 0f, 100f, n * rowH), rowH, n, 1f);
            c.VisibleAtoms.ShouldBe(n);
            c.MaxOffset.ShouldBe(0f);
        }
    }

    [Fact]
    public void WheelDown_ScrollsTowardEnd()
    {
        var c = Make();
        c.HandleInput(new InputEvent.Scroll(-1f, 50f, 50f)).ShouldBeTrue(); // negative delta = down
        c.Offset.ShouldBe(3f); // one notch = WheelStepAtoms (3)
        c.FirstVisibleAtom.ShouldBe(3);
    }

    [Fact]
    public void WheelUp_ScrollsTowardStart()
    {
        var c = Make();
        c.AtomOffset = 10;
        c.HandleInput(new InputEvent.Scroll(1f, 50f, 50f)); // positive delta = up
        c.Offset.ShouldBe(7f);
    }

    [Fact]
    public void Wheel_ClampsAtBothEnds()
    {
        var c = Make();
        for (var i = 0; i < 20; i++) c.HandleInput(new InputEvent.Scroll(-1f, 50f, 50f));
        c.Offset.ShouldBe(20f); // clamped at MaxOffset
        for (var i = 0; i < 20; i++) c.HandleInput(new InputEvent.Scroll(1f, 50f, 50f));
        c.Offset.ShouldBe(0f);
    }

    [Fact]
    public void FullWheelNotch_LeavesNoFractionalRemainder()
    {
        // A plain mouse-wheel notch (|delta| == 1) from a whole-atom offset lands on a whole atom with
        // no sub-atom drift — the atom-model analogue of the superseded ScrollBar's `accum == 0` invariant.
        var c = Make();
        c.HandleInput(new InputEvent.Scroll(-1f, 50f, 50f));
        c.Offset.ShouldBe(3f);
        c.SubAtomPx.ShouldBe(0f);
        c.HandleInput(new InputEvent.Scroll(-1f, 50f, 50f));
        c.Offset.ShouldBe(6f);
        c.SubAtomPx.ShouldBe(0f);
    }

    [Fact]
    public void SubUnitWheelDeltas_Accumulate_InsteadOfTruncatingToZero()
    {
        // The trackpad / web-bridge bug: |delta| < 1 used to truncate to zero every event.
        // Here four 0.1 deltas (x3 step = 0.3 atoms each) accumulate to 1.2 atoms → row 1 visible.
        var c = Make();
        for (var i = 0; i < 4; i++) c.HandleInput(new InputEvent.Scroll(-0.1f, 50f, 50f));
        c.Offset.ShouldBe(1.2f, 0.0001);
        c.FirstVisibleAtom.ShouldBe(1);
        c.SubAtomPx.ShouldBe(2f, 0.0001); // 0.2 atom * 10px
    }

    [Fact]
    public void ScrollOutsideViewport_IsNotConsumed()
    {
        var c = Make();
        c.HandleInput(new InputEvent.Scroll(-1f, 500f, 500f)).ShouldBeFalse();
        c.Offset.ShouldBe(0f);
    }

    [Fact]
    public void WhenContentFits_ScrollIsNoOp_ClipOnlyDegenerateMode()
    {
        var c = Make(total: 5); // 5 <= 10 visible
        c.MaxOffset.ShouldBe(0f);
        c.HandleInput(new InputEvent.Scroll(-1f, 50f, 50f)).ShouldBeFalse();
        c.Offset.ShouldBe(0f);

        var rects = new List<(float, float, float, float, RGBAColor32)>();
        c.DrawScrollBar((x, y, w, h, col) => rects.Add((x, y, w, h, col)));
        rects.ShouldBeEmpty(); // no scrollbar drawn when it fits
    }

    [Fact]
    public void TapOnRelease_SelectsAtomUnderPress()
    {
        var c = Make();
        c.HandleInput(new InputEvent.MouseDown(50f, 25f)); // atom 2 (y 20..30)
        c.HandleInput(new InputEvent.MouseUp(50f, 25f));
        c.TakeAtomTap().ShouldBe(2);
        c.TakeAtomTap().ShouldBeNull(); // consumed
    }

    [Fact]
    public void DragScrolls_WithoutEmittingATap()
    {
        var c = Make();
        c.HandleInput(new InputEvent.MouseDown(50f, 50f));
        c.HandleInput(new InputEvent.MouseMove(50f, 35f)); // dragged up 15px → +1.5 atoms
        c.DebugIsDragging.ShouldBeTrue();
        c.Offset.ShouldBe(1.5f, 0.0001);
        c.HandleInput(new InputEvent.MouseUp(50f, 35f));
        c.TakeAtomTap().ShouldBeNull(); // a drag never taps
    }

    [Fact]
    public void SnapToAtom_RoundsOnDragEnd()
    {
        var c = Make(snap: true);
        c.HandleInput(new InputEvent.MouseDown(50f, 50f));
        c.HandleInput(new InputEvent.MouseMove(50f, 37f)); // +1.3 atoms
        c.Offset.ShouldBe(1.3f, 0.0001); // smooth during the drag
        c.HandleInput(new InputEvent.MouseUp(50f, 37f));
        c.Offset.ShouldBe(1f); // snapped on release
    }

    [Fact]
    public void EnsureVisible_ScrollsMinimally_AndRespectsMargin()
    {
        var c = Make();
        c.EnsureVisible(15); // 16 > 0+10 → offset 6, shows 6..15
        c.Offset.ShouldBe(6f);
        c.EnsureVisible(15); // already visible → no-op
        c.Offset.ShouldBe(6f);

        var c2 = Make();
        c2.EnsureVisible(15, marginAtoms: 2); // offset 15+1+2-10 = 8
        c2.Offset.ShouldBe(8f);
    }

    [Fact]
    public void BottomAnchor_StartsAtTail_AndFollowsAppends()
    {
        var c = Make(anchor: ScrollAnchor.Bottom);
        c.Offset.ShouldBe(20f); // resting at the tail, showing the newest atoms
        c.FirstVisibleAtom.ShouldBe(20);

        c.SetExtent(new RectF32(0f, 0f, 100f, 100f), 10f, 40, 1f); // 10 new atoms appended
        c.Offset.ShouldBe(30f); // followed the tail (still pinned)
    }

    [Fact]
    public void BottomAnchor_ScrollingUp_ReleasesTailPin()
    {
        var c = Make(anchor: ScrollAnchor.Bottom);
        c.HandleInput(new InputEvent.Scroll(1f, 50f, 50f)); // scroll up into history → offset 17
        c.Offset.ShouldBe(17f);

        c.SetExtent(new RectF32(0f, 0f, 100f, 100f), 10f, 40, 1f); // appends
        c.Offset.ShouldBe(17f); // pin released — position held, does NOT jump to the new tail
    }

    [Fact]
    public void BottomAnchor_FittingAgain_ReestablishesTailPin()
    {
        var c = Make(anchor: ScrollAnchor.Bottom);
        c.HandleInput(new InputEvent.Scroll(1f, 50f, 50f)); // scroll up into history → pin released
        c.Offset.ShouldBe(17f);

        // The list clears + refills (a session restart resetting its log): once the content fits
        // (MaxOffset 0) there is no history position to hold, so the tail pin re-establishes and
        // the refilled list tail-follows again.
        c.SetExtent(new RectF32(0f, 0f, 100f, 100f), 10f, 0, 1f);  // cleared
        c.Offset.ShouldBe(0f);
        c.SetExtent(new RectF32(0f, 0f, 100f, 100f), 10f, 25, 1f); // refilled past the viewport
        c.Offset.ShouldBe(15f); // pinned at the new tail
    }

    [Fact]
    public void ThumbGeometry_IsRatioBased_AndReachesBothEnds()
    {
        var c = Make(); // track 100px, visible 10, total 30
        var (start0, extent) = c.DebugThumbGeometry();
        extent.ShouldBe(100f * 10f / 30f, 0.001); // trackExtent * visible / total
        start0.ShouldBe(0f);

        c.AtomOffset = 20; // max
        var (startMax, extentMax) = c.DebugThumbGeometry();
        extentMax.ShouldBe(extent, 0.001);
        (startMax + extentMax).ShouldBe(100f, 0.001); // thumb bottom sits at the track bottom
    }

    [Fact]
    public void ThumbDrag_MovesOffsetProportionally()
    {
        var c = Make(); // scrollbar column is the right 6px of a 100px-wide viewport
        c.HandleInput(new InputEvent.MouseDown(97f, 5f)); // grab the thumb near its top (thumb 0..33)
        c.HandleInput(new InputEvent.MouseMove(97f, 68f)); // drag toward the bottom of the usable track
        c.Offset.ShouldBeGreaterThan(15f);
        c.HandleInput(new InputEvent.MouseUp(97f, 68f));
    }

    [Fact]
    public void TrackPageClick_PagesByViewport()
    {
        var c = Make();
        c.AtomOffset = 5;
        // Click in the scrollbar column below the thumb → page down by VisibleAtoms (10).
        c.HandleInput(new InputEvent.MouseDown(97f, 95f));
        c.Offset.ShouldBe(15f);
    }

    [Fact]
    public void HorizontalAxis_ScrollsAndTapsAlongX()
    {
        var c = new ListScrollController { Axis = ScrollAxis.Horizontal };
        c.SetExtent(new RectF32(0f, 0f, 100f, 50f), 10f, 30, 1f);
        c.VisibleAtoms.ShouldBe(10);
        c.HandleInput(new InputEvent.Scroll(-1f, 50f, 25f));
        c.Offset.ShouldBe(3f);

        var c2 = new ListScrollController { Axis = ScrollAxis.Horizontal, Mode = ScrollBarMode.None };
        c2.SetExtent(new RectF32(0f, 0f, 100f, 50f), 10f, 30, 1f);
        c2.HandleInput(new InputEvent.MouseDown(25f, 25f));
        c2.HandleInput(new InputEvent.MouseUp(25f, 25f));
        c2.TakeAtomTap().ShouldBe(2); // atom under x=25
    }

    [Fact]
    public void AtomOffset_RoundTrips_AndClampsToGeometry()
    {
        var c = Make();
        c.AtomOffset = 7;
        c.AtomOffset.ShouldBe(7);
        c.AtomOffset = 999;
        c.AtomOffset.ShouldBe(20); // clamped to MaxOffset
    }

    [Fact]
    public void ContentCrossExtent_ReservesScrollBarColumnOnlyWhenNeeded()
    {
        var fits = Make(total: 5);
        fits.ContentCrossExtentPx.ShouldBe(100f); // full width when it fits

        var overflows = Make(total: 30);
        overflows.ContentCrossExtentPx.ShouldBe(100f - ListScrollController.ScrollBarBaseWidthPx);
    }

    [Fact]
    public void Changed_FiresOnInput_NotOnSetExtent()
    {
        var c = Make();
        var count = 0;
        c.Changed += () => count++;

        c.SetExtent(new RectF32(0f, 0f, 100f, 100f), 10f, 30, 1f); // geometry only
        count.ShouldBe(0);

        c.HandleInput(new InputEvent.Scroll(-1f, 50f, 50f));
        count.ShouldBe(1);
    }

    [Fact]
    public void ContentArea_ReservesScrollBarColumnOnlyWhenOverflowing()
    {
        // 30 atoms in a 10-visible viewport overflows -> reserve the 6px scrollbar column (width 94).
        Make(snap: true, total: 30).ContentArea.ShouldBe(new RectF32(0f, 0f, 94f, 100f));
        // 5 atoms fit -> no scrollbar, full width.
        Make(snap: true, total: 5).ContentArea.ShouldBe(new RectF32(0f, 0f, 100f, 100f));
    }

    [Fact]
    public void VisibleRows_Snapped_YieldsFullyVisibleAtomsAtScreenRects()
    {
        var c = Make(snap: true); // 100x100, atom 10, total 30 -> 10 visible, content width 94
        var rows = new List<(int Index, RectF32 Rect)>();
        foreach (var r in c.VisibleRows()) rows.Add(r);

        rows.Count.ShouldBe(10);
        rows[0].Index.ShouldBe(0);
        rows[0].Rect.ShouldBe(new RectF32(0f, 0f, 94f, 10f));
        rows[9].Index.ShouldBe(9);
        rows[9].Rect.ShouldBe(new RectF32(0f, 90f, 94f, 10f));
    }

    [Fact]
    public void VisibleRows_Snapped_Scrolled_ShiftsIndicesNotScreenRects()
    {
        var c = Make(snap: true);
        c.AtomOffset = 5;
        var rows = new List<(int Index, RectF32 Rect)>();
        foreach (var r in c.VisibleRows()) rows.Add(r);

        rows.Count.ShouldBe(10);
        rows[0].Index.ShouldBe(5);   // first visible atom scrolled to 5...
        rows[0].Rect.Y.ShouldBe(0f); // ...but still drawn at the top of the viewport
        rows[9].Index.ShouldBe(14);
        rows[9].Rect.Y.ShouldBe(90f);
    }

    [Fact]
    public void VisibleRows_ClipOnly_YieldsEveryAtom()
    {
        var c = Make(snap: true, total: 4); // 4 <= 10 visible -> no scroll, no reservation
        var indices = new List<int>();
        foreach (var (i, _) in c.VisibleRows()) indices.Add(i);
        indices.ShouldBe([0, 1, 2, 3]);
    }

    [Fact]
    public void VisibleRows_Horizontal_PlacesAtomsAlongX()
    {
        var c = new ListScrollController { Axis = ScrollAxis.Horizontal, SnapToAtom = true };
        c.SetExtent(new RectF32(0f, 0f, 100f, 50f), 10f, 30, 1f); // 10 visible, content height 44
        var rows = new List<(int Index, RectF32 Rect)>();
        foreach (var r in c.VisibleRows()) rows.Add(r);

        rows.Count.ShouldBe(10);
        rows[0].Rect.ShouldBe(new RectF32(0f, 0f, 10f, 44f));
        rows[9].Rect.X.ShouldBe(90f);
    }

    [Fact]
    public void VisibleRows_Smooth_AppliesSubAtomShiftAndIncludesPartialTrailingRow()
    {
        var c = Make(snap: false); // smooth surface
        c.HandleInput(new InputEvent.Scroll(-0.5f / 3f, 50f, 50f)); // offset -= delta*3 -> +0.5 atoms
        c.Offset.ShouldBe(0.5f, 0.001);

        var rows = new List<(int Index, RectF32 Rect)>();
        foreach (var r in c.VisibleRows()) rows.Add(r);

        rows.Count.ShouldBe(11);          // 10 visible + 1 partially-visible trailing atom
        rows[0].Index.ShouldBe(0);
        rows[0].Rect.Y.ShouldBe(-5f, 0.001); // first atom shifted up by the half-atom sub-shift (0.5*10)
    }
}
