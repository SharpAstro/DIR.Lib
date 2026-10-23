using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

// Data views: a virtualised list, a table header/row pair that cannot drift apart, a tree row, and a
// treemap. Like Progress and ButtonGroup, none of them adds a mechanism -- each is the primitives arranged
// the way every consumer was arranging them by hand, with the decisions that go wrong stated once.
public static partial class Builder
{
    /// <summary>
    /// A scrolled list of <paramref name="count"/> rows of <paramref name="rowHeight"/> design units that
    /// builds only the rows on screen: <paramref name="row"/> is called for the viewport's rows (plus
    /// <paramref name="overscan"/> either side) and never for the rest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rows not built are still there to the engine.</b> They are two spacers, one above the window
    /// and one below it, of exactly the height the rows would have had, so the stack's extent -- what the
    /// scrollbar, the wheel and <see cref="ListScrollController.EnsureVisible"/> measure -- is the whole
    /// list's. A list of a hundred thousand rows costs the same per frame as one of thirty.
    /// </para>
    /// <para>
    /// The window is read off <paramref name="scroll"/>, i.e. LAST frame's viewport, so a frame that grows
    /// the viewport builds one frame's worth too few rows at the new bottom edge: the frame a resize asks
    /// for anyway draws them. Before the first paint has bound a viewport the window is a generous guess.
    /// </para>
    /// <para>
    /// <b>The keyboard needs the count.</b> A row the paint did not build registered nothing, so
    /// <see cref="ListCursor"/> can only step past the window when it was opened with
    /// <see cref="ListCursor.Open(string,int,int)"/>; hang <see cref="ListScrollController.EnsureVisible"/>
    /// off <see cref="ListCursor.Moved"/> to bring the row it lands on into view.
    /// </para>
    /// <para>Sets <see cref="ListScrollController.AtomDesignUnits"/> to <paramref name="rowHeight"/>, so an
    /// atom is a row: the offset, the wheel step and <c>EnsureVisible</c> all count rows.</para>
    /// </remarks>
    /// <param name="row">Builds row <c>i</c>. Its height is set to <paramref name="rowHeight"/> and its width to
    /// the list's (<see cref="Node.RowH"/>), so a row's background spans the list.</param>
    public static Node VirtualList(int count, float rowHeight, ListScrollController scroll, Func<int, Node> row,
        int overscan = 2)
    {
        ArgumentNullException.ThrowIfNull(scroll);
        ArgumentNullException.ThrowIfNull(row);
        count = Math.Max(0, count);
        scroll.AtomDesignUnits = rowHeight;

        const int unboundWindow = 64;
        var visible = scroll.Viewport.Height > 0f ? scroll.VisibleAtoms + 1 : unboundWindow;
        var first = Math.Clamp(scroll.FirstVisibleAtom - overscan, 0, count);
        var last = Math.Clamp(scroll.FirstVisibleAtom + visible + overscan, first, count);

        var nodes = new List<Node>(last - first + 2);
        if (first > 0) nodes.Add(Spacer().WStar().HFixed(first * rowHeight));
        // A row spans the list, like RowH: left Auto, a row would be as wide as its content, and a row's
        // background and selection would end wherever its text happened to.
        for (var i = first; i < last; i++) nodes.Add(row(i) with { Width = Sizing.Star(), Height = Sizing.Fixed(rowHeight) });
        if (last < count) nodes.Add(Spacer().WStar().HFixed((count - last) * rowHeight));

        return new Node.Stack([.. nodes], Axis.Vertical) { Scroll = scroll };
    }

    /// <summary>
    /// A table's header row: each column's title, the sorted one with a caret pointing the way it sorts,
    /// and a press on a sortable title handing <see cref="TableSort.Next"/> to <paramref name="onSort"/>.
    /// Pair it with <see cref="TableRow(ReadOnlySpan{TableColumn}, ReadOnlySpan{Node}, in TableStyle)"/> over the SAME <paramref name="columns"/> and style.
    /// </summary>
    /// <remarks>
    /// Every header cell declares a hit, an unsortable one included, so a press on the header never falls
    /// through to the rows behind it. A null <paramref name="onSort"/> makes the header a display.
    /// </remarks>
    /// <param name="hitPrefix">Names each cell's <see cref="HitResult.ButtonHit"/> as
    /// <c>"{hitPrefix}{title}"</c>, so two tables in one window stay apart.</param>
    public static Node TableHeader(ReadOnlySpan<TableColumn> columns, TableSort? sort, Action<TableSort>? onSort,
        in TableStyle style, float fontSize = 14f, string hitPrefix = "table-header:")
        => TableHeaderCore(columns, sort, onSort, style, null, default, fontSize, hitPrefix);

