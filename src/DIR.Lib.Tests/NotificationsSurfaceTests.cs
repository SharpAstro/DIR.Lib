using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib.Notifications;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Every toast card and the open bar are surfaces (#282): the page beneath gets neither the hover, the tooltip, the
/// cursor, a press nor the wheel there. A fading toast keeps its <see cref="NotificationHit"/> and its click; a staying
/// card's body takes the press and does nothing with it, and its own buttons act. Driven through a real
/// <see cref="InputRouter"/> over one widget painting the whole <see cref="NotificationsLayout.Build(Layout.Node, NotificationFeed, NotificationsState, NotificationsColors, NotificationsStyle)"/>
/// tree, as pilot paints it, at a wide and a narrow window.
/// </summary>
public class NotificationsSurfaceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly NotificationsColors Colors = new()
    {
        Info = new(0x10, 0x80, 0xff, 0xff),
        Warn = new(0xff, 0xb0, 0x20, 0xff),
        Error = new(0xff, 0x30, 0x30, 0xff),
        Success = new(0x20, 0xc0, 0x60, 0xff),
        Surface = new(0x20, 0x20, 0x28, 0xff),
        Card = new(0x30, 0x30, 0x3a, 0xff),
        Text = new(0xee, 0xee, 0xee, 0xff),
        Dim = new(0x90, 0x90, 0x98, 0xff),
        Separator = new(0x50, 0x50, 0x58, 0xff),
        Track = new(0x61, 0x62, 0x63, 0xff),
    };

    public static TheoryData<float, float> Windows => new() { { 1000f, 640f }, { 420f, 800f } };

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = T0;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class Harness
    {
        private readonly DeclarationWidget _widget;
        private readonly RectF32 _window;

        public Harness(float w, float h)
        {
            _window = new RectF32(0, 0, w, h);
            _widget = new DeclarationWidget(new DeclarationStubRenderer((uint)w, (uint)h));
            PageScroll.SetExtent(new RectF32(0, 0, w, h), atomExtentPx: 10f, totalAtoms: 400, DesignScale.One);
            Router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), () => { })
            {
                Widgets = () => [_widget],
                Unhandled = e =>
                {
                    Unhandled.Add(e);
                    return false;
                },
                Clock = Clock,
            };
        }

        public NotificationFeed Feed { get; } = new();
        public NotificationsState State { get; } = new();
        public InputRouter Router { get; }
        public ManualClock Clock { get; } = new();
        public ListScrollController PageScroll { get; } = new();
        public List<InputEvent> Unhandled { get; } = [];
        public int PageClicks;

        /// <summary>The application's page: one region over all of it, with a hover, a tooltip, a cursor, a click and a wheel.</summary>
        private Layout.Node Page() => Layout.Builder.Spacer().Stretch()
            .BgHover(new RGBAColor32(40, 40, 60, 255))
            .WithTooltip("page")
            .WithScroll(PageScroll)
            .Clickable(new HitResult.ButtonHit("page"), _ => PageClicks++, CursorKind.Pointer);

        public void Paint()
        {
            var tree = NotificationsLayout.Build(Page(), Feed, State, Colors, new NotificationsStyle());
            _widget.Render(tree, _window);
            Router.AfterPaint();
        }

        public List<ClickableRegion> Regions()
        {
            var list = new List<ClickableRegion>();
            _widget.CollectPaintedRegions(list);
            return list;
        }

        public List<ClickableRegion> Surfaces() => [.. Regions().Where(r => r.IsSurface)];

        public HitResult? HitTest(float x, float y) => _widget.HitTest(x, y);

        public bool Press(float x, float y) => Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));

        public TooltipRequest? TooltipAfterRest(float x, float y)
        {
            Router.Handle(new InputEvent.MouseMove(x, y, MouseButton.None));
            Clock.Advance(TimeSpan.FromSeconds(1));
            return Router.Tooltip;
        }
    }

    // A point inside a card's left padding, beside its severity border: the card's body and nothing of its own over it.
    private static (float X, float Y) BodyOf(ClickableRegion card) => (card.X + 6f, card.Y + card.Height / 2f);

    private static Notification Stay(string title) => new() { Title = title, Severity = NotificationSeverity.Warn, Kind = NotificationKind.Staying };

    private static Notification Fade(string title) => new() { Title = title, Kind = NotificationKind.Fading };

    [Theory]
    [MemberData(nameof(Windows))]
    public void EveryToastCardIsASurface_AndAFadingOneKeepsItsNotificationHit(float w, float h)
    {
        var harness = new Harness(w, h);
        var fading = harness.Feed.Post(Fade("Saved"), T0);
        harness.Feed.Post(Stay("Press refused"), T0);
        harness.Paint();

        var cards = harness.Surfaces();
        cards.Count.ShouldBe(2, "one surface per toast card");
        cards.Count(c => c.Result is NotificationHit { Part: NotificationPart.Toast } hit && hit.Id == fading.Id).ShouldBe(1);
        cards.Count(c => c.Result is HitResult.ChromeHit).ShouldBe(1, "the staying card's body is plain chrome, and a surface");

        var fadingCard = cards.Single(c => c.Result is NotificationHit);
        var (x, y) = BodyOf(fadingCard);
        NotificationsLayout.ToastIdOf(harness.HitTest(x, y)).ShouldBe(fading.Id, "the host still reads the fading toast under the pointer");
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void AStayingCardsBodyTakesThePressAndDoesNothing_ThePageBeneathGetsNothing(float w, float h)
    {
        var harness = new Harness(w, h);
        var staying = harness.Feed.Post(Stay("Press refused"), T0);
        harness.Paint();
        var (x, y) = BodyOf(harness.Surfaces().ShouldHaveSingleItem());

        harness.Press(x, y).ShouldBeTrue("the card takes the press");

        harness.PageClicks.ShouldBe(0);
        harness.Unhandled.ShouldNotContain(e => e is InputEvent.MouseDown);
        harness.Feed.Toasts.ShouldContain(t => t.Notification.Id == staying.Id, "the body does nothing with it");
        harness.TooltipAfterRest(x, y).ShouldBeNull("the page's tooltip does not show through the card");
        harness.Router.CursorAt(x, y).ShouldBeNull("nor its cursor");
        harness.Router.Handle(new InputEvent.Scroll(-3f, x, y)).ShouldBeTrue();
        harness.PageScroll.Offset.ShouldBe(0f, "nor does the wheel scroll it");
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void AStayingCardsDismissButtonStillActs(float w, float h)
    {
        var harness = new Harness(w, h);
        var staying = harness.Feed.Post(Stay("Press refused"), T0);
        harness.Paint();
        var dismiss = harness.Regions().Single(r => r.Result is NotificationHit { Part: NotificationPart.Dismiss });

        harness.Press(dismiss.X + dismiss.Width / 2f, dismiss.Y + dismiss.Height / 2f);

        harness.Feed.Toasts.ShouldNotContain(t => t.Notification.Id == staying.Id);
        harness.PageClicks.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void APressOnAFadingToastIsTheToastsAndNotThePages(float w, float h)
    {
        var harness = new Harness(w, h);
        harness.Feed.Post(Fade("Saved"), T0);
        harness.Paint();
        var (x, y) = BodyOf(harness.Surfaces().ShouldHaveSingleItem());

        harness.Press(x, y).ShouldBeTrue();

        harness.PageClicks.ShouldBe(0);
        harness.TooltipAfterRest(x, y).ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void TheOpenBarIsASurface_ThePageBeneathItGetsNothing(float w, float h)
    {
        var harness = new Harness(w, h);
        harness.Feed.Post(Stay("Press refused"), T0);
        harness.State.Open = true;
        harness.Paint();
        var bar = harness.Surfaces().ShouldHaveSingleItem("the open bar, and no toast while it is open");
        // The bar's bottom left, below its list's last row: the bar's own chrome and nothing of its own over it.
        var (x, y) = (bar.X + 2f, bar.Y + bar.Height - 2f);

        harness.Press(x, y).ShouldBeTrue();

        harness.PageClicks.ShouldBe(0);
        harness.TooltipAfterRest(x, y).ShouldBeNull();
        harness.Router.CursorAt(x, y).ShouldBe(CursorKind.Default, "the bar's own cursor, not the page's hand");
        harness.Router.Handle(new InputEvent.Scroll(-3f, x, y));
        harness.PageScroll.Offset.ShouldBe(0f);
    }
}
