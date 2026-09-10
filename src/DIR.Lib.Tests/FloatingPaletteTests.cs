using DIR.Lib;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;

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

    private sealed class UnitContext : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) =>
            new(text.Length * fontSize, fontSize);

        public float ToSurface(float designUnits) => designUnits;
    }
}
