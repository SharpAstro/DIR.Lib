using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The <see cref="FloatingPalette"/> overload whose gestures are DECLARED: the grip's drag is a
/// <see cref="DragCapture"/> the router owns, a double press collapses by the platform's click count, and the panel
/// claims the pointer over its padding. Driven through <see cref="InputRouter"/>, since that is the only place a
/// capture, a click count and a swallowed press exist; the other overload left all three to each host.
/// </summary>
public class FloatingPaletteDeclaredGripTests
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

    private static readonly PaletteItem[] Items =
        [.. Enumerable.Range(0, 3).Select(i => new PaletteItem($"Row {i}", $"row:{i}", IsOn: i == 0, KeyHint: null, IsAvailable: true))];

    private sealed class Harness
    {
        private readonly DeclarationWidget _widget = new(new DeclarationStubRenderer(400, 300));

        public Harness()
            => Router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), () => { })
            {
                Widgets = () => [_widget],
                Unhandled = evt =>
                {
                    UnhandledEvents.Add(evt);
                    return false;
                },
            };

        public InputRouter Router { get; }

        public FloatingPaletteState State { get; } = new() { OffsetAlong = 20f };

        public DesignScale Scale { get; set; } = DesignScale.One;

        public List<InputEvent> UnhandledEvents { get; } = [];

        public int Changed { get; private set; }

        public int Released { get; private set; }

        public List<string> Items { get; } = [];

        public void Paint(bool declared = true)
            => _widget.Render(declared
                    ? FloatingPalette.Build(State, "LAYERS", FloatingPaletteDeclaredGripTests.Items, in Colors, 12f, "grip",
                        Items.Add, Scale, () => Changed++, () => Released++)
                    : FloatingPalette.Build(State, "LAYERS", FloatingPaletteDeclaredGripTests.Items, in Colors, 12f, "grip",
                        Items.Add, () => Items.Add("grip-press")),
                new RectF32(0, 0, 400, 300));

        public ClickableRegion Region(string action)
        {
            var regions = new List<ClickableRegion>();
            _widget.CollectPaintedRegions(regions);
            return regions.Single(r => r.Result is HitResult.ButtonHit { Action: var a } && a == action);
        }

        public HitResult? HitTest(float x, float y) => _widget.HitTest(x, y);

        public CursorKind? CursorAt(float x, float y) => _widget.HitTestCursor(x, y);

        public bool Press(float x, float y, int clicks = 1)
            => Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, clicks));

        public bool Move(float x, float y) => Router.Handle(new InputEvent.MouseMove(x, y, MouseButton.Left));

        public bool Release(float x, float y) => Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left));
    }

    private static (float X, float Y) Centre(in ClickableRegion r) => (r.X + r.Width / 2f, r.Y + r.Height / 2f);

    [Fact]
    public void TheGripsDragIsTheRoutersFromPressToRelease()
    {
        var h = new Harness();
        h.Paint();
        var (x, y) = Centre(h.Region("grip"));

        h.Press(x, y).ShouldBeTrue();
        h.State.IsDragging.ShouldBeTrue("the press begins the drag where it landed");

        h.Move(x, y + 40f).ShouldBeTrue("the capture takes the move");
        h.State.OffsetAlong.ShouldBe(60f, 0.01f, "a right-pinned panel slides down its edge with the pointer");

        h.Release(x, y + 40f).ShouldBeTrue();
        h.State.IsDragging.ShouldBeFalse();
        h.Released.ShouldBe(1);
        h.Changed.ShouldBeGreaterThanOrEqualTo(3, "the press, the move and the release each changed the panel");
        h.UnhandledEvents.ShouldBeEmpty("no part of the gesture reached the host's own handlers");
    }

    [Fact]
    public void ASecondPressCollapsesInsteadOfStartingAnotherDrag()
    {
        var h = new Harness();
        h.Paint();
        var (x, y) = Centre(h.Region("grip"));

        h.Press(x, y, clicks: 1);
        h.Release(x, y);
        h.Press(x, y, clicks: 2).ShouldBeTrue();

        h.State.Collapsed.ShouldBeTrue("the platform counted a double click");
        h.State.IsDragging.ShouldBeFalse("the second press declined the drag for the double-click");
    }

    [Fact]
    public void ADoublePressDoesNotCollapseAPaletteThatMayNot()
    {
        var h = new Harness();
        h.State.AllowCollapse = false;
        h.Paint();
        var (x, y) = Centre(h.Region("grip"));

        h.Press(x, y, clicks: 2).ShouldBeTrue();

        h.State.Collapsed.ShouldBeFalse();
        h.State.IsDragging.ShouldBeTrue("with no collapse to give it to, the second press drags like the first");
    }

    [Fact]
    public void AFreePanelFollowsThePointerInBothAxesInDesignUnits()
    {
        var h = new Harness();
        h.State.Side = null;
        h.State.OffsetAcross = 30f;
        h.Scale = new DesignScale(1.5f, 1.5f);
        h.Paint();
        var (x, y) = Centre(h.Region("grip"));

        h.Press(x, y);
        h.Move(x + 30f, y + 60f);

        h.State.OffsetAlong.ShouldBe(40f, 0.01f, "free, along is X: 30 device px at 1.5 is 20 design units");
        h.State.OffsetAcross.ShouldBe(70f, 0.01f, "and across is Y: 60 device px at 1.5 is 40");
    }

    [Fact]
    public void APressInThePanelsPaddingIsSwallowedAndAHitTestThereAnswersThePanel()
    {
        var h = new Harness();
        h.Paint();
        var panel = h.Region("grip:Panel");
        // The bottom padding: inside the panel, below its last row.
        var (x, y) = (panel.X + 2f, panel.Y + panel.Height - 2f);

        h.HitTest(x, y).ShouldBe(new HitResult.ButtonHit("grip:Panel"));
        h.CursorAt(x, y).ShouldBe(CursorKind.Default, "the padding shows the arrow, not what is behind it");
        h.Press(x, y).ShouldBeTrue();
        h.UnhandledEvents.ShouldBeEmpty("a press on the panel never reaches what is behind it");
        h.Items.ShouldBeEmpty("and it toggles no row");
    }

    [Fact]
    public void ARowStillTakesItsPressInsideTheClaimingPanel()
    {
        var h = new Harness();
        h.Paint();
        var (x, y) = Centre(h.Region("row:1"));

        h.Press(x, y);
        h.Release(x, y);

        h.Items.ShouldBe(["row:1"], "a row registers after the panel, so it wins its own rect");
    }

    [Fact]
    public void TheOtherOverloadKeepsItsClickGripAndClaimsNoPadding()
    {
        var h = new Harness();
        h.Paint(declared: false);
        var (x, y) = Centre(h.Region("grip"));

        h.Press(x, y);
        h.Release(x, y);

        h.Items.ShouldBe(["grip-press"], "the grip is still the click the host routes to PressGrip");
        h.State.IsDragging.ShouldBeFalse("nothing in the palette began a drag on its own");
        Should.Throw<InvalidOperationException>(() => h.Region("grip:Panel"), "no panel region is declared");
    }
}
