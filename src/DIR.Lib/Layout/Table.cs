using System;

namespace DIR.Lib.Layout;

/// <summary>
/// One column of a table built with <see cref="Builder.TableHeader(ReadOnlySpan{TableColumn}, TableSort?, Action{TableSort}?, in TableStyle, float, string)"/>
/// and <see cref="Builder.TableRow(ReadOnlySpan{TableColumn}, ReadOnlySpan{Node}, in TableStyle)"/>.
/// The same span of columns is handed to both, which is the whole point: a header and its rows that each
/// state their own widths drift the first time one of them is edited, and the drift is a column whose
/// title no longer sits over its values.
/// </summary>
/// <param name="Header">The column's title.</param>
/// <param name="Width">Its width: <see cref="Sizing.Fixed"/> for a value column, <see cref="Sizing.Star"/>
/// for the one that takes the slack (a path, a name).</param>
/// <param name="Align">Where its header and its cells sit: <see cref="TextAlign.Far"/> for numbers, so the
/// digits line up.</param>
public readonly record struct TableColumn(string Header, Sizing Width, TextAlign Align = TextAlign.Near)
{
    /// <summary>Whether a press on the header sorts by this column. A column that cannot be sorted still
    /// swallows the press, so it never falls through to whatever the header sits on.</summary>
    public bool Sortable { get; init; } = true;

    /// <summary>The direction a FIRST press on this column sorts in. Descending suits a size or a date, where
    /// the reader is looking for the biggest or newest; ascending suits a name.</summary>
    public bool DescendingFirst { get; init; }

    /// <summary>Hover text for the header cell.</summary>
    public string? Tooltip { get; init; }

    /// <summary>The narrowest this column may be dragged to, design units, or null for
    /// <see cref="TableColumnWidths.DefaultMinWidth"/>. Init-only rather than a constructor parameter, so the
    /// primary constructor every compiled caller binds to is unchanged.</summary>
    public float? MinWidth { get; init; }
}

/// <summary>
/// The widths of a table's <see cref="Sizing.Fixed"/> columns once a reader has dragged them: consumer-owned
/// state, like <see cref="TableSort"/>, handed to BOTH <see cref="Builder.TableHeader(ReadOnlySpan{TableColumn}, TableSort?, Action{TableSort}?, in TableStyle, TableColumnWidths, DesignScale, float, string)"/>
/// and <see cref="Builder.TableRow(ReadOnlySpan{TableColumn}, ReadOnlySpan{Node}, in TableStyle, TableColumnWidths)"/>
/// so a title and its values read the same number and cannot drift.
/// </summary>
/// <remarks>
/// A column nobody has dragged keeps the <see cref="Sizing"/> its <see cref="TableColumn"/> declares, so an
/// untouched object lays a table out exactly as one built without it. A <see cref="Sizing.Star"/> column is
/// never overridden: it stays the one that takes the slack, and a drag beside it resizes only the fixed side.
/// </remarks>
public sealed class TableColumnWidths
{
    private float?[] _overrides = [];

    /// <summary>The narrowest a column may be when it states no <see cref="TableColumn.MinWidth"/>, design units.</summary>
    public float DefaultMinWidth { get; init; } = 24f;

    /// <summary>Invoked whenever a drag changed a width, so a host can ask for a frame.</summary>
    public Action? Changed { get; set; }

    /// <summary>
    /// How wide column <c>i</c> must be to show its widest cell, design units, or null when the consumer cannot
    /// say. The consumer knows its rows and the header does not, so a double-click on a divider asks here and
    /// sizes the FIXED column on the divider's left to the answer (never below its minimum). Left null, a
    /// double-click does nothing and a second press drags like the first.
    /// </summary>
    /// <remarks>
    /// <b>It must be cheap</b>: it is called once per double-click, on the input thread, and a consumer
    /// typically answers from a widest value it already cached while building its rows rather than measuring
    /// every row again. A star column is never fitted (it would turn fixed and stop taking the slack), so a
    /// double-click on the divider right of a star column does nothing; the fixed column on its right is not
    /// fitted in its place, because that divider is the star's, not the neighbour's.
    /// </remarks>
    public Func<int, float?>? FitWidth { get; set; }

