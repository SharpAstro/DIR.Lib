using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

/// <summary>
/// One item of a grouped tree, as the consumer hands it over: a group (<see cref="GroupedTreeGroup{TGroup,TRow}"/>,
/// which holds more items) or a leaf row (<see cref="GroupedTreeLeaf{TGroup,TRow}"/>). The tree stays the
/// consumer's; <see cref="GroupedTreeState"/> flattens it and remembers which groups are folded, by
/// <see cref="Key"/> rather than by object, so a tree rebuilt from fresh objects keeps its folds and its
/// selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>A key must be unique among every item of the tree that can be visible at once</b>, groups and rows
/// together, and must be stable: the same logical item has the same key on every refresh and on every run
/// (an id, a path, <c>"org/repo"</c>; never an object hash or a position). Two items with one key share a
/// fold and answer to one selection, and nothing checks for it.
/// </para>
/// <para>
/// Only <see cref="GroupedTreeGroup{TGroup,TRow}"/> and <see cref="GroupedTreeLeaf{TGroup,TRow}"/> derive from
/// this, so a consumer matches on those two. <see cref="Group"/> and <see cref="Leaf"/> build them without
/// restating both type arguments at every call.
/// </para>
/// </remarks>
/// <typeparam name="TGroup">What a group carries: the consumer's own type for an org, a repo, a section.</typeparam>
/// <typeparam name="TRow">What a leaf row carries: the consumer's own type for one table row.</typeparam>
public abstract record GroupedTreeNode<TGroup, TRow>
{
    private protected GroupedTreeNode(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
    }

    /// <summary>The item's stable identity. Unique among the visible items; see the type's remarks.</summary>
    public string Key { get; }

    /// <summary>A group of <paramref name="key"/> carrying <paramref name="payload"/> and
    /// <paramref name="children"/>, in the order to show them.</summary>
    public static GroupedTreeNode<TGroup, TRow> Group(string key, TGroup payload,
        IReadOnlyList<GroupedTreeNode<TGroup, TRow>> children)
        => new GroupedTreeGroup<TGroup, TRow>(key, payload, children);

    /// <summary>A leaf row of <paramref name="key"/> carrying <paramref name="payload"/>.</summary>
    public static GroupedTreeNode<TGroup, TRow> Leaf(string key, TRow payload)
        => new GroupedTreeLeaf<TGroup, TRow>(key, payload);
}

/// <summary>A group of a grouped tree: a header the reader can fold, over children that are groups or rows.</summary>
/// <param name="Key">Stable and unique, see <see cref="GroupedTreeNode{TGroup,TRow}"/>.</param>
/// <param name="Payload">The consumer's data for the header.</param>
/// <param name="Children">The items under it, in the order they are shown. Never reordered; never null (an
/// empty list is a group with nothing in it, which draws no caret).</param>
public sealed record GroupedTreeGroup<TGroup, TRow>(string Key, TGroup Payload,
    IReadOnlyList<GroupedTreeNode<TGroup, TRow>> Children) : GroupedTreeNode<TGroup, TRow>(Key);

/// <summary>A leaf row of a grouped tree: one row of the table, with nothing under it.</summary>
/// <param name="Key">Stable and unique, see <see cref="GroupedTreeNode{TGroup,TRow}"/>.</param>
/// <param name="Payload">The consumer's data for the row.</param>
public sealed record GroupedTreeLeaf<TGroup, TRow>(string Key, TRow Payload) : GroupedTreeNode<TGroup, TRow>(Key);

/// <summary>What a visible row of a grouped tree is.</summary>
public enum GroupedTreeRowKind
{
    /// <summary>A group header: it can fold, and Enter on it toggles that.</summary>
    Group,

    /// <summary>A leaf row: Enter on it activates it.</summary>
    Row,
}

