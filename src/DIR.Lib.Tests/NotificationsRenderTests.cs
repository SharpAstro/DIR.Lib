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
/// clock: the collapsed strip with three toasts at the bottom right (the default) and at the top right (asked for), and the bar open over the page. Pixel baselines live in <c>Baselines/</c>, cell
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
    private const float NarrowW = 400f;

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

    private static Layout.Node Tree(NotificationFeed feed, NotificationsState state, NotificationsStyle? style = null)
        => NotificationsLayout.Build(Page(), feed, state, Colors, style ?? Style);

    // ---- pixels ----

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, FontPath, DesignScale.One);
        }
    }

    private static RgbaImage Draw(Layout.Node tree, float width = W, float height = H, RGBAColor32? clear = null)
    {
        var renderer = new RgbaImageRenderer((uint)width, (uint)height);
        renderer.Surface.Clear(clear ?? Theme.Palette.ContentBg);
        new Widget(renderer) { FontPath = NotificationsRenderTests.FontPath }.Render(tree, new RectF32(0, 0, width, height));
        return renderer.Surface;
    }

    // ---- toast look B: tinted by severity, icon, Close, shadow; dark, light and one-hue palettes ----

    internal static readonly UiPalette Light = new()
    {
        ContentBg = new(246, 247, 249, 255),
        PanelBg = new(255, 255, 255, 255),
        HeaderBg = new(236, 238, 242, 255),
        Separator = new(208, 212, 220, 255),
        BodyText = new(30, 32, 38, 255),
        DimText = new(92, 98, 110, 255),
        Accent = new(20, 90, 200, 255),
        Selection = new(200, 218, 245, 255),
        HeaderText = new(20, 22, 28, 255),
        SelectionText = new(0, 0, 0, 255),
        Info = new(20, 100, 210, 255),
        Warn = new(170, 100, 0, 255),
        Error = new(200, 30, 40, 255),
        Success = new(20, 130, 60, 255),
    };

    /// <summary>Red on black, as pilot's night palette: every colour is one hue, so severity is not tinted.</summary>
    internal static readonly UiPalette OneHue = new()
    {
        ContentBg = new(0, 0, 0, 255),
        PanelBg = new(20, 0, 0, 255),
        HeaderBg = new(32, 0, 0, 255),
        Separator = new(90, 0, 0, 255),
        BodyText = new(255, 70, 70, 255),
        DimText = new(190, 50, 50, 255),
        Accent = new(255, 40, 40, 255),
        Selection = new(90, 0, 0, 255),
        HeaderText = new(255, 90, 90, 255),
        SelectionText = new(255, 120, 120, 255),
        Info = new(255, 60, 60, 255),
        Warn = new(255, 80, 40, 255),
        Error = new(255, 30, 30, 255),
        Success = new(230, 60, 60, 255),
    };

    private const float LookH = 720f;
    private static readonly NotificationsStyle LookStyle = new() { MaxToasts = 5 };

    /// <summary>One toast of each severity (fading) and a staying card with two buttons and a long title.</summary>
    internal static NotificationFeed LookFeed()
    {
        var feed = new NotificationFeed();
        feed.Post(new Notification { Title = "Frame saved", Text = "IMG_0412.fits, 120 s, gain 120.", Severity = NotificationSeverity.Success, Kind = NotificationKind.Fading, Source = "camera" }, At(0));
        feed.Post(new Notification { Title = "Plate solve finished", Text = "Centre moved 4.2 arcmin from target.", Severity = NotificationSeverity.Info, Kind = NotificationKind.Fading, Source = "solver" }, At(0));
        feed.Post(new Notification { Title = "Guiding RMS above 1.5 arcsec", Text = "Wind gusts are shaking the mount; the subframes since 21:04 may be soft.", Severity = NotificationSeverity.Warn, Kind = NotificationKind.Fading, Source = "guider" }, At(0));
        feed.Post(new Notification { Title = "Focuser lost its connection during the autofocus run on the luminance filter", Text = "USB link lost.", Severity = NotificationSeverity.Error, Kind = NotificationKind.Fading, Source = "focuser" }, At(0));
        feed.Post(new Notification { Title = "Camera disconnected while the long exposure sequence was still running", Text = "Reconnect to resume at frame 41 of 120.", Severity = NotificationSeverity.Error, Kind = NotificationKind.Staying, ActionLabel = "Reconnect", Action = () => { }, Source = "camera" }, At(0));
        feed.Advance(At(0));
        feed.Advance(At(2));
        return feed;
    }

    private static RgbaImage DrawLook(UiPalette palette, float width, NotificationFeed? feed = null)
    {
        var tree = NotificationsLayout.Build(Layout.Builder.Box(0, 0).Stretch().Bg(palette.ContentBg), feed ?? LookFeed(),
            new NotificationsState(), NotificationsColors.From(palette), LookStyle);
        return Draw(tree, width, LookH, palette.ContentBg);
    }

    [Fact] public void ToastLookDarkWide() => CompareBaseline(DrawLook(Theme.Palette, W), "notifications_toast_look_dark.png");
    [Fact] public void ToastLookDarkNarrow() => CompareBaseline(DrawLook(Theme.Palette, NarrowW), "notifications_toast_look_dark_narrow.png");
    [Fact] public void ToastLookLightWide() => CompareBaseline(DrawLook(Light, W), "notifications_toast_look_light.png");
    [Fact] public void ToastLookLightNarrow() => CompareBaseline(DrawLook(Light, NarrowW), "notifications_toast_look_light_narrow.png");
    [Fact] public void ToastLookOneHueWide() => CompareBaseline(DrawLook(OneHue, W), "notifications_toast_look_onehue.png");
    [Fact] public void ToastLookOneHueNarrow() => CompareBaseline(DrawLook(OneHue, NarrowW), "notifications_toast_look_onehue_narrow.png");

    private static readonly NotificationsStyle TopRight = new() { Corner = ToastCorner.TopRight };

    [Fact]
    public void CollapsedStripWithThreeToastsAtBottomRight()
        => CompareBaseline(Draw(Tree(ToastFeed(), new NotificationsState())), "notifications_strip_toasts_bottom_right.png");

    [Fact]
    public void CollapsedStripWithThreeToastsAtTopRightWhenAsked()
        => CompareBaseline(Draw(Tree(ToastFeed(), new NotificationsState(), TopRight)), "notifications_strip_toasts_top_right.png");

    [Fact]
    public void NarrowCollapsedStripWithToastsAtBottomRightAndALongMessage()
        => CompareBaseline(Draw(Tree(NarrowFeed(), new NotificationsState()), NarrowW), "notifications_strip_toasts_bottom_right_narrow.png");

    private static NotificationFeed NarrowFeed()
    {
        var feed = new NotificationFeed();
        feed.Post(new Notification { Title = "Plate solve finished", Text = "Centre moved 4.2 arcmin from target.", Severity = NotificationSeverity.Info, Kind = NotificationKind.Fading, Source = "solver" }, At(0));
        feed.Post(new Notification { Title = "Dome shutter will not close", Text = "Rain sensor reports wet; the shutter has not moved for 90 seconds, the mount is parked and the camera is still cooling, so someone needs to go and look at the roof before the next shower arrives from the west.", Severity = NotificationSeverity.Warn, Kind = NotificationKind.Staying, ActionLabel = "Retry", Action = () => { }, Source = "dome" }, At(0));
        feed.Advance(At(0));
        feed.Advance(At(1));
        return feed;
    }

    [Fact]
    public void BarOpenOverThePageWithNeedsYouAndHistory()
        => CompareBaseline(Draw(Tree(ListFeed(), new NotificationsState { Open = true })), "notifications_bar_open.png");

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
    public void CellsCollapsedStripWithThreeToastsAtBottomRight()
        => CompareSnapshot(Cells(Tree(ToastFeed(), new NotificationsState())), "notifications_strip_toasts_bottom_right.txt");

    [Fact]
    public void CellsCollapsedStripWithThreeToastsAtTopRightWhenAsked()
        => CompareSnapshot(Cells(Tree(ToastFeed(), new NotificationsState(), TopRight)), "notifications_strip_toasts_top_right.txt");

    [Fact]
    public void CellsBarOpenOverThePageWithNeedsYouAndHistory()
        => CompareSnapshot(Cells(Tree(ListFeed(), new NotificationsState { Open = true })), "notifications_bar_open.txt");

    [Fact]
    public void CellsToastLookHasNoShadowAndOneCharacterPerIcon()
    {
        var tree = NotificationsLayout.Build(Page(), LookFeed(), new NotificationsState(), Colors, LookStyle);
        CompareSnapshot(Cells(tree), "notifications_toast_look.txt");
    }

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