    /// <summary>
    /// Sizes the fixed column <paramref name="divider"/> to <see cref="FitWidth"/>'s answer, held at or above
    /// its minimum. Only that column moves: its neighbour keeps its width and the star column takes the
    /// difference. Returns whether a width changed; false without a callback, with an unknown answer, or when
    /// the column is a star.
    /// </summary>
    public bool Fit(ReadOnlySpan<TableColumn> columns, int divider)
    {
        if (FitWidth is not { } fit || (uint)divider >= (uint)columns.Length || divider + 1 >= columns.Length
            || !columns[divider].Width.IsFixed || fit(divider) is not { } wanted || float.IsNaN(wanted))
        {
            return false;
        }

        var changed = Set(divider, Math.Max(MinWidthOf(columns[divider]), wanted));
        if (changed)
        {
            Changed?.Invoke();
        }

        return changed;
    }

    /// <summary>The smallest <paramref name="spec"/> may be, design units.</summary>
    public float MinWidthOf(in TableColumn spec) => Math.Max(0f, spec.MinWidth ?? DefaultMinWidth);

    /// <summary>
    /// How column <paramref name="index"/> is sized: its declared <see cref="TableColumn.Width"/>, unless it is a
    /// fixed column that has been dragged, in which case the dragged width, held at or above its minimum.
    /// </summary>
    public Sizing SizingOf(int index, in TableColumn spec)
        => spec.Width.IsFixed && (uint)index < (uint)_overrides.Length && _overrides[index] is { } w
            ? Sizing.Fixed(Math.Max(w, Math.Min(MinWidthOf(spec), spec.Width.Value)))
            : spec.Width;

    /// <summary>
    /// The pure resize rule. Dragging the divider between a left and a right column by
    /// <paramref name="delta"/> design units (positive = right) grows the left and shrinks the right when both
    /// are fixed; beside a star column only the fixed side moves (the star takes the difference); two stars
    /// move nothing. Neither fixed column goes below its minimum, however far the delta runs.
    /// </summary>
    /// <param name="leftStart">The left column's width, or null when it is not fixed.</param>
    /// <param name="rightStart">The right column's width, or null when it is not fixed.</param>
    /// <returns>The new width of each fixed side; null for a side that is not fixed.</returns>
    public static (float? Left, float? Right) Resolve(float? leftStart, float? rightStart, float leftMin,
        float rightMin, float delta)
    {
        // A column already under its minimum (a declared width below it) can still not shrink further, but
        // it is not forced up either: the room to shrink is never negative.
        var shrinkLeft = leftStart is { } l ? Math.Max(0f, l - leftMin) : 0f;
        var shrinkRight = rightStart is { } r ? Math.Max(0f, r - rightMin) : 0f;
        switch (leftStart, rightStart)
        {
            case ({ } l2, { } r2):
                var d = Math.Clamp(delta, -shrinkLeft, shrinkRight);
                return (l2 + d, r2 - d);
            case ({ } l3, null):
                return (l3 + Math.Max(delta, -shrinkLeft), null);
            case (null, { } r3):
                return (null, r3 - Math.Min(delta, shrinkRight));
            default:
                return (null, null);
        }
    }

    /// <summary>
    /// Begins a drag of the divider after column <paramref name="divider"/>: remembers the widths NOW, so every
    /// move is measured from the press and a pointer that wanders back lands where it started instead of
    /// accumulating. Null when <paramref name="divider"/> has no column on one side.
    /// </summary>
    public ResizeGesture? Begin(ReadOnlySpan<TableColumn> columns, int divider)
    {
        if (divider < 0 || divider + 1 >= columns.Length)
        {
            return null;
        }

        var left = columns[divider];
        var right = columns[divider + 1];
        var leftSizing = SizingOf(divider, left);
        var rightSizing = SizingOf(divider + 1, right);
        return new ResizeGesture(this, divider,
            leftSizing.IsFixed ? leftSizing.Value : null, rightSizing.IsFixed ? rightSizing.Value : null,
            MinWidthOf(left), MinWidthOf(right));
    }

