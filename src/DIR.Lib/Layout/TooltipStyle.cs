namespace DIR.Lib.Layout;

/// <summary>
/// How a host's tooltips look, for <see cref="Builder.Tooltip"/>. Stated once per window, so every tooltip
/// in it is one decision rather than a box measured and coloured at each place that wanted one.
/// </summary>
/// <param name="Background">The box's fill.</param>
/// <param name="Text">The text's colour.</param>
/// <param name="Border">The one-unit edge around the box, which is what keeps a tooltip reading as floating
/// over the content rather than cut into it.</param>
/// <param name="FontSize">The text's size, design units.</param>
public readonly record struct TooltipStyle(
    RGBAColor32 Background,
    RGBAColor32 Text,
    RGBAColor32 Border,
    float FontSize = 14f)
{
    /// <summary>Space between the text and the box's left and right edges, design units.</summary>
    public float PadX { get; init; } = 6f;

    /// <summary>Space between the text and the box's top and bottom edges, design units.</summary>
    public float PadY { get; init; } = 3f;

    /// <summary>The border's width, design units; 0 draws none.</summary>
    public float BorderWidth { get; init; } = 1f;

    /// <summary>The gap between the box and the region it explains, and the least distance it keeps from the
    /// window's edges, design units.</summary>
    public float Margin { get; init; } = 4f;

    /// <summary>The widest the text may run before it wraps, design units, measured inside the padding and
    /// border; 0 means no cap. A long request wraps at words (a path, after its separators) and a content
    /// tooltip's box is capped at this plus the padding and border.</summary>
    public float MaxWidth { get; init; } = 420f;
}
