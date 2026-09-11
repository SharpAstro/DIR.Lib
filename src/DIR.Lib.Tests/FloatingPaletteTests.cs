using DIR.Lib;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace DIR.Lib.Tests;

/// <summary>
/// Pins <see cref="FloatingPalette"/> and its state machine.
/// <para>
/// Hoisted out of two independent implementations of the same panel, so the cases here are the ones
/// that had already gone wrong in one of them rather than a survey of the API: the clamp
/// reconciliation, the collapsed fade floor, and the fact that a grip drag has nothing but successive
/// presses and the last pointer position to work with.
/// </para>
/// </summary>
public class FloatingPaletteTests
{
    private static readonly PaletteColors Colors = new(
        PanelBg: new(0x14, 0x14, 0x1C, 0x9C),
        GripBg: new(0x2A, 0x2A, 0x36, 0xB4),
        TitleInk: new(0xE8, 0xDC, 0xB4, 0xFF),
        CountInk: new(0xFF, 0xEE, 0x60, 0xFF),
        RowOnBg: new(0x37, 0x48, 0x5C, 0xDC),
        RowOffBg: new(0x20, 0x20, 0x2A, 0xA0),
        RowHoverBg: new(0x44, 0x56, 0x6A, 0xE6),
        KeyChipBg: new(0x2C, 0x2C, 0x36, 0xC8),
        OnInk: new(0xE8, 0xE8, 0xF0, 0xFF),
        OffInk: new(0x9A, 0x9A, 0xA6, 0xFF),
        DisabledInk: new(0x5A, 0x5A, 0x64, 0xFF));

    private static PaletteItem[] Items(int count = 4, bool lastUnavailable = false) =>
        [.. Enumerable.Range(0, count).Select(i => new PaletteItem(
            Label: $"Row {i}",
            Action: $"row:{i}",
            IsOn: i % 2 == 0,
            KeyHint: ((char)('A' + i)).ToString(),
            IsAvailable: !(lastUnavailable && i == count - 1)))];

    private static Layout.Node Build(FloatingPaletteState state, PaletteItem[] items,
        Action<string>? onItem = null, Action? onGrip = null) =>
        FloatingPalette.Build(state, "LAYERS", items, in Colors, 12f, "grip",
            onItem ?? (_ => { }), onGrip ?? (() => { }));

    private static float PanelTop(FloatingPaletteState state, PaletteItem[] items, Rect<float> bounds) =>
        Layout.Engine.Arrange(Build(state, items), bounds, new UnitContext())
            .First(a => a.Node is Layout.Node.Stack { Axis: Layout.Axis.Vertical })
            .Bounds.Y;

    [Theory]
    [InlineData(0f, false, false, 1f)]
    [InlineData(2.5f, false, false, 1f)]
    [InlineData(2.7f, false, false, 0.7f)]
    [InlineData(2.9f, false, false, 0.4f)]
    [InlineData(60f, false, false, 0.4f)]
    [InlineData(60f, true, false, 1f)]
    [InlineData(60f, false, true, FloatingPalette.CollapsedIdleAlpha)]
    public void TheIdleFadeHoldsThenRecedesToItsFloor(
        float idleSeconds, bool engaged, bool collapsed, float expected)
    {
        // Engaged pins it fully present at any age, which is what stops a panel receding out from
        // under the pointer using it; and a COLLAPSED panel barely recedes at all, because rolled up
        // the title bar is the only thing left to fade.
        FloatingPalette.FadeFor(idleSeconds, engaged, collapsed).ShouldBe(expected, 0.001f);
    }

    [Fact]
    public void ACollapsedPanelRecedesLessThanAnExpandedOne()
        => FloatingPalette.CollapsedIdleAlpha.ShouldBeGreaterThan(FloatingPalette.IdleAlpha);

    [Theory]
    [InlineData(0.1f, true)]
    [InlineData(0.4f, true)]
    [InlineData(0.5f, false)]
    public void TwoPressesCloseTogetherAreADoubleClick(float seconds, bool expected)
        => FloatingPalette.IsDoubleClick(seconds).ShouldBe(expected);

