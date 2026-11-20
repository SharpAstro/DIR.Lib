using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib.ChildWindows;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// A floating window is a surface (#282, on top of #276): within its rect nothing painted before it, the host's content
/// or a window behind it, gets the tooltip, the cursor, the hover, a press or the wheel. Before 11.86 the window's body
/// region already swallowed a press, but a body states no tooltip and no scroll, so the host's tooltip showed through it,
/// the wheel scrolled the host's list behind it, a back window's close button lit and explained itself through the window
/// in front, and a maximised window's border, where no region lies, passed a press to the host.
/// </summary>
public class ChildWindowsSurfaceTests
{
    private static readonly RectF32 Host = new(0, 0, 800, 600);

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class Harness
    {
        private readonly DeclarationWidget _widget;
        private Layout.Node _root = Layout.Builder.Spacer();

        public Harness()
        {
            _widget = new DeclarationWidget(new DeclarationStubRenderer((uint)Host.Width, (uint)Host.Height));
            Set.NoteHost(Host);
            HostScroll.SetExtent(Host, atomExtentPx: 10f, totalAtoms: 400, DesignScale.One);
            Router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), () => Redraws++)
            {
                Widgets = () => [_widget],
                Unhandled = _ => false,
                Clock = Clock,
            };
        }

        public ChildWindowSet Set { get; } = new();
        public InputRouter Router { get; }
        public ManualClock Clock { get; } = new();
        public ListScrollController HostScroll { get; } = new();
        public int HostPresses;
        public int Redraws;

        /// <summary>The host's content: one region over all of it, with a hover, a tooltip, a cursor, a press and a wheel.</summary>
        private Layout.Node Content() => Layout.Builder.Spacer().WStar().HStar()
            .BgHover(new RGBAColor32(40, 40, 60, 255))
            .WithTooltip("host")
            .WithScroll(HostScroll)
            .Clickable(new HitResult.ButtonHit("host"), _ => HostPresses++, CursorKind.Pointer);

        public void Paint()
        {
            _root = ChildWindowsLayout.Build(Content(), Set, _ => Layout.Builder.Spacer(), ChildWindowColors.Dark, null, DesignScale.One);
            _widget.Render(_root, Host);
            Router.AfterPaint();
        }

        public RectF32 Of(long id, ChildWindowPart part)
        {
            var n = Layout.Engine.Arrange(_root, new Rect<float>(Host.X, Host.Y, Host.Width, Host.Height), _widget.Context())
                .First(a => a.Node.Hit is ChildWindowHit h && h.Id == id && h.Part == part);
            return new RectF32(n.Bounds.X, n.Bounds.Y, n.Bounds.Width, n.Bounds.Height);
        }

        public bool Press(float x, float y) => Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));

        public void Move(float x, float y) => Router.Handle(new InputEvent.MouseMove(x, y, MouseButton.None));

        public TooltipRequest? TooltipAfterRest(float x, float y)
        {
            Move(x, y);
            Clock.Advance(TimeSpan.FromSeconds(1));
            return Router.Tooltip;
        }
    }

    private static bool Inside(RectF32 r, float x, float y) => x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height;

    [Fact]
    public void TheHostsTooltipAndWheelDoNotReachThroughAWindowsBody()
    {
        var h = new Harness();
        var window = h.Set.Add("Log", new RectF32(100, 100, 300, 200));
        h.Paint();
        var body = h.Of(window.Id, ChildWindowPart.Body);
        var (x, y) = (body.X + body.Width / 2f, body.Y + body.Height / 2f);

        h.TooltipAfterRest(x, y).ShouldBeNull("the body says nothing, and the host's tooltip must not say it for it");
        h.Router.CursorAt(x, y).ShouldBe(CursorKind.Default);
        h.Router.Handle(new InputEvent.Scroll(-3f, x, y)).ShouldBeTrue();
        h.HostScroll.Offset.ShouldBe(0f, "the wheel over a window does not scroll the host behind it");
        h.Press(x, y);
        h.HostPresses.ShouldBe(0);

        // The host in the open answers as ever.
        h.TooltipAfterRest(20f, 20f).ShouldNotBeNull().Text.ShouldBe("host");
        h.Router.Handle(new InputEvent.Scroll(-3f, 20f, 20f));
        h.HostScroll.Offset.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void ABackWindowsCloseButtonDoesNotLightNorExplainItselfThroughTheWindowInFront()
    {
        var h = new Harness();
        var back = h.Set.Add("Back", new RectF32(40, 80, 240, 180));
        var front = h.Set.Add("Front", new RectF32(100, 20, 300, 260));
        h.Paint();
        var close = h.Of(back.Id, ChildWindowPart.Close);
        var (x, y) = (close.X + close.Width / 2f, close.Y + close.Height / 2f);
        Inside(h.Of(front.Id, ChildWindowPart.Body), x, y).ShouldBeTrue("the test needs the front body over the back close");

        h.TooltipAfterRest(x, y).ShouldBeNull("not the back window's \"Close\"");

        // From elsewhere on the front body, a move onto the covered close button changes nothing the frame shows.
        h.Move(x, y + 40f);
        h.Router.AfterPaint();
        h.Redraws = 0;
        h.Move(x, y);
        h.Redraws.ShouldBe(0, "the back window's close button does not light under the front window");
    }

    [Fact]
    public void AMaximisedWindowsBorderTakesAPressTheHostWouldOtherwiseGet()
    {
        var h = new Harness();
        var window = h.Set.Add("Big", new RectF32(100, 100, 300, 200));
        h.Set.SetMaximised(window.Id, true);
        h.Paint();

        // The left border column: the title bar and the body sit inside it, and a maximised window has no handles.
        h.Press(0.5f, Host.Height / 2f).ShouldBeTrue();

        h.HostPresses.ShouldBe(0);
        h.Router.CursorAt(0.5f, Host.Height / 2f).ShouldNotBe(CursorKind.Pointer, "not the host's hand either");
    }
}
