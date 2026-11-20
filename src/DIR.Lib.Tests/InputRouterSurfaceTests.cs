using System;
using System.Collections.Generic;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// What is painted over something takes the pointer from it (<see cref="Layout.Node.IsSurface"/>, #282). A row painted
/// first, a surface painted over part of it: within the surface's rect the row gets no tooltip, no cursor, no press, no
/// release, no double click, no context menu and no wheel. Each case runs twice, the surface painted by a LATER widget and
/// painted LATER in the same tree, because the router reaches the two through different walks (the next widget, the next
/// region of one widget) and either could leak alone. The later widget runs twice more, sharing the earlier widget's
/// settings and with settings of its own (a host that paints its toasts in a widget it never shares with the page, as pilot
/// does), because the painter's hover reads the surfaces off the settings it paints with. The surface's own button and
/// tooltip act as ever.
/// </summary>
public class InputRouterSurfaceTests
{
    // The surface covers x 0..100, y 0..100. The row runs the full width at y 0..20, so (10, 10) is a point of the row
    // under the surface and (150, 10) a point of the row in the open. The surface's own button is x 20..60, y 40..60.
    private static readonly (float X, float Y) Covered = (10f, 10f);
    private static readonly (float X, float Y) Open = (150f, 10f);
    private static readonly (float X, float Y) OnButton = (30f, 50f);

    /// <summary>Where the surface is painted, relative to the row.</summary>
    public enum Placement
    {
        /// <summary>Later in the same tree: the row, then an overlay layer with the surface.</summary>
        SameTree,

        /// <summary>By a later widget that shares the row's widget's settings.</summary>
        LaterWidget,

        /// <summary>By a later widget with settings of its own.</summary>
        LaterWidgetOwnSettings,
    }

    public static TheoryData<Placement> Placements => new() { Placement.SameTree, Placement.LaterWidget, Placement.LaterWidgetOwnSettings };

    private static readonly RGBAColor32 RowHot = new(40, 40, 60, 255);

    // The row's own background, so each frame repaints the pixel (the stub renderer does not clear between frames).
    private static readonly RGBAColor32 RowRest = new(20, 30, 50, 255);

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>The row's handlers, counted.</summary>
    private sealed class Row
    {
        public int DoubleClicks;
        public int Presses;
        public int Releases;
        public int Menus;
        public ListScrollController Scroll { get; } = NewScroll();

        private static ListScrollController NewScroll()
        {
            var scroll = new ListScrollController();
            scroll.SetExtent(new RectF32(0, 0, 200, 20), atomExtentPx: 10f, totalAtoms: 40, DesignScale.One);
            return scroll;
        }

        /// <summary>A full-width row at the top: hover, tooltip, cursor, click, double click, drag, menu and wheel.</summary>
        public Layout.Node Node() => Layout.Builder.Spacer().WStar().RowH(20f)
            .Bg(RowRest)
            .BgHover(RowHot)
            .WithTooltip("row")
            .WithScroll(Scroll)
            .WithContextMenu(_ => Menus++)
            .DoubleClickable(_ => DoubleClicks++)
            .Pressable(new HitResult.ButtonHit("row"), press =>
            {
                Presses++;
                // A second press declines the drag, so the double click is what it means.
                return press.Clicks >= 2 ? null : new DragCapture(_ => { }, _ => Releases++);
            }, CursorKind.Pointer);
    }

    private sealed class Harness
    {
        public Harness(Placement placement)
        {
            _placement = placement;
            Renderer = new DeclarationStubRenderer(200, 200);
            Back = new DeclarationWidget(Renderer);
            Front = new DeclarationWidget(Renderer);
            if (placement is not Placement.LaterWidgetOwnSettings)
            {
                Back.ShareWith(Front);
            }

            Router = new InputRouter(Back.Ui, new BackgroundTaskTracker(), () => Redraws++)
            {
                Widgets = () => [Back, Front],
                Unhandled = evt =>
                {
                    UnhandledEvents.Add(evt);
                    return false;
                },
                Clock = Clock,
            };
        }

        private readonly Placement _placement;