    [Fact]
    public void TheGripAndOneRowPerAvailableItemAreBound()
    {
        var actions = ClickActions(Build(new FloatingPaletteState(), Items(lastUnavailable: true)));

        // Named rather than counted loosely: a row that stops being bound cannot then be masked by a
        // new binding appearing elsewhere in the panel.
        actions.Keys.OrderBy(static k => k)
            .ShouldBe(["grip", "row:0", "row:1", "row:2"]);
    }

    [Fact]
    public void AnUnavailableRowIsDrawnButNotClickable()
    {
        // Shown and dimmed rather than dropped: a row that comes and goes moves every row under it,
        // and an absent row says nothing about WHY the thing is unavailable.
        var tree = Build(new FloatingPaletteState(), Items(lastUnavailable: true));

        ClickActions(tree).ShouldNotContainKey("row:3");
        TextRuns(tree).ShouldContain("Row 3");
    }

    [Fact]
    public void CollapsedThePanelIsItsTitleBarAndStillSaysHowManyAreLit()
    {
        var state = new FloatingPaletteState { Collapsed = true };

        var tree = Build(state, Items());

        ClickActions(tree).Keys.ShouldBe(["grip"]);
        TextRuns(tree).ShouldBe(["LAYERS", "2/4"]);
    }

    [Fact]
    public void PressingTheGripStartsADragAndPressingItAgainCollapses()
    {
        // The whole drag entry point: a host dispatches clickable regions from the PRESS and forwards
        // the mouse-down to the widget only when nothing was hit, so a grip that is a region can never
        // be picked up by the widget's own mouse-down path.
        var state = new FloatingPaletteState();

        state.PressGrip(100f).ShouldBeFalse();
        state.IsDragging.ShouldBeTrue();

        state.PressGrip(100f).ShouldBeTrue("a second press in the window collapses");
        state.Collapsed.ShouldBeTrue();
        state.IsDragging.ShouldBeFalse("collapsing must not leave a drag armed");
    }

    [Fact]
    public void ADragMovesTheOffsetByThePointerDeltaInDesignUnits()
    {
        var state = new FloatingPaletteState { OffsetAlong = 10f };
        state.PressGrip(100f);

        state.DragTo(250f, dpiScale: 1.5f).ShouldBeTrue();

        // 150 surface pixels at 1.5x is 100 design units; without the divide the panel runs away from
        // the pointer at exactly the scale factor.
        state.OffsetAlong.ShouldBe(110f, 0.001f);

        state.ReleaseGrip().ShouldBeTrue();
        state.DragTo(400f, 1.5f).ShouldBeFalse("a move after the release is not the palette's");
    }

    [Fact]
    public void EachDragStepIsMeasuredFromThePressRatherThanAccumulated()
    {
        // Absolute from the press anchor, so a write-back to OffsetAlong between moves (which
        // NoteArranged does every frame) cannot make the panel drift under the pointer.
        var state = new FloatingPaletteState { OffsetAlong = 0f };
        state.PressGrip(0f);

        state.DragTo(50f, 1f);
        state.OffsetAlong = 999f; // as if an arrange had clamped and written back
        state.DragTo(80f, 1f);

        state.OffsetAlong.ShouldBe(80f, 0.001f);
    }

    [Fact]
    public void CollapsingDoesNotMoveTheTitleBarWhereTheClampWasBiting()
    {
        // Near the far edge the clamp pulls an EXPANDED panel back to make it fit while the stored
        // offset still says where the pointer left it. The divergence is invisible until the panel's
        // height changes -- collapse, the clamp stops binding, and the title bar jumps to the stale
        // offset. Reported against a real consumer as "collapsing near the bottom moves the header
        // slightly down; in the middle it stays put". NoteArranged is what closes it.
        var bounds = new Rect<float>(0f, 0f, 900f, 200f);
        var items = Items(8);
        var state = new FloatingPaletteState { OffsetAlong = 180f };

        var expandedTop = PanelTop(state, items, bounds);
        expandedTop.ShouldBeLessThan(180f, "the clamp should have pulled it back");

        state.NoteArranged(new RectF32(0f, expandedTop, 124f, 100f), bounds.Y, dpiScale: 1f);
        state.Collapsed = true;

        PanelTop(state, items, bounds).ShouldBe(expandedTop, 0.01f);
    }

