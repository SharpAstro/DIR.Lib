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

    /// <summary>Hover text for an enabled segment.</summary>
    public string? Tooltip { get; init; }

    /// <summary>A fill for THIS segment instead of the style's, selected or not: a warning tint on the one
    /// choice that has consequences, say. Hover still follows the style.</summary>
    public RGBAColor32? Fill { get; init; }
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
}