        public DeclarationStubRenderer Renderer { get; }
        public DeclarationWidget Back { get; }
        public DeclarationWidget Front { get; }
        public InputRouter Router { get; }
        public ManualClock Clock { get; } = new();
        public int Redraws { get; set; }
        public List<InputEvent> UnhandledEvents { get; } = [];
        public Row Row { get; } = new();
        public int ButtonClicks;

        /// <summary>The surface: a 100x100 card holding a button with a tooltip of its own.</summary>
        public Layout.Node Surface(bool surface = true)
        {
            var button = Layout.Builder.Spacer().WFixed(40f).HFixed(20f)
                .WithTooltip("button")
                .Clickable(new HitResult.ButtonHit("button"), _ => ButtonClicks++, CursorKind.Pointer);
            var card = Layout.Builder.VStack(
                    Layout.Builder.Spacer().RowH(40f),
                    Layout.Builder.HStack(Layout.Builder.Spacer().WFixed(20f), button))
                .WFixed(100f).HFixed(100f);
            return surface ? card.AsSurface() : card;
        }

        /// <summary>A card at the top left of a layer that is otherwise empty (it registers nothing).</summary>
        private Layout.Node Layer(bool surface) => Layout.Builder.HStack(Surface(surface), Layout.Builder.Spacer().WStar());

        /// <summary>Paints one frame. Placed in a later widget: the row in Back, the surface in Front; otherwise both in
        /// Back's one tree (the row, then an overlay layer with the surface) and Front paints nothing.</summary>
        public void Paint(bool surface = true)
        {
            var bounds = new RectF32(0, 0, 200, 200);
            var page = Layout.Builder.VStack(Row.Node()).Stretch();
            if (_placement is not Placement.SameTree)
            {
                Back.Render(page, bounds);
                Front.Render(Layer(surface), bounds);
            }
            else
            {
                Back.Render(Layout.Builder.Overlay(page, Layer(surface)), bounds);
                Front.Render(Layout.Builder.Spacer(), bounds);
            }

            Router.AfterPaint();
        }

        public bool Press((float X, float Y) at, int clicks = 1, MouseButton button = MouseButton.Left)
            => Router.Handle(new InputEvent.MouseDown(at.X, at.Y, button, InputModifier.None, clicks));

        public bool Release((float X, float Y) at) => Router.Handle(new InputEvent.MouseUp(at.X, at.Y, MouseButton.Left));

        public bool Move((float X, float Y) at) => Router.Handle(new InputEvent.MouseMove(at.X, at.Y, MouseButton.None));

        /// <summary>The pixel at <paramref name="at"/> of the frame last painted.</summary>
        public RGBAColor32 PixelAt((float X, float Y) at)
        {
            var img = Renderer.Surface;
            var i = (((int)at.Y * img.Width) + (int)at.X) * 4;
            return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
        }

