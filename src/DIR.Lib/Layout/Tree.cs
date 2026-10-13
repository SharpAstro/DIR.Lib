using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DIR.Lib.Layout;

/// <summary>
/// One visible row of a tree, as <see cref="TreeExpansion{T}.Flatten"/> lays it out: the item, how deep it
/// sits and whether it can open. The tree is shown as these rows -- a list -- so a tree gets the list's
/// keyboard (<see cref="ListCursor"/>), scrolling and virtualisation (<see cref="Builder.VirtualList"/>)
/// rather than a second copy of each.
/// </summary>
/// <param name="Item">The node.</param>
/// <param name="Depth">0 for a root.</param>
/// <param name="HasChildren">Whether the node has, or may have, children: drawn as a caret rather than
/// a leaf.</param>
/// <param name="IsExpanded">Whether its children are among the rows below it.</param>
/// <param name="ParentIndex">The row index of its parent, or -1 for a root: what Left steps to.</param>
public readonly record struct TreeRowItem<T>(T Item, int Depth, bool HasChildren, bool IsExpanded, int ParentIndex);

/// <summary>
/// Which nodes of a tree are open, and the rows that makes visible. The tree itself stays the consumer's --
/// this holds no node, only the open set, keyed by reference -- so a node that is rebuilt is a new node
/// and starts closed, and one that survives a refresh keeps its state.
/// </summary>
/// <remarks>
/// <para>
/// <b>Children are read only for an open node.</b> That is what makes a lazily loaded tree free: a
/// <c>children</c> callback that enumerates a directory runs when the reader opens it and not before, and
/// a tree of a million folders flattens to the few dozen rows that are open.
/// </para>
/// </remarks>
public sealed class TreeExpansion<T> where T : class
{
    private readonly HashSet<T> _open = new(ReferenceEqualityComparer.Instance);

    /// <summary>Whether <paramref name="item"/> is open.</summary>
    public bool IsExpanded(T item) => _open.Contains(item);

    /// <summary>Opens <paramref name="item"/>; true when that was a change.</summary>
    public bool Expand(T item) => _open.Add(item);

    /// <summary>Closes <paramref name="item"/>; true when that was a change. Its descendants keep their own
    /// state, so reopening it shows the subtree as it was left.</summary>
    public bool Collapse(T item) => _open.Remove(item);

    /// <summary>Opens a closed node and closes an open one.</summary>
    public void Toggle(T item)
    {
        if (!_open.Remove(item)) _open.Add(item);
    }

    /// <summary>Closes everything.</summary>
    public void Clear() => _open.Clear();

    /// <summary>
    /// The rows a reader sees: <paramref name="root"/> (unless <paramref name="includeRoot"/> is false), then
    /// every open node's children, depth first, in the order <paramref name="children"/> gives them.
    /// </summary>
    /// <param name="children">A node's children. Called only for an open node.</param>
    /// <param name="hasChildren">Whether a node has, or may have, children -- for a node that has not been
    /// loaded yet, say so without loading it. Null means "<paramref name="children"/> is non-empty", which
    /// loads every visible node's children to draw a caret.</param>
    public List<TreeRowItem<T>> Flatten(T root, Func<T, IReadOnlyList<T>> children,
        Func<T, bool>? hasChildren = null, bool includeRoot = true)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(children);
        var rows = new List<TreeRowItem<T>>();
        if (includeRoot)
        {
            Add(root, 0, -1);
        }
        else
        {
            foreach (var child in children(root)) Add(child, 0, -1);
        }

        return rows;

        void Add(T item, int depth, int parent)
        {
            var open = _open.Contains(item);
            var can = hasChildren?.Invoke(item) ?? children(item).Count > 0;
            var index = rows.Count;
            rows.Add(new TreeRowItem<T>(item, depth, can, open && can, parent));
            if (!open || !can) return;
            foreach (var child in children(item)) Add(child, depth + 1, index);
        }
    }

    /// <summary>
    /// The tree keys a list does not already own: Right opens the row (or steps onto its first child when it
    /// is open), Left closes it (or steps to its parent when it is closed). Up, Down, Home, End and Enter
    /// stay with the list (<see cref="PixelWidgetBase{TSurface}.HandleListKey"/>).
    /// </summary>
    /// <param name="index">The cursor's row, or -1 for none.</param>
    /// <param name="moveTo">The row to move the cursor to, or -1 to leave it.</param>
    /// <returns>Whether the key did anything; when it opened or closed a node, flatten again.</returns>
    public bool HandleKey(InputKey key, int index, IReadOnlyList<TreeRowItem<T>> rows, out int moveTo)
    {
        moveTo = -1;
        if (index < 0 || index >= rows.Count) return false;
        var row = rows[index];
        switch (key)
        {
            case InputKey.Right when row.HasChildren && !row.IsExpanded:
                return Expand(row.Item);
            case InputKey.Right when row.IsExpanded && index + 1 < rows.Count && rows[index + 1].ParentIndex == index:
                moveTo = index + 1;
                return true;
            case InputKey.Left when row.IsExpanded:
                return Collapse(row.Item);
            case InputKey.Left when row.ParentIndex >= 0:
                moveTo = row.ParentIndex;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>How a tree row's indent and caret look, stated once per tree.</summary>
/// <param name="Caret">The open/closed caret.</param>
public readonly record struct TreeRowStyle(RGBAColor32 Caret)
{
    /// <summary>Indent per level, design units.</summary>
    public float Indent { get; init; } = 16f;

    /// <summary>The caret column's width, design units. A leaf keeps it empty so names line up across levels.</summary>
    public float CaretWidth { get; init; } = 16f;
}
