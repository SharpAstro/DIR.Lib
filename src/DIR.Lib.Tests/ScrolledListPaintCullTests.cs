using System.Collections.Immutable;
using System.Diagnostics;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Paint already follows the viewport (DIR.Lib #338): a scrolled VStack draws, registers, captures and hands to
/// <c>drawFill</c> only the nodes that touch its clip, so a frame's draw cost is the visible rows' cost whatever the
/// list's length. These tests pin that, and one fact writes the per-frame cost line the next leg starts from.
/// </summary>
/// <remarks>
/// The fixture is <see cref="LayoutScrollCullTests"/>'s shape: rows built once and kept, a fresh scrolled column around
/// them each frame, plain text rather than Markdown so a test runs in milliseconds. Every row also paints a background,
/// a rounded edge and holds a small <see cref="Layout.Content.Fill"/> leaf, so each of the four draw calls and
/// <c>drawFill</c> are exercised per row.
/// </remarks>
public class ScrolledListPaintCullTests(ITestOutputHelper output)
{
    private const float Width = 600f;
    private const float ViewportH = 400f;
    private const float Pad = 6f;
    private const float RowGap = 4f;
    private const float Font = 10f;
    private const string ListId = "paint-cull";
    private const string FillKeyPrefix = "row-fill-";
    private const float Offset = 100f;

    private static readonly RGBAColor32 Back = new(40, 40, 40, 255);
    private static readonly RGBAColor32 Edge = new(120, 120, 120, 255);

    private static Layout.Node Row(int i)
    {
        var words = new Layout.Node[4 + (i * 7) % 61];
        for (var k = 0; k < words.Length; k++)
        {
            words[k] = Layout.Builder.Text(new string((char)('a' + (i + k) % 26), 3 + (i + k) % 5), Font);
        }

        return Layout.Builder.HStack(
                Layout.Builder.Text("user", Font),
                Layout.Builder.Fill(40f, 10f, FillKeyPrefix + i),
                Layout.Builder.WrapH(words).WithGap(4f).WithLineGap(2f).WStar().HAuto())
            .WithGap(8f).Pad(Pad).WStar().HAuto()
            .Bg(Back).Radius(4f).Border(Edge, 1f, 4f)
            .Clickable(new HitResult.ListItemHit(ListId, i), _ => { });
    }

    private static Layout.Node[] BuildRows(int count) => [.. Enumerable.Range(0, count).Select(Row)];

    private static Layout.Node List(Layout.Node[] rows, ListScrollController scroll)
        => Layout.Builder.VStack([.. rows]).WithGap(RowGap).WStar().HClamp(0f, ViewportH).Pad(Pad).WithScroll(scroll);

    private static readonly RectF32 Viewport = new(0, 0, Width, ViewportH);

    private static bool Touches(float x, float y, float w, float h)
        => x < Width && x + w > 0f && y < ViewportH && y + h > 0f;

    private sealed class CountingRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public readonly List<RectInt> TextRects = [];
        public readonly List<RectInt> RectFills = [];
        public readonly List<RectInt> RoundedFills = [];
        public readonly List<RectInt> Borders = [];

        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize, RGBAColor32 fontColor,
            in RectInt layout, TextAlign horizAlign = TextAlign.Near, TextAlign vertAlign = TextAlign.Center)
            => TextRects.Add(layout);

        public override void FillRectangle(in RectInt rect, RGBAColor32 fillColor)
        {
            RectFills.Add(rect);
            base.FillRectangle(rect, fillColor);
        }

        public override void FillRoundedRectangle(in RectInt rect, RGBAColor32 fillColor, float cornerRadius)
        {
            RoundedFills.Add(rect);
            base.FillRoundedRectangle(rect, fillColor, cornerRadius);
        }

        public override void DrawBorder(in RectInt rect, RGBAColor32 color, int width, float radius, Layout.BorderSides sides)
        {
            Borders.Add(rect);
            base.DrawBorder(rect, color, width, radius, sides);
        }

        /// <summary>The calls PaintLayout itself makes. FillRectangle is left out: the base renderer's rounded fill and
        /// border decompose into scanline FillRectangle calls, and a row straddling the edge has scanlines above it
        /// (the renderer clips them), so those are checked against the rounded and border rects instead.</summary>
        public IEnumerable<RectInt> AllRects => TextRects.Concat(RoundedFills).Concat(Borders);

        public void Reset()
        {
            TextRects.Clear();
            RectFills.Clear();
            RoundedFills.Clear();
            Borders.Clear();
        }
    }

    private sealed class Widget : PixelWidgetBase<RgbaImage>
    {
        public Widget(CountingRenderer renderer) : base(renderer)
        {
        }

        public List<int> FilledRows { get; } = [];

        private void CountFill(Layout.Content.Fill fill, RectF32 rect)
        {
            if (fill.Key is { } k && k.StartsWith(FillKeyPrefix, StringComparison.Ordinal))
            {
                FilledRows.Add(int.Parse(k[FillKeyPrefix.Length..]));
                Touches(rect.X, rect.Y, rect.Width, rect.Height).ShouldBeTrue("drawFill ran for a leaf off the viewport");
            }
        }

        public void Render(Layout.Node root)
        {
            BeginFrame();
            FilledRows.Clear();
            RenderLayout(root, Viewport, "stub.ttf", DesignScale.One, CountFill);
        }

        public ImmutableArray<Layout.ArrangedNode<float>> Arrange(Layout.Node root)
            => ArrangeLayout(root, Viewport, "stub.ttf", DesignScale.One);

        public void Paint(ImmutableArray<Layout.ArrangedNode<float>> arranged)
        {
            BeginFrame();
            FilledRows.Clear();
            PaintLayout(arranged, "stub.ttf", DesignScale.One, CountFill);
        }

        public List<Layout.ArrangedNode<float>> Painted()
        {
            var into = new List<Layout.ArrangedNode<float>>();
            CollectPaintedNodes(into);
            return into;
        }
    }

    private sealed class Rig
    {
        public Rig(int rows)
        {
            Rows = BuildRows(rows);
            Scroll = new ListScrollController { Mode = ScrollBarMode.None };
            Renderer = new CountingRenderer((uint)Width, (uint)ViewportH);
            Widget = new Widget(Renderer);
        }

        public Layout.Node[] Rows { get; }
        public ListScrollController Scroll { get; }
        public CountingRenderer Renderer { get; }
        public Widget Widget { get; }

        public Layout.Node Tree() => List(Rows, Scroll);

        /// <summary>One frame at <paramref name="offset"/>, the first to learn the extent, the second the one measured.</summary>
        public void Frame(int offset)
        {
            Widget.Render(Tree());
            Scroll.AtomOffset = offset;
            Renderer.Reset();
            Widget.Render(Tree());
        }
    }

    private static List<int> PaintedRows(Widget widget)
        => [.. widget.Painted().Select(a => a.Node.Hit).OfType<HitResult.ListItemHit>().Select(h => h.Index).Order()];

    private static List<int> VisibleRows(Layout.Node[] rows, float offset)
    {
        var room = new Layout.Size<float>(Width - 2 * Pad, ViewportH - 2 * Pad);
        var tops = new float[rows.Length];
        var heights = new float[rows.Length];
        var at = Pad;
        for (var i = 0; i < rows.Length; i++)
        {
            tops[i] = at;
            heights[i] = MeasureRow(rows[i], room);
            at += heights[i] + RowGap;
        }

        return [.. Enumerable.Range(0, rows.Length).Where(i => tops[i] - offset + heights[i] > 0f && tops[i] - offset < ViewportH)];
    }

    /// <summary>Rows whose Fill leaf (placed by arranging the row alone) touches the viewport: a row straddling an edge
    /// by its padding only has no leaf on screen, so this is narrower than the rows that touch.</summary>
    private static List<int> FillRowsTouching(Layout.Node[] rows, float offset)
    {
        var room = new Layout.Size<float>(Width - 2 * Pad, ViewportH - 2 * Pad);
        var widget = new DeclarationWidget(new DeclarationStubRenderer(1, 1));
        var result = new List<int>();
        var at = Pad;
        for (var i = 0; i < rows.Length; i++)
        {
            var h = widget.Measure(rows[i], room).Height;
            var alone = Layout.Engine.Arrange(rows[i], new Rect<float>(0f, 0f, room.Width, h), widget.Context());
            var leaf = alone.Single(a => a.Node is Layout.Node.Leaf { Content: Layout.Content.Fill });
            var top = at + leaf.Bounds.Y - offset;
            if (top + leaf.Bounds.Height > 0f && top < ViewportH) result.Add(i);
            at += h + RowGap;
        }

        return result;
    }

    private static float MeasureRow(Layout.Node row, Layout.Size<float> room)
        => new DeclarationWidget(new DeclarationStubRenderer(1, 1)).Measure(row, room).Height;

    private static void ShouldAllTouchTheViewport(Rig rig)
    {
        foreach (var r in rig.Renderer.AllRects)
        {
            var x = Math.Min(r.UpperLeft.X, r.LowerRight.X);
            var y = Math.Min(r.UpperLeft.Y, r.LowerRight.Y);
            Touches(x, y, r.Width, r.Height).ShouldBeTrue($"a draw call at {r} lies outside the viewport");
        }

        // A scanline lies within the rect of the rounded fill or border that made it, and those touch the viewport.
        var parents = rig.Renderer.RoundedFills.Concat(rig.Renderer.Borders).ToList();
        foreach (var r in rig.Renderer.RectFills)
        {
            var y = Math.Min(r.UpperLeft.Y, r.LowerRight.Y);
            parents.Any(p => y >= Math.Min(p.UpperLeft.Y, p.LowerRight.Y) && y <= Math.Max(p.UpperLeft.Y, p.LowerRight.Y))
                .ShouldBeTrue($"a FillRectangle at {r} belongs to no rounded fill or border");
        }

        foreach (var region in rig.Widget.GetRegisteredRegions())
        {
            Touches(region.X, region.Y, region.Width, region.Height)
                .ShouldBeTrue($"a region at {region.X},{region.Y} {region.Width}x{region.Height} lies outside the viewport");
        }
    }

    [Fact]
    public void TheSameOffsetPaintsTheSameWhateverTheListLength()
    {
        var rigs = new[] { 30, 300, 1000 }.Select(n => new Rig(n)).ToArray();
        foreach (var rig in rigs)
        {
            rig.Frame((int)Offset);
            rig.Scroll.MaxOffset.ShouldBeGreaterThan(Offset + ViewportH, "even 30 rows fill the viewport and its margin here");
            ShouldAllTouchTheViewport(rig);
        }

        var (a, b, c) = (rigs[0], rigs[1], rigs[2]);
        foreach (var other in new[] { b, c })
        {
            other.Renderer.TextRects.Count.ShouldBe(a.Renderer.TextRects.Count, "DrawText calls");
            other.Renderer.RectFills.Count.ShouldBe(a.Renderer.RectFills.Count, "FillRectangle calls");
            other.Renderer.RoundedFills.Count.ShouldBe(a.Renderer.RoundedFills.Count, "FillRoundedRectangle calls");
            other.Renderer.Borders.Count.ShouldBe(a.Renderer.Borders.Count, "DrawBorder calls");
            other.Renderer.TextRects.ShouldBe(a.Renderer.TextRects);
            other.Renderer.RoundedFills.ShouldBe(a.Renderer.RoundedFills);
            other.Renderer.Borders.ShouldBe(a.Renderer.Borders);

            other.Widget.GetRegisteredRegions().Length.ShouldBe(a.Widget.GetRegisteredRegions().Length, "registered regions");
            other.Widget.GetRegisteredRegions().Select(r => (r.X, r.Y, r.Width, r.Height))
                .ShouldBe(a.Widget.GetRegisteredRegions().Select(r => (r.X, r.Y, r.Width, r.Height)));

            var mine = other.Widget.Painted().Select(n => (n.Bounds.X, n.Bounds.Y, n.Bounds.Width, n.Bounds.Height)).ToList();
            mine.ShouldBe(a.Widget.Painted().Select(n => (n.Bounds.X, n.Bounds.Y, n.Bounds.Width, n.Bounds.Height)).ToList(),
                "captured painted nodes");
            other.Widget.FilledRows.ShouldBe(a.Widget.FilledRows, "drawFill leaves");
        }

        output.WriteLine($"paint at offset {Offset}: {a.Renderer.TextRects.Count} DrawText, {a.Renderer.RectFills.Count} FillRectangle, "
            + $"{a.Renderer.RoundedFills.Count} FillRoundedRectangle, {a.Renderer.Borders.Count} DrawBorder, "
            + $"{a.Widget.GetRegisteredRegions().Length} regions, {a.Widget.Painted().Count} painted nodes, "
            + $"{a.Widget.FilledRows.Count} drawFill calls, equal at 30, 300 and 1000 rows");
    }

    [Theory]
    [InlineData("top")]
    [InlineData("middle")]
    [InlineData("end")]
    public void OnlyTheVisibleRowsArePaintedAndFilled(string where)
    {
        var rig = new Rig(600);
        rig.Widget.Render(rig.Tree());
        var offset = where switch
        {
            "middle" => (int)(rig.Scroll.MaxOffset / 2f),
            "end" => (int)rig.Scroll.MaxOffset,
            _ => 0,
        };
        rig.Frame(offset);

        var visible = VisibleRows(rig.Rows, rig.Scroll.Offset);
        visible.Count.ShouldBeGreaterThan(2);
        PaintedRows(rig.Widget).ShouldBe(visible, $"the rows painted at the {where} are the rows that touch the viewport");
        rig.Widget.FilledRows.Order().ToList().ShouldBe(FillRowsTouching(rig.Rows, rig.Scroll.Offset),
            "drawFill runs exactly for the Fill leaves that touch the viewport");
        rig.Widget.FilledRows.ShouldBeSubsetOf(visible);
        ShouldAllTouchTheViewport(rig);
        output.WriteLine($"{where}: {visible.Count} of 600 rows painted, {rig.Widget.Painted().Count} nodes captured");
    }

    [Fact]
    public void CostLinePerRepeatFrame()
    {
        const int Frames = 5;
        foreach (var rows in new[] { 300, 3000 })
        {
            var rig = new Rig(rows);
            rig.Widget.Render(rig.Tree());
            rig.Scroll.AtomOffset = (int)(rig.Scroll.MaxOffset / 2f);
            for (var i = 0; i < 3; i++)
            {
                rig.Widget.Paint(rig.Widget.Arrange(rig.Tree()));
            }

            var arrangeTicks = 0L;
            var paintTicks = 0L;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Frames; i++)
            {
                var t0 = Stopwatch.GetTimestamp();
                var arranged = rig.Widget.Arrange(rig.Tree());
                var t1 = Stopwatch.GetTimestamp();
                rig.Widget.Paint(arranged);
                var t2 = Stopwatch.GetTimestamp();
                arrangeTicks += t1 - t0;
                paintTicks += t2 - t1;
            }

            var kb = (GC.GetAllocatedBytesForCurrentThread() - before) / 1024.0 / Frames;
            var arrangeMs = arrangeTicks * 1000.0 / Stopwatch.Frequency / Frames;
            var paintMs = paintTicks * 1000.0 / Stopwatch.Frequency / Frames;
            output.WriteLine($"COST {rows} kept rows: arrange {arrangeMs:F2} ms, paint {paintMs:F2} ms, {kb:F0} KB allocated per repeat frame");
            rig.Widget.Painted().Count.ShouldBeGreaterThan(0);
        }
    }
}
