using System.Collections.Immutable;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A scrolled stack arranges only the children in or near its viewport; the rest are skipped, their main extents still
/// taken from the measure so the scroll extent counts every row (DIR.Lib #170, part 1).
/// </summary>
/// <remarks>
/// <para>
/// The list is about what pilot's chat is: 300 rows, each a padded row with a label and a few words of wrapping text, in
/// a scrolled column. The row nodes are built once and a fresh stack is built around them every frame, which is what a
/// host that keeps its heavy subtrees between frames does, so the measure memo keeps every row across frames.
/// </para>
/// <para>
/// The expected geometry comes from measuring each row on its own and adding the heights up, never from the arrange under
/// test: that is what the extent was before culling, so a culled arrange has to land on the same number.
/// </para>
/// </remarks>
public class LayoutScrollCullTests(ITestOutputHelper output)
{
    private const int Rows = 300;
    private const float Width = 600f;
    private const float ViewportH = 400f;
    private const float Pad = 6f;
    private const float RowGap = 4f;
    private const float Font = 10f;
    private const string ListId = "chat";

    private static readonly Layout.Size<float> RowRoom = new(Width - 2 * Pad, ViewportH - 2 * Pad);

    /// <summary>A label and between 4 and 64 words, so rows run from one line to about eight.</summary>
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

    private static Layout.Node[] BuildRows() => [.. Enumerable.Range(0, Rows).Select(i => Row(i))];

    /// <summary>A fresh scrolled column around the same rows, clamped to the viewport.</summary>
    private static Layout.Node List(IReadOnlyList<Layout.Node> rows, ListScrollController scroll)
        => Layout.Builder.VStack([.. rows]).WithGap(RowGap).WStar().HClamp(0f, ViewportH).Pad(Pad).WithScroll(scroll);

    private static ImmutableArray<Layout.ArrangedNode<float>> Frame(IReadOnlyList<Layout.Node> rows,
        ListScrollController scroll, CountingContext ctx)
        => Layout.Engine.Arrange(List(rows, scroll), new Rect<float>(0f, 0f, Width, ViewportH), ctx);

    /// <summary>Where each row sits in what the list scrolls over, from the rows' own measures.</summary>
    private readonly record struct Geometry(float[] Tops, float[] Heights, float Total);

    private static Geometry Reference(IReadOnlyList<Layout.Node> rows, Layout.Size<float> room)
    {
        // No key, so nothing is shared with the context under test.
        var plain = new CountingContext();
        var heights = new float[rows.Count];
        var tops = new float[rows.Count];
        var at = Pad;
        for (var i = 0; i < rows.Count; i++)
        {
            heights[i] = Layout.Engine.Measure(rows[i], room, plain).Height;
            tops[i] = at;
            at += heights[i] + RowGap;
        }

        return new Geometry(tops, heights, at - RowGap + Pad);
    }

    private static int ExpectedAtoms(Geometry g) => (int)MathF.Ceiling(g.Total);

    private static List<int> ArrangedRows(ImmutableArray<Layout.ArrangedNode<float>> arranged)
        => [.. arranged.Select(a => a.Node.Hit).OfType<HitResult.ListItemHit>().Select(h => h.Index)];

    private static int CountNodes(Layout.Node node) => node switch
    {
        Layout.Node.Stack s => 1 + s.Children.Sum(CountNodes),
        Layout.Node.Wrap w => 1 + w.Children.Sum(CountNodes),
        _ => 1,
    };

    private static int CountLeaves(Layout.Node node) => node switch
    {
        Layout.Node.Stack s => s.Children.Sum(CountLeaves),
        Layout.Node.Wrap w => w.Children.Sum(CountLeaves),
        _ => 1,
    };

    /// <summary>The rows a viewport at <paramref name="offset"/> shows, at all.</summary>
    private static List<int> Visible(Geometry g, float offset)
        => [.. Enumerable.Range(0, Rows).Where(i => g.Tops[i] - offset + g.Heights[i] > 0f && g.Tops[i] - offset < ViewportH)];

    /// <summary>
    /// Every row whose extent lies within a viewport's height of the viewport is arranged, and no row further off is, with a
    /// pixel of slack at the two edges so the rule's boundary is not what a test depends on. And each arranged row is where
    /// the unculled list puts it: skipping the rows before it moved nothing.
    /// </summary>
    private static void ShouldArrangeTheViewportAndItsMargin(ImmutableArray<Layout.ArrangedNode<float>> arranged,
        Geometry g, float offset)
    {
        var got = ArrangedRows(arranged).ToHashSet();
        for (var i = 0; i < Rows; i++)
        {
            var top = g.Tops[i] - offset;
            var bottom = top + g.Heights[i];
            if (bottom > -ViewportH + 1f && top < 2 * ViewportH - 1f)
            {
                got.ShouldContain(i, $"row {i} ({top}..{bottom}) is in or within a viewport of the viewport");
            }

            if (bottom < -ViewportH - 1f || top > 2 * ViewportH + 1f)
            {
                got.ShouldNotContain(i, $"row {i} ({top}..{bottom}) is further than a viewport from the viewport");
            }
        }

        foreach (var a in arranged.Where(a => a.Node.Hit is HitResult.ListItemHit))
        {
            var i = ((HitResult.ListItemHit)a.Node.Hit!).Index;
            a.Bounds.Y.ShouldBe(g.Tops[i] - offset, 0.01f, $"row {i} is where the unculled list puts it");
            a.Bounds.Height.ShouldBe(g.Heights[i], 0.01f, $"row {i} is as tall as it measures");
        }
    }

