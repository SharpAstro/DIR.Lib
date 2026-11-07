using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Builder.GroupedTreeTable{TGroup,TRow}"/> and the two helpers a host calls around it
/// (<see cref="GroupedTreeState.SyncCursor{TGroup,TRow}"/>, <see cref="GroupedTreeState.HandleTableKey{TGroup,TRow}"/>).
/// Each is asserted through the regions a real paint registers, as the data-view tests are, since draw==hit is the
/// property the builder exists to keep. The tree is org / repo / row, rebuilt from fresh objects where a test
/// means a refresh.
/// </summary>
public class LayoutGroupedTreeTableTests
{
    private const string ListId = "tree";
    private const float RowH = 20f;
    private const float PadX = 6f;
    private const float Width = 400f;

    private static readonly RGBAColor32 Selected = new(0x30, 0x50, 0x90, 0xff);
    private static readonly RGBAColor32 Focus = new(0x40, 0x68, 0xb0, 0xff);
    private static readonly RGBAColor32 Band = new(0x10, 0x14, 0x18, 0xff);

    private static readonly TableStyle TableLook = new(
        new RGBAColor32(0x20, 0x20, 0x20, 0xff), new RGBAColor32(0xff, 0xff, 0xff, 0xff),
        new RGBAColor32(0x30, 0x30, 0x30, 0xff), new RGBAColor32(0xff, 0xcc, 0x00, 0xff));

    private static readonly GroupedTreeTableStyle DefaultStyle = new(TableLook, new TreeRowStyle(new RGBAColor32(0xff, 0xff, 0xff, 0xff)))
    {
        SelectedFill = Selected,
        FocusFill = Focus,
    };

    private static readonly TableColumn[] Columns =
    [
        new("size", Sizing.Fixed(80f), TextAlign.Far) { DescendingFirst = true },
        new("kind", Sizing.Fixed(60f)) { Sortable = false },
        new("name", Sizing.Star()),
    ];

    private static GroupedTreeNode<string, string> G(string key, params GroupedTreeNode<string, string>[] kids)
        => GroupedTreeNode<string, string>.Group(key, "title " + key, kids);

    private static GroupedTreeNode<string, string> L(string key) => GroupedTreeNode<string, string>.Leaf(key, "name " + key);

    /// <summary>
    /// <code>
    /// 0 org:a               6 org:b
    /// 1  repo:a/web         7  row:b#1
    /// 2   row:a/web#1
    /// 3   row:a/web#2
    /// 4  repo:a/api
    /// 5   row:a/api#1
    /// </code>
    /// </summary>
    private static GroupedTreeNode<string, string>[] SmallTree() =>
    [
        G("org:a",
            G("repo:a/web", L("row:a/web#1"), L("row:a/web#2")),
            G("repo:a/api", L("row:a/api#1"))),
        G("org:b", L("row:b#1")),
    ];

    /// <summary>Fifty groups of ninety-nine rows: 5000 visible rows, group <c>g</c> at index 100g.</summary>
    private static GroupedTreeNode<string, string>[] BigTree() =>
    [
        .. Enumerable.Range(0, 50).Select(g => G($"g{g}", [.. Enumerable.Range(0, 99).Select(i => L($"g{g}/r{i}"))])),
    ];

    private sealed class Fixture
    {
        private InputRouter? _router;

        public Fixture(GroupedTreeNode<string, string>[]? tree = null)
        {
            Renderer = new DeclarationStubRenderer(400, 300);
            Widget = new DeclarationWidget(Renderer);
            Tree = tree ?? SmallTree();
            OnActivate = row => Activated.Add(row.Key);
        }

        public DeclarationStubRenderer Renderer { get; }
        public DeclarationWidget Widget { get; }
        public GroupedTreeState State { get; } = new();
        public ListScrollController Scroll { get; } = new() { Mode = ScrollBarMode.None };
        public GroupedTreeNode<string, string>[] Tree { get; set; }
        public List<GroupedTreeRow<string, string>> Rows { get; private set; } = [];
        public GroupedTreeTableStyle Style { get; set; } = DefaultStyle;
        public TableColumnWidths? Widths { get; set; }
        public TableSort? Sort { get; set; }
        public TableSort? SortAsked { get; private set; }
        public Action<GroupedTreeRow<string, string>>? OnActivate { get; set; }
        public float Height { get; set; } = 200f;

