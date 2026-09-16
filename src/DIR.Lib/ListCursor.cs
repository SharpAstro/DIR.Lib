using System;
using System.Collections.Generic;

namespace DIR.Lib;

/// <summary>
/// Where the keyboard is inside a list that a layout tree has already declared — the position the
/// arrow keys move and Enter acts on.
/// </summary>
/// <remarks>
/// <para>A list is declared by its rows, not by a model beside them: a row that says
/// <c>.Clickable(new HitResult.ListItemHit("views", i), …)</c> is row <c>i</c> of the list
/// <c>views</c>, and that one statement is enough to click it, to navigate to it, and — with
/// <see cref="Layout.Node.FocusBackground"/> — to light it up. Nothing else has to be kept in step.
/// </para>
/// <para>
/// <b>Reachability comes for free, and that is the point.</b> A row a reader cannot act on is simply
/// not clickable, so it registers no region and the cursor steps over it. A widget that tracked its
/// own highlight index instead has to answer "can this row be acted on" a second time, beside the
/// list, and a wrong answer there is invisible: the card looks drawn and stops responding.
/// </para>
/// <para>
/// The cursor holds no rows. It is a list id and an index, resolved against whatever the last paint
/// put on screen — which is the same list a click is tested against, so what the arrows reach and what
/// the mouse reaches cannot differ. A pixel widget hands <see cref="Step"/> the regions its tracker
/// registered; a cell surface hands it the rows it drew. The WALK is here rather than on either, so
/// there is one answer to "where does Down go" instead of one per surface.
/// </para>
/// </remarks>
public sealed class ListCursor
{
    /// <summary>The list the cursor is in, or null when it is in none.</summary>
    public string? ListId { get; private set; }

    /// <summary>
    /// The row within <see cref="ListId"/>, or -1 for a list the reader has not yet moved in.
    /// </summary>
    /// <remarks>
    /// -1 is a real state and not an error: it paints nothing, so a card that has just opened carries
    /// no highlight the reader did not ask for. The first arrow lands on the first row, and Enter
    /// before any arrow acts on the first row too — so a list is usable from the keyboard without
    /// arriving pre-lit.
    /// </remarks>
    public int Index { get; private set; } = -1;

    /// <summary>Whether the cursor is in a list at all.</summary>
    public bool IsOpen => ListId is not null;

    /// <summary>
    /// How many rows the list has, when the caller has stated it, or null when it has not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what lets a cursor walk a VIRTUALISED list.</b> Without it the rows the last paint
    /// registered are the whole of what exists, which is the right answer for a list that paints all of
    /// itself and wrong for one that paints only its viewport: a cursor that cannot step onto an
    /// unpainted row cannot reach the twentieth row of a list showing five, so the arrows stop dead at
    /// the bottom of the window and the list can only be scrolled with the mouse.
    /// </para>
    /// <para>
    /// Stating it does not weaken the reachability rule where it still applies. Within the span the paint
    /// DID register, an unregistered or disabled row is still skipped -- the paint is evidence there, and
    /// a row it left out of its own window is a row the reader cannot act on. Beyond that span there is
    /// no evidence either way, and the count is what says the row is there at all.
    /// </para>
    /// </remarks>
    public int? RowCount { get; private set; }

    /// <summary>
    /// Raised whenever the cursor actually MOVES, with the row it moved to -- the hook a consumer hangs
    /// <see cref="ListScrollController.EnsureVisible"/> off.
    /// </summary>
    /// <remarks>
    /// It exists for the same reason <see cref="RowCount"/> does: once a step can land on a row the last
    /// paint did not draw, something has to bring that row into view, and the cursor is the only thing
    /// that knows the step happened. Not raised by <see cref="Open"/> or <see cref="Close"/>, which place
    /// the cursor rather than moving it -- a list opening at its current selection has not scrolled.
    /// </remarks>
    public event Action<int>? Moved;

    /// <summary>
    /// Puts the cursor in <paramref name="listId"/>, at <paramref name="index"/> when the caller has
    /// a row to start on — normally whatever the list is currently showing, so the arrows step off
    /// something the reader can see rather than from the top of a list of thirty.
    /// </summary>
    public void Open(string listId, int index = -1)
    {
        ListId = listId;
        Index = index;
        RowCount = null;
    }

    /// <summary>
    /// <see cref="Open(string,int)"/> for a list that knows how long it is, which is what a virtualised
    /// list must say: see <see cref="RowCount"/> for why the painted rows alone cannot answer.
    /// </summary>
    /// <param name="count">The list's row count. Clamps the arrows to <c>[0, count)</c>.</param>
    public void Open(string listId, int index, int count)
    {
        ListId = listId;
        Index = index;
        RowCount = Math.Max(0, count);
    }

    /// <summary>Takes the cursor out of any list.</summary>
    public void Close()
    {
        ListId = null;
        Index = -1;
        RowCount = null;
    }

    /// <summary>Moves the cursor to <paramref name="index"/> of the list it is already in, raising
    /// <see cref="Moved"/> when that is a change.</summary>
    public void MoveTo(int index)
    {
        if (Index == index)
        {
            return;
        }

        Index = index;
        Moved?.Invoke(index);
    }

