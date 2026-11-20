using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib.Notifications;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The tinted toast (look B) by rule rather than by picture: contrast of each ink against the fill it is drawn on,
/// the Close cross against the card's own region, no shadow in cells, and that 11.86-style initializers still compile.
/// </summary>
public class NotificationsToastLookTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly NotificationSeverity[] Severities =
        [NotificationSeverity.Info, NotificationSeverity.Success, NotificationSeverity.Warn, NotificationSeverity.Error];

    private static readonly UiPalette Dark = new()
    {
        ContentBg = new(18, 18, 20, 255),
        PanelBg = new(28, 29, 32, 255),
        HeaderBg = new(40, 42, 46, 255),
        Separator = new(64, 66, 72, 255),
        BodyText = new(220, 222, 226, 255),
        DimText = new(150, 154, 162, 255),
        Accent = new(90, 160, 255, 255),
        Selection = new(50, 80, 130, 255),
        HeaderText = new(235, 237, 240, 255),
        SelectionText = new(255, 255, 255, 255),
        Info = new(90, 160, 255, 255),
        Warn = new(240, 190, 60, 255),
        Error = new(230, 80, 80, 255),
        Success = new(60, 200, 110, 255),
    };

    public static TheoryData<string> Palettes => new() { "dark", "light" };

    private static UiPalette PaletteOf(string name) => name == "dark" ? Dark : NotificationsRenderTests.Light;

    [Theory]
    [MemberData(nameof(Palettes))]
    public void TitleBodyAndButtonInkReach4_5To1OnTheFillTheyAreDrawnOn(string name)
    {
        var p = PaletteOf(name);
        var c = NotificationsColors.From(p);
        foreach (var s in Severities)
        {
            var fill = c.TintedFill(s);
            RGBAColor32.ContrastRatio(c.Text, fill).ShouldBeGreaterThanOrEqualTo(4.5f, $"{name} title on {s}");
            RGBAColor32.ContrastRatio(c.Body, fill).ShouldBeGreaterThanOrEqualTo(4.5f, $"{name} body on {s}");
            RGBAColor32.ContrastRatio(c.InkOn(s), c.Of(s)).ShouldBeGreaterThanOrEqualTo(4.5f, $"{name} primary button ink on {s}");
            // The plain button and the Close cross sit on the card's own fill, in Text ink.
            RGBAColor32.ContrastRatio(c.Text, fill).ShouldBeGreaterThanOrEqualTo(4.5f, $"{name} plain button on {s}");
        }
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void IconAndEdgeReach3To1(string name)
    {
        var p = PaletteOf(name);
        var c = NotificationsColors.From(p);
        foreach (var s in Severities)
        {
            RGBAColor32.ContrastRatio(c.Of(s), c.TintedFill(s)).ShouldBeGreaterThanOrEqualTo(3f, $"{name} icon on its fill, {s}");
            RGBAColor32.ContrastRatio(c.Of(s), p.ContentBg).ShouldBeGreaterThanOrEqualTo(3f, $"{name} icon on the page, {s}");
            RGBAColor32.ContrastRatio(c.TintedEdge(s), p.ContentBg).ShouldBeGreaterThanOrEqualTo(1.5f, $"{name} edge against the page, {s}");
        }
    }

    [Fact]
    public void OneHuePaletteIsNotTintedAndCarriesNoShadow()
    {
        var c = NotificationsColors.From(NotificationsRenderTests.OneHue);
        c.OneHue.ShouldBeTrue();
        c.ToastShadow.ShouldBeNull();
        c.TintedFill(NotificationSeverity.Error).ShouldBe(c.Raised);
        c.TintedFill(NotificationSeverity.Info).ShouldBe(c.TintedFill(NotificationSeverity.Warn));
    }

    // ---- Close against the card ----

    private sealed class Harness
    {
        private readonly DeclarationWidget _widget;
        private readonly RectF32 _window = new(0, 0, 800, 480);

        public Harness()
        {
            _widget = new DeclarationWidget(new DeclarationStubRenderer(800, 480));
            Router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), () => { })
            {
                Widgets = () => [_widget],
                Unhandled = _ => false,
            };
        }

        public NotificationFeed Feed { get; } = new();
        public InputRouter Router { get; }

        public void Paint()
        {
            var tree = NotificationsLayout.Build(Layout.Builder.Spacer().Stretch(), Feed, new NotificationsState(),
                NotificationsColors.From(Dark), new NotificationsStyle());
            _widget.Render(tree, _window);
            Router.AfterPaint();
        }

        public List<ClickableRegion> Regions()
        {
            var list = new List<ClickableRegion>();
            _widget.CollectPaintedRegions(list);
            return list;
        }

        public HitResult? HitTest(float x, float y) => _widget.HitTest(x, y);

        public void Click(float x, float y)
        {
            Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
            Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left, InputModifier.None));
        }
    }

    [Fact]
    public void CloseRegionDismissesAFadingToast_AndTheCardsOwnHitStillReachesTheHost()
    {
        var h = new Harness();
        var n = h.Feed.Post(new Notification { Title = "Saved", Text = "Frame 12", Kind = NotificationKind.Fading }, T0);
        h.Feed.Advance(T0);
        h.Paint();

        var close = h.Regions().Single(r => r.Result is NotificationHit { Part: NotificationPart.Close });
        var card = h.Regions().Single(r => r.Result is NotificationHit { Part: NotificationPart.Toast });
        close.X.ShouldBeGreaterThan(card.X + card.Width / 2f, "the cross is at the right");
        close.Y.ShouldBeLessThan(card.Y + card.Height / 2f, "and at the top");

        // Over the card's body the host still reads the toast (it pauses the countdown through it) and nothing dismisses.
        NotificationsLayout.ToastIdOf(h.HitTest(card.X + 40f, card.Y + card.Height / 2f)).ShouldBe(n.Id);
        h.Click(card.X + 40f, card.Y + card.Height / 2f);
        h.Feed.Toasts.Count.ShouldBe(1);

        // Over the cross the hit is the Close part, not the card's.
        h.HitTest(close.X + close.Width / 2f, close.Y + close.Height / 2f).ShouldBe(new NotificationHit(NotificationPart.Close, n.Id));
        h.Click(close.X + close.Width / 2f, close.Y + close.Height / 2f);
        h.Feed.Toasts.ShouldBeEmpty();
    }

    [Fact]
    public void AStayingCardHasNoCloseCrossAndItsFirstButtonIsTheAction()
    {
        var h = new Harness();
        h.Feed.Post(new Notification { Title = "Gone", Kind = NotificationKind.Staying, ActionLabel = "Reconnect", Action = () => { } }, T0);
        h.Feed.Advance(T0);
        h.Paint();

        var regions = h.Regions();
        regions.Any(r => r.Result is NotificationHit { Part: NotificationPart.Close }).ShouldBeFalse();
        var action = regions.Single(r => r.Result is NotificationHit { Part: NotificationPart.Action });
        var dismiss = regions.Single(r => r.Result is NotificationHit { Part: NotificationPart.Dismiss });
        action.X.ShouldBeLessThan(dismiss.X);
        action.Height.ShouldBe(dismiss.Height, "one height for the row of buttons");
        action.Y.ShouldBe(dismiss.Y, "and one top");
    }

    // ---- cells ----

    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
        public object? MeasureKey => "toast-look-cells";
    }

    private static string Arranged(NotificationsColors colors)
    {
        var tree = NotificationsLayout.Build(Layout.Builder.Spacer().Stretch(), NotificationsRenderTests.LookFeed(),
            new NotificationsState(), colors, new NotificationsStyle { MaxToasts = 5 });
        return string.Join('\n', Layout.Engine.Arrange(tree, new Rect<int>(0, 0, 100, 45), new CellCtx())
            .Select(a => $"{a.Depth} {a.Node.GetType().Name} {a.Bounds.X} {a.Bounds.Y} {a.Bounds.Width} {a.Bounds.Height}"));
    }

    [Fact]
    public void AShadowTakesNoCellsSoTheCellArrangeIsTheSameWithAndWithoutIt()
    {
        var withShadow = NotificationsColors.From(Dark);
        withShadow.ToastShadow.ShouldNotBeNull();
        Arranged(withShadow).ShouldBe(Arranged(withShadow with { Shadow = new Layout.ShadowStyle { Color = new RGBAColor32(0, 0, 0, 0) } }));
    }

    [Fact]
    public void EachSeverityIconAndCloseHaveOneDistinctCellCharacter()
    {
        var kinds = new[] { Layout.IconKind.Info, Layout.IconKind.Warning, Layout.IconKind.Error, Layout.IconKind.Close };
        kinds.Select(IconGlyphs.For).Distinct().Count().ShouldBe(4);
        kinds.ShouldAllBe(k => IconGlyphs.For(k) != '?');
    }

    // ---- compatibility ----

    [Fact]
    public void Initializers11_86StillCompile()
    {
        var colors = new NotificationsColors
        {
            Info = new(1, 2, 3, 255), Warn = new(4, 5, 6, 255), Error = new(7, 8, 9, 255), Success = new(10, 11, 12, 255),
            Surface = new(20, 20, 20, 255), Card = new(30, 30, 30, 255), Text = new(240, 240, 240, 255),
            Dim = new(150, 150, 150, 255), Separator = new(60, 60, 60, 255), Track = new(90, 90, 90, 255),
        };
        var style = new NotificationsStyle { ToastWidth = 280f, MaxToasts = 2, Corner = ToastCorner.TopRight, FontSize = 12f };
        var feed = new NotificationFeed();
        feed.Post(new Notification { Title = "t" }, T0);
        NotificationsLayout.Build(Layout.Builder.Spacer(), feed, new NotificationsState(), colors, style).ShouldNotBeNull();
        colors.Raised.ShouldBe(colors.Card);
        colors.Body.ShouldBe(colors.Text);
    }
}
