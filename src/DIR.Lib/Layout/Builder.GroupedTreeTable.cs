using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

/// <summary>
/// How a grouped tree table looks, stated once per table like <see cref="TableStyle"/> and
/// <see cref="TreeRowStyle"/>, which it holds: the header and the rows' shared gap and padding, and the caret
/// and indent of a group header. Every colour of its own is an init-only property, so the constructor a compiled
/// caller binds to never changes when one is added.
/// </summary>
/// <param name="Table">The header's colours and the gap and padding the header, the leaf rows and the group
/// headers all use, which is what keeps a title over its values.</param>
/// <param name="Tree">The caret's colour, the indent per level and the caret column's width.</param>
public readonly record struct GroupedTreeTableStyle(TableStyle Table, TreeRowStyle Tree)
{
    /// <summary>The fill of the selected row (<see cref="GroupedTreeState.SelectedKey"/>), group header or leaf.</summary>
    public RGBAColor32 SelectedFill { get; init; } = new(0x30, 0x50, 0x90, 0xff);

    /// <summary>The fill of the row the keyboard cursor is on (<see cref="Node.FocusBackground"/>). It replaces
    /// <see cref="SelectedFill"/> on the row that is both, which is the usual case: the cursor and the selection
    /// are one row until the reader clicks elsewhere.</summary>
    public RGBAColor32 FocusFill { get; init; } = new(0x40, 0x68, 0xb0, 0xff);

    /// <summary>A band behind every group header, or null for none, so a header stands out from the rows under it.</summary>
    public RGBAColor32? GroupFill { get; init; }

    /// <summary>The caret's size, design units.</summary>
    public float CaretSize { get; init; } = 12f;
}