        /// <summary>Cells and group content that declare hits of their own, so a test can ask where they landed.</summary>
        public bool Hits { get; set; }

        /// <summary>Whether a frame re-finds the selection and puts the cursor on it, as a host should.</summary>
        public bool AutoSync { get; set; } = true;

        /// <summary>Whether the first frame puts the cursor in the table, as a host does when the table takes the keyboard.</summary>
        public bool OpenCursor { get; set; } = true;

        public List<string> Activated { get; } = [];
        public List<string> Built { get; } = [];

        public void Paint()
        {
            Rows = State.Flatten<string, string>(Tree);
            if (OpenCursor && !Widget.ListCursor.IsOpen) Widget.ListCursor.Open(ListId);
            if (AutoSync) State.SyncCursor(Rows, Widget.ListCursor, ListId);

            var node = Layout.Builder.GroupedTreeTable<string, string>(Columns, Style, Rows, State, Scroll, ListId, RowH,
                groupHeader: row =>
                {
                    Built.Add(row.Key);
                    return Hits
                        ? Layout.Builder.Spacer().Clickable(new HitResult.ButtonHit("gh:" + row.Key))
                        : Layout.Builder.Text(row.GroupPayload!, 14f);
                },
                rowCells: row =>
                {
                    Built.Add(row.Key);
                    return Columns.Select((_, i) => Hits
                        ? Layout.Builder.Spacer().Clickable(new HitResult.ButtonHit($"cell{i}:{row.Key}"))
                        : Layout.Builder.Spacer()).ToList();
                },
                sort: Sort,
                onSort: s => SortAsked = s,
                widths: Widths,
                scale: DesignScale.One,
                onActivate: OnActivate);

            Widget.Render(node.Stretch(), new RectF32(0, 0, Width, Height));
        }

        /// <summary>The host's per-key line: the list's answer first, then the state's.</summary>
        public bool Key(InputKey key)
            => State.HandleTableKey(key, Widget.HandleListKey(key), Widget.ListCursor, ListId, Rows, OnActivate);

        public List<ClickableRegion> Regions() => Widget.GetRegisteredRegions().ToList();

        public ClickableRegion Named(string name)
            => Regions().Single(r => r.Result is HitResult.ButtonHit b && b.Action == name);

        public ClickableRegion Item(int index)
            => Regions().Single(r => r.Result is HitResult.ListItemHit h && h.ListId == ListId && h.Index == index);

        public int[] PaintedIndices()
            => [.. Regions().Where(r => r.Result is HitResult.ListItemHit h && h.ListId == ListId)
                .Select(r => ((HitResult.ListItemHit)r.Result).Index).OrderBy(i => i)];

        public int IndexOf(string key) => Rows.FindIndex(r => r.Key == key);

        public RGBAColor32 PixelAtRow(int index)
        {
            var region = Item(index);
            var img = Renderer.Surface;
            var x = 300;
            var y = (int)(region.Y + region.Height / 2f);
            var i = (y * img.Width + x) * 4;
            return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
        }