    /// <summary>Whether <paramref name="hit"/> is the row the cursor is on.</summary>
    public bool IsOn(HitResult? hit)
        => Index >= 0 && ListId is { } list
            && hit is HitResult.ListItemHit item
            && item.Index == Index
            && string.Equals(item.ListId, list, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="hit"/> belongs to the list the cursor is in.</summary>
    public bool Owns(HitResult? hit)
        => ListId is { } list
            && hit is HitResult.ListItemHit item
            && string.Equals(item.ListId, list, StringComparison.Ordinal);

    /// <summary>
    /// A row the last paint put on screen: its index in the list, and whether it refused the press.
    /// </summary>
    /// <remarks>
    /// The two surfaces answer this from different evidence -- a pixel widget from the regions its
    /// tracker registered, a cell surface from the window of rows it drew -- and that difference is the
    /// whole of what is per-surface about navigating a list. Reduced to this pair, the STEP is one rule.
    /// </remarks>
    /// <param name="Index">The row's index within the list.</param>
    /// <param name="IsDisabled">Whether the row declared itself unavailable, so the cursor steps over it:
    /// parking where Enter will refuse reads as a stuck key.</param>
    public readonly record struct PaintedRow(int Index, bool IsDisabled = false);

    /// <summary>
    /// Moves the cursor <paramref name="delta"/> rows through <paramref name="painted"/>. False when the
    /// cursor is in no list, when nothing of that list was painted, or when there is no row that way --
    /// in which case the cursor stays where it was, because stopping at the end reads as an end and
    /// moving to nothing reads as a broken control.
    /// </summary>
    /// <remarks>
    /// From <see cref="Index"/> of -1 -- a list the reader has not moved in yet -- this lands on the
    /// first row for a forward step and the last for a backward one.
    /// </remarks>
    public bool Step(int delta, IReadOnlyList<PaintedRow> painted)
    {
        ArgumentNullException.ThrowIfNull(painted);
        if (delta == 0 || !IsOpen)
        {
            return false;
        }

        var moved = false;
        var direction = Math.Sign(delta);
        for (var step = 0; step < Math.Abs(delta); step++)
        {
            if (!TryStep(direction, painted))
            {
                break;
            }

            moved = true;
        }

        return moved;
    }

    /// <summary>
    /// One row along, to the NEAREST painted row in that direction. Nearest rather than "index plus
    /// one", because the indices are the list's and need not be dense -- a list that leaves out the rows
    /// a reader cannot act on has gaps exactly where a step must not stop.
    /// </summary>
    private bool TryStep(int direction, IReadOnlyList<PaintedRow> painted)
    {
        var from = Index;
        var best = -1;
        var paintedLow = int.MaxValue;
        var paintedHigh = int.MinValue;
        for (var i = 0; i < painted.Count; i++)
        {
            var row = painted[i];
            // The span this paint covered, so a counted list can tell "off the bottom of the viewport"
            // from "deliberately unreachable" -- see TryStepPastTheViewport.
            if (row.Index < paintedLow) paintedLow = row.Index;
            if (row.Index > paintedHigh) paintedHigh = row.Index;
            // Declared unavailable: on screen so the press is swallowed, and stepped over for the same
            // reason a row that is not clickable is -- the cursor must never park somewhere Enter will
            // refuse.
            if (row.IsDisabled) continue;
            // Beyond where we are, in the direction of travel. From -1 -- a list the reader has not moved
            // in -- every row qualifies, so this lands on the first or the last.
            if (from >= 0 && (direction > 0 ? row.Index <= from : row.Index >= from)) continue;
            if (best >= 0 && (direction > 0 ? row.Index >= best : row.Index <= best)) continue;
            best = row.Index;
        }

        if (best >= 0)
        {
            MoveTo(best);
            return true;
        }

        return TryStepPastTheViewport(direction, from, paintedLow, paintedHigh);
    }

    /// <summary>
    /// The step a VIRTUALISED list needs: onto a row the last paint never drew, because the list only
    /// paints its viewport. Only possible where the cursor was opened with a row count -- see
    /// <see cref="RowCount"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The painted span stays the evidence wherever there is any: the walk above has already refused
    /// every row inside it, on purpose, so the first row that can still be stepped onto is the one just
    /// past that span -- never a row within it, which would reach over the top of the reachability rule.
    /// Beyond the span there is no evidence at all, and the count is the only thing that says the row is
    /// there.
    /// </para>
    /// <para>
    /// A list NOTHING of which was painted is still unnavigable, counted or not: a card that is not on
    /// screen must not move its cursor over rows nobody can see.
    /// </para>
    /// <para>
    /// The move raises <see cref="Moved"/> like any other, which is how the row is brought into view --
    /// without that the cursor lands somewhere nobody can see either.
    /// </para>
    /// </remarks>
    private bool TryStepPastTheViewport(int direction, int from, int paintedLow, int paintedHigh)
    {
        if (RowCount is not { } count || count <= 0) return false;
        if (paintedHigh < paintedLow) return false;

        var candidate = direction > 0
            ? Math.Max(from + 1, paintedHigh + 1)
            : Math.Min(from < 0 ? count - 1 : from - 1, paintedLow - 1);
        if (candidate < 0 || candidate >= count) return false;

        MoveTo(candidate);
        return true;
    }
}