/// <summary>
/// One visible row of a grouped tree, as <see cref="GroupedTreeState.Flatten{TGroup,TRow}"/> lays it out. Like
/// <see cref="TreeRowItem{T}"/> it is a row of a list, so the tree gets the list's keyboard
/// (<see cref="ListCursor"/>), scrolling and virtualisation rather than a second copy of each.
/// </summary>
/// <param name="Kind">Whether this is a group header or a leaf row.</param>
/// <param name="Key">The item's stable key: what folds and selection are held by.</param>
/// <param name="GroupPayload">The group's payload when <paramref name="Kind"/> is
/// <see cref="GroupedTreeRowKind.Group"/>, otherwise default.</param>
/// <param name="RowPayload">The leaf's payload when <paramref name="Kind"/> is
/// <see cref="GroupedTreeRowKind.Row"/>, otherwise default.</param>
/// <param name="Depth">0 for a top-level item.</param>
/// <param name="HasChildren">Whether this is a group with something in it: drawn as a caret. A leaf, and a
/// group with nothing in it, have none.</param>
/// <param name="IsExpanded">Whether its children are among the rows below it. Always false without
/// <paramref name="HasChildren"/>.</param>
/// <param name="ParentIndex">The row index of its parent group, or -1 at the top level: what Left steps to.</param>
public readonly record struct GroupedTreeRow<TGroup, TRow>(
    GroupedTreeRowKind Kind,
    string Key,
    TGroup? GroupPayload,
    TRow? RowPayload,
    int Depth,
    bool HasChildren,
    bool IsExpanded,
    int ParentIndex);

/// <summary>
/// The state of a grouped tree table that has to outlive the tree: which groups are folded, which item is
/// selected, and the rows that makes visible. It holds no node and no payload, only keys, so one state serves
/// whatever the consumer's group and row types are, and a tree rebuilt from fresh objects on every refresh
/// (or after a restart) keeps its folds and its selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Groups are open unless folded.</b> The state records the folded keys, not the open ones, so a group that
/// appears after the state was made, or restored, is open. That is the useful default for a table whose data
/// arrives from outside: nothing the reader has not folded is hidden.
/// </para>
/// <para>
/// <b>The widget never reorders.</b> <see cref="Flatten{TGroup,TRow}"/> emits items depth first in exactly
/// the order the consumer's tree gives them; sorting is the consumer's job, applied to each group's children
/// before the tree is handed over.
/// </para>
/// <para>
/// <b>Not thread-safe.</b> Like the rest of the layout state it belongs to the render thread.
/// </para>
/// </remarks>
public sealed class GroupedTreeState
{
    private readonly HashSet<string> _folded = new(StringComparer.Ordinal);

    // The rows the selection was last resolved against: what Reselect needs to find a neighbour or a parent
    // for a selected row that has since been dropped, because by then the row is no longer in the new list.
    private readonly List<string> _anchorKeys = [];
    private readonly List<int> _anchorParents = [];

    /// <summary>
    /// Invoked whenever the set of folded groups changed (a fold, an unfold, <see cref="ExpandAll"/>, a
    /// <see cref="Restore"/>), so a host can ask for a frame and persist <see cref="Save"/>. A selection move
    /// does not raise it, and neither does a call that changed nothing.
    /// </summary>
    public Action? Changed { get; set; }

    /// <summary>Whether the group <paramref name="key"/> is open. True for a key the state has never heard of.</summary>
    public bool IsExpanded(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return !_folded.Contains(key);
    }

