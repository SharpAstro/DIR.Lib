using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The data views of 11.19: <see cref="Layout.Builder.VirtualList"/>, <see cref="Layout.Builder.TableHeader"/> /
/// <see cref="Layout.Builder.TableRow"/>, <see cref="TreeExpansion{T}"/> with <see cref="Layout.Builder.TreeRow"/>,
/// and <see cref="Layout.Builder.Treemap"/>. Each is asserted through the regions a real paint registers,
/// since draw==hit is the property each of them exists to keep.
/// </summary>
public class LayoutDataViewsTests
{
    private static DeclarationWidget Widget(uint w = 400, uint h = 300) => new(new DeclarationStubRenderer(w, h));

    private static List<ClickableRegion> Regions(DeclarationWidget widget) => widget.GetRegisteredRegions().ToList();

    private static ClickableRegion Named(DeclarationWidget widget, string name)
        => Regions(widget).Single(r => r.Result is HitResult.ButtonHit b && b.Action == name);

    // ---- VirtualList -------------------------------------------------------------------------

    private static Layout.Node List(ListScrollController scroll, int count, List<int> built)
        => Layout.Builder.VirtualList(count, 20f, scroll, i =>
        {
            built.Add(i);
            return Layout.Builder.Spacer().RowH(20f).Clickable(new HitResult.ListItemHit("rows", i), _ => { });
        }).Stretch();

    [Fact]
    public void VirtualList_BuildsOnlyTheViewport_ButScrollsOverEveryRow()
    {
        var widget = Widget();
        var scroll = new ListScrollController { Mode = ScrollBarMode.None };
        var bounds = new RectF32(0, 0, 200, 100);
        var built = new List<int>();

        widget.Render(List(scroll, 100_000, built), bounds);   // binds the viewport
        built.Clear();
        widget.Render(List(scroll, 100_000, built), bounds);

        built.Count.ShouldBeLessThan(20, "five rows show; only those and the overscan are built");
        scroll.TotalAtoms.ShouldBe(100_000, "the spacers stand in for every row not built");
        Regions(widget).Count(r => r.Result is HitResult.ListItemHit).ShouldBe(5);
    }

    [Fact]
    public void VirtualList_ScrolledToTheMiddle_BuildsTheMiddle()
    {
        var widget = Widget();
        var scroll = new ListScrollController { Mode = ScrollBarMode.None };
        var bounds = new RectF32(0, 0, 200, 100);
        var built = new List<int>();

        widget.Render(List(scroll, 100_000, built), bounds);
        scroll.AtomOffset = 50_000;
        built.Clear();
        widget.Render(List(scroll, 100_000, built), bounds);

        built.ShouldContain(50_000);
        built.Min().ShouldBeGreaterThan(49_990);
        var top = Regions(widget).Where(r => r.Result is HitResult.ListItemHit).OrderBy(r => r.Y).First();
        ((HitResult.ListItemHit)top.Result).Index.ShouldBe(50_000);
        top.Y.ShouldBe(0f, 0.5f);
    }

    // ---- Table -------------------------------------------------------------------------------

    private static readonly TableStyle Style = new(
        new RGBAColor32(0x20, 0x20, 0x20, 0xff), new RGBAColor32(0xff, 0xff, 0xff, 0xff),
        new RGBAColor32(0x30, 0x30, 0x30, 0xff), new RGBAColor32(0xff, 0xcc, 0x00, 0xff));

    private static readonly TableColumn[] Columns =
    [
        new("size", Sizing.Fixed(80f), TextAlign.Far) { DescendingFirst = true },
        new("kind", Sizing.Fixed(60f)) { Sortable = false },
        new("path", Sizing.Star()),
    ];

    [Fact]
    public void TableHeaderAndRow_PutEveryTitleOverItsValues()
    {
        var widget = Widget();
        var cells = Columns.Select((c, i) => Layout.Builder.Spacer().Clickable(new HitResult.ButtonHit("cell" + i), _ => { })).ToArray();
        var tree = Layout.Builder.VStack(
            Layout.Builder.TableHeader(Columns, new TableSort(0, true), _ => { }, Style).RowH(24f),
            Layout.Builder.TableRow(Columns, cells, Style).RowH(20f)).Stretch();

        widget.Render(tree, new RectF32(0, 0, 400, 100));

        for (var i = 0; i < Columns.Length; i++)
        {
            var head = Named(widget, "table-header:" + Columns[i].Header);
            var cell = Named(widget, "cell" + i);
            head.X.ShouldBe(cell.X, 0.01f, Columns[i].Header);
            head.Width.ShouldBe(cell.Width, 0.01f, Columns[i].Header);
        }
    }

    [Fact]
    public void TableHeader_APressSortsThatColumn_AndAgainFlipsIt()
    {
        var widget = Widget();
        TableSort? asked = null;
        void Paint(TableSort? sort) => widget.Render(
            Layout.Builder.TableHeader(Columns, sort, s => asked = s, Style).RowH(24f), new RectF32(0, 0, 400, 24));

        Paint(null);
        Named(widget, "table-header:size").OnClick!(InputModifier.None);
        asked.ShouldBe(new TableSort(0, Descending: true), "a size column starts biggest-first");

        Paint(asked);
        Named(widget, "table-header:size").OnClick!(InputModifier.None);
        asked.ShouldBe(new TableSort(0, Descending: false));

        Paint(asked);
        Named(widget, "table-header:path").OnClick!(InputModifier.None);
        asked.ShouldBe(new TableSort(2, Descending: false), "another column starts in its own direction");

        Named(widget, "table-header:kind").OnClick.ShouldBeNull("an unsortable title swallows the press and does nothing");
    }

