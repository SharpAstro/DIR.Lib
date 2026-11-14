using System;
using System.Collections.Generic;

namespace DIR.Lib;

/// <summary>
/// The step and activate walks of a declared list, over the regions a paint registered, so the pixel
/// widget's own keys and <see cref="InputRouter"/>'s declared-list keys are one implementation.
/// </summary>
public static class ListKeys
{
    /// <summary>
    /// Moves <paramref name="cursor"/> <paramref name="delta"/> rows through the rows of its list that
    /// <paramref name="regions"/> registered, plus <paramref name="clipped"/> (rows a scrolled list arranged
    /// and its viewport clipped out). Disabled rows are stepped over. False when nothing moved.
    /// </summary>
    /// <param name="scratch">A buffer the caller reuses, so a key repeat allocates nothing.</param>
    public static bool Move(ListCursor cursor, int delta, ReadOnlySpan<ClickableRegion> regions,
        IReadOnlyList<ListCursor.PaintedRow> clipped, List<ListCursor.PaintedRow> scratch)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        if (delta == 0 || !cursor.IsOpen) return false;

        scratch.Clear();
        for (var i = 0; i < regions.Length; i++)
        {
            if (regions[i].Result is HitResult.ListItemHit item && cursor.Owns(item))
            {
                scratch.Add(new ListCursor.PaintedRow(item.Index, regions[i].IsDisabled));
            }
        }

        for (var i = 0; i < clipped.Count; i++)
        {
            scratch.Add(clipped[i]);
        }

        return cursor.Step(delta, scratch);
    }

    /// <summary>
    /// Invokes the cursor's row as a click on it would: its <see cref="ClickableRegion.OnActivate"/>, else
    /// its <see cref="ClickableRegion.OnClick"/>. With no row under the cursor yet it acts on the first.
    /// False when the cursor is in no list, the row was not painted, is disabled, or carries no handler.
    /// </summary>
    /// <param name="onMoved">Called after the implicit step to the first row, so a caller can reveal it.</param>
    public static bool Activate(ListCursor cursor, InputModifier modifier, ReadOnlySpan<ClickableRegion> regions,
        IReadOnlyList<ListCursor.PaintedRow> clipped, List<ListCursor.PaintedRow> scratch, Action? onMoved = null)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        if (!cursor.IsOpen) return false;
        if (cursor.Index < 0)
        {
            if (!Move(cursor, 1, regions, clipped, scratch)) return false;
            onMoved?.Invoke();
        }

        for (var i = 0; i < regions.Length; i++)
        {
            var region = regions[i];
            if (!cursor.IsOn(region.Result)) continue;
            // Disabled: the press is swallowed, so Enter is too. A row with neither handler registers so
            // the cursor can reach it while its host acts elsewhere, so nothing was acted on.
            if (region.IsDisabled || (region.OnActivate ?? region.OnClick) is not { } act)
            {
                return false;
            }

            act(modifier);
            return true;
        }

        return false;
    }
}