    /// <summary>Opens the group <paramref name="key"/>; true when that was a change. Its descendants keep their
    /// own state, so reopening it shows the subtree as it was left.</summary>
    public bool Expand(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_folded.Remove(key)) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Folds the group <paramref name="key"/>; true when that was a change.</summary>
    public bool Collapse(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_folded.Add(key)) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Opens a folded group and folds an open one.</summary>
    public void Toggle(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_folded.Remove(key)) _folded.Add(key);
        Changed?.Invoke();
    }

    /// <summary>Opens every group, the state a fresh <see cref="GroupedTreeState"/> is in; true when anything
    /// was folded.</summary>
    public bool ExpandAll()
    {
        if (_folded.Count == 0) return false;
        _folded.Clear();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// The rows a reader sees: every top-level item of <paramref name="roots"/>, each followed (when it is an
    /// open group) by its children, depth first, in exactly the order given. The children of a folded group
    /// are skipped, and the folded group itself stays, with <see cref="GroupedTreeRow{TGroup,TRow}.IsExpanded"/>
    /// false.
    /// </summary>
    public List<GroupedTreeRow<TGroup, TRow>> Flatten<TGroup, TRow>(IReadOnlyList<GroupedTreeNode<TGroup, TRow>> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var rows = new List<GroupedTreeRow<TGroup, TRow>>();
        for (var i = 0; i < roots.Count; i++) Append(rows, roots[i], 0, -1);
        return rows;
    }

    private void Append<TGroup, TRow>(List<GroupedTreeRow<TGroup, TRow>> rows, GroupedTreeNode<TGroup, TRow> node,
        int depth, int parent)
    {
        switch (node)
        {
            case GroupedTreeGroup<TGroup, TRow> group:
                var children = group.Children;
                var can = children is { Count: > 0 };
                var open = can && !_folded.Contains(group.Key);
                var index = rows.Count;
                rows.Add(new GroupedTreeRow<TGroup, TRow>(GroupedTreeRowKind.Group, group.Key, group.Payload, default,
                    depth, can, open, parent));
                if (!open) return;
                for (var i = 0; i < children.Count; i++) Append(rows, children[i], depth + 1, index);
                return;
            case GroupedTreeLeaf<TGroup, TRow> leaf:
                rows.Add(new GroupedTreeRow<TGroup, TRow>(GroupedTreeRowKind.Row, leaf.Key, default, leaf.Payload,
                    depth, false, false, parent));
                return;
            default:
                throw new ArgumentException("A grouped tree holds only GroupedTreeGroup and GroupedTreeLeaf items.", nameof(node));
        }
    }

    /// <summary>
    /// The tree keys a list does not already own, and Enter. Right unfolds a folded group, or steps onto the
    /// first child of an open one. Left folds an open group, or steps to the parent. Enter toggles a group
    /// that has children, and on a leaf row reports <paramref name="activate"/>. Up and Down stay with the
    /// list (<see cref="ListCursor"/>), and so does everything else.
    /// </summary>
    /// <param name="index">The cursor's row, or -1 for none.</param>
    /// <param name="rows">The rows <see cref="Flatten{TGroup,TRow}"/> last returned.</param>
    /// <param name="moveTo">The row to move the cursor to, or -1 to leave it. Selection is the caller's to
    /// update from it (<see cref="SelectedKey"/>): the cursor owns the index, this owns the key.</param>
    /// <param name="activate">True when Enter landed on a leaf row, which the consumer opens.</param>
    /// <returns>Whether the key did anything. True with no <paramref name="moveTo"/> and no
    /// <paramref name="activate"/> is a fold change (also raised on <see cref="Changed"/>): <b>flatten
    /// again</b>, then <see cref="Reselect{TGroup,TRow}"/>.</returns>
    public bool HandleKey<TGroup, TRow>(InputKey key, int index, IReadOnlyList<GroupedTreeRow<TGroup, TRow>> rows,
        out int moveTo, out bool activate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        moveTo = -1;
        activate = false;
        if ((uint)index >= (uint)rows.Count) return false;
        var row = rows[index];
        switch (key)
        {
            case InputKey.Right when row.HasChildren && !row.IsExpanded:
                return Expand(row.Key);
            case InputKey.Right when row.IsExpanded && index + 1 < rows.Count && rows[index + 1].ParentIndex == index:
                moveTo = index + 1;
                return true;
            case InputKey.Left when row.IsExpanded:
                return Collapse(row.Key);
            case InputKey.Left when row.ParentIndex >= 0:
                moveTo = row.ParentIndex;
                return true;
            case InputKey.Enter when row.Kind == GroupedTreeRowKind.Row:
                activate = true;
                return true;
            case InputKey.Enter when row.HasChildren:
                Toggle(row.Key);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The key of the selected item, or null for none. Held by key, so it follows its item across a
    /// refresh, a re-sort and a fold; <see cref="Reselect{TGroup,TRow}"/> says where that item is now.</summary>
    public string? SelectedKey { get; set; }

    /// <summary>
    /// After a re-flatten: the index of the selected item in <paramref name="rows"/>, or -1 for none. Call it
    /// with every fresh list, because it also remembers that list, which is what lets a later drop be answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the selected item is no longer a row, the selection moves to the nearest row that survived, and
    /// <see cref="SelectedKey"/> follows it, so the next refresh starts from where the cursor now is. In order:
    /// </para>
    /// <list type="number">
    /// <item>The item is still there: its new index, wherever the order put it.</item>
    /// <item>A group that holds it is folded: that group, since the row is hidden, not gone.</item>
    /// <item>The nearest surviving sibling (same parent), the one after it before the one before it.</item>
    /// <item>The surviving group that held it.</item>
    /// <item>The nearest surviving row in the old order, wherever it went.</item>
    /// <item>Nothing from the old list survived: the row at the old position, held inside the new list.</item>
    /// </list>
    /// <para>
    /// A key the state has never seen as a row (a selection restored from a settings file before the data
    /// arrived, or one under a group that was folded the whole time) is left alone and gives -1, in case the
    /// row appears. An empty <paramref name="rows"/> also gives -1 and leaves everything as it was, so a
    /// refresh that is briefly empty does not lose the selection.
    /// </para>
    /// </remarks>
    public int Reselect<TGroup, TRow>(IReadOnlyList<GroupedTreeRow<TGroup, TRow>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return -1;

        var found = -1;
        if (SelectedKey is { } key)
        {
            found = IndexOfKey(rows, key);
            if (found < 0)
            {
                found = Fallback(rows, key);
                if (found >= 0) SelectedKey = rows[found].Key;
            }
        }

        _anchorKeys.Clear();
        _anchorParents.Clear();
        for (var i = 0; i < rows.Count; i++)
        {
            _anchorKeys.Add(rows[i].Key);
            _anchorParents.Add(rows[i].ParentIndex);
        }

        return found;
    }

    /// <summary>
    /// Every frame, after <see cref="Flatten{TGroup,TRow}"/>: finds the selected row (<see cref="Reselect{TGroup,TRow}"/>)
    /// and puts <paramref name="cursor"/> on it, with the row count, so the arrows can walk past the rows
    /// <see cref="Builder.GroupedTreeTable{TGroup,TRow}"/> built. That is what keeps the selection and the cursor on one
    /// row across a refresh, a re-sort and a fold: the key is the truth and the index follows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It refreshes a cursor that is in <paramref name="listId"/> and touches no other.</b> A cursor in no list stays
    /// out of it, and one in another list stays there, so a host that closed the cursor (focus went elsewhere) is not
    /// answered by it opening again, and two lists on one widget do not take it from each other every frame. The host
    /// puts it in this list when the table takes the keyboard, <c>cursor.Open(listId)</c>, and takes it out with
    /// <see cref="ListCursor.Close"/>.
    /// </para>
    /// <para>Opening raises no <see cref="ListCursor.Moved"/>, so a refresh never scrolls.</para>
    /// </remarks>
    /// <returns>The selected row's index in <paramref name="rows"/>, or -1, as <see cref="Reselect{TGroup,TRow}"/>,
    /// whether or not the cursor was in the list.</returns>
    public int SyncCursor<TGroup, TRow>(IReadOnlyList<GroupedTreeRow<TGroup, TRow>> rows, ListCursor cursor, string listId)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(listId);

        var index = Reselect(rows);
        if (string.Equals(cursor.ListId, listId, StringComparison.Ordinal))
        {
            cursor.Open(listId, index, rows.Count);
        }

        return index;
    }

    /// <summary>
    /// The whole keyboard of a grouped tree table, called for every key AFTER the widget's own
    /// <c>HandleListKey</c> (Up, Down and Enter on a painted row) and handed what it answered:
    /// <c>state.HandleTableKey(key, widget.HandleListKey(key, mods), widget.ListCursor, listId, rows, onActivate)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the list claimed the key, the cursor has moved (or a row's own handler has run), so the selection follows
    /// the cursor: <see cref="SelectedKey"/> becomes the cursor's row. Nothing else runs, which is what stops Enter
    /// being answered twice (the list activated the row, and this would toggle it back).
    /// </para>
    /// <para>
    /// Otherwise the key is the state's (<see cref="HandleKey{TGroup,TRow}"/>): Left and Right fold, unfold and step,
    /// and Enter, which only reaches here when the cursor was on a row that was not painted, toggles a group or
    /// reports a leaf to <paramref name="activate"/>. A step moves the cursor with <see cref="ListCursor.MoveTo"/>,
    /// which raises <see cref="ListCursor.Moved"/> so the row is scrolled into view, and moves the selection with it.
    /// </para>
    /// <para>
    /// A cursor that is in another list is not touched: the key was not for this table, so the answer is the list's.
    /// <b>Flatten again after a true</b>, since a fold changes the rows; the next <see cref="SyncCursor{TGroup,TRow}"/>
    /// then re-finds the selection.
    /// </para>
    /// </remarks>
    /// <param name="key">The key pressed.</param>
    /// <param name="listHandled">What the widget's <c>HandleListKey</c> returned for it.</param>
    /// <param name="cursor">The widget's <c>ListCursor</c>.</param>
    /// <param name="listId">The list the table was built for.</param>
    /// <param name="rows">The rows <see cref="Flatten{TGroup,TRow}"/> last returned.</param>
    /// <param name="activate">Opens a leaf row, for Enter on one the list did not activate. May be null.</param>
    /// <returns>Whether the key was claimed, by the list or here.</returns>
    public bool HandleTableKey<TGroup, TRow>(InputKey key, bool listHandled, ListCursor cursor, string listId,
        IReadOnlyList<GroupedTreeRow<TGroup, TRow>> rows, Action<GroupedTreeRow<TGroup, TRow>>? activate = null)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(listId);
        ArgumentNullException.ThrowIfNull(rows);

        if (!cursor.IsOpen || !string.Equals(cursor.ListId, listId, StringComparison.Ordinal))
        {
            return listHandled;
        }

        if (listHandled)
        {
            if ((uint)cursor.Index < (uint)rows.Count) SelectedKey = rows[cursor.Index].Key;
            return true;
        }

        if (!HandleKey(key, cursor.Index, rows, out var moveTo, out var activated))
        {
            return false;
        }

        if ((uint)moveTo < (uint)rows.Count)
        {
            cursor.MoveTo(moveTo);
            SelectedKey = rows[moveTo].Key;
        }

        if (activated) activate?.Invoke(rows[cursor.Index]);
        return true;
    }

    private static int IndexOfKey<TGroup, TRow>(IReadOnlyList<GroupedTreeRow<TGroup, TRow>> rows, string key)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (string.Equals(rows[i].Key, key, StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    private int Fallback<TGroup, TRow>(IReadOnlyList<GroupedTreeRow<TGroup, TRow>> rows, string key)
    {
        var oldIndex = _anchorKeys.IndexOf(key);
        if (oldIndex < 0) return -1;

        var now = new Dictionary<string, int>(rows.Count, StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++) now.TryAdd(rows[i].Key, i);

        var ancestor = -1;
        for (var p = _anchorParents[oldIndex]; p >= 0; p = _anchorParents[p])
        {
            if (now.TryGetValue(_anchorKeys[p], out ancestor)) break;
            ancestor = -1;
        }

        if (ancestor >= 0 && rows[ancestor] is { HasChildren: true, IsExpanded: false }) return ancestor;

        var sibling = SurvivingSibling(oldIndex, now);
        if (sibling >= 0) return sibling;
        if (ancestor >= 0) return ancestor;

        for (var distance = 1; oldIndex + distance < _anchorKeys.Count || oldIndex - distance >= 0; distance++)
        {
            if (oldIndex + distance < _anchorKeys.Count && now.TryGetValue(_anchorKeys[oldIndex + distance], out var after)) return after;
            if (oldIndex - distance >= 0 && now.TryGetValue(_anchorKeys[oldIndex - distance], out var before)) return before;
        }

        return Math.Min(oldIndex, rows.Count - 1);
    }

    /// <summary>The new index of the closest old sibling of row <paramref name="oldIndex"/> that is still a
    /// row, the next one taking precedence over the previous one at each step, or -1.</summary>
    private int SurvivingSibling(int oldIndex, Dictionary<string, int> now)
    {
        var parent = _anchorParents[oldIndex];
        int next = NextSibling(oldIndex, parent), previous = PreviousSibling(oldIndex, parent);
        while (next >= 0 || previous >= 0)
        {
            if (next >= 0)
            {
                if (now.TryGetValue(_anchorKeys[next], out var after)) return after;
                next = NextSibling(next, parent);
            }

            if (previous >= 0)
            {
                if (now.TryGetValue(_anchorKeys[previous], out var before)) return before;
                previous = PreviousSibling(previous, parent);
            }
        }

        return -1;
    }

    // A group's subtree is contiguous and every row in it has a parent index of at least the group's, so the
    // walk leaves the parent's subtree on the first row whose parent index is below it.
    private int NextSibling(int from, int parent)
    {
        for (var i = from + 1; i < _anchorParents.Count; i++)
        {
            if (_anchorParents[i] == parent) return i;
            if (_anchorParents[i] < parent) return -1;
        }

        return -1;
    }

    private int PreviousSibling(int from, int parent)
    {
        for (var i = from - 1; i >= 0 && i > parent; i--)
        {
            if (_anchorParents[i] == parent) return i;
        }

        return -1;
    }

    /// <summary>
    /// The folded groups as plain data a consumer can persist: the keys, sorted so that saving an unchanged
    /// state writes the same bytes. Only folds are recorded, so a group the reader never touched, and any group
    /// added since, comes back open.
    /// </summary>
    /// <param name="Folded">The keys of the groups that were folded. Includes keys of groups that are not in
    /// the tree right now (hidden by a filter, say), on purpose: filtering a group away must not unfold it.</param>
    public sealed record Saved(string[] Folded);

    /// <summary>The folds as a <see cref="Saved"/> record; <see cref="Restore"/> is its inverse.</summary>
    public Saved Save()
    {
        var keys = new string[_folded.Count];
        _folded.CopyTo(keys);
        Array.Sort(keys, StringComparer.Ordinal);
        return new Saved(keys);
    }

    /// <summary>
    /// Replaces the folds with <paramref name="saved"/>, and raises <see cref="Changed"/>. Tolerant of a tree
    /// that changed since: a key with no group in the tree is simply never looked up, a group the record does
    /// not name is open, so a restore never throws and never hides what it did not mention. The selection is not
    /// part of it.
    /// </summary>
    public void Restore(Saved saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        _folded.Clear();
        if (saved.Folded is { } keys)
        {
            foreach (var key in keys)
            {
                if (key is not null) _folded.Add(key);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>A new state holding <paramref name="saved"/>'s folds.</summary>
    public static GroupedTreeState From(Saved saved)
    {
        var state = new GroupedTreeState();
        state.Restore(saved);
        return state;
    }
}