    [Fact]
    public void TheOffsetSlidesThePanelAlongTheEdge()
    {
        var bounds = new Rect<float>(0f, 0f, 900f, 900f);
        var items = Items();

        var near = new FloatingPaletteState { OffsetAlong = 20f };
        var far = new FloatingPaletteState { OffsetAlong = 140f };

        (PanelTop(far, items, bounds) - PanelTop(near, items, bounds)).ShouldBe(120f, 0.01f);
    }

    [Fact]
    public void APaneNarrowerThanThePanelStillPlacesItInside()
    {
        var bounds = new Rect<float>(0f, 0f, 60f, 400f);

        var arranged = Layout.Engine.Arrange(
            Build(new FloatingPaletteState(), Items()), bounds, new UnitContext());
        var panel = arranged.First(a => a.Node is Layout.Node.Stack { Axis: Layout.Axis.Vertical });

        panel.Bounds.X.ShouldBeGreaterThanOrEqualTo(bounds.X);
    }

    [Fact]
    public void HoverHoldsTheFadeOpenAndLeavingReleasesIt()
    {
        var state = new FloatingPaletteState();
        state.NoteArranged(new RectF32(100f, 100f, 120f, 200f), 0f, 1f);

        state.NotePointer(150f, 150f);
        state.IsEngaged.ShouldBeTrue();
        state.Fade.ShouldBe(1f, 0.001f);

        state.NotePointer(10f, 10f);
        state.IsEngaged.ShouldBeFalse();
    }

    [Fact]
    public void ClickingARowReportsThatRowsAction()
    {
        var clicked = new List<string>();
        var tree = Build(new FloatingPaletteState(), Items(), onItem: clicked.Add);

        ClickActions(tree)["row:2"].Invoke(InputModifier.None);

        clicked.ShouldBe(["row:2"]);
    }

    private static Dictionary<string, Action<InputModifier>> ClickActions(Layout.Node root)
    {
        var found = new Dictionary<string, Action<InputModifier>>();
        Walk(root, n =>
        {
            if (n is { Hit: HitResult.ButtonHit button, OnClick: { } click })
            {
                found[button.Action] = click;
            }
        });
        return found;
    }

    private static List<string> TextRuns(Layout.Node root)
    {
        var found = new List<string>();
        Walk(root, n =>
        {
            if (n is Layout.Node.Leaf { Content: Layout.Content.Text text })
            {
                found.Add(text.Value);
            }
        });
        return found;
    }

    private static void Walk(Layout.Node node, Action<Layout.Node> visit)
    {
        visit(node);
        switch (node)
        {
            case Layout.Node.Stack stack:
                foreach (var child in stack.Children) Walk(child, visit);
                break;
            case Layout.Node.Anchored anchored:
                Walk(anchored.Child, visit);
                break;
        }
    }

    [Fact]
    public void APanelReleasedNearAnEdgeTakesThatEdge()
    {
        var content = new RectF32(0f, 0f, 900f, 600f);

        FloatingPalette.SnapSideFor(new RectF32(4f, 300f, 40f, 200f), content, 26f, 8f)
            .ShouldBe(Layout.DockSide.Left);
        FloatingPalette.SnapSideFor(new RectF32(880f, 300f, 40f, 200f), content, 26f, 8f)
            .ShouldBe(Layout.DockSide.Right);
        FloatingPalette.SnapSideFor(new RectF32(400f, 6f, 40f, 200f), content, 26f, 8f)
            .ShouldBe(Layout.DockSide.Top);
        FloatingPalette.SnapSideFor(new RectF32(400f, 300f, 40f, 200f), content, 26f, 8f)
            .ShouldBeNull();
    }

    [Fact]
    public void ReleasedIntoACornerAPanelPinsToTheSideRatherThanTheTop()
    {
        // A tall strip belongs against a side; the order the edges are tested in is what decides it,
        // and both tests pass in a corner.
        FloatingPalette.SnapSideFor(
                new RectF32(4f, 4f, 40f, 200f), new RectF32(0f, 0f, 900f, 600f), 26f, 8f)
            .ShouldBe(Layout.DockSide.Left);
    }