        public bool Press(float x, float y, int clicks)
        {
            _router ??= new InputRouter(Widget.Ui, new BackgroundTaskTracker(), () => { }) { Widgets = () => [Widget] };
            return _router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, clicks));
        }

        public bool PressRow(int index, int clicks)
        {
            var region = Item(index);
            return Press(region.X + 30f, region.Y + region.Height / 2f, clicks);
        }
    }

    private static void ShouldLineUp(Fixture f, string leaf)
    {
        for (var i = 0; i < Columns.Length; i++)
        {
            var head = f.Named("table-header:" + Columns[i].Header);
            var cell = f.Named($"cell{i}:{leaf}");
            head.X.ShouldBe(cell.X, 0.01f, Columns[i].Header);
            head.Width.ShouldBe(cell.Width, 0.01f, Columns[i].Header);
        }
    }

    // ---- the columns ---------------------------------------------------------------------------

    [Fact]
    public void HeaderAndLeafCellsLineUpInOneColumnSet_WhateverTheDepth()
    {
        var f = new Fixture { Hits = true };

        f.Paint();

        ShouldLineUp(f, "row:a/web#1");   // two levels down
        ShouldLineUp(f, "row:b#1");       // one level down
        f.Named("cell0:row:b#1").X.ShouldBe(f.Named("cell0:row:a/web#1").X, 0.01f, "a leaf does not indent its columns");
    }

    [Fact]
    public void HeaderAndLeafCellsStayLinedUp_WhenAColumnIsDragged()
    {
        var f = new Fixture { Hits = true, Widths = new TableColumnWidths() };
        f.Paint();
        var divider = f.Named("table-header:divider:0");
        var pressX = divider.X + divider.Width / 2f;
        var capture = divider.OnPress.ShouldNotBeNull()(new PointerPress(pressX, divider.Y + 2f, MouseButton.Left, InputModifier.None, 1))
            .ShouldNotBeNull();

        capture.Move(new PointerMove(pressX + 20f, 5f, MouseButton.Left, InputModifier.None));
        f.Paint();

        f.Named("table-header:size").Width.ShouldBe(100f, 0.01f);
        f.Named("cell0:row:a/web#1").Width.ShouldBe(100f, 0.01f, "the leaf reads the same dragged width as the title");
        f.Named("table-header:kind").Width.ShouldBe(40f, 0.01f);
        f.Named("cell1:row:a/web#1").Width.ShouldBe(40f, 0.01f);
        ShouldLineUp(f, "row:a/web#1");
        ShouldLineUp(f, "row:b#1");
    }

    [Fact]
    public void GroupHeadersSpanTheWholeRow_AndAreNotInTheColumns()
    {
        var f = new Fixture { Hits = true };

        f.Paint();

        f.Item(0).X.ShouldBe(0f, 0.01f);
        f.Item(0).Width.ShouldBe(Width, 0.01f, "the row is the list's whole width");

        // Right edge of the last column is where a leaf's last cell ends: a header's content reaches it too.
        var cellRight = f.Named("cell2:row:a/web#1").X + f.Named("cell2:row:a/web#1").Width;
        cellRight.ShouldBe(Width - PadX, 0.01f);

        var top = f.Named("gh:org:a");
        top.X.ShouldBe(PadX + 16f, 0.01f, "the padding, then the caret column");
        (top.X + top.Width).ShouldBe(cellRight, 0.01f, "content spans on to the last column's edge");
        top.Width.ShouldBeGreaterThan(Columns[0].Width.Value + Columns[1].Width.Value, "it spans several columns, not one");

        var nested = f.Named("gh:repo:a/web");
        nested.X.ShouldBe(PadX + 16f + 16f, 0.01f, "one level of indent");
        (nested.X + nested.Width).ShouldBe(cellRight, 0.01f);
    }

    [Fact]
    public void TheHeaderIsTheTableHeader_AndReportsASortPress()
    {
        var f = new Fixture();
        f.Paint();

        f.Named("table-header:size").OnClick.ShouldNotBeNull()(InputModifier.None);
        f.SortAsked.ShouldBe(new TableSort(0, Descending: true), "a size column starts biggest-first, as TableHeader says");

        f.Named("table-header:kind").OnClick.ShouldBeNull("an unsortable title swallows the press");
        f.Named("table-header:name").Y.ShouldBe(0f, 0.01f);
        f.Named("table-header:name").Height.ShouldBe(RowH, 0.01f, "the header takes the row height unless told otherwise");
    }

    // ---- the mouse -----------------------------------------------------------------------------

    [Fact]
    public void AClickOnAGroupHeaderTogglesItsFold_AndSelectsIt()
    {
        var f = new Fixture();
        f.Paint();
        f.Rows.Count.ShouldBe(8);

        f.Item(1).OnClick.ShouldNotBeNull()(InputModifier.None);   // repo:a/web

        f.State.IsExpanded("repo:a/web").ShouldBeFalse();
        f.State.SelectedKey.ShouldBe("repo:a/web");
        f.Paint();
        f.Rows.Select(r => r.Key).ShouldBe(["org:a", "repo:a/web", "repo:a/api", "row:a/api#1", "org:b", "row:b#1"]);

        f.Item(1).OnClick.ShouldNotBeNull()(InputModifier.None);
        f.State.IsExpanded("repo:a/web").ShouldBeTrue("a second press opens it again");
        f.Paint();
        f.Rows.Count.ShouldBe(8);
    }

    [Fact]
    public void AClickOnTheCaretTogglesToo_FromInsideTheRow()
    {
        var f = new Fixture();
        f.Paint();
        var row = f.Item(0);

        var caret = f.Regions().Single(r => r.Result is HitResult.ChromeHit && r.OnClick is not null && Math.Abs(r.Y - row.Y) < 0.01f);
        caret.OnClick!(InputModifier.None);

        f.State.IsExpanded("org:a").ShouldBeFalse();
        f.State.SelectedKey.ShouldBe("org:a");
    }

    [Fact]
    public void AGroupWithNothingInItHasNoCaret_AndStillTakesThePress()
    {
        var f = new Fixture([G("empty"), G("full", L("row"))]);
        f.Paint();

        f.Regions().Count(r => r.Result is HitResult.ChromeHit && r.OnClick is not null).ShouldBe(1, "only the group with children has a caret");
        f.Item(0).OnClick.ShouldNotBeNull()(InputModifier.None);
        f.State.SelectedKey.ShouldBe("empty");
    }

    [Fact]
    public void AClickOnALeafSelectsIt_AndDoesNotFoldOrActivate()
    {
        var f = new Fixture();
        f.Paint();

        f.Item(3).OnClick.ShouldNotBeNull()(InputModifier.None);   // row:a/web#2

        f.State.SelectedKey.ShouldBe("row:a/web#2");
        f.Activated.ShouldBeEmpty();
        f.State.IsExpanded("repo:a/web").ShouldBeTrue();
        f.State.IsExpanded("org:a").ShouldBeTrue();
    }

    [Fact]
    public void ADoubleClickOnALeafActivatesIt_InsteadOfSelectingAgain()
    {
        var f = new Fixture();
        f.Paint();

        f.PressRow(2, clicks: 1).ShouldBeTrue();
        f.State.SelectedKey.ShouldBe("row:a/web#1");
        f.Activated.ShouldBeEmpty("one click selects");

        f.PressRow(2, clicks: 2).ShouldBeTrue();
        f.Activated.ShouldBe(["row:a/web#1"]);
    }

    [Fact]
    public void ASinglePressOnAHeaderThroughTheRouter_FoldsItOnce()
    {
        var f = new Fixture();
        f.Paint();

        f.PressRow(6, clicks: 1).ShouldBeTrue();   // org:b

        f.State.IsExpanded("org:b").ShouldBeFalse();
        f.State.SelectedKey.ShouldBe("org:b");
        f.Activated.ShouldBeEmpty();
    }

    [Fact]
    public void WithoutAnActivateCallback_ALeafOnlySelects_AndTheSecondPressIsAnotherClick()
    {
        var f = new Fixture { OnActivate = null };
        f.Paint();

        f.PressRow(2, clicks: 1);
        f.PressRow(2, clicks: 2);

        f.State.SelectedKey.ShouldBe("row:a/web#1");
        f.Activated.ShouldBeEmpty();
        f.Widget.ListCursor.Open(ListId, 2, f.Rows.Count);
        f.Widget.ActivateListCursor().ShouldBeTrue("Enter falls back to the click, which selects");
    }

    // ---- the list ------------------------------------------------------------------------------

    [Fact]
    public void EveryVisibleRowIsAListItemHit_GroupHeadersAndLeavesAlike()
    {
        var f = new Fixture();
        f.Paint();

        f.PaintedIndices().ShouldBe([0, 1, 2, 3, 4, 5, 6, 7], "one hit per row, indices dense, none skipped for being a header");
        f.Rows.Select(r => r.Kind).ShouldContain(GroupedTreeRowKind.Group);
        f.Rows.Select(r => r.Kind).ShouldContain(GroupedTreeRowKind.Row);
    }

    [Fact]
    public void TheCursorOpenedWithTheCount_WalksEveryRowOfAFiveThousandRowTable()
    {
        var f = new Fixture(BigTree()) { AutoSync = false };
        f.Paint();
        f.Paint();   // the first frame binds the viewport; the second builds for it
        f.Rows.Count.ShouldBe(5000);
        f.Widget.ListCursor.Open(ListId, 0, f.Rows.Count);
        f.Widget.ListCursor.Moved += i => f.Scroll.EnsureVisible(i);
        f.Paint();

        for (var expected = 1; expected < 60; expected++)
        {
            f.Widget.MoveListCursor(1).ShouldBeTrue($"row {expected} is reachable");
            f.Widget.ListCursor.Index.ShouldBe(expected);
            f.Paint();
            f.PaintedIndices().ShouldContain(expected, $"row {expected} is on screen once the cursor lands on it");
        }

        f.Scroll.FirstVisibleAtom.ShouldBeGreaterThan(40, "the window followed the cursor past its first screen");

        f.Scroll.AtomOffset = 4999;   // clamped to the last screen
        f.Paint();
        f.Widget.ListCursor.Open(ListId, 4999, f.Rows.Count);
        f.Widget.MoveListCursor(1).ShouldBeFalse("the end is an end");
        f.Widget.MoveListCursor(-1).ShouldBeTrue();
        f.Widget.ListCursor.Index.ShouldBe(4998);
    }

    [Fact]
    public void OnlyTheWindowIsBuilt_ForFiveThousandRows_AndTheScrollbarStillMeasuresAllOfThem()
    {
        var f = new Fixture(BigTree());
        f.Paint();
        f.Built.Clear();

        f.Paint();

        f.Rows.Count.ShouldBe(5000);
        f.Built.Count.ShouldBeLessThan(30, "nine rows show; only those and the overscan are built");
        f.Scroll.TotalAtoms.ShouldBe(5000, "spacers stand in for every row not built");
        f.PaintedIndices().Length.ShouldBe(9, "the viewport under the header shows nine rows of twenty");

        f.Scroll.AtomOffset = 2500;
        f.Built.Clear();
        f.Paint();

        f.Built.ShouldContain(f.Rows[2500].Key);
        f.Built.Count.ShouldBeLessThan(30);
        f.PaintedIndices()[0].ShouldBe(2500);
    }

    [Fact]
    public void TheHeaderStaysPut_WhenTheListScrolls()
    {
        var f = new Fixture(BigTree());
        f.Paint();
        f.Paint();

        f.Scroll.AtomOffset = 500;
        f.Paint();

        f.Named("table-header:size").Y.ShouldBe(0f, 0.01f);
        f.PaintedIndices()[0].ShouldBe(500);
    }

    // ---- the rows' look -------------------------------------------------------------------------

    [Fact]
    public void TheCursorsRowPaintsTheFocusFill_AndTheSelectedRowTheSelectedFill()
    {
        var f = new Fixture();
        f.State.SelectedKey = "row:a/web#1";
        f.Paint();   // the sync puts the cursor on the selection

        f.PixelAtRow(2).ShouldBe(Focus, "the cursor's row, which is the selected one");
        f.PixelAtRow(3).ShouldNotBe(Focus);
        f.PixelAtRow(3).ShouldNotBe(Selected);

        f.Widget.ListCursor.Close();
        f.OpenCursor = false;   // the host's side: it only opens the cursor when the table takes the keyboard
        f.Paint();

        f.Widget.ListCursor.IsOpen.ShouldBeFalse("a frame does not reopen a cursor the host closed");
        f.PixelAtRow(2).ShouldBe(Selected, "with the cursor away, the selection still shows");
        f.PixelAtRow(1).ShouldNotBe(Selected);
    }

    [Fact]
    public void ASelectedGroupHeaderIsFilledToo_AndAGroupFillBandsTheOthers()
    {
        var f = new Fixture { Style = DefaultStyle with { GroupFill = Band } };
        f.State.SelectedKey = "org:b";
        f.OpenCursor = false;
        f.Paint();

        f.PixelAtRow(6).ShouldBe(Selected, "a selected header wears the selection, not the band");
        f.PixelAtRow(0).ShouldBe(Band);
        f.PixelAtRow(1).ShouldBe(Band, "at every depth");
        f.PixelAtRow(2).ShouldNotBe(Band, "a leaf is not banded");
    }

    // ---- the keyboard ---------------------------------------------------------------------------

    [Fact]
    public void Right_UnfoldsAFoldedGroup_ThenStepsOntoItsFirstChild()
    {
        var f = new Fixture();
        f.State.Collapse("repo:a/web");
        f.State.SelectedKey = "repo:a/web";
        f.Paint();
        f.Widget.ListCursor.Index.ShouldBe(1);

        f.Key(InputKey.Right).ShouldBeTrue();
        f.State.IsExpanded("repo:a/web").ShouldBeTrue("the first Right unfolds");
        f.Paint();
        f.Widget.ListCursor.Index.ShouldBe(1, "and stays on the group");

        f.Key(InputKey.Right).ShouldBeTrue();
        f.Widget.ListCursor.Index.ShouldBe(2, "the second steps onto the first child");
        f.State.SelectedKey.ShouldBe("row:a/web#1", "and the selection goes with the cursor");
        f.Paint();
        f.Widget.ListCursor.Index.ShouldBe(2);
    }

    [Fact]
    public void Left_FoldsAnOpenGroup_OtherwiseStepsToTheParent()
    {
        var f = new Fixture();
        f.State.SelectedKey = "row:a/web#2";
        f.Paint();

        f.Key(InputKey.Left).ShouldBeTrue();
        f.State.SelectedKey.ShouldBe("repo:a/web", "Left on a leaf steps to its group");
        f.Widget.ListCursor.Index.ShouldBe(1);

        f.Key(InputKey.Left).ShouldBeTrue();
        f.State.IsExpanded("repo:a/web").ShouldBeFalse("Left on an open group folds it");
        f.Paint();
        f.Widget.ListCursor.Index.ShouldBe(1);

        f.Key(InputKey.Left).ShouldBeTrue();
        f.State.SelectedKey.ShouldBe("org:a", "Left on a folded group steps to its parent");

        f.Key(InputKey.Left).ShouldBeTrue();
        f.State.IsExpanded("org:a").ShouldBeFalse();
        f.Paint();
        f.Key(InputKey.Left).ShouldBeFalse("a folded top-level group has nowhere to go");
    }

    [Fact]
    public void Enter_ActivatesALeafOnce_AndTogglesAGroupOnce()
    {
        var f = new Fixture();
        f.State.SelectedKey = "row:a/web#1";
        f.Paint();

        f.Key(InputKey.Enter).ShouldBeTrue();
        f.Activated.ShouldBe(["row:a/web#1"], "the list ran the row's own activation and the state did not run it again");

        f.State.SelectedKey = "repo:a/api";
        f.Paint();
        f.Key(InputKey.Enter).ShouldBeTrue();
        f.State.IsExpanded("repo:a/api").ShouldBeFalse("toggled once, not back again");
        f.Paint();
        f.Key(InputKey.Enter).ShouldBeTrue();
        f.State.IsExpanded("repo:a/api").ShouldBeTrue();
        f.Activated.Count.ShouldBe(1, "a group is never activated");
    }

    [Fact]
    public void Enter_OnARowThatWasNotPainted_StillReachesTheStateAndActivatesIt()
    {
        var f = new Fixture(BigTree());
        f.Paint();
        f.Paint();
        f.State.SelectedKey = "g30/r5";   // row 3006, far below the window
        f.Paint();
        f.Widget.ListCursor.Index.ShouldBe(3006);

        f.Widget.HandleListKey(InputKey.Enter).ShouldBeFalse("nothing there was painted, so the list has nothing to run");
        f.Key(InputKey.Enter).ShouldBeTrue();

        f.Activated.ShouldBe(["g30/r5"]);
    }

    [Fact]
    public void DownAndUp_MoveTheCursor_AndTheSelectionFollowsIt()
    {
        var f = new Fixture();
        f.State.SelectedKey = "org:a";
        f.Paint();

        f.Key(InputKey.Down).ShouldBeTrue();
        f.Widget.ListCursor.Index.ShouldBe(1);
        f.State.SelectedKey.ShouldBe("repo:a/web", "the key follows the cursor, so a re-flatten keeps both on one row");

        f.Paint();
        f.Key(InputKey.Down).ShouldBeTrue();
        f.State.SelectedKey.ShouldBe("row:a/web#1");

        f.Paint();
        f.Key(InputKey.Up).ShouldBeTrue();
        f.State.SelectedKey.ShouldBe("repo:a/web");
    }

    [Fact]
    public void AKeyTheTableDoesNotOwn_IsNotClaimed()
    {
        var f = new Fixture();
        f.State.SelectedKey = "org:a";
        f.Paint();

        f.Key(InputKey.Tab).ShouldBeFalse();
        f.Key(InputKey.Escape).ShouldBeFalse();
    }

    [Fact]
    public void ACursorInAnotherList_IsNotTakenOrTouched()
    {
        var f = new Fixture();
        f.State.SelectedKey = "org:a";
        f.Widget.ListCursor.Open("other", 2, 5);

        f.Paint();

        f.Widget.ListCursor.ListId.ShouldBe("other", "a frame does not take the cursor from another list");
        f.Widget.ListCursor.Index.ShouldBe(2);

        f.Key(InputKey.Left).ShouldBeFalse("the key was not for this table");
        f.State.IsExpanded("org:a").ShouldBeTrue();
        f.State.HandleTableKey(InputKey.Down, listHandled: true, f.Widget.ListCursor, ListId, f.Rows).ShouldBeTrue("the other list's answer stands");
        f.State.SelectedKey.ShouldBe("org:a", "and the selection is not moved by it");
    }

    // ---- the cursor and the selection across a refresh ---------------------------------------------

    [Fact]
    public void AfterARefreshThatReordersTheRows_TheCursorIsOnTheSelectedRowAtItsNewIndex()
    {
        var f = new Fixture();
        f.State.SelectedKey = "row:a/api#1";
        f.Paint();
        f.Widget.ListCursor.Index.ShouldBe(5);

        f.Tree = [.. Enumerable.Reverse(SmallTree())];   // fresh objects, org:b first
        f.Paint();

        var now = f.IndexOf("row:a/api#1");
        now.ShouldNotBe(5);
        f.Widget.ListCursor.Index.ShouldBe(now);
        f.Widget.ListCursor.RowCount.ShouldBe(f.Rows.Count);
        f.PixelAtRow(now).ShouldBe(Focus, "and that is the row drawn lit");
    }

    [Fact]
    public void FoldingTheGroupThatHoldsTheSelection_MovesBothTheKeyAndTheCursorOntoTheGroup()
    {
        var f = new Fixture();
        f.State.SelectedKey = "row:a/web#2";
        f.Paint();
        f.Widget.ListCursor.Index.ShouldBe(3);

        f.State.Collapse("repo:a/web");
        f.Paint();

        f.State.SelectedKey.ShouldBe("repo:a/web");
        f.Widget.ListCursor.Index.ShouldBe(1);
    }

    [Fact]
    public void SyncCursor_ReturnsTheSelectedIndex_AndOpensTheCursorWithTheRowCount()
    {
        var state = new GroupedTreeState();
        var cursor = new ListCursor();
        var rows = state.Flatten<string, string>(SmallTree());
        cursor.Open(ListId);

        state.SyncCursor(rows, cursor, ListId).ShouldBe(-1, "nothing selected, nothing lit");
        cursor.ListId.ShouldBe(ListId);
        cursor.Index.ShouldBe(-1);
        cursor.RowCount.ShouldBe(8);

        state.SelectedKey = "org:b";
        state.SyncCursor(rows, cursor, ListId).ShouldBe(6);
        cursor.Index.ShouldBe(6);

        state.SelectedKey = "key that was never a row";
        state.SyncCursor(rows, cursor, ListId).ShouldBe(-1);
        state.SelectedKey.ShouldBe("key that was never a row", "a selection restored before its data arrived is kept");
    }

    [Fact]
    public void SyncCursor_LeavesAClosedCursorClosed_AndStillSaysWhereTheSelectionIs()
    {
        var state = new GroupedTreeState { SelectedKey = "org:b" };
        var cursor = new ListCursor();

        state.SyncCursor(state.Flatten<string, string>(SmallTree()), cursor, ListId).ShouldBe(6);

        cursor.IsOpen.ShouldBeFalse("closing the cursor is the host's way to take the keyboard away");
    }

    [Fact]
    public void SyncCursor_RaisesNoMoved_SoARefreshNeverScrolls()
    {
        var state = new GroupedTreeState { SelectedKey = "org:b" };
        var cursor = new ListCursor();
        cursor.Open(ListId);
        var moved = new List<int>();
        cursor.Moved += moved.Add;

        state.SyncCursor(state.Flatten<string, string>(SmallTree()), cursor, ListId);

        moved.ShouldBeEmpty();
    }

    [Fact]
    public void TheTableTakesNoNullArguments()
    {
        var state = new GroupedTreeState();
        var rows = state.Flatten<string, string>(SmallTree());

        Should.Throw<ArgumentNullException>(() => state.SyncCursor(rows, null!, ListId));
        Should.Throw<ArgumentNullException>(() => state.HandleTableKey(InputKey.Left, false, null!, ListId, rows));
        Should.Throw<ArgumentNullException>(() => state.HandleTableKey<string, string>(InputKey.Left, false, new ListCursor(), ListId, null!));
        Should.Throw<ArgumentNullException>(() => Layout.Builder.GroupedTreeTable<string, string>(Columns, DefaultStyle, rows, null!,
            new ListScrollController(), ListId, RowH, _ => Layout.Builder.Spacer(), _ => []));
    }
}
