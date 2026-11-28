using System.Collections.Immutable;
using System.Numerics;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A repeat arrange of a scrolled stack resolves only the rows whose node changed, so its cost and its allocation follow
/// the arranged window and not the list's length (DIR.Lib #338). The rows are the shape <see cref="LayoutScrollCullTests"/>
/// uses, built once and kept between frames, with a fresh stack built around them every frame.
/// </summary>
public class ScrolledStackKeptExtentsTests(ITestOutputHelper output)
{
    private const float Width = 600f;
    private const float ViewportH = 400f;
    private const float Pad = 6f;
    private const float RowGap = 4f;
    private const float Font = 10f;
    private const string ListId = "chat";

    private static Layout.Node Row(int i, int extraWords = 0)
    {
        var words = new Layout.Node[4 + (i * 7) % 61 + extraWords];
        for (var k = 0; k < words.Length; k++)
        {
            words[k] = Layout.Builder.Text(new string((char)('a' + (i + k) % 26), 3 + (i + k) % 5), Font);
        }

        return Layout.Builder.HStack(
                Layout.Builder.Text("user", Font),
                Layout.Builder.WrapH(words).WithGap(4f).WithLineGap(2f).WStar().HAuto())
            .WithGap(8f).Pad(Pad).WStar().HAuto()
            .Clickable(new HitResult.ListItemHit(ListId, i), _ => { });
    }

    private static Layout.Node[] BuildRows(int count) => [.. Enumerable.Range(0, count).Select(i => Row(i))];

    private static Layout.Node List(IReadOnlyList<Layout.Node> rows, ListScrollController scroll, float listPad = Pad)
        => Layout.Builder.VStack([.. rows]).WithGap(RowGap).WStar().HClamp(0f, ViewportH).Pad(listPad).WithScroll(scroll);

    private static int CountNodes(Layout.Node node) => node switch
    {
        Layout.Node.Stack s => 1 + s.Children.Sum(CountNodes),
        Layout.Node.Wrap w => 1 + w.Children.Sum(CountNodes),
        _ => 1,
    };

    private static int ScaledAtoms(float total) => (int)MathF.Ceiling(total);

    /// <summary>Whole pixels, and a key that names the scale, as a pixel host's context does.</summary>
    private sealed class FloatContext(float scale = 1f, bool keyed = true) : Layout.IMeasureContext<float>
    {
        public object? MeasureKey => keyed ? ("float", scale) : null;

        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize)
            => new(text.Length * fontSize * scale, fontSize * scale);

