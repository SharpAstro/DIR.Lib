namespace DIR.Lib;

/// <summary>
/// Which edge of its host a <see cref="TabBar{TSurface}"/> is attached to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Orientation is DERIVED from this, not set beside it.</b> A separate <c>Orientation</c> property
/// would let a caller state a vertical strip on the top edge -- a combination with no meaning that the
/// bar would have to resolve by preferring one of the two, silently ignoring the other. Here the
/// question cannot be asked.
/// </para>
/// <para>
/// The side decides three things at once, and they move together: the flow axis tabs advance along, the
/// edge the active accent is drawn on (the OUTER one, away from the content the strip heads), and the
/// edge the bar rules against that content (the opposite one). <see cref="Top"/> reproduces the
/// strip's original rendering exactly.
/// </para>
/// </remarks>
public enum TabStripSide
{
    /// <summary>Along the top edge; tabs advance left to right, accent on top. The default, and the
    /// only value that existed before sides did.</summary>
    Top,

    /// <summary>Along the bottom edge; tabs advance left to right, accent underneath.</summary>
    Bottom,

    /// <summary>Down the left edge; tabs advance top to bottom, accent on the left. A nav rail --
    /// pair it with <see cref="TabSizing.Uniform"/>.</summary>
    Left,

    /// <summary>Down the right edge; tabs advance top to bottom, accent on the right.</summary>
    Right,
}

/// <summary>
/// How a <see cref="TabBar{TSurface}"/> sizes a tab along the axis its tabs advance on.
/// </summary>
public enum TabSizing
{
    /// <summary>
    /// Sized to fit the tab's own content -- measured label, plus the icon box and close box when
    /// present -- clamped to the bar's minimum and maximum. What a document tab strip wants, and what
    /// the strip has always done.
    /// </summary>
    Content,

    /// <summary>
    /// Every tab the same extent as the strip's own thickness, i.e. a square cell. What a nav rail
    /// wants, and it is not merely a preference on a vertical strip: <see cref="Content"/> there sizes
    /// a tab's HEIGHT from the width of a label, which is meaningless, and on an icon-only rail it
    /// sizes it from a label that is not drawn at all.
    /// <para>
    /// A uniform tab draws its icon centred and, having no room beside it, neither the label nor the
    /// close box -- the label belongs in the host's tooltip
    /// (see <see cref="TabBar{TSurface}.HoveredIndex"/>). With no icon the label is drawn centred and
    /// truncated instead, so a uniform strip is never blank.
    /// </para>
    /// </summary>
    Uniform,
}