    [Theory]
    [InlineData("top")]
    [InlineData("middle")]
    [InlineData("end")]
    public void TheSecondFrameOfAKeyedListCostsWhatItsVisibleRowsCost(string where)
    {
        var rows = BuildRows();
        var scroll = new ListScrollController { Mode = ScrollBarMode.None };
        var ctx = new CountingContext { Key = "k" };

        // Frame 1: cold, and it has to measure every row, because the extent is every row's height.
        Frame(rows, scroll, ctx);
        var reference = Reference(rows, RowRoom);
        scroll.TotalAtoms.ShouldBe(ExpectedAtoms(reference), "the extent is every row's height, as before culling");
        var coldCalls = ctx.TextCalls;
        coldCalls.ShouldBeLessThanOrEqualTo(rows.Sum(CountLeaves), "no leaf is measured twice in one pass");

        scroll.AtomOffset = where switch
        {
            "middle" => (int)(scroll.MaxOffset / 2f),
            "end" => (int)scroll.MaxOffset,
            _ => 0,
        };

        ctx.TextCalls = 0;
        var arranged = Frame(rows, scroll, ctx);

        var visible = Visible(reference, scroll.Offset);
        visible.Count.ShouldBeGreaterThan(2);
        ShouldArrangeTheViewportAndItsMargin(arranged, reference, scroll.Offset);
        scroll.TotalAtoms.ShouldBe(ExpectedAtoms(reference), $"the extent at the {where} is every row's height too");

        var visibleLeaves = visible.Sum(i => CountLeaves(rows[i]));
        ctx.TextCalls.ShouldBeLessThanOrEqualTo(3 * visibleLeaves, "the measures are kept, and only what is shown could need one");

        var unculledNodes = 1 + rows.Sum(CountNodes);
        var visibleNodes = 1 + visible.Sum(i => CountNodes(rows[i]));
        output.WriteLine($"{where}: {visible.Count} rows visible, {ArrangedRows(arranged).Count} of {Rows} arranged; "
            + $"{arranged.Length} of {unculledNodes} nodes ({visibleNodes} visible); {ctx.TextCalls} MeasureText calls "
            + $"(frame 1: {coldCalls} for {rows.Sum(CountLeaves)} leaves)");
        arranged.Length.ShouldBeLessThanOrEqualTo(5 * visibleNodes, "a small multiple of what is visible");
        arranged.Length.ShouldBeLessThan(unculledNodes / 4, "and nowhere near every row");
        ArrangedRows(arranged).Count.ShouldBeLessThanOrEqualTo(4 * visible.Count + 4);
    }

    [Fact]
    public void AListAnchoredToItsTailArrangesTheTailOnItsFirstFrame()
    {
        // A chat follows its tail: the controller pins the offset to the end inside SetExtent, which is before the rows are
        // placed, so the first frame already culls around the end rather than around the top.
        var rows = BuildRows();
        var scroll = new ListScrollController { Mode = ScrollBarMode.None, Anchor = ScrollAnchor.Bottom };

        var arranged = Frame(rows, scroll, new CountingContext { Key = "k" });

        var reference = Reference(rows, RowRoom);
        scroll.TotalAtoms.ShouldBe(ExpectedAtoms(reference));
        scroll.Offset.ShouldBe(scroll.MaxOffset);
        ShouldArrangeTheViewportAndItsMargin(arranged, reference, scroll.Offset);
        ArrangedRows(arranged).ShouldContain(Rows - 1);
        ArrangedRows(arranged).ShouldNotContain(0);
    }

    [Fact]
    public void ARowFarOutsideTheViewportStillCountsTowardsTheExtent()
    {
        var rows = BuildRows();
        var scroll = new ListScrollController { Mode = ScrollBarMode.None };
        var ctx = new CountingContext { Key = "k" };
        Frame(rows, scroll, ctx);
        var before = scroll.TotalAtoms;

        // Row 250 is nowhere near the viewport, and a fresh node replaces it, taller: the host changed one reply.
        var replacement = Row(250, extraWords: 40);
        rows[250] = replacement;
        ctx.TextCalls = 0;
        var arranged = Frame(rows, scroll, ctx);

        ArrangedRows(arranged).ShouldNotContain(250, "it is skipped");
        var reference = Reference(rows, RowRoom);
        scroll.TotalAtoms.ShouldBe(ExpectedAtoms(reference), "and its new height is in the extent");
        scroll.TotalAtoms.ShouldBeGreaterThan(before);
        ctx.TextCalls.ShouldBeLessThanOrEqualTo(CountLeaves(replacement), "only the replaced row is measured");
    }