    /// <summary>
    /// A table header whose columns can be resized by dragging the divider between two titles. The widths
    /// live in <paramref name="widths"/>, which the rows read too (<see cref="TableRow(ReadOnlySpan{TableColumn}, ReadOnlySpan{Node}, in TableStyle, TableColumnWidths)"/>),
    /// so a drag moves a title and its values together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The dividers are an overlay of the header, not part of it.</b> A second row, laid out with the header's
    /// own gap, padding and column sizing, holds an empty cell per column and a divider in each gap, and sits
    /// over the header, so a divider region is registered after the cells and wins the press over its own few
    /// units. A divider is as wide as <see cref="TableStyle.Gap"/>; a style with no gap has nothing to grab and
    /// gets no dividers.
    /// </para>
    /// <para>
    /// The drag is relative to the press and converted to design units by <paramref name="scale"/> (the
    /// design-to-surface mapping the tree is arranged at), and every move applies the rule to the widths AT THE
    /// PRESS, so it does not accumulate. See <see cref="TableColumnWidths.Resolve"/> for what it does beside a star
    /// column and at a minimum width. The dividers are named <c>"{hitPrefix}divider:{i}"</c>, <c>i</c> the column
    /// to its left.
    /// </para>
    /// </remarks>
    public static Node TableHeader(ReadOnlySpan<TableColumn> columns, TableSort? sort, Action<TableSort>? onSort,
        in TableStyle style, TableColumnWidths widths, DesignScale scale, float fontSize = 14f,
        string hitPrefix = "table-header:")
    {
        ArgumentNullException.ThrowIfNull(widths);
        return TableHeaderCore(columns, sort, onSort, style, widths, scale, fontSize, hitPrefix);
    }

    private static Node TableHeaderCore(ReadOnlySpan<TableColumn> columns, TableSort? sort, Action<TableSort>? onSort,
        in TableStyle style, TableColumnWidths? widths, DesignScale scale, float fontSize, string hitPrefix)
    {
        var cells = new Node[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            var column = columns[i];
            var sorted = sort is { } s && s.Column == i;
            // The caret sits against the title, on the side away from the column's alignment edge, so a
            // right-aligned number column reads "▼ size" flush right rather than a caret stranded at the
            // far left of the cell. The slack goes to a spacer on the other side.
            Node face;
            if (sorted)
            {
                var caret = Icon(sort!.Value.Descending ? IconKind.CaretDown : IconKind.CaretUp, color: style.SortIndicator).WAuto().HStar();
                var tight = Text(column.Header, fontSize, style.HeaderText, column.Align, TextAlign.Center).WAuto().HStar();
                face = column.Align == TextAlign.Far
                    ? HStack(Spacer().WStar().HStar(), caret, tight).WithGap(3f)
                    : HStack(tight, caret, Spacer().WStar().HStar()).WithGap(3f);
            }
            else
            {
                face = Text(column.Header, fontSize, style.HeaderText, column.Align, TextAlign.Center).WStar().HStar();
            }
            face = face.CrossCenter() with { Width = widths?.SizingOf(i, column) ?? column.Width, Height = Sizing.Star() };

            if (onSort is not null)
            {
                var index = i;
                var spec = column;
                Action<InputModifier>? press = column.Sortable ? _ => onSort(TableSort.Next(sort, index, spec)) : null;
                face = face.Clickable(new HitResult.ButtonHit(hitPrefix + column.Header), press);
                if (column.Sortable) face = face.BgHover(style.HoverFill);
                if (column.Tooltip is { Length: > 0 } tip) face = face.WithTooltip(tip);
            }

            cells[i] = face;
        }

        var header = HStack(cells).WithGap(style.Gap).PadX(style.PadX).Bg(style.HeaderFill);
        if (widths is null || columns.Length < 2 || style.Gap <= 0f)
        {
            return header;
        }

        // The same row again with nothing in it but the grips: columns sized as the header's are, and the gap
        // between two of them a divider the gap's width. A zero-gap stack of [column, gap, column, ...] puts
        // every edge where the header's gapped stack puts it, star columns included, because the fixed total
        // is the same.
        var scaleOrOne = scale.OrOne();
        var copy = columns.ToArray();
        var top = new List<Node>(columns.Length * 2 - 1);
        for (var i = 0; i < columns.Length; i++)
        {
            top.Add(Spacer() with { Width = widths.SizingOf(i, columns[i]), Height = Sizing.Star() });
            if (i + 1 < columns.Length)
            {
                var divider = i;
                top.Add(Spacer().WFixed(style.Gap).HStar().Pressable(
                    new HitResult.ButtonHit($"{hitPrefix}divider:{i}"), DividerPress(copy, widths, i, scaleOrOne),
                    CursorKind.ResizeEW).DoubleClickable(_ => widths.Fit(copy, divider)));
            }
        }

        return Overlay(header, HStack(top.ToArray()).PadX(style.PadX));
    }