    // ---- Tree --------------------------------------------------------------------------------

    private sealed class N(string name, params N[] children)
    {
        public string Name { get; } = name;
        public N[] Kids { get; } = children;
        public int Reads;
        public override string ToString() => Name;
    }

    private static IReadOnlyList<N> Kids(N n) { n.Reads++; return n.Kids; }

    [Fact]
    public void Flatten_ReadsChildrenOnlyOfOpenNodes()
    {
        var deep = new N("deep", new N("x"));
        var a = new N("a", deep);
        var root = new N("root", a, new N("b"));
        var tree = new TreeExpansion<N>();
        tree.Expand(root);

        var rows = tree.Flatten(root, Kids, n => n.Kids.Length > 0);

        rows.Select(r => r.Item.Name).ShouldBe(["root", "a", "b"]);
        rows.Select(r => r.Depth).ShouldBe([0, 1, 1]);
        rows[1].HasChildren.ShouldBeTrue();
        rows[1].IsExpanded.ShouldBeFalse();
        rows[1].ParentIndex.ShouldBe(0);
        a.Reads.ShouldBe(0, "a closed node's children are never asked for");
        deep.Reads.ShouldBe(0);
    }

    [Fact]
    public void TreeKeys_RightOpensThenDescends_LeftClosesThenAscends()
    {
        var a = new N("a", new N("a1"));
        var root = new N("root", a);
        var tree = new TreeExpansion<N>();
        tree.Expand(root);
        List<TreeRowItem<N>> Rows() => tree.Flatten(root, Kids, n => n.Kids.Length > 0);

        tree.HandleKey(InputKey.Right, 1, Rows(), out var move).ShouldBeTrue();
        move.ShouldBe(-1, "the first Right opens");
        tree.IsExpanded(a).ShouldBeTrue();

        tree.HandleKey(InputKey.Right, 1, Rows(), out move).ShouldBeTrue();
        move.ShouldBe(2, "the second steps onto the first child");

        tree.HandleKey(InputKey.Left, 2, Rows(), out move).ShouldBeTrue();
        move.ShouldBe(1, "Left on a closed leaf steps to its parent");

        tree.HandleKey(InputKey.Left, 1, Rows(), out move).ShouldBeTrue();
        tree.IsExpanded(a).ShouldBeFalse("Left on an open node closes it");

        tree.HandleKey(InputKey.Up, 1, Rows(), out _).ShouldBeFalse("Up and Down stay with the list");
    }

    [Fact]
    public void TreeRow_TheCaretTakesItsOwnPress_InsideTheRow()
    {
        var widget = Widget();
        var toggled = 0;
        var selected = 0;
        var row = Layout.Builder.TreeRow(2, hasChildren: true, isExpanded: false, () => toggled++,
                Layout.Builder.Text("name"), new TreeRowStyle(new RGBAColor32(0xff, 0xff, 0xff, 0xff)))
            .RowH(20f).Clickable(new HitResult.ListItemHit("tree", 0), _ => selected++);

        widget.Render(row, new RectF32(0, 0, 300, 20));

        var caret = Regions(widget).Single(r => r.Result is HitResult.ChromeHit && r.OnClick is not null);
        caret.X.ShouldBe(32f, 0.01f, "two levels of 16 indent");
        caret.OnClick!(InputModifier.None);
        toggled.ShouldBe(1);
        selected.ShouldBe(0);
    }

    // ---- Treemap -----------------------------------------------------------------------------

    [Fact]
    public void Squarify_KeepsCellsCloseToSquare()
    {
        // The paper's example: six weights in a 6x4 rect.
        double[] weights = [6, 6, 4, 3, 2, 2, 1];
        var strips = Layout.Treemap.Squarify(weights, 6f / 4f);

        strips.Sum(s => s.Count).ShouldBe(weights.Length);
        strips[0].ShouldBe(new Layout.Treemap.Strip(0, 2, Vertical: true));
    }

    [Fact]
    public void Treemap_CellAreasAreProportional_AndTileTheRect()
    {
        var widget = Widget();
        double[] weights = [1, 50, 7, 300, 20, 20, 3, 0, 120];   // unsorted, with a zero
        var bounds = new RectF32(0, 0, 300, 200);

        widget.Render(Layout.Builder.Treemap(weights, 1.5f,
            i => Layout.Builder.Spacer().Clickable(new HitResult.ButtonHit("c" + i), _ => { })).Stretch(), bounds);

        var total = weights.Sum();
        var area = 0.0;
        for (var i = 0; i < weights.Length; i++)
        {
            var regions = Regions(widget).Where(r => r.Result is HitResult.ButtonHit b && b.Action == "c" + i).ToList();
            if (weights[i] == 0)
            {
                regions.ShouldBeEmpty("a zero weight gets no cell");
                continue;
            }

            var r = regions.Single();
            var cell = r.Width * r.Height;
            area += cell;
            (cell / (300.0 * 200.0)).ShouldBe(weights[i] / total, 0.002, $"cell {i}");
            r.X.ShouldBeGreaterThanOrEqualTo(-0.01f);
            (r.X + r.Width).ShouldBeLessThanOrEqualTo(300.01f);
            (r.Y + r.Height).ShouldBeLessThanOrEqualTo(200.01f);
        }

        area.ShouldBe(300.0 * 200.0, 1.0, "the cells tile the rect");
    }
}
