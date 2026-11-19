using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using DIR.Lib.ChildWindows;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The child-window declaration drawn in pixels (<see cref="RgbaImageRenderer"/>, DejaVu Sans, the UI text face) and
/// arranged in cells (the int context, 8x16 design units a cell): overlapping free windows with the active one in
/// front, a cascade of four, a tile of three at a wide and a narrow host, and tabs, one title long enough to
/// ellipsise. Pixel baselines live in <c>Baselines/childwindows_*.png</c>, cell snapshots in <c>Snapshots/</c>.
/// <c>DIR_LIB_UPDATE_BASELINES=1</c> / <c>DIR_LIB_UPDATE_SNAPSHOTS=1</c> with a filter on this class write them.
/// </summary>
public class ChildWindowsRenderTests
{
    private const float W = 800f;
    private const float H = 480f;
    private const float NarrowW = 320f;
    private const string LongTitle = "Guiding graph: RMS over the last forty minutes of the second imaging session";

    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly bool UpdateSnapshots = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_SNAPSHOTS") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static readonly RGBAColor32 PageBg = new(18, 18, 20, 255);
    private static readonly RGBAColor32 BodyInk = new(220, 222, 226, 255);

    private static Layout.Node Page() => Layout.Builder.Box(0, 0).Stretch().Bg(PageBg);

    private static Layout.Node Body(ChildWindow c)
        => Layout.Builder.Text("Content of " + c.Title, 12f, BodyInk, TextAlign.Near, TextAlign.Near).WStar().HStar();

    private static ChildWindowSet Set(float width, params string[] titles)
    {
        var set = new ChildWindowSet();
        set.NoteHost(new RectF32(0, 0, width, H));
        var x = 24f;
        foreach (var t in titles)
        {
            set.Add(t, new RectF32(x, 24f + (x - 24f) / 2f, 280f, 180f));
            x += 120f;
        }

        return set;
    }

    private static Layout.Node Tree(ChildWindowSet set)
        => ChildWindowsLayout.Build(Page(), set, Body, ChildWindowColors.Dark, null, DesignScale.One);

    /// <summary>Four free windows; the one added last (Focuser) is in front, then Mount is raised over the rest.</summary>
    private static ChildWindowSet FreeOverlap()
    {
        var s = Set(W, "Camera", "Mount", LongTitle, "Focuser");
        s.BringToFront(2);
        return s;
    }

    private static ChildWindowSet Cascaded()
    {
        var s = Set(W, "Camera", "Mount", LongTitle, "Focuser");
        s.Cascade(new RectF32(0, 0, W, H));
        return s;
    }

    private static ChildWindowSet Tiled(float width)
    {
        var s = Set(width, "Camera", "Mount", LongTitle);
        s.Tile(new RectF32(0, 0, width, H));
        return s;
    }

    private static ChildWindowSet Tabbed()
    {
        var s = Set(W, "Camera", "Mount", LongTitle);
        s.ShowAsTabs(new RectF32(0, 0, W, H));
        s.BringToFront(3);
        return s;
    }

    // ---- pixels ----

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, FontPath, DesignScale.One);
        }
    }

    private static RgbaImage Draw(ChildWindowSet set, float width)
    {
        var renderer = new RgbaImageRenderer((uint)width, (uint)H);
        renderer.Surface.Clear(PageBg);
        new Widget(renderer) { FontPath = ChildWindowsRenderTests.FontPath }.Render(Tree(set), new RectF32(0, 0, width, H));
        return renderer.Surface;
    }

    [Fact]
    public void FreeOverlappingWithTheActiveInFront()
        => CompareBaseline(Draw(FreeOverlap(), W), "childwindows_free_overlap.png");

    [Fact]
    public void CascadeOfFour()
        => CompareBaseline(Draw(Cascaded(), W), "childwindows_cascade.png");

    [Fact]
    public void TileOfThreeWide()
        => CompareBaseline(Draw(Tiled(W), W), "childwindows_tile_wide.png");

    [Fact]
    public void TileOfThreeNarrow()
        => CompareBaseline(Draw(Tiled(NarrowW), NarrowW), "childwindows_tile_narrow.png");

    [Fact]
    public void TabsWithTheLongTitleActive()
        => CompareBaseline(Draw(Tabbed(), W), "childwindows_tabs.png");

    // ---- cells ----

    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
        public object? MeasureKey => "childwindows-cell";

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
        Layout.Node.Leaf l => "Leaf:" + l.Content.GetType().Name,
        _ => n.GetType().Name,
    } + (n.Hit is ChildWindowHit h ? " [" + h.Part + "#" + h.Id + (h.Edge != ResizeEdge.None ? ":" + h.Edge : "") + "]" : "");

    private static string Cells(ChildWindowSet set, int cols)
    {
        // Styled as a cell host styles it: no tab rule, which would take the strip's one row from the labels.
        var tree = ChildWindowsLayout.Build(Page(), set, Body, ChildWindowColors.Dark, new ChildWindowStyle { TabRule = 0f }, DesignScale.One);
        var arranged = Layout.Engine.Arrange(tree, new Rect<int>(0, 0, cols, (int)(H / 16)), new CellCtx());
        var sb = new StringBuilder("# cell " + cols + "x30 (8x16)\n");
        foreach (var a in arranged)
        {
            var b = a.Bounds;
            sb.Append(a.Depth).Append(' ').Append(Label(a.Node)).Append(' ')
              .Append(b.X).Append(' ').Append(b.Y).Append(' ').Append(b.Width).Append(' ').Append(b.Height).Append('\n');
        }

        return sb.ToString();
    }

    [Fact]
    public void CellsFreeOverlapping() => CompareSnapshot(Cells(FreeOverlap(), 100), "childwindows_free_overlap.txt");

    [Fact]
    public void CellsCascade() => CompareSnapshot(Cells(Cascaded(), 100), "childwindows_cascade.txt");

    [Fact]
    public void CellsTileWide() => CompareSnapshot(Cells(Tiled(W), 100), "childwindows_tile_wide.txt");

    [Fact]
    public void CellsTileNarrow() => CompareSnapshot(Cells(Tiled(NarrowW), 40), "childwindows_tile_narrow.txt");

    [Fact]
    public void CellsTabs() => CompareSnapshot(Cells(Tabbed(), 100), "childwindows_tabs.txt");

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