    private static Func<PointerPress, DragCapture?> DividerPress(TableColumn[] columns, TableColumnWidths widths,
        int divider, DesignScale scale)
        => press =>
        {
            if (press.Button != MouseButton.Left || widths.Begin(columns, divider) is not { } gesture)
            {
                return null;
            }

            // The second press of a pair is the double-click's, which fits the column; declining hands it to
            // the router. Where there is nothing to fit it is a press like any other, and drags.
            if (press.Clicks >= 2 && widths.FitWidth is not null && columns[divider].Width.IsFixed)
            {
                return null;
            }

            void To(in PointerMove at) => gesture.Apply(scale.ToDesignX(at.X - press.X));
            return new DragCapture(move => To(move), release => To(release));
        };

    /// <summary>
    /// A table row: <paramref name="cells"/> sized by <paramref name="columns"/>, with the gap and padding of
    /// the header built from the same style, so a title sits over its values whatever either is edited to.
    /// Extra cells are dropped and missing ones left empty. Size the row's height at the call site.
    /// </summary>
    public static Node TableRow(ReadOnlySpan<TableColumn> columns, ReadOnlySpan<Node> cells, in TableStyle style)
        => TableRowCore(columns, cells, style, null);

    /// <summary>
    /// A table row sized by <paramref name="widths"/>, the object the header's dividers write, so a column the
    /// reader has dragged is as wide here as its title is.
    /// </summary>
    public static Node TableRow(ReadOnlySpan<TableColumn> columns, ReadOnlySpan<Node> cells, in TableStyle style,
        TableColumnWidths widths)
    {
        ArgumentNullException.ThrowIfNull(widths);
        return TableRowCore(columns, cells, style, widths);
    }

    private static Node TableRowCore(ReadOnlySpan<TableColumn> columns, ReadOnlySpan<Node> cells, in TableStyle style,
        TableColumnWidths? widths)
    {
        var laid = new Node[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            var cell = i < cells.Length ? cells[i] : Spacer();
            laid[i] = cell with { Width = widths?.SizingOf(i, columns[i]) ?? columns[i].Width, Height = Sizing.Star() };
        }

        return HStack(laid).WithGap(style.Gap).PadX(style.PadX);
    }

    /// <summary>
    /// A tree row: the indent for <paramref name="depth"/>, the caret (a press on it hands the row to
    /// <paramref name="onToggle"/>), then <paramref name="content"/>, which takes the rest of the row.
    /// </summary>
    /// <remarks>
    /// The caret's press is its own, inside the row: make the ROW <c>.Clickable</c> with a
    /// <see cref="HitResult.ListItemHit"/> for selection and the keyboard, and the caret, registered later,
    /// wins the press over its own few units. A leaf keeps the caret's column empty so names line up across
    /// levels. Rows come from <see cref="TreeExpansion{T}.Flatten"/>.
    /// </remarks>
    public static Node TreeRow(int depth, bool hasChildren, bool isExpanded, Action? onToggle, Node content,
        in TreeRowStyle style, float caretSize = 12f)
    {
        var indent = Spacer().WFixed(Math.Max(0, depth) * style.Indent).HStar();
        Node caret = hasChildren
            ? Icon(isExpanded ? IconKind.CaretDown : IconKind.CaretRight, caretSize, style.Caret)
            : Spacer();
        caret = caret.WFixed(style.CaretWidth).HStar();
        if (hasChildren && onToggle is not null)
        {
            caret = caret.Clickable(new HitResult.ChromeHit(), _ => onToggle(), CursorKind.Pointer);
        }

        return HStack(indent, caret, content.WStar().HStar()).CrossCenter();
    }

