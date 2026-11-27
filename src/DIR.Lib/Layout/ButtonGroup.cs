namespace DIR.Lib.Layout;

/// <summary>
/// One segment of a <see cref="Builder.ButtonGroup{T}"/>: the value it selects and what it shows.
/// </summary>
/// <param name="Value">The value a press on this segment selects, compared against the group's current
/// selection with <see cref="System.Collections.Generic.EqualityComparer{T}.Default"/>.</param>
/// <param name="Label">The segment's text, or null for an icon-only segment.</param>
/// <param name="Icon">The segment's icon, drawn instead of <paramref name="Label"/> when set.</param>
public readonly record struct ButtonGroupOption<T>(T Value, string? Label = null, IconKind? Icon = null)
{
    /// <summary>The segment's hit, so a host or an inspector can find it by name. Null gives it a
    /// <see cref="HitResult.ButtonHit"/> named after <see cref="Label"/> (or the value, for an icon).</summary>
    public HitResult? Hit { get; init; }

    /// <summary>Why the segment cannot be chosen right now, or null when it can. A disabled segment is
    /// dimmed, swallows its press with <see cref="CursorKind.NotAllowed"/> and shows the reason as its
    /// tooltip, exactly as a disabled dropdown row does (<see cref="Node.Disabled(string)"/>).</summary>
    public string? DisabledReason { get; init; }

    /// <summary>A key that chooses this segment, matched against the painted tree like any node's shortcut
    /// (<see cref="Node.WithShortcut(KeyChord)"/>). Used by <see cref="Builder.Segmented{T}(ReadOnlySpan{ButtonGroupOption{T}}, T, Action{T}, UiTheme, ListCursor, string, float)"/>.</summary>
    public KeyChord? Shortcut { get; init; }

    /// <summary>Hover text for an enabled segment.</summary>
    public string? Tooltip { get; init; }

    /// <summary>A fill for THIS segment instead of the style's, selected or not: a warning tint on the one
    /// choice that has consequences, say. Pair it with <see cref="HoverFill"/>, or the warning turns into the
    /// style's neutral hover exactly when the pointer is about to press it.</summary>
    public RGBAColor32? Fill { get; init; }

    /// <summary>The background this segment takes under the pointer instead of the style's
    /// <see cref="ButtonGroupStyle.HoverFill"/>: the lit form of its own <see cref="Fill"/>.</summary>
    public RGBAColor32? HoverFill { get; init; }

    /// <summary>Text measured instead of <see cref="Label"/> when the group sizes its segments
    /// (<see cref="ButtonGroupStyle.EqualToWidest"/>), exactly as <c>Content.Text.WidthSample</c>: for a
    /// segment whose word changes, so the group never moves. The label is still what is drawn. It decides
    /// nothing under the equal share or a fixed <see cref="ButtonGroupStyle.SegmentWidth"/>.</summary>
    public string? WidthSample { get; init; }
}

/// <summary>
/// How a <see cref="Builder.ButtonGroup{T}"/> shows which segment is chosen. Stated once per group, so the
/// selected and unselected looks are one decision rather than two colours picked at each call site --
/// which is how a group once drew both halves in the same fill and said nothing about which side won.
/// </summary>
/// <param name="SelectedFill">The chosen segment's background.</param>
/// <param name="UnselectedFill">Every other segment's background, or null to leave them bare, so the row
/// reads as one control with a current value rather than several buttons.</param>
/// <param name="SelectedContent">The chosen segment's text or icon colour.</param>
/// <param name="UnselectedContent">Every other segment's text or icon colour.</param>
/// <param name="HoverFill">The background an actionable segment takes under the pointer. The chosen one
/// never lights, since a press on it changes nothing; nor does a disabled one.</param>
public readonly record struct ButtonGroupStyle(
    RGBAColor32 SelectedFill,
    RGBAColor32? UnselectedFill,
    RGBAColor32 SelectedContent,
    RGBAColor32 UnselectedContent,
    RGBAColor32 HoverFill)
{
    /// <summary>Space between segments, design units.</summary>
    public float Gap { get; init; } = 4f;

    /// <summary>Corner radius of each segment's background, design units.</summary>
    public float CornerRadius { get; init; }

    /// <summary>How much of the row's height a segment's background fills, centred: 1 fills it, 0.7 draws
    /// an inset pill. The press still covers the whole height, so an inset look costs no hit area.</summary>
    public float InsetFraction { get; init; } = 1f;

    /// <summary>A fixed width per segment in design units, or null to share the row equally.</summary>
    public float? SegmentWidth { get; init; }

    /// <summary>When true, every segment is as wide as the widest option's measured content (its
    /// <see cref="ButtonGroupOption{T}.WidthSample"/>, else its label, else its icon) plus
    /// <see cref="SegmentPadding"/> either side, so the group has an intrinsic width and sits at Auto width in
    /// its row. A disabled option counts toward the widest. A set <see cref="SegmentWidth"/> wins over this.</summary>
    public bool EqualToWidest { get; init; }

    /// <summary>Design units of padding either side of each segment's content, inside the segment's fill.
    /// Default 0, which leaves a group that does not set it unchanged.</summary>
    public float SegmentPadding { get; init; }
}
