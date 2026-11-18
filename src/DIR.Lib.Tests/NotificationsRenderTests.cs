using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using DIR.Lib.Notifications;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The notifications declaration drawn in pixels (<see cref="RgbaImageRenderer"/>, DejaVu Sans, the UI text face) and
/// arranged in cells (the int context, 8x16 design units a cell), for three states of one feed stepped by a fake
/// clock: the collapsed strip with three toasts at the top right, the same with the pointer where those toasts sit (so
/// they go to the bottom right), and the bar open over the page. Pixel baselines live in <c>Baselines/</c>, cell
/// snapshots in <c>Snapshots/</c>. <c>DIR_LIB_UPDATE_BASELINES=1</c> / <c>DIR_LIB_UPDATE_SNAPSHOTS=1</c> with a filter on
/// this class write them; read each capture before keeping it.
/// </summary>
public class NotificationsRenderTests
{
    private const float W = 800f;
    private const float H = 480f;
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(double s) => T0.AddSeconds(s);

    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly bool UpdateSnapshots = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_SNAPSHOTS") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static readonly UiTheme Theme = new()
    {
        Palette = new UiPalette
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
        },
        Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f),
    };

    private static readonly NotificationsColors Colors = NotificationsColors.From(Theme.Palette);
    private static readonly NotificationsStyle Style = new();
    private static readonly RectF32 Content = new(0, 0, W - Style.StripWidth, H);

    private static Layout.Node Page() => Layout.Builder.Box(0, 0).Stretch().Bg(Theme.Palette.ContentBg);

    /// <summary>Three toasts (newest on top) and nothing else: two fading, one staying with its buttons.</summary>
    private static NotificationFeed ToastFeed()
    {
        var feed = new NotificationFeed();
        feed.Post(new Notification { Title = "Plate solve finished", Text = "Centre moved 4.2 arcmin from target.", Severity = NotificationSeverity.Info, Kind = NotificationKind.Fading, Source = "solver" }, At(0));
        feed.Post(new Notification { Title = "Guiding RMS above 1.5 arcsec", Text = "Wind gusts are shaking the mount; the subframes since 21:04 may be soft and could be worth rejecting later in stacking.", Severity = NotificationSeverity.Warn, Kind = NotificationKind.Fading, Source = "guider" }, At(0));
        feed.Post(new Notification { Title = "Camera disconnected", Text = "USB link lost.", Severity = NotificationSeverity.Error, Kind = NotificationKind.Staying, ActionLabel = "Reconnect", Action = () => { }, Source = "camera" }, At(0));
        feed.Advance(At(0));
        feed.Advance(At(1));
        return feed;
    }

    /// <summary>Two rows that need the user and two that have gone to history.</summary>
    private static NotificationFeed ListFeed()
    {
        var feed = new NotificationFeed();
        feed.Post(new Notification { Title = "Meridian flip done", Text = "Resumed on frame 41 of 120.", Severity = NotificationSeverity.Success, Kind = NotificationKind.Fading, Source = "mount" }, At(0));
        feed.Post(new Notification { Title = "Focus drifted", Text = "HFR rose from 2.1 to 3.4 px; refocus before the next filter change.", Severity = NotificationSeverity.Warn, Kind = NotificationKind.Fading, Source = "focuser" }, At(5));
        feed.Advance(At(0));
        feed.Advance(At(60));
        feed.Post(new Notification { Title = "Camera disconnected", Text = "USB link lost.", Severity = NotificationSeverity.Error, Kind = NotificationKind.Staying, ActionLabel = "Reconnect", Action = () => { }, Source = "camera" }, At(120));
        feed.Post(new Notification { Title = "Dome shutter will not close", Text = "Rain sensor reports wet; the shutter has not moved for 90 seconds and needs a hand.", Severity = NotificationSeverity.Warn, Kind = NotificationKind.Staying, ActionLabel = "Retry", Action = () => { }, Source = "dome" }, At(121));
        feed.Advance(At(121));
        return feed;
    }

    private static Layout.Node Tree(NotificationFeed feed, NotificationsState state, Vector2? pointer)
        => NotificationsLayout.Build(Page(), feed, state, Colors, Style, pointer, Content);

    private static Vector2 OverTopRightToasts() => NotificationsLayout.TopRightRegion(Content, 3, Style).Center;

    // ---- pixels ----

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, FontPath, DesignScale.One);
        }
    }

    private static RgbaImage Draw(Layout.Node tree)
    {
        var renderer = new RgbaImageRenderer((uint)W, (uint)H);
        renderer.Surface.Clear(Theme.Palette.ContentBg);
        new Widget(renderer) { FontPath = NotificationsRenderTests.FontPath }.Render(tree, new RectF32(0, 0, W, H));
        return renderer.Surface;
    }

    [Fact]
    public void CollapsedStripWithThreeToastsAtTopRight()
        => CompareBaseline(Draw(Tree(ToastFeed(), new NotificationsState(), null)), "notifications_strip_toasts_top_right.png");

    [Fact]
    public void PointerAtTopRightMovesTheToastsToTheBottomRight()
        => CompareBaseline(Draw(Tree(ToastFeed(), new NotificationsState(), OverTopRightToasts())), "notifications_strip_toasts_bottom_right.png");

    [Fact]
    public void BarOpenOverThePageWithNeedsYouAndHistory()
        => CompareBaseline(Draw(Tree(ListFeed(), new NotificationsState { Open = true }, null)), "notifications_bar_open.png");

    // ---- cells ----

    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
        public object? MeasureKey => "notifications-cell";

        // As a terminal answers it: a one-line row is one row tall and a filled extent never rounds away.
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

    private static string Label(Layout.Node n) => n switch
    {
        Layout.Node.Leaf { Content: Layout.Content.Text t } => "Text \"" + t.Value + "\"",
        Layout.Node.Leaf { Content: Layout.Content.Icon i } => "Icon " + i.Kind,
        Layout.Node.Leaf l => "Leaf:" + l.Content.GetType().Name,
        _ => n.GetType().Name,
    } + (n.Hit is NotificationHit h ? " [" + h.Part + (h.Id != 0 ? "#" + h.Id.ToString(CultureInfo.InvariantCulture) : "") + "]" : "");

    private static string Cells(Layout.Node tree)
    {
        var arranged = Layout.Engine.Arrange(tree, new Rect<int>(0, 0, (int)(W / 8), (int)(H / 16)), new CellCtx());
        var sb = new StringBuilder("# cell 100x30 (8x16)\n");
        foreach (var a in arranged)
        {
            var b = a.Bounds;
            sb.Append(a.Depth).Append(' ').Append(Label(a.Node)).Append(' ')
              .Append(b.X).Append(' ').Append(b.Y).Append(' ').Append(b.Width).Append(' ').Append(b.Height).Append('\n');
        }

        return sb.ToString();
    }

    [Fact]
    public void CellsCollapsedStripWithThreeToastsAtTopRight()
        => CompareSnapshot(Cells(Tree(ToastFeed(), new NotificationsState(), null)), "notifications_strip_toasts_top_right.txt");

    [Fact]
    public void CellsPointerAtTopRightMovesTheToastsToTheBottomRight()
        => CompareSnapshot(Cells(Tree(ToastFeed(), new NotificationsState(), OverTopRightToasts())), "notifications_strip_toasts_bottom_right.txt");

    [Fact]
    public void CellsBarOpenOverThePageWithNeedsYouAndHistory()
        => CompareSnapshot(Cells(Tree(ListFeed(), new NotificationsState { Open = true }, null)), "notifications_bar_open.txt");

    private static string ThisFile([CallerFilePath] string? file = null) => file!;

    private static void CompareSnapshot(string actual, string name)
    {
        if (UpdateSnapshots)
        {
            var dir = Path.Combine(Path.GetDirectoryName(ThisFile())!, "Snapshots");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name), actual);
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Snapshots", name);
        File.Exists(path).ShouldBeTrue($"Snapshot {name} is missing; set DIR_LIB_UPDATE_SNAPSHOTS=1 once to create it.");
        File.ReadAllText(path).ShouldBe(actual);
    }

    private static void CompareBaseline(RgbaImage img, string name)
    {
        var baselinePath = Path.Combine(BaselineDir, name);
        if (UpdateBaselines)
        {
            Directory.CreateDirectory(SourceBaselineDir);
            Directory.CreateDirectory(BaselineDir);
            var png = PngWriter.Encode(img.Pixels, img.Width, img.Height);
            File.WriteAllBytes(Path.Combine(SourceBaselineDir, name), png);
            File.WriteAllBytes(baselinePath, png);
            return;
        }

        File.Exists(baselinePath).ShouldBeTrue($"Baseline '{name}' is missing; generate it with DIR_LIB_UPDATE_BASELINES=1.");
        var baseline = PngReader.Decode(File.ReadAllBytes(baselinePath));
        baseline.Width.ShouldBe(img.Width);
        baseline.Height.ShouldBe(img.Height);

        var maxDiff = 0;
        var diffCount = 0;
        for (var i = 0; i < baseline.Pixels.Length; i++)
        {
            var diff = Math.Abs(img.Pixels[i] - baseline.Pixels[i]);
            if (diff > 0) diffCount++;
            if (diff > maxDiff) maxDiff = diff;
        }

        if (maxDiff > 2)
        {
            var actualPath = Path.ChangeExtension(baselinePath, ".actual.png");
            PngWriter.Save(actualPath, img.Pixels, img.Width, img.Height);
            Assert.Fail($"Baseline mismatch for '{name}': {diffCount} pixels differ, max diff={maxDiff}. Actual saved to '{actualPath}'.");
        }
    }
}
