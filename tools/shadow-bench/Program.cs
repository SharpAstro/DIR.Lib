using System.Diagnostics;
using System.Runtime.CompilerServices;
using DIR.Lib;
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

Layout.Node Build(int count, float height, bool shadow)
{
    var items = new List<Layout.Node>();
    for (var i = 0; i < count; i++)
    {
        var box = Layout.Builder.Box(count == 3 ? 600 : 1100, height).Bg(new RGBAColor32(52, 56, 64, 255)).Radius(count == 3 ? 12f : 6f);
        items.Add(shadow ? box.Elevated(3) : box);
    }

    return new Layout.Node.Stack(items, Layout.Axis.Vertical, count == 3 ? 60f : 6f) { Padding = 30f }.Bg(new RGBAColor32(24, 25, 28, 255));
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