    [Fact]
    public void AListWhoseBarShowsKeepsItsExtentAndCullsToo()
    {
        // The bar's column narrows the rows, which is a second resolve before the rows are placed; the cull comes after both.
        var rows = BuildRows();
        var scroll = new ListScrollController { Mode = ScrollBarMode.Interactive };

        var arranged = Frame(rows, scroll, new CountingContext { Key = "k" });

        scroll.ContentCrossExtentPx.ShouldBeLessThan(Width, "the bar shows");
        var reference = Reference(rows, new Layout.Size<float>(scroll.ContentCrossExtentPx - Pad, ViewportH - 2 * Pad));
        scroll.TotalAtoms.ShouldBe(ExpectedAtoms(reference));
        ShouldArrangeTheViewportAndItsMargin(arranged, reference, scroll.Offset);
    }

    [Fact]
    public void AShortListIsArrangedWhole()
    {
        var rows = BuildRows().Take(3).ToArray();
        var scroll = new ListScrollController { Mode = ScrollBarMode.None };

        var arranged = Frame(rows, scroll, new CountingContext { Key = "k" });

        ArrangedRows(arranged).ShouldBe([0, 1, 2]);
    }

    private static readonly RectF32 WidgetBounds = new(0, 0, Width, ViewportH);

    private static (DeclarationWidget Widget, ListScrollController Scroll) Fixture()
        => (new DeclarationWidget(new DeclarationStubRenderer((uint)Width, (uint)ViewportH)),
            new ListScrollController { Mode = ScrollBarMode.None });

    private static ClickableRegion? Region(DeclarationWidget widget, int index)
        => widget.GetRegisteredRegions().Cast<ClickableRegion?>()
            .FirstOrDefault(r => r!.Value.Result is HitResult.ListItemHit { Index: var i } && i == index);

    private static int LastRegistered(DeclarationWidget widget)
        => widget.GetRegisteredRegions().Select(r => r.Result).OfType<HitResult.ListItemHit>().Max(h => h.Index);

    [Fact]
    public void TheKeyboardStepsFromTheLastVisibleRowOntoTheNext()
    {
        var rows = BuildRows();
        var (widget, scroll) = Fixture();
        widget.Render(List(rows, scroll), WidgetBounds);
        var last = LastRegistered(widget);
        last.ShouldBeGreaterThan(1);
        Region(widget, last + 1).ShouldBeNull("the row below is clipped: nothing registers for it");

        widget.ListCursor.Open(ListId, last);
        widget.Render(List(rows, scroll), WidgetBounds);

        widget.MoveListCursor(1).ShouldBeTrue("the row below was arranged, clipped, and so can be stepped onto");
        widget.ListCursor.Index.ShouldBe(last + 1);

        widget.Render(List(rows, scroll), WidgetBounds);
        Region(widget, last + 1).ShouldNotBeNull("and it is brought into view");
    }

    [Fact]
    public void TheArrowsWalkAcrossRowsTheListHadSkipped()
    {
        var rows = BuildRows();
        var (widget, scroll) = Fixture();
        widget.ListCursor.Open(ListId, 0);
        widget.Render(List(rows, scroll), WidgetBounds);

        // Far past the first viewport and its margin: every step needs the next row to have been arranged.
        for (var expected = 1; expected <= 80; expected++)
        {
            widget.MoveListCursor(1).ShouldBeTrue($"row {expected} is reachable");
            widget.ListCursor.Index.ShouldBe(expected);
            widget.Render(List(rows, scroll), WidgetBounds);
            Region(widget, expected).ShouldNotBeNull($"row {expected} is in view");
        }

        // And back up through rows that scrolled out of the arranged window on the way.
        for (var expected = 79; expected >= 40; expected--)
        {
            widget.MoveListCursor(-1).ShouldBeTrue($"row {expected} is reachable going up");
            widget.ListCursor.Index.ShouldBe(expected);
            widget.Render(List(rows, scroll), WidgetBounds);
            Region(widget, expected).ShouldNotBeNull($"row {expected} is in view");
        }
    }

    private sealed class CountingContext : Layout.IMeasureContext<float>
    {
        public object? Key { get; init; }

        public int TextCalls { get; set; }

        public object? MeasureKey => Key;

        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize)
        {
            TextCalls++;
            return new(text.Length * fontSize, fontSize);
        }

        public float ToSurface(float designUnits) => designUnits;
    }
}
