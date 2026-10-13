namespace DIR.Lib.Layout;

/// <summary>
/// One column of a table built with <see cref="Builder.TableHeader"/> and <see cref="Builder.TableRow"/>.
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
}

/// <summary>Which column a table is sorted by, and which way. Consumer-owned state, like a split's extent:
/// <see cref="Builder.TableHeader"/> shows it and reports the next one, and the consumer sorts its rows.</summary>
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
