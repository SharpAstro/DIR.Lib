using System;
using System.Collections.Generic;

namespace DIR.Lib;

/// <summary>
/// A list a layout node declares: the <see cref="ListCursor"/> that owns its rows and the id its rows
/// state in <see cref="HitResult.ListItemHit"/>. Set with <see cref="Layout.Node.ListOf(ListCursor, string)"/> on the list's
/// container, so any router reading the PAINTED nodes finds it, whatever kind of widget painted them.
/// </summary>
/// <param name="Cursor">The cursor the arrows move and Enter acts on.</param>
/// <param name="ListId">The list id of the rows this cursor owns.</param>
public sealed record DeclaredList(ListCursor Cursor, string ListId)
{
    /// <summary>True for a list laid out along the row: <see cref="InputRouter"/> steps it with Left/Right instead of
    /// Up/Down. Init-only, so the constructor keeps its shape.</summary>
    public bool Horizontal { get; init; }

    /// <summary>True when a step also acts on the row it lands on, as Enter would: a segmented control chooses the segment
    /// the arrow reaches. A row with no handler (the chosen one) is only landed on.</summary>
    public bool SelectOnStep { get; init; }

    /// <summary>
    /// THE active-list rule, stated once: the declared list holding the focus, else the one holding the
    /// pointer's last press, else the only list declared on the frame, else none.
    /// </summary>
    /// <remarks>
    /// "None" is an answer and not a gap: with two lists and no hint, guessing would step one the reader
    /// never pointed at, so the key is left for the host (<see cref="InputRouter.Unhandled"/>). A hint
    /// naming a list that is not on the frame is ignored, as a list that is not painted owns no keys.
    /// </remarks>
    /// <param name="onFrame">The lists declared by the nodes the frame painted.</param>
    /// <param name="focusedListId">The list holding the keyboard focus, or null.</param>
    /// <param name="pressedListId">The list the pointer last pressed in, or null.</param>
    public static DeclaredList? Active(IReadOnlyList<DeclaredList> onFrame, string? focusedListId, string? pressedListId)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        if (Find(onFrame, focusedListId) is { } focused) return focused;
        if (Find(onFrame, pressedListId) is { } pressed) return pressed;

        DeclaredList? only = null;
        foreach (var list in onFrame)
        {
            if (only is null) only = list;
            else if (!string.Equals(only.ListId, list.ListId, StringComparison.Ordinal)) return null;
        }

        return only;
    }

    private static DeclaredList? Find(IReadOnlyList<DeclaredList> onFrame, string? id)
    {
        if (id is null) return null;
        foreach (var list in onFrame)
        {
            if (string.Equals(list.ListId, id, StringComparison.Ordinal)) return list;
        }

        return null;
    }
}