    /// <summary>A divider drag in progress; <see cref="Apply"/> may be called on every move.</summary>
    public sealed class ResizeGesture
    {
        private readonly TableColumnWidths _owner;
        private readonly int _divider;
        private readonly float? _leftStart, _rightStart;
        private readonly float _leftMin, _rightMin;

        internal ResizeGesture(TableColumnWidths owner, int divider, float? leftStart, float? rightStart,
            float leftMin, float rightMin)
        {
            _owner = owner;
            _divider = divider;
            _leftStart = leftStart;
            _rightStart = rightStart;
            _leftMin = leftMin;
            _rightMin = rightMin;
        }

        /// <summary>Sets the two widths to what <paramref name="delta"/> design units from the press makes of them.</summary>
        public void Apply(float delta)
        {
            var (left, right) = Resolve(_leftStart, _rightStart, _leftMin, _rightMin, delta);
            var changed = _owner.Set(_divider, left);
            changed |= _owner.Set(_divider + 1, right);
            if (changed)
            {
                _owner.Changed?.Invoke();
            }
        }
    }

    private bool Set(int index, float? width)
    {
        if (width is not { } w)
        {
            return false;
        }

        if (index >= _overrides.Length)
        {
            Array.Resize(ref _overrides, index + 1);
        }

        if (_overrides[index] == w)
        {
            return false;
        }

        _overrides[index] = w;
        return true;
    }

    /// <summary>
    /// What a table's column widths come to, as plain data a consumer can persist: one entry per column, null
    /// where the column was never dragged (it keeps its declared sizing, so a changed declaration still
    /// applies).
    /// </summary>
    /// <param name="Widths">Design units, by column index.</param>
    public sealed record Saved(float?[] Widths);

    /// <summary>The widths as a <see cref="Saved"/> record; <see cref="Restore"/> is its inverse.</summary>
    public Saved Save() => new((float?[])_overrides.Clone());

    /// <summary>
    /// Replaces the widths with <paramref name="saved"/>. Tolerant of a table whose columns changed since: entries
    /// past the current column count are never read, and a column the record has no entry for keeps its declared
    /// width, so a restore never throws or shifts a column that was not in it.
    /// </summary>
    public void Restore(Saved saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        _overrides = (float?[])saved.Widths.Clone();
        Changed?.Invoke();
    }

    /// <summary>A new object holding <paramref name="saved"/>'s widths.</summary>
    public static TableColumnWidths From(Saved saved)
    {
        var widths = new TableColumnWidths();
        widths.Restore(saved);
        return widths;
    }
}

/// <summary>Which column a table is sorted by, and which way. Consumer-owned state, like a split's extent:
/// <see cref="Builder.TableHeader(ReadOnlySpan{TableColumn}, TableSort?, Action{TableSort}?, in TableStyle, float, string)"/>
/// shows it and reports the next one, and the consumer sorts its rows.</summary>
public readonly record struct TableSort(int Column, bool Descending)
{
    /// <summary>
    /// The sort a press on <paramref name="column"/> asks for: the same column flips direction, another column
    /// starts in its own <see cref="TableColumn.DescendingFirst"/> direction.
    /// </summary>
    public static TableSort Next(TableSort? current, int column, in TableColumn spec)
        => current is { } c && c.Column == column
            ? c with { Descending = !c.Descending }
            : new TableSort(column, spec.DescendingFirst);
}

/// <summary>How a table header looks. Stated once per table, like <see cref="ButtonGroupStyle"/>.</summary>
/// <param name="HeaderFill">The header row's background.</param>
/// <param name="HeaderText">Header titles.</param>
/// <param name="HoverFill">A sortable header cell under the pointer.</param>
/// <param name="SortIndicator">The caret beside the sorted column's title.</param>
public readonly record struct TableStyle(
    RGBAColor32 HeaderFill,
    RGBAColor32 HeaderText,
    RGBAColor32 HoverFill,
    RGBAColor32 SortIndicator)
{
    /// <summary>Space between columns, design units. Header and rows use the same gap, so they line up.</summary>
    public float Gap { get; init; } = 8f;

    /// <summary>Horizontal padding at both ends of the row, design units.</summary>
    public float PadX { get; init; } = 6f;
}
