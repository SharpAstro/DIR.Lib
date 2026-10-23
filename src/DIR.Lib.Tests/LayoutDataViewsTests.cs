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

    // ---- Table: columns resized by dragging a divider ------------------------------------------

    private const string Divider0 = "table-header:divider:0";
    private const string Divider1 = "table-header:divider:1";

    private static void PaintResizable(DeclarationWidget widget, TableColumnWidths widths, TableColumn[]? columns = null,
        DesignScale? scale = null)
    {
        columns ??= Columns;
        var cells = columns.Select((c, i) => Layout.Builder.Spacer().Clickable(new HitResult.ButtonHit("cell" + i), _ => { })).ToArray();
        widget.Render(Layout.Builder.VStack(
            Layout.Builder.TableHeader(columns, null, _ => { }, Style, widths, scale ?? DesignScale.One).RowH(24f),
            Layout.Builder.TableRow(columns, cells, Style, widths).RowH(20f)).Stretch(), new RectF32(0, 0, 400, 100));
    }

    /// <summary>Presses the divider's middle and returns the capture, as a router does for a real press.</summary>
    private static DragCapture Grab(DeclarationWidget widget, string divider, out float pressX)
    {
        var region = Named(widget, divider);
        pressX = region.X + region.Width / 2f;
        return region.OnPress.ShouldNotBeNull()(new PointerPress(pressX, region.Y + 2f, MouseButton.Left, InputModifier.None, 1))
            .ShouldNotBeNull();
    }

    private static void DragTo(DragCapture capture, float x) => capture.Move(new PointerMove(x, 5f, MouseButton.Left, InputModifier.None));

    private static void ShouldLineUp(DeclarationWidget widget, TableColumn[] columns)
    {
        for (var i = 0; i < columns.Length; i++)
        {
            var head = Named(widget, "table-header:" + columns[i].Header);
            var cell = Named(widget, "cell" + i);
            head.X.ShouldBe(cell.X, 0.01f, columns[i].Header);
            head.Width.ShouldBe(cell.Width, 0.01f, columns[i].Header);
        }
    }

    [Fact]
    public void DraggingTheDividerBetweenTwoFixedColumns_WidensOneAndNarrowsItsNeighbour_HeaderAndRowsTogether()
    {
        var widget = Widget();
        var widths = new TableColumnWidths();
        PaintResizable(widget, widths);
        var pathBefore = Named(widget, "cell2").Width;
        var capture = Grab(widget, Divider0, out var x);

        DragTo(capture, x + 20f);
        PaintResizable(widget, widths);

        Named(widget, "table-header:size").Width.ShouldBe(100f, 0.01f);
        Named(widget, "table-header:kind").Width.ShouldBe(40f, 0.01f);
        Named(widget, "cell0").Width.ShouldBe(100f, 0.01f);
        Named(widget, "cell1").Width.ShouldBe(40f, 0.01f);
        Named(widget, "cell2").Width.ShouldBe(pathBefore, 0.01f, "the star column is untouched by a drag between two fixed ones");
        ShouldLineUp(widget, Columns);
    }

    [Fact]
    public void TheDragIsMeasuredFromThePress_NotAccumulatedAcrossMoves()
    {
        var widget = Widget();
        var widths = new TableColumnWidths();
        PaintResizable(widget, widths);
        var capture = Grab(widget, Divider0, out var x);

        DragTo(capture, x + 10f);
        DragTo(capture, x + 20f);
        DragTo(capture, x + 5f);
        PaintResizable(widget, widths);

        Named(widget, "table-header:size").Width.ShouldBe(85f, 0.01f);
        Named(widget, "table-header:kind").Width.ShouldBe(55f, 0.01f);
    }

    [Fact]
    public void TheDragIsConvertedToDesignUnits_ByTheScale()
    {
        var widget = Widget(800, 300);
        var widths = new TableColumnWidths();
        var scale = new DesignScale(2f);
        PaintResizable(widget, widths, scale: scale);
        var capture = Grab(widget, Divider0, out var x);

        DragTo(capture, x + 40f);   // 40 surface units are 20 design units at 2x

        widths.SizingOf(0, Columns[0]).Value.ShouldBe(100f, 0.01f);
        widths.SizingOf(1, Columns[1]).Value.ShouldBe(40f, 0.01f);
    }

    [Fact]
    public void DraggingBesideAStarColumn_KeepsTheStarAStar_AndResizesOnlyTheFixedNeighbour()
    {
        var widget = Widget();
        var widths = new TableColumnWidths();
        PaintResizable(widget, widths);
        var sizeBefore = Named(widget, "cell0").Width;
        var pathBefore = Named(widget, "cell2").Width;
        var capture = Grab(widget, Divider1, out var x);

        DragTo(capture, x + 15f);
        PaintResizable(widget, widths);

        widths.SizingOf(2, Columns[2]).IsStar.ShouldBeTrue("a star is never overridden");
        Named(widget, "cell0").Width.ShouldBe(sizeBefore, 0.01f);
        Named(widget, "cell1").Width.ShouldBe(75f, 0.01f, "the fixed side follows the pointer");
        Named(widget, "cell2").Width.ShouldBe(pathBefore - 15f, 0.01f, "the star takes the difference");
        ShouldLineUp(widget, Columns);

        // And dragging the other way narrows the fixed column, the star taking the room.
        DragTo(capture, x - 10f);
        PaintResizable(widget, widths);
        Named(widget, "cell1").Width.ShouldBe(50f, 0.01f);
        Named(widget, "cell2").Width.ShouldBe(pathBefore + 10f, 0.01f);
    }

    [Fact]
    public void ResolveBesideAStarOnTheLeft_ResizesTheFixedRightColumnInverselyToTheDrag()
    {
        TableColumnWidths.Resolve(null, 60f, 24f, 24f, 10f).ShouldBe((null, 50f), "dragging right shrinks a fixed right neighbour");
        TableColumnWidths.Resolve(null, 60f, 24f, 24f, -10f).ShouldBe((null, 70f));
        TableColumnWidths.Resolve(60f, null, 24f, 24f, 10f).ShouldBe((70f, null), "dragging right grows a fixed left column");
        TableColumnWidths.Resolve(null, null, 24f, 24f, 10f).ShouldBe((null, null), "two stars have nothing to resize");
        TableColumnWidths.Resolve(80f, 60f, 24f, 24f, 20f).ShouldBe((100f, 40f));
    }

    [Theory]
    [InlineData(1000f)]
    [InlineData(-1000f)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    public void NoColumnGoesBelowItsMinimum_HoweverFarTheDragGoes(float delta)
    {
        var (left, right) = TableColumnWidths.Resolve(80f, 60f, 30f, 20f, delta);

        left.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(30f);
        right.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(20f);
        (left!.Value + right!.Value).ShouldBe(140f, 0.01f, "what one gains the other loses");
        TableColumnWidths.Resolve(80f, null, 30f, 20f, delta).Left.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(30f);
        TableColumnWidths.Resolve(null, 60f, 30f, 20f, delta).Right.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(20f);
    }

    [Fact]
    public void NoColumnGoesBelowItsMinimum_WhenDraggedThroughTheBuiltTable()
    {
        var columns = new TableColumn[]
        {
            new("size", Sizing.Fixed(80f), TextAlign.Far),
            new("kind", Sizing.Fixed(60f)) { MinWidth = 40f },
            new("path", Sizing.Star()),
        };
        var widget = Widget();
        var widths = new TableColumnWidths();
        PaintResizable(widget, widths, columns);
        var capture = Grab(widget, Divider0, out var x);

        DragTo(capture, x + 1000f);
        PaintResizable(widget, widths, columns);
        Named(widget, "cell1").Width.ShouldBe(40f, 0.01f, "the column's own MinWidth");
        Named(widget, "cell0").Width.ShouldBe(100f, 0.01f);

        DragTo(capture, x - 1000f);
        PaintResizable(widget, widths, columns);
        Named(widget, "cell0").Width.ShouldBe(widths.DefaultMinWidth, 0.01f, "the default minimum");
        Named(widget, "cell1").Width.ShouldBe(116f, 0.01f);
        ShouldLineUp(widget, columns);
    }

    [Fact]
    public void TheDividerShowsTheResizeCursor_AndTheDragKeepsIt()
    {
        var widget = Widget();
        var widths = new TableColumnWidths();
        PaintResizable(widget, widths);
        Named(widget, Divider0).Cursor.ShouldBe(CursorKind.ResizeEW);
        Named(widget, Divider1).Cursor.ShouldBe(CursorKind.ResizeEW);

        var capture = Grab(widget, Divider0, out var x);
        DragTo(capture, x + 20f);
        PaintResizable(widget, widths);

        var moved = Named(widget, Divider0);
        moved.Cursor.ShouldBe(CursorKind.ResizeEW, "the divider that followed the drag still asks for it");
        moved.X.ShouldBeGreaterThan(x - 4f);
    }

    [Fact]
    public void ADividerLiesInTheGapBetweenTwoTitles()
    {
        var widget = Widget();
        PaintResizable(widget, new TableColumnWidths());

        var divider = Named(widget, Divider0);
        var left = Named(widget, "table-header:size");
        var right = Named(widget, "table-header:kind");
        divider.X.ShouldBe(left.X + left.Width, 0.01f);
        (divider.X + divider.Width).ShouldBe(right.X, 0.01f);
        Regions(widget).Count(r => r.Result is HitResult.ButtonHit b && b.Action.Contains("divider")).ShouldBe(Columns.Length - 1);
    }

    [Fact]
    public void WidthsSavedToARecordAndRestoredIntoANewObject_LayOutTheSame()
    {
        var widget = Widget();
        var widths = new TableColumnWidths();
        PaintResizable(widget, widths);
        DragTo(Grab(widget, Divider0, out var x), x + 17f);
        DragTo(Grab(widget, Divider1, out var y), y + 9f);
        PaintResizable(widget, widths);
        var expected = Regions(widget).Select(r => (r.X, r.Width)).ToArray();

        var saved = widths.Save();
        var restored = TableColumnWidths.From(saved);
        var other = Widget();
        PaintResizable(other, restored);

        Regions(other).Select(r => (r.X, r.Width)).ToArray().ShouldBe(expected);
        restored.Save().Widths.ShouldBe(saved.Widths);
    }

    [Fact]
    public void RestoringWidthsIntoATableWithDifferentColumns_NeitherThrowsNorMovesAColumnItDoesNotKnow()
    {
        var widths = new TableColumnWidths();
        widths.Restore(new TableColumnWidths.Saved([100f, 40f, 500f, 70f, 70f]));
        TableColumn[] fewer = [Columns[0], Columns[2]];
        TableColumn[] more = [.. Columns, new("extra", Sizing.Fixed(33f))];

        widths.SizingOf(0, fewer[0]).Value.ShouldBe(100f);
        widths.SizingOf(1, fewer[1]).IsStar.ShouldBeTrue("column 1 is a star now, and a star is never overridden");
        widths.SizingOf(3, more[3]).Value.ShouldBe(70f);

        var shorter = TableColumnWidths.From(new TableColumnWidths.Saved([90f]));
        shorter.SizingOf(0, more[0]).Value.ShouldBe(90f);
        shorter.SizingOf(1, more[1]).Value.ShouldBe(60f, "no entry: the declared width");
        shorter.SizingOf(3, more[3]).Value.ShouldBe(33f);

        var widget = Widget();
        PaintResizable(widget, widths, fewer);
        PaintResizable(widget, widths, more);
    }

    [Fact]
    public void AnUntouchedWidthsObject_LaysATableOutAsOneBuiltWithoutIt()
    {
        var plain = Widget();
        var cells = Columns.Select((c, i) => Layout.Builder.Spacer().Clickable(new HitResult.ButtonHit("cell" + i), _ => { })).ToArray();
        plain.Render(Layout.Builder.VStack(
            Layout.Builder.TableHeader(Columns, null, _ => { }, Style).RowH(24f),
            Layout.Builder.TableRow(Columns, cells, Style).RowH(20f)).Stretch(), new RectF32(0, 0, 400, 100));
        var resizable = Widget();
        PaintResizable(resizable, new TableColumnWidths());

        foreach (var name in new[] { "table-header:size", "table-header:kind", "table-header:path", "cell0", "cell1", "cell2" })
        {
            var a = Named(plain, name);
            var b = Named(resizable, name);
            (b.X, b.Y, b.Width, b.Height).ShouldBe((a.X, a.Y, a.Width, a.Height), name);
        }

        Regions(plain).Any(r => r.Result is HitResult.ButtonHit { Action: var a } && a.Contains("divider")).ShouldBeFalse();
    }

    [Fact]
    public void TheOldTableHeaderAndTableRowSignaturesStillExist()
    {
        var methods = typeof(Layout.Builder).GetMethods();

        methods.ShouldContain(m => m.Name == "TableHeader" && m.GetParameters().Length == 6
            && m.GetParameters()[4].ParameterType == typeof(float) && m.GetParameters()[5].ParameterType == typeof(string));
        methods.ShouldContain(m => m.Name == "TableRow" && m.GetParameters().Length == 3);
        typeof(TableColumn).GetConstructors().Single().GetParameters().Length.ShouldBe(3, "no positional parameter was added to TableColumn");
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