public static partial class Builder
{
    /// <summary>
    /// A table whose rows are grouped, to any depth, under headers the reader can fold: one
    /// <see cref="TableHeader(ReadOnlySpan{TableColumn}, TableSort?, Action{TableSort}?, in TableStyle, float, string)"/>,
    /// then a <see cref="VirtualList"/> of <paramref name="rows"/>, each a group header that spans the row or a leaf
    /// row laid out in the header's columns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing here is a second copy.</b> The header is <c>TableHeader</c> (with the dividers when
    /// <paramref name="widths"/> is given), a leaf is <c>TableRow</c> over the SAME columns, style and widths, so a title
    /// sits over its values however a column is dragged; a group header is <c>TreeRow</c>, and the rows are a
    /// <c>VirtualList</c>, so only the window is built whatever <paramref name="rows"/>.Count is. The model is
    /// <see cref="GroupedTreeState"/>; this only draws it and wires the pointer to it.
    /// </para>
    /// <para>
    /// <b>A group header is not in the columns.</b> It is one node across the whole row, indented by its depth,
    /// the caret, then <paramref name="groupHeader"/>'s content taking the rest: a name, counts, actions, spaced as the
    /// consumer likes. A leaf is where the columns are, so it does not indent itself; a consumer that wants its first
    /// cell indented under its group makes it with <see cref="TreeRow"/> from <see cref="GroupedTreeRow{TGroup,TRow}.Depth"/>.
    /// </para>
    /// <para>
    /// <b>Every visible row is row <c>i</c> of <paramref name="listId"/></b> (<see cref="HitResult.ListItemHit"/>), group
    /// headers and leaves alike, so the cursor, the click and the lit row are one list. A press on a header, or on
    /// its caret, selects it and toggles its fold; a press on a leaf selects it; a double-click on a leaf (and Enter
    /// on one) is <paramref name="onActivate"/>. The selected row is filled with
    /// <see cref="GroupedTreeTableStyle.SelectedFill"/>, and the row the keyboard cursor is on with
    /// <see cref="GroupedTreeTableStyle.FocusFill"/>. <b><see cref="GroupedTreeState.SelectedKey"/> can therefore name
    /// a group</b>: a consumer showing the selected leaf looks the key up among its rows and checks the kind.
    /// </para>
    /// <para>
    /// <b>The call order, every frame and every key.</b>
    /// </para>
    /// <list type="number">
    /// <item><description>Once, <c>cursor.Moved += i => scroll.EnsureVisible(i)</c>, so a cursor that steps past the window
    /// brings the row it lands on into view (<see cref="VirtualList"/> explains why the count matters). And whenever the
    /// table takes the keyboard, <c>cursor.Open(listId)</c>; <c>cursor.Close()</c> when it gives it up.</description></item>
    /// <item><description>Per frame, <c>rows = state.Flatten(tree)</c>, then
    /// <see cref="GroupedTreeState.SyncCursor{TGroup,TRow}"/> (which re-finds the selected key and puts an open cursor on
    /// it, with the row count), then this builder.</description></item>
    /// <item><description>Per key, <c>state.HandleTableKey(key, widget.HandleListKey(key, mods), widget.ListCursor, listId, rows,
    /// onActivate)</c>: the list claims Up, Down and Enter on a painted row, and the state takes selection along with
    /// the cursor and Left, Right and, where the list did not claim it, Enter. Flatten again after it returns true; a
    /// fold change also raises <see cref="GroupedTreeState.Changed"/>.</description></item>
    /// </list>
    /// <para>
    /// <b>The sort is the consumer's.</b> <paramref name="sort"/> only draws the caret on the header and
    /// <paramref name="onSort"/> reports the press; ordering a group's children is done before the tree is flattened.
    /// </para>
    /// </remarks>
    /// <param name="columns">The columns the header and every leaf row share.</param>
    /// <param name="style">The table and tree styles, and the row fills.</param>
    /// <param name="rows">What <see cref="GroupedTreeState.Flatten{TGroup,TRow}"/> returned this frame.</param>
    /// <param name="state">Folds and selection, written by a press.</param>
    /// <param name="scroll">The list's scroll controller; its atom becomes <paramref name="rowHeight"/>.</param>
    /// <param name="listId">Names the list for the cursor and for each row's hit.</param>
    /// <param name="rowHeight">Every row's height, group headers included, design units.</param>
    /// <param name="groupHeader">A group header's content, taking the row's width after the caret. Called only for the
    /// rows built, which is the window.</param>
    /// <param name="rowCells">A leaf's cells, one per column, in the columns' order; extras are dropped and missing ones
    /// left empty (as <see cref="TableRow(ReadOnlySpan{TableColumn}, ReadOnlySpan{Node}, in TableStyle)"/> does).</param>
    /// <param name="sort">The sort the header shows.</param>
    /// <param name="onSort">Receives the next sort when a sortable title is pressed; null makes the header a display.</param>
    /// <param name="widths">Dragged column widths; given, the header gets its dividers and the rows read the same widths.</param>
    /// <param name="scale">The design-to-surface mapping the tree is arranged at, which the dividers need to turn a drag
    /// into design units. Unused without <paramref name="widths"/>.</param>
    /// <param name="onActivate">Opens a leaf: its double-click and its Enter. Null leaves both to select.</param>
    /// <param name="headerHeight">The header's height, or null for <paramref name="rowHeight"/>.</param>
    /// <param name="fontSize">The header titles' size.</param>
    /// <param name="hitPrefix">Prefix of the header's hit names, see <c>TableHeader</c>.</param>
    public static Node GroupedTreeTable<TGroup, TRow>(
        ReadOnlySpan<TableColumn> columns,
        in GroupedTreeTableStyle style,
        IReadOnlyList<GroupedTreeRow<TGroup, TRow>> rows,
        GroupedTreeState state,
        ListScrollController scroll,
        string listId,
        float rowHeight,
        Func<GroupedTreeRow<TGroup, TRow>, Node> groupHeader,
        Func<GroupedTreeRow<TGroup, TRow>, IReadOnlyList<Node>> rowCells,
        TableSort? sort = null,
        Action<TableSort>? onSort = null,
        TableColumnWidths? widths = null,
        DesignScale scale = default,
        Action<GroupedTreeRow<TGroup, TRow>>? onActivate = null,
        float? headerHeight = null,
        float fontSize = 14f,
        string hitPrefix = "table-header:")
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scroll);
        ArgumentNullException.ThrowIfNull(listId);
        ArgumentNullException.ThrowIfNull(groupHeader);
        ArgumentNullException.ThrowIfNull(rowCells);

        // Spans and `in` parameters cannot be captured by the row callback, which runs later, inside VirtualList.
        var table = style.Table;
        var look = style;
        var shared = columns.ToArray();

        var header = (widths is null
            ? TableHeader(columns, sort, onSort, table, fontSize, hitPrefix)
            : TableHeader(columns, sort, onSort, table, widths, scale, fontSize, hitPrefix))
            .RowH(headerHeight ?? rowHeight);

        var list = VirtualList(rows.Count, rowHeight, scroll, i =>
        {
            var row = rows[i];
            var hit = new HitResult.ListItemHit(listId, i);
            var key = row.Key;
            var selected = string.Equals(state.SelectedKey, key, StringComparison.Ordinal);
            Node node;

            if (row.Kind == GroupedTreeRowKind.Group)
            {
                // The caret and a press on the row do the same thing, so a click anywhere on a header folds it.
                // The caret registers inside the row and wins its own few units, which is why it needs its own
                // callback and not just the row's.
                void Press()
                {
                    state.SelectedKey = key;
                    state.Toggle(key);
                }

                node = TreeRow(row.Depth, row.HasChildren, row.IsExpanded, row.HasChildren ? Press : null,
                        groupHeader(row), look.Tree, look.CaretSize)
                    .PadX(look.Table.PadX)
                    .Clickable(hit, _ => Press());
                if (!selected && look.GroupFill is { } band) node = node.Bg(band);
            }
            else
            {
                var cells = rowCells(row);
                var laid = new Node[cells.Count];
                for (var c = 0; c < laid.Length; c++) laid[c] = cells[c];

                node = (widths is null
                        ? TableRow(shared, laid, look.Table)
                        : TableRow(shared, laid, look.Table, widths))
                    .Clickable(hit, _ => state.SelectedKey = key);
                if (onActivate is not null)
                {
                    // Enter and a double-click are the same act, and neither is a click: the click already
                    // selected the row, and a second one that selected again would be a no-op that swallowed
                    // the double-click.
                    node = node.DoubleClickable(_ => onActivate(row)).Activatable(_ => onActivate(row));
                }
            }

            if (selected) node = node.Bg(look.SelectedFill);
            return node.BgFocus(look.FocusFill);
        });

        return VStack(header, list.Stretch());
    }
}
