using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DIR.Lib.Notifications;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The notifications declaration, arranged by a float design-unit context and by an int cell context (8x16 design
/// units a cell). Every case runs on both: the rects are brought back to design units, and a cell is allowed its own
/// rounding.
/// </summary>
public class NotificationsLayoutTests
{
    private const float W = 1000f;
    private const float H = 640f;
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(double s) => T0.AddSeconds(s);

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

    private static readonly NotificationsStyle Style = new();

    private sealed class FloatCtx : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * fontSize * 0.55f, fontSize * 1.2f);
        public float ToSurface(float designUnits) => designUnits;
    }

    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
        public object? MeasureKey => "notifications-cell";

        // The cell rule of LayoutExtentHookTests.HookCellCtx, as a terminal surface answers it: a one-line row is one
        // row tall, and an extent that paints a fill never rounds away (a 3-unit countdown bar is one row, not none).
        public int MapExtent(in Layout.ExtentRequest r)
        {
            var unit = r.Axis == Layout.Axis.Horizontal ? 8f : 16f;
            if (r.Kind == Layout.ExtentKind.Padding)
            {
                return !r.HasContent && !r.HasFill ? 0 : (int)MathF.Floor(r.Value / unit);
            }

            if (r.Axis == Layout.Axis.Vertical && r.HasContent && r.ContentLines == 1)
            {
                return 1;
            }

            var rounded = (int)MathF.Round(r.Value / unit);
            if (!r.HasContent && !r.HasFill)
            {
                return 0;
            }

            return r.HasFill && r.Value > 0f ? Math.Max(1, rounded) : rounded;
        }
    }

    /// <summary>One arranged node with its rect in design units.</summary>
    private readonly record struct Placed(Layout.Node Node, RectF32 Rect);

    private static List<Placed> Arrange(Layout.Node root, bool cells)
    {
        if (!cells)
        {
            return [.. Layout.Engine.Arrange(root, new Rect<float>(0, 0, W, H), new FloatCtx())
                .Select(a => new Placed(a.Node, new RectF32(a.Bounds.X, a.Bounds.Y, a.Bounds.Width, a.Bounds.Height)))];
        }

        return [.. Layout.Engine.Arrange(root, new Rect<int>(0, 0, (int)(W / 8), (int)(H / 16)), new CellCtx())
            .Select(a => new Placed(a.Node, new RectF32(a.Bounds.X * 8f, a.Bounds.Y * 16f, a.Bounds.Width * 8f, a.Bounds.Height * 16f)))];
    }

    private static float Tol(bool cells) => cells ? 16f : 0.5f;

    private static readonly Layout.Node Page = Layout.Builder.Spacer().Stretch();

    private static Layout.Node Tree(NotificationFeed feed, NotificationsState state, NotificationsStyle? style = null)
        => NotificationsLayout.Build(Page, feed, state, Colors, style ?? Style);

    private static RectF32 ContentOf(bool cells)
        => Arrange(Tree(new NotificationFeed(), new NotificationsState()), cells).First(p => ReferenceEquals(p.Node, Page)).Rect;

    private static Notification Fade(string title = "f", NotificationSeverity s = NotificationSeverity.Info)
        => new() { Title = title, Severity = s, Kind = NotificationKind.Fading };

    private static Notification Stay(string title = "s", NotificationSeverity s = NotificationSeverity.Warn)
        => new() { Title = title, Severity = s, Kind = NotificationKind.Staying };

    private static List<Placed> Toasts(List<Placed> all)
        => [.. all.Where(p => p.Node.Hit is NotificationHit { Part: NotificationPart.Toast }).OrderBy(p => p.Rect.Y)];

    private static IEnumerable<string> TextsOf(List<Placed> all)
        => all.Where(p => p.Node is Layout.Node.Leaf { Content: Layout.Content.Text }).Select(p => ((Layout.Content.Text)((Layout.Node.Leaf)p.Node).Content).Value);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePagesRectIsTheSameOpenAndCollapsed(bool cells)
    {
        var feed = new NotificationFeed();
        feed.Post(Stay(), At(0));
        var closed = Arrange(Tree(feed, new NotificationsState { Open = false }), cells).First(p => ReferenceEquals(p.Node, Page)).Rect;
        var open = Arrange(Tree(feed, new NotificationsState { Open = true }), cells).First(p => ReferenceEquals(p.Node, Page)).Rect;

        open.ShouldBe(closed);
        closed.Right.ShouldBe(W - Style.StripWidth, Tol(cells));
        closed.Height.ShouldBe(H, Tol(cells));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOpenBarCoversTheRightOfThePage_AndTheStripStaysAtTheEdge(bool cells)
    {
        var feed = new NotificationFeed();
        var all = Arrange(Tree(feed, new NotificationsState { Open = true }), cells);
        var page = all.First(p => ReferenceEquals(p.Node, Page)).Rect;
        var strip = all.First(p => p.Node.Hit is NotificationHit { Part: NotificationPart.Bell }).Rect;
        var clear = all.First(p => p.Node.Hit is NotificationHit { Part: NotificationPart.Clear }).Rect;

        strip.Right.ShouldBe(W, Tol(cells));
        strip.Width.ShouldBe(Style.StripWidth, Tol(cells));
        clear.Right.ShouldBeGreaterThan(page.Right - Style.BarWidth);
        clear.Right.ShouldBeLessThanOrEqualTo(page.Right + Tol(cells));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToastsShowOnlyWhileCollapsed(bool cells)
    {
        var feed = new NotificationFeed();
        feed.Post(Fade("a"), At(0));
        feed.Post(Stay("b"), At(0));

        Toasts(Arrange(Tree(feed, new NotificationsState { Open = false }), cells)).ShouldNotBeEmpty();
        var open = Arrange(Tree(feed, new NotificationsState { Open = true }), cells);
        Toasts(open).ShouldBeEmpty();
        // The staying one is in the list instead, not as a toast.
        TextsOf(open).ShouldContain("b");
    }

    private static readonly NotificationsStyle TopRightStyle = new() { Corner = ToastCorner.TopRight };

    private static List<long> PostFive(NotificationFeed feed)
    {
        var ids = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(feed.Post(Fade($"t{i}"), At(i * 0.1)).Id);
        }

        return ids;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDefaultPutsTheStackAtTheBottomRightOfThePage(bool cells)
    {
        new NotificationsStyle().Corner.ShouldBe(ToastCorner.BottomRight);
        var feed = new NotificationFeed();
        feed.Post(Fade("a"), At(0));
        feed.Post(Fade("b"), At(0));
        var content = ContentOf(cells);

        var toasts = Toasts(Arrange(Tree(feed, new NotificationsState()), cells));

        toasts.Count.ShouldBe(2);
        toasts.Max(t => t.Rect.Bottom).ShouldBe(content.Bottom - Style.Margin, Tol(cells));
        toasts.Max(t => t.Rect.Right).ShouldBe(content.Right - Style.Margin, Tol(cells));
        toasts.ShouldAllBe(t => Math.Abs(t.Rect.Width - Style.ToastWidth) <= Tol(cells));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CornerTopRightPutsTheStackAtTheTopRight(bool cells)
    {
        var feed = new NotificationFeed();
        feed.Post(Fade("a"), At(0));
        var content = ContentOf(cells);

        var toast = Toasts(Arrange(Tree(feed, new NotificationsState(), TopRightStyle), cells)).Single().Rect;

        toast.Y.ShouldBe(content.Y + Style.Margin, Tol(cells));
        toast.Right.ShouldBe(content.Right - Style.Margin, Tol(cells));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtMostThreeToasts_NewestNearestTheCorner_InBothCorners(bool cells)
    {
        var feed = new NotificationFeed();
        var ids = PostFive(feed);

        var bottom = Toasts(Arrange(Tree(feed, new NotificationsState()), cells));
        var top = Toasts(Arrange(Tree(feed, new NotificationsState(), TopRightStyle), cells));

        // Bottom right: the newest is the bottom card. Top right: the newest is the top card. Sorted by Y either way.
        bottom.Select(t => ((NotificationHit)t.Node.Hit!).Id).ShouldBe([ids[2], ids[3], ids[4]]);
        top.Select(t => ((NotificationHit)t.Node.Hit!).Id).ShouldBe([ids[4], ids[3], ids[2]]);
    }

#pragma warning disable CS0618 // the obsolete overload is called on purpose: it must ignore the pointer
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePointerNeverMovesTheStack_NotEvenOverWhereATopRightStackWouldSit(bool cells)
    {
        var feed = new NotificationFeed();
        feed.Post(Fade("a"), At(0));
        feed.Post(Fade("b"), At(0));
        var content = ContentOf(cells);
        var none = Toasts(Arrange(Tree(feed, new NotificationsState()), cells)).Select(t => t.Rect).ToList();

        var pointers = new Vector2?[]
        {
            null,
            new Vector2(50f, 300f),
            new Vector2(content.Right - Style.Margin - Style.ToastWidth / 2f, content.Y + Style.Margin + 40f),
            none[0].Center,
            none[1].Center,
        };
        foreach (var pointer in pointers)
        {
            var tree = NotificationsLayout.Build(Page, feed, new NotificationsState(), Colors, Style, pointer, content);
            Toasts(Arrange(tree, cells)).Select(t => t.Rect).ShouldBe(none);
        }
    }
#pragma warning restore CS0618

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToastsDoNotSlide_AsTheirTimeRunsDown(bool cells)
    {
        var feed = new NotificationFeed();
        feed.Post(Fade("a"), At(0));
        var content = ContentOf(cells);
        var before = Toasts(Arrange(Tree(feed, new NotificationsState()), cells)).Single().Rect;
        feed.Advance(At(0));
        feed.Advance(At(2));
        var after = Toasts(Arrange(Tree(feed, new NotificationsState()), cells)).Single().Rect;

        after.ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheBellShowsCountAndWorstSeverity(bool cells)
    {
        var none = new NotificationFeed();
        BellInk(Arrange(Tree(none, new NotificationsState()), cells)).ShouldBe(Colors.Dim);

        var warn = new NotificationFeed();
        warn.Post(Stay("a", NotificationSeverity.Info), At(0));
        warn.Post(Stay("b", NotificationSeverity.Warn), At(0));
        var all = Arrange(Tree(warn, new NotificationsState()), cells);
        BellInk(all).ShouldBe(Colors.Warn);
        TextsOf(all).ShouldContain("2");

        var error = new NotificationFeed();
        error.Post(Stay("a", NotificationSeverity.Error), At(0));
        BellInk(Arrange(Tree(error, new NotificationsState()), cells)).ShouldBe(Colors.Error);
    }

    private static RGBAColor32 BellInk(List<Placed> all)
        => all.Select(p => p.Node).OfType<Layout.Node.Leaf>()
            .Select(l => l.Content).OfType<Layout.Content.Icon>().Single(i => i.Kind == Layout.IconKind.Bell).Color;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheBellTogglesTheBarByPress_AndByADeclaredKey(bool cells)
    {
        var feed = new NotificationFeed();
        var state = new NotificationsState();
        var bell = Arrange(Tree(feed, state), cells).First(p => p.Node.Hit is NotificationHit { Part: NotificationPart.Bell }).Node;

        bell.Shortcut.ShouldBe(Style.ToggleKey);
        bell.OnClick.ShouldNotBeNull();
        bell.OnClick!(InputModifier.None);
        state.Open.ShouldBeTrue();
        bell.OnClick!(InputModifier.None);
        state.Open.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheListPutsNeedsYouFirst_ThenHistory_WithSeverityTimeAndSource(bool cells)
    {
        var feed = new NotificationFeed();
        feed.Post(Fade("old one") with { Source = "mount" }, At(0));
        feed.Advance(At(10));
        feed.Post(Stay("needs me", NotificationSeverity.Error) with { Source = "camera" }, At(60));
        var all = Arrange(Tree(feed, new NotificationsState { Open = true }), cells);

        float YOf(string text) => all.First(p => p.Node is Layout.Node.Leaf { Content: Layout.Content.Text t } && t.Value == text).Rect.Y;

        YOf("Needs you (1)").ShouldBeLessThan(YOf("needs me"));
        YOf("needs me").ShouldBeLessThan(YOf("History (1)"));
        YOf("History (1)").ShouldBeLessThan(YOf("old one"));
        TextsOf(all).ShouldContain("camera");
        TextsOf(all).ShouldContain("mount");
        TextsOf(all).ShouldContain("12:01");
        TextsOf(all).ShouldContain("12:00");
        // The severity mark is the row's left border in the severity's colour.
        all.Any(p => p.Node.BorderStyle is { } b && b.Color == Colors.Error).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheListHasAClear_ThatEmptiesTheHistory(bool cells)
    {
        var feed = new NotificationFeed();
        feed.Post(Fade("old"), At(0));
        feed.Advance(At(10));
        feed.History.Count.ShouldBe(1);
        var clear = Arrange(Tree(feed, new NotificationsState { Open = true }), cells)
            .First(p => p.Node.Hit is NotificationHit { Part: NotificationPart.Clear }).Node;

        clear.OnClick!(InputModifier.None);

        feed.History.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFadingToastDeclaresItsCountdownBar_AndAHoverRegion(bool cells)
    {
        var feed = new NotificationFeed();
        var n = feed.Post(Fade("a", NotificationSeverity.Warn), At(0));
        feed.Advance(At(0));
        feed.Advance(At(2.5));
        var all = Arrange(Tree(feed, new NotificationsState()), cells);
        var toast = Toasts(all).Single();

        NotificationsLayout.ToastIdOf(toast.Node.Hit).ShouldBe(n.Id);
        var inside = all.Where(p => toast.Rect.Contains(p.Rect.Center)).ToList();
        var track = inside.First(p => p.Node.Background == Colors.Track).Rect;
        var fill = inside.First(p => p.Node.Background == Colors.Warn && p.Node.Hit is null && p.Rect.Height <= track.Height + 1f && p.Rect.Width > 0f).Rect;
        // Half the time is gone: the fill is half of the track.
        (fill.Width / track.Width).ShouldBe(0.5f, 0.15f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStayingToastCarriesItsOwnButtons_AndNoCountdown(bool cells)
    {
        var ran = false;
        var feed = new NotificationFeed();
        feed.Post(Stay("s") with { ActionLabel = "Go", Action = () => ran = true }, At(0));
        var all = Arrange(Tree(feed, new NotificationsState()), cells);

        all.Any(p => p.Node.Background == Colors.Track).ShouldBeFalse();
        var action = all.First(p => p.Node.Hit is NotificationHit { Part: NotificationPart.Action }).Node;
        all.Any(p => p.Node.Hit is NotificationHit { Part: NotificationPart.Dismiss }).ShouldBeTrue();
        action.OnClick!(InputModifier.None);
        ran.ShouldBeTrue();
        feed.NeedsYou.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARowTheConsumerSuppliesIsUsedInPlaceOfTheDefault(bool cells)
    {
        var marker = Layout.Builder.Text("custom row", 14f);
        var feed = new NotificationFeed();
        feed.Post(Stay("hidden title") with { Row = () => marker }, At(0));
        var all = Arrange(Tree(feed, new NotificationsState { Open = true }), cells);

        TextsOf(all).ShouldContain("custom row");
        TextsOf(all).ShouldNotContain("hidden title");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongTextWrapsWithAnEllipsisAndTheWholeTextOnHover(bool cells)
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 120));
        var feed = new NotificationFeed();
        feed.Post(Stay("long") with { Text = text }, At(0));
        var all = Arrange(Tree(feed, new NotificationsState { Open = true }), cells);

        var wrap = all.First(p => p.Node is Layout.Node.Wrap).Node;
        wrap.Tooltip.ShouldBe(text);
        ((Layout.Node.Wrap)wrap).Ellipsis.ShouldBeTrue();
        ((Layout.Node.Wrap)wrap).MaxLines.ShouldBe(Style.MaxTextLines);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryToastCardAndTheOpenBarAreSurfaces_OnEitherContext(bool cells)
    {
        var feed = new NotificationFeed();
        feed.Post(Fade("a"), At(0));
        feed.Post(Stay("b"), At(0));

        var collapsed = Arrange(Tree(feed, new NotificationsState()), cells);
        collapsed.Count(p => p.Node.IsSurface).ShouldBe(2, "one per card, fading and staying alike");
        collapsed.Single(p => p.Node.IsSurface && p.Node.Hit is NotificationHit { Part: NotificationPart.Toast })
            .Rect.Width.ShouldBe(Style.ToastWidth, Tol(cells));

        var open = Arrange(Tree(feed, new NotificationsState { Open = true }), cells);
        open.Single(p => p.Node.IsSurface).Rect.Width.ShouldBe(Style.BarWidth, Tol(cells));
    }
}