        public float ToSurface(float designUnits) => designUnits * scale;
    }

    /// <summary>A cell surface: a design unit is a fraction of a cell, rounded, as Console.Lib's is.</summary>
    private sealed class CellContext(float unitsPerCell = 6f, bool keyed = true) : Layout.IMeasureContext<int>
    {
        public object? MeasureKey => keyed ? ("cell", unitsPerCell) : null;

        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);

        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits / unitsPerCell);
    }

    private static ImmutableArray<Layout.ArrangedNode<T>> Arrange<T>(Layout.Node list, Rect<T> bounds,
        Layout.IMeasureContext<T> ctx) where T : INumber<T>
        => Layout.Engine.Arrange(list, bounds, ctx);

    private static Rect<float> FloatBounds(float width = Width, float height = ViewportH) => new(0f, 0f, width, height);

    private static Rect<int> CellBounds => new(0, 0, 100, 67);

    private static ListScrollController NewScroll(ScrollBarMode mode = ScrollBarMode.None)
        => new() { Mode = mode };

    /// <summary>The number of memo lookups the arrange of an already built list costs this thread.</summary>
    private static int LookupsOf(Layout.Node list, ListScrollController scroll, Rect<float> bounds, FloatContext ctx)
    {
        var before = Layout.MeasureMemo<float>.Lookups;
        Arrange(list, bounds, ctx);
        return Layout.MeasureMemo<float>.Lookups - before;
    }

    private static void SetUp(ListScrollController scroll, IReadOnlyList<Layout.Node> rows, Rect<float> bounds,
        FloatContext ctx, int offset)
    {
        Arrange(List(rows, scroll), bounds, ctx);
        scroll.AtomOffset = offset;
        Arrange(List(rows, scroll), bounds, ctx);
    }

    [Fact]
    public void ARepeatFrameDoesTheSameLookupsWhateverTheListLength()
    {
        var short300 = BuildRows(300);
        var long3000 = BuildRows(3000);
        var ctx = new FloatContext();
        var counts = new List<int>();
        foreach (var rows in new[] { short300, long3000 })
        {
            var scroll = NewScroll();
            SetUp(scroll, rows, FloatBounds(), ctx, 1000);
            var list = List(rows, scroll);
            counts.Add(LookupsOf(list, scroll, FloatBounds(), ctx));
        }

        var coldScroll = NewScroll();
        var coldList = List(short300, coldScroll);
        var cold = LookupsOf(coldList, coldScroll, FloatBounds(), new FloatContext(scale: 1.5f));

        output.WriteLine($"repeat frame lookups: 300 rows {counts[0]}, 3000 rows {counts[1]}; a cold frame of 300 rows {cold}");
        counts[0].ShouldBeLessThan(cold / 4, "a repeat frame does not look every row up");
        counts[1].ShouldBe(counts[0], "and 3000 rows cost what 300 do at the same offset");
    }

    [Fact]
    public void ReplacingOneRowFarFromTheWindowAddsThatRowsLookupsOnly()
    {
        var rows = BuildRows(1500);
        var scroll = NewScroll();
        var ctx = new FloatContext();
        SetUp(scroll, rows, FloatBounds(), ctx, 1000);
        var baseline = LookupsOf(List(rows, scroll), scroll, FloatBounds(), ctx);

        var replacement = Row(1300, extraWords: 40);
        rows[1300] = replacement;
        var list = List(rows, scroll);
        var after = LookupsOf(list, scroll, FloatBounds(), ctx);

        output.WriteLine($"baseline {baseline}, with one row replaced {after} ({CountNodes(replacement)} nodes in it)");
        (after - baseline).ShouldBeGreaterThan(0, "the new row is resolved");
        (after - baseline).ShouldBe(CountNodes(replacement), "one lookup for each node of it, and no other row is asked about");

        LookupsOf(List(rows, scroll), scroll, FloatBounds(), ctx).ShouldBe(baseline, "the next frame keeps it too");
    }

    [Fact]
    public void AnotherKeyAvailableSizeOrTheBarResolvesEveryRowAgain()
    {
        const int Count = 600;
        var rows = BuildRows(Count);

        int Repeat(Func<ListScrollController, (Rect<float>, FloatContext)> first, Func<ListScrollController, (Rect<float>, FloatContext)> then,
            ScrollBarMode firstMode = ScrollBarMode.None, ScrollBarMode thenMode = ScrollBarMode.None)
        {
            var scroll = NewScroll(firstMode);
            var (b1, c1) = first(scroll);
            Arrange(List(rows, scroll), b1, c1);
            Arrange(List(rows, scroll), b1, c1);
            scroll.Mode = thenMode;
            var (b2, c2) = then(scroll);
            return LookupsOf(List(rows, scroll), scroll, b2, c2);
        }

        var ctx = new FloatContext();
        (Rect<float>, FloatContext) Same(ListScrollController _) => (FloatBounds(), ctx);

        var cold = Repeat(Same, _ => (FloatBounds(), new FloatContext(scale: 1.25f)));
        cold.ShouldBeGreaterThanOrEqualTo(Count, "another scale (another MeasureKey)");
        Repeat(Same, Same).ShouldBeLessThan(cold / 4, "control: nothing changed");
        Repeat(Same, _ => (FloatBounds(width: 500f), ctx))
            .ShouldBeGreaterThanOrEqualTo(Count, "another width");
        Repeat(Same, _ => (FloatBounds(height: 300f), ctx))
            .ShouldBeGreaterThanOrEqualTo(Count, "another viewport height");
        // The bar's column is reserved only beyond the stack's own padding (6 units, as wide as the bar), so this list has none.
        var barScroll = NewScroll();
        Arrange(List(rows, barScroll, 0f), FloatBounds(), ctx);
        Arrange(List(rows, barScroll, 0f), FloatBounds(), ctx);
        barScroll.Mode = ScrollBarMode.Interactive;
        LookupsOf(List(rows, barScroll, 0f), barScroll, FloatBounds(), ctx)
            .ShouldBeGreaterThanOrEqualTo(Count, "the bar appearing resolves the rows at the narrower extent");
        barScroll.ContentCrossExtentPx.ShouldBeLessThan(Width, "the bar shows");

        // And with the bar up the next frame is kept again, both passes.
        LookupsOf(List(rows, barScroll, 0f), barScroll, FloatBounds(), ctx).ShouldBeLessThan(cold / 2, "a list with its bar is kept too");
    }

    [Fact]
    public void AControllerHandedToAnotherStackKeepsNothingStale()
    {
        var scroll = NewScroll();
        var ctx = new FloatContext();
        var first = BuildRows(400);
        var second = Enumerable.Range(0, 400).Select(i => Row(i + 1000)).ToArray();
        Arrange(List(first, scroll), FloatBounds(), ctx);
        Arrange(List(first, scroll), FloatBounds(), ctx);

        Arrange(List(second, scroll), FloatBounds(), ctx);
        var plain = NewScroll();
        Arrange(List(second, plain), FloatBounds(), new FloatContext(keyed: false));
        scroll.TotalAtoms.ShouldBe(plain.TotalAtoms, "the other stack's rows are measured, not the first stack's kept mains");
    }

    private readonly record struct Geometry(float[] Tops, float[] Heights, float Total);

    private static Geometry Reference<T>(IReadOnlyList<Layout.Node> rows, Rect<T> bounds, Layout.IMeasureContext<T> keyless)
        where T : INumber<T>
    {
        var pad = keyless.ToSurface(Pad);
        var gap = keyless.ToSurface(RowGap);
        var room = new Layout.Size<T>(bounds.Width - pad - pad, bounds.Height - pad - pad);
        var heights = new float[rows.Count];
        var tops = new float[rows.Count];
        var at = float.CreateChecked(pad);
        for (var i = 0; i < rows.Count; i++)
        {
            heights[i] = float.CreateChecked(Layout.Engine.Measure(rows[i], room, keyless).Height);
            tops[i] = at;
            at += heights[i] + float.CreateChecked(gap);
        }

        return new Geometry(tops, heights, at - float.CreateChecked(gap) + float.CreateChecked(pad));
    }

    private void ShouldMatchTheReference<T>(string label, IReadOnlyList<Layout.Node> rows, Rect<T> bounds,
        Layout.IMeasureContext<T> keyed, Layout.IMeasureContext<T> keyless) where T : INumber<T>
    {
        var reference = Reference(rows, bounds, keyless);
        var scroll = NewScroll();
        Arrange(List(rows, scroll), bounds, keyed);
        scroll.TotalAtoms.ShouldBe(ScaledAtoms(reference.Total), $"{label}: the extent");

        foreach (var where in new[] { "top", "middle", "end" })
        {
            scroll.AtomOffset = where switch
            {
                "middle" => (int)(scroll.MaxOffset / 2f),
                "end" => (int)scroll.MaxOffset,
                _ => 0,
            };

            // Two kept frames, so the second is a repeat that takes the kept mains (the first resolves them at this offset's
            // context), then the ordinary path (no key, so nothing is kept and the next offset starts cold again).
            Arrange(List(rows, scroll), bounds, keyed);
            foreach (var (path, ctx) in new[] { ("kept", keyed), ("unkept", keyless) })
            {
                var arranged = Arrange(List(rows, scroll), bounds, ctx);
                scroll.TotalAtoms.ShouldBe(ScaledAtoms(reference.Total), $"{label} {where} {path}: TotalAtoms");

                var offset = MathF.Truncate(scroll.Offset);
                var seen = 0;
                foreach (var a in arranged.Where(a => a.Node.Hit is HitResult.ListItemHit))
                {
                    var i = ((HitResult.ListItemHit)a.Node.Hit!).Index;
                    float.CreateChecked(a.Bounds.Y).ShouldBe(reference.Tops[i] - offset, 0.01f, $"{label} {where} {path}: row {i} Y");
                    float.CreateChecked(a.Bounds.Height).ShouldBe(reference.Heights[i], 0.01f, $"{label} {where} {path}: row {i} height");
                    seen++;
                }

                seen.ShouldBeGreaterThan(2, $"{label} {where} {path}: rows are arranged");
            }
        }
    }

    [Fact]
    public void ArrangedRowsAreWhereTheirOwnMeasuresPutThem_Float()
        => ShouldMatchTheReference("float", BuildRows(300), FloatBounds(), new FloatContext(), new FloatContext(keyed: false));

    [Fact]
    public void ArrangedRowsAreWhereTheirOwnMeasuresPutThem_Cells()
        => ShouldMatchTheReference("cells", BuildRows(300), CellBounds, new CellContext(), new CellContext(keyed: false));

    [Fact]
    public void ARepeatFrameAllocatesNoMoreForALongListThanForAShortOne()
    {
        var ctx = new FloatContext();
        var allocated = new List<long>();
        foreach (var count in new[] { 200, 600, 1200 })
        {
            var rows = BuildRows(count);
            var scroll = NewScroll();
            SetUp(scroll, rows, FloatBounds(), ctx, 1000);
            Arrange(List(rows, scroll), FloatBounds(), ctx);

            // The stack around the rows is built before the count starts: that is the host's, and a list of rows is its length.
            var list = List(rows, scroll);
            var before = GC.GetAllocatedBytesForCurrentThread();
            Arrange(list, FloatBounds(), ctx);
            allocated.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        }

        output.WriteLine($"repeat arrange allocated {allocated[0]} B at 200 rows, {allocated[1]} B at 600, {allocated[2]} B at 1200");
        (allocated[2] - allocated[0]).ShouldBeLessThan(8 * 1024, "the allocation follows the window, not the list");
    }

    [Fact]
    public void AParentThatMeasuresTheListAsksAboutChangedRowsOnly()
    {
        var rows = BuildRows(1500);
        var scroll = NewScroll();
        var ctx = new FloatContext();
        var room = new Layout.Size<float>(Width, 10_000f);

        int Lookups(out Layout.Size<float> size)
        {
            var list = List(rows, scroll);
            var before = Layout.MeasureMemo<float>.Lookups;
            size = Layout.Engine.Measure(list, room, ctx);
            return Layout.MeasureMemo<float>.Lookups - before;
        }

        var cold = Lookups(out var coldSize);
        var repeat = Lookups(out var repeatSize);
        output.WriteLine($"parent measure lookups: cold {cold}, repeat {repeat}");
        repeatSize.ShouldBe(coldSize);
        repeat.ShouldBeLessThan(cold / 100, "a repeat measure does not look every row up");

        var plain = Layout.Engine.Measure(List(rows, NewScroll()), room, new FloatContext(keyed: false));
        repeatSize.ShouldBe(plain, "the kept measure is the one the ordinary resolve gives");
    }

    [Fact]
    public void ACollapsedChildOnTheOrdinaryPathDoesNotShortenTheScrollExtent()
    {
        // No MeasureKey: the stack takes the ordinary path, which counts included children across its two passes
        // (the second runs because the bar shows). A collapsed child must be counted once, not once per pass.
        static ListScrollController Total(bool withCollapsing)
        {
            var kids = new List<Layout.Node>();
            for (var i = 0; i < 40; i++)
            {
                kids.Add(Layout.Builder.Text("row", Font).HFixed(30f));
                if (withCollapsing && i == 10)
                {
                    kids.Add(Layout.Builder.Text("x", Font).HFixed(5f).CollapseBelow(20f));
                }
            }

            var scroll = NewScroll(ScrollBarMode.Interactive);
            var list = Layout.Builder.VStack([.. kids]).WithGap(4f).WStar().HStar().WithScroll(scroll);
            var ctx = new FloatContext(keyed: false);
            Arrange(list, new Rect<float>(0f, 0f, 300f, 200f), ctx);
            Arrange(list, new Rect<float>(0f, 0f, 300f, 200f), ctx);
            return scroll;
        }

        Total(true).TotalAtoms.ShouldBe(Total(false).TotalAtoms);
    }
}