    /// <summary>
    /// A squarified treemap of <paramref name="weights"/>: cell <c>i</c> is <paramref name="cell"/>(i), its
    /// area proportional to <c>weights[i]</c>. Built for a rect <paramref name="aspect"/> times as wide as it is
    /// tall -- last frame's is fine (see <see cref="Treemap"/>) -- and filling whatever rect it is given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every cell is a node in nested star-weighted stacks, so it is drawn and hit through one rect and
    /// can carry anything a node can: a background, a label, <c>.Clickable</c>, <c>.BgHover</c>, a tooltip,
    /// a nested treemap. Weights need not be sorted; a weight that is not positive gets no cell.
    /// </para>
    /// <para>
    /// The node count is the cell count plus about one per strip, so cap what is drawn (the largest few
    /// hundred, and one cell for the rest) rather than handing it every file on a disk.
    /// </para>
    /// </remarks>
    /// <param name="gap">Space between cells, design units.</param>
    public static Node Treemap(ReadOnlySpan<double> weights, float aspect, Func<int, Node> cell, float gap = 0f)
    {
        ArgumentNullException.ThrowIfNull(cell);

        // Descending order, carrying each weight's original index so cell(i) is the caller's i.
        var order = new List<int>(weights.Length);
        for (var i = 0; i < weights.Length; i++)
        {
            if (weights[i] > 0 && double.IsFinite(weights[i])) order.Add(i);
        }

        if (order.Count == 0) return Spacer().Stretch();

        var w = weights.ToArray();
        order.Sort((a, b) => w[b].CompareTo(w[a]));
        // As fractions of the total: the star weights are floats, and a disk's byte counts are not.
        double total = 0;
        foreach (var i in order) total += w[i];
        var sorted = new double[order.Count];
        for (var i = 0; i < sorted.Length; i++) sorted[i] = w[order[i]] / total;

        var strips = Layout.Treemap.Squarify(sorted, aspect);

        // Built from the LAST strip outward: each level is [strip, everything after it], the two star-weighted
        // by their totals so the remaining rect splits exactly as the squarify pass assumed.
        Node? rest = null;
        double restWeight = 0;
        for (var s = strips.Count - 1; s >= 0; s--)
        {
            var strip = strips[s];
            var items = new Node[strip.Count];
            double stripWeight = 0;
            for (var k = 0; k < strip.Count; k++)
            {
                var weight = sorted[strip.Start + k];
                stripWeight += weight;
                var node = cell(order[strip.Start + k]);
                items[k] = strip.Vertical
                    ? node with { Width = Sizing.Star(), Height = Sizing.Star((float)weight) }
                    : node with { Width = Sizing.Star((float)weight), Height = Sizing.Star() };
            }

            var stripNode = (strip.Vertical ? VStack(items) : HStack(items)).WithGap(gap);
            if (rest is null)
            {
                rest = stripNode.Stretch();
            }
            else
            {
                // A vertical strip is a column at the left: strip and rest sit side by side.
                rest = strip.Vertical
                    ? HStack(stripNode with { Width = Sizing.Star((float)stripWeight), Height = Sizing.Star() },
                             rest with { Width = Sizing.Star((float)restWeight), Height = Sizing.Star() }).WithGap(gap)
                    : VStack(stripNode with { Height = Sizing.Star((float)stripWeight), Width = Sizing.Star() },
                             rest with { Height = Sizing.Star((float)restWeight), Width = Sizing.Star() }).WithGap(gap);
            }

            restWeight += stripWeight;
        }

        return rest!;
    }
}