        public TooltipRequest? TooltipAfterRest((float X, float Y) at)
        {
            Move(at);
            Clock.Advance(TimeSpan.FromSeconds(1));
            return Router.Tooltip;
        }
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void TheRowUnderASurfaceShowsNoTooltip_TheRowInTheOpenAndTheSurfacesButtonDo(Placement placement)
    {
        var h = new Harness(placement);
        h.Paint();

        h.TooltipAfterRest(Covered).ShouldBeNull("the card's body has nothing to say, and the row beneath must not say it for it");
        h.TooltipAfterRest(Open).ShouldNotBeNull().Text.ShouldBe("row");
        h.TooltipAfterRest(OnButton).ShouldNotBeNull().Text.ShouldBe("button");
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void WithoutTheSurfaceTheRowsTooltipShowsThrough_WhichIsTheBugThisPins(Placement placement)
    {
        var h = new Harness(placement);
        h.Paint(surface: false);

        h.TooltipAfterRest(Covered).ShouldNotBeNull().Text.ShouldBe("row");
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void TheRowUnderASurfaceSetsNoCursor(Placement placement)
    {
        var h = new Harness(placement);
        h.Paint();

        h.Router.CursorAt(Covered.X, Covered.Y).ShouldBeNull("the host's default over the card, not the row's hand");
        h.Router.CursorAt(Open.X, Open.Y).ShouldBe(CursorKind.Pointer);
        h.Router.CursorAt(OnButton.X, OnButton.Y).ShouldBe(CursorKind.Pointer);
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void APressAndReleaseOnASurfaceNeverReachTheRow_AndAreNotTheHostsEither(Placement placement)
    {
        var h = new Harness(placement);
        h.Paint();

        h.Press(Covered).ShouldBeTrue("the surface takes the press");
        h.Release(Covered);

        h.Row.Presses.ShouldBe(0);
        h.Row.Releases.ShouldBe(0, "the row armed no drag, so no release of its own runs");
        h.UnhandledEvents.ShouldNotContain(e => e is InputEvent.MouseDown);

        h.Press(Open);
        h.Release(Open);
        h.Row.Presses.ShouldBe(1);
        h.Row.Releases.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void ADoubleClickOnASurfaceDoesNotOpenTheRow(Placement placement)
    {
        var h = new Harness(placement);
        h.Paint();

        h.Press(Covered, clicks: 1);
        h.Release(Covered);
        h.Press(Covered, clicks: 2);
        h.Release(Covered);

        h.Row.DoubleClicks.ShouldBe(0);

        h.Press(Open, clicks: 1);
        h.Release(Open);
        h.Press(Open, clicks: 2);
        h.Release(Open);
        h.Row.DoubleClicks.ShouldBe(1, "the same gesture on the row in the open opens it");
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void ARightPressOnASurfaceOpensNotTheRowsMenu(Placement placement)
    {
        var h = new Harness(placement);
        h.Paint();

        h.Press(Covered, button: MouseButton.Right);
        h.Row.Menus.ShouldBe(0);

        h.Press(Open, button: MouseButton.Right);
        h.Row.Menus.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void TheWheelOverASurfaceScrollsNothingBeneath_NorReachesTheHost(Placement placement)
    {
        var h = new Harness(placement);
        h.Paint();

        h.Router.Handle(new InputEvent.Scroll(-3f, Covered.X, Covered.Y)).ShouldBeTrue("the surface takes the wheel");
        h.Row.Scroll.Offset.ShouldBe(0f);
        h.UnhandledEvents.ShouldNotContain(e => e is InputEvent.Scroll);

        h.Router.Handle(new InputEvent.Scroll(-3f, Open.X, Open.Y)).ShouldBeTrue();
        h.Row.Scroll.Offset.ShouldBeGreaterThan(0f);
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void TheSurfacesOwnButtonStillActs(Placement placement)
    {
        var h = new Harness(placement);
        h.Paint();

        h.Press(OnButton).ShouldBeTrue();

        h.ButtonClicks.ShouldBe(1);
        h.Row.Presses.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void AMoveOntoTheSurfaceFromTheOpenRowUnlightsIt_AndAMoveWithinTheCoveredPartAsksForNothing(Placement placement)
    {
        var h = new Harness(placement);
        h.Paint();
        h.Move(Open);
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        h.Router.AfterPaint();

        h.Redraws = 0;
        h.Move(Covered);
        h.Redraws.ShouldBeGreaterThan(0, "the lit row goes dark and its tooltip goes");

        h.Redraws = 0;
        h.Move((Covered.X + 5f, Covered.Y));
        h.Redraws.ShouldBe(0, "nothing under the card lights, so a move there changes nothing");
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void TheRowUnderASurfaceDoesNotLight_ByTheFrameAfterTheSurfaceAppears(Placement placement)
    {
        var h = new Harness(placement);
        h.Move(Covered);

        // The frame the surface appears in carries nothing yet, so the router asks for one more; that one is dark.
        h.Paint();
        h.Redraws = 0;
        h.Paint();
        h.PixelAt(Covered).ShouldBe(RowRest, "the row lies under the card here, however the two widgets hold their settings");
        h.Redraws.ShouldBe(0, "a settled surface asks for no further frame");

        h.Move(Open);
        h.Paint();
        h.PixelAt(Open).ShouldBe(RowHot, "the row in the open lights as ever");
    }
}