    [Fact]
    public void AFreeFloatingPanelKeepsBothOffsetsWhereAPinnedOneKeepsJustTheOne()
    {
        // The clever form -- one ternary on "is it horizontal" -- is right for the pinned states and
        // wrong for floating, where along is X and across is Y. A panel that took Y for both could only
        // ever travel the diagonal.
        var panel = new RectF32(150f, 70f, 40f, 200f);
        var content = new RectF32(50f, 20f, 900f, 600f);

        FloatingPalette.DrawnOffsets(panel, content, null, 1f).ShouldBe((100f, 50f));
        FloatingPalette.DrawnOffsets(panel, content, Layout.DockSide.Left, 1f).ShouldBe((50f, 0f));
        FloatingPalette.DrawnOffsets(panel, content, Layout.DockSide.Top, 1f).ShouldBe((100f, 0f));
    }

    [Fact]
    public void APinnedPanelSlidesAlongItsEdgeWhileAFreeOneFollowsThePointer()
    {
        var pinned = new FloatingPaletteState { Side = Layout.DockSide.Left, OffsetAlong = 10f };
        pinned.PressGrip(200f, 300f);
        pinned.DragTo(260f, 340f, 1f).ShouldBeTrue();
        pinned.OffsetAlong.ShouldBe(50f, 0.001f);    // the 40 of Y, never the 60 of X
        pinned.OffsetAcross.ShouldBe(0f);

        var free = new FloatingPaletteState { Side = null, OffsetAlong = 10f, OffsetAcross = 5f };
        free.PressGrip(200f, 300f);
        free.DragTo(260f, 340f, 1f).ShouldBeTrue();
        free.OffsetAlong.ShouldBe(70f, 0.001f);
        free.OffsetAcross.ShouldBe(45f, 0.001f);
    }

    [Fact]
    public void ATopPinnedPanelRunsAcrossAndSlidesWithTheXAxis()
    {
        var state = new FloatingPaletteState { Side = Layout.DockSide.Top, OffsetAlong = 10f };
        state.IsHorizontal.ShouldBeTrue();

        state.PressGrip(200f, 300f);
        state.DragTo(260f, 340f, 1f);
        state.OffsetAlong.ShouldBe(70f, 0.001f);
    }

    [Fact]
    public void WhereCollapseIsNotAllowedASecondQuickPressStartsADragInstead()
    {
        var state = new FloatingPaletteState { AllowCollapse = false };

        state.PressGrip(0f, 100f).ShouldBeFalse();
        state.ReleaseGrip();
        state.PressGrip(0f, 100f).ShouldBeFalse();

        state.Collapsed.ShouldBeFalse();
        state.IsDragging.ShouldBeTrue();
    }

    [Fact]
    public void PinningOnReleaseTakesTheOffsetDownTheNewEdgeRatherThanTheOneItHadWhileFloating()
    {
        // The offsets mean different AXES on either side of this change: floating, along is X; pinned
        // to a side, along is Y. A panel dragged from the right edge to the left arrives with an along
        // of ~900 (an X), and carrying that number over reads it as 900 DOWN the left edge — so the
        // panel docks correctly, gets clamped back to the top, and the drop looks ignored. Seen in a
        // real consumer, which is why both offsets are re-derived from the rect and never carried.
        var content = new RectF32(0f, 0f, 900f, 600f);
        var state = new FloatingPaletteState { Side = null, OffsetAlong = 880f, OffsetAcross = 300f };

        state.SnapOnRelease(new RectF32(4f, 300f, 40f, 200f), content, 26f, 8f).ShouldBeTrue();
        state.Side.ShouldBe(Layout.DockSide.Left);
        state.OffsetAlong.ShouldBe(300f, 0.001f);   // where it was dropped DOWN the edge, not the 880
        state.OffsetAcross.ShouldBe(0f);

        // And released in open space it comes off the edge again, with both offsets back to a position.
        state.SnapOnRelease(new RectF32(400f, 250f, 40f, 200f), content, 26f, 8f).ShouldBeTrue();
        state.Side.ShouldBeNull();
        state.OffsetAlong.ShouldBe(400f, 0.001f);
        state.OffsetAcross.ShouldBe(250f, 0.001f);
    }

