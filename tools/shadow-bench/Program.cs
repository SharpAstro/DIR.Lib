using System.Diagnostics;
using System.Runtime.CompilerServices;
using DIR.Lib;
using DIR.Lib.Notifications;
using Layout = DIR.Lib.Layout;

// Mean frame time of a 1280x800 software paint with node shadows off and on, at 3 cards and at 30 rows.
// Each case does a few warm-up frames, then a fixed number of timed ones. Nothing waits: every frame is a paint.
const uint W = 1280, H = 800;
const int Warmup = 3, Frames = 30;
var fontPath = Path.GetFullPath(Path.Combine(SourceDir(), "..", "..", "src", "DIR.Lib.Tests", "Fonts", "DejaVuSans.ttf"));

Console.WriteLine($"shadow-bench: {W}x{H}, RgbaImageRenderer, {Frames} frames per case (after {Warmup} warm-up)");
foreach (var (name, count, h) in new[] { ("3 cards", 3, 160f), ("30 rows", 30, 20f) })
{
    var off = MeanMs(Build(count, h, shadow: false));
    var on = MeanMs(Build(count, h, shadow: true));
    Console.WriteLine($"{name,-8} shadows off {off,8:F2} ms/frame   shadows on {on,8:F2} ms/frame   cost {on - off,8:F2} ms ({on / off:F2}x)");
}

// The real notification tree: 3 toasts (fading, fading, staying with two buttons) and the open bar with 30 rows.
// For the bar the shadow switch changes nothing (its rows carry none), so off and on are the same paint.
foreach (var (name, build) in new (string, Func<bool, Layout.Node>)[] { ("real 3 toasts", ToastTree), ("real 30 rows", BarTree) })
{
    var off = MeanMs(build(false));
    var on = MeanMs(build(true));
    Console.WriteLine($"{name,-13} shadows off {off,8:F2} ms/frame   shadows on {on,8:F2} ms/frame   cost {on - off,8:F2} ms ({on / off:F2}x)");
}

NotificationsColors RealColors(bool shadow)
{
    var c = NotificationsColors.From(new UiPalette
    {
        ContentBg = new(18, 18, 20, 255), PanelBg = new(28, 29, 32, 255), HeaderBg = new(40, 42, 46, 255),
        Separator = new(64, 66, 72, 255), BodyText = new(220, 222, 226, 255), DimText = new(150, 154, 162, 255),
        Accent = new(90, 160, 255, 255), Selection = new(50, 80, 130, 255), HeaderText = new(235, 237, 240, 255),
        SelectionText = new(255, 255, 255, 255), Info = new(90, 160, 255, 255), Warn = new(240, 190, 60, 255),
        Error = new(230, 80, 80, 255), Success = new(60, 200, 110, 255),
    });
    return shadow ? c : c with { Shadow = new Layout.ShadowStyle { Color = new RGBAColor32(0, 0, 0, 0) } };
}

Layout.Node ToastTree(bool shadow)
{
    var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    var feed = new NotificationFeed();
    feed.Post(new Notification { Title = "Guiding RMS above 1.5 arcsec", Text = "Wind gusts are shaking the mount; the subframes since 21:04 may be soft.", Severity = NotificationSeverity.Warn, Source = "guider" }, t0);
    feed.Post(new Notification { Title = "Plate solve finished", Text = "Centre moved 4.2 arcmin from target.", Source = "solver" }, t0);
    feed.Post(new Notification { Title = "Camera disconnected", Text = "USB link lost.", Severity = NotificationSeverity.Error, Kind = NotificationKind.Staying, ActionLabel = "Reconnect", Action = () => { }, Source = "camera" }, t0);
    feed.Advance(t0);
    return NotificationsLayout.Build(Layout.Builder.Spacer().Stretch(), feed, new NotificationsState(), RealColors(shadow), new NotificationsStyle { MaxToasts = 5 });
}

Layout.Node BarTree(bool shadow)
{
    var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    var feed = new NotificationFeed();
    for (var i = 0; i < 30; i++)
    {
        feed.Post(new Notification
        {
            Title = $"Event {i}", Text = "Something happened that wraps onto a second line of the row in the list.",
            Severity = (NotificationSeverity)(i % 4), Source = "src",
            Kind = i % 2 == 0 ? NotificationKind.Staying : NotificationKind.Fading,
            ActionLabel = i % 2 == 0 ? "Retry" : null, Action = i % 2 == 0 ? () => { } : null,
        }, t0);
    }

    feed.Advance(t0.AddHours(1));
    return NotificationsLayout.Build(Layout.Builder.Spacer().Stretch(), feed, new NotificationsState { Open = true }, RealColors(shadow), new NotificationsStyle());
}

Layout.Node Build(int count, float height, bool shadow)
{
    var items = new List<Layout.Node>();
    for (var i = 0; i < count; i++)
    {
        var box = Layout.Builder.Box(count == 3 ? 600 : 1100, height).Bg(new RGBAColor32(52, 56, 64, 255)).Radius(count == 3 ? 12f : 6f);
        items.Add(shadow ? box.Elevated(3) : box);
    }

    return new Layout.Node.Stack(System.Collections.Immutable.ImmutableArray.CreateRange(items), Layout.Axis.Vertical, count == 3 ? 60f : 6f) { Padding = 30f }.Bg(new RGBAColor32(24, 25, 28, 255));
}

double MeanMs(Layout.Node tree)
{
    var renderer = new RgbaImageRenderer(W, H);
    var widget = new Widget(renderer) { FontPath = fontPath };
    for (var i = 0; i < Warmup; i++) Paint(widget, renderer, tree);
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < Frames; i++) Paint(widget, renderer, tree);
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds / Frames;
}

void Paint(Widget widget, RgbaImageRenderer renderer, Layout.Node tree)
{
    renderer.Surface.Clear(new RGBAColor32(24, 25, 28, 255));
    widget.Render(tree, new RectF32(0, 0, W, H), fontPath);
}

static string SourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
{
    public void Render(Layout.Node root, RectF32 bounds, string fontPath)
    {
        BeginFrame();
        RenderLayout(root, bounds, fontPath, DesignScale.One);
    }
}