    [Fact]
    public void ReleasingOnTheSameEdgeStillUpdatesWhereAlongItSits()
    {
        // Returns false because the SIDE did not change, but the offset must still follow the drop --
        // a "nothing changed" early return here would pin a panel to its edge and never let it slide.
        var content = new RectF32(0f, 0f, 900f, 600f);
        var state = new FloatingPaletteState { Side = Layout.DockSide.Left, OffsetAlong = 40f };

        state.SnapOnRelease(new RectF32(4f, 320f, 40f, 200f), content, 26f, 8f).ShouldBeFalse();
        state.Side.ShouldBe(Layout.DockSide.Left);
        state.OffsetAlong.ShouldBe(320f, 0.001f);
    }

    [Fact]
    public void UnpinningLeavesThePanelExactlyWhereItIsDrawn()
    {
        // The mirror of the above, and the same axis conversion: a panel pinned to the left edge has an
        // along measured DOWN it, and lifting it off has to turn that back into a position. Seeding the
        // drag from the stored offsets instead makes the panel jump the moment the grip is pressed.
        var content = new RectF32(50f, 20f, 900f, 600f);
        var state = new FloatingPaletteState { Side = Layout.DockSide.Left, OffsetAlong = 300f };

        state.Unpin(new RectF32(60f, 320f, 40f, 200f), content);

        state.Side.ShouldBeNull();
        state.OffsetAlong.ShouldBe(10f, 0.001f);
        state.OffsetAcross.ShouldBe(300f, 0.001f);
    }

    [Fact]
    public void APlacementSurvivesBeingStoredAndReadBack()
    {
        PalettePlacement[] originals =
        [
            new(Layout.DockSide.Left, 12.5f, 0f),
            new(null, 12.5f, -3.25f),
            new(Layout.DockSide.Top, 0f, 0f),
            new(Layout.DockSide.Bottom, 900f, 0f),
        ];

        foreach (var original in originals)
        {
            PalettePlacement.TryParse(original.ToString(), out var read).ShouldBeTrue();
            read.ShouldBe(original);
        }
    }

    [Fact]
    public void APlacementIsWrittenAndReadInTheInvariantCulture()
    {
        // On its own thread, so a culture set for this assertion cannot leak into a test running beside
        // it. A settings file written where the separator is a comma has to be readable where it is a
        // point, which is the whole reason the format states its culture.
        string? written = null;
        var read = default(PalettePlacement);
        var parsed = false;

        var thread = new Thread(() =>
        {
            written = new PalettePlacement(Layout.DockSide.Right, 12.5f, 0f).ToString();
            parsed = PalettePlacement.TryParse(written, out read);
        })
        {
            CurrentCulture = new CultureInfo("de-DE"),
        };
        thread.Start();
        thread.Join();

        written.ShouldBe("right:12.5:0");
        parsed.ShouldBeTrue();
        read.Along.ShouldBe(12.5f);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("left:12.5")]
    [InlineData("sideways:1:2")]
    [InlineData("left:x:2")]
    public void ADamagedPlacementIsRefusedRatherThanReadAsFloating(string? text)
    {
        // The refusal matters more than it looks: reading an unrecognised side as "floating" would
        // strand the panel at a pair of coordinates that meant something else entirely.
        PalettePlacement.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void TheStatesPlacementIsTheWholeOfWhatAConsumerHasToStore()
    {
        var state = new FloatingPaletteState { Side = Layout.DockSide.Top, OffsetAlong = 42f };
        var stored = state.Placement.ToString();

        PalettePlacement.TryParse(stored, out var read).ShouldBeTrue();
        var restored = new FloatingPaletteState { Placement = read };

        restored.Side.ShouldBe(Layout.DockSide.Top);
        restored.OffsetAlong.ShouldBe(42f);
        restored.OffsetAcross.ShouldBe(0f);
    }

    private sealed class UnitContext : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) =>
            new(text.Length * fontSize, fontSize);

        public float ToSurface(float designUnits) => designUnits;
    }
}
