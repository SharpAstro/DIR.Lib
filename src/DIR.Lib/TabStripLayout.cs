using DIR.Lib.Layout;

namespace DIR.Lib;

/// <summary>
/// The sizes a tab strip is built from, in the DESIGN units of whatever surface will paint it — pixels
/// for a GPU host (already DPI-scaled), whole cells for a terminal.
/// </summary>
/// <remarks>
/// A parameter rather than constants on the widget, because this is the whole reason one description can
/// serve two surfaces: the shapes and the rules are identical, the numbers are not. A terminal has no
/// half cell, so its border is 0 and its pad is 1.
/// </remarks>
/// <param name="Thickness">The strip's extent across the axis its tabs advance on.</param>
/// <param name="FontSize">Label size.</param>
/// <param name="Pad">Inset from a tab's leading edge.</param>
/// <param name="Border">Rule thickness. 0 draws no accent, separators or bar edge — right for a cell
/// surface, which cannot rule a fraction of a cell.</param>
/// <param name="IconBox">Extent reserved for a <see cref="TabItem{T}.Icon"/>, never measured (see
/// <see cref="TabBar{TSurface}"/>).</param>
/// <param name="CloseBox">Extent reserved for the ✕, when the strip closes tabs.</param>
/// <param name="MinTabExtent">Floor for a content-sized tab.</param>
/// <param name="MaxTabExtent">Ceiling for a content-sized tab.</param>
public readonly record struct TabStripMetrics(
    float Thickness,
    float FontSize,
    float Pad,
    float Border,
    float IconBox,
    float CloseBox,
    float MinTabExtent,
    float MaxTabExtent)
{
    /// <summary>Size a glyph is drawn at. Null = <see cref="FontSize"/>; see <see cref="TabBar{TSurface}.IconSize"/>.</summary>
    public float? IconSize { get; init; }

    /// <summary>
    /// Space BETWEEN tabs, along the flow axis. Default 0, which is the pixel strip: it separates tabs
    /// with a ruled edge instead, and a gap as well would read as a broken rule. A cell surface has no
    /// rule available (see <see cref="Border"/>), so a gap is the only separation it can draw.
    /// </summary>
    public float Gap { get; init; }

    /// <summary>
    /// One cell per unit: a single-row strip with no rules, which is what a terminal can actually draw.
    /// Tabs are content-sized with no ceiling, since a terminal tab bar runs out of columns rather than
    /// wanting a maximum.
    /// </summary>
    public static TabStripMetrics Cells { get; } = new(
        Thickness: 1f, FontSize: 1f, Pad: 0f, Border: 0f,
        IconBox: 2f, CloseBox: 1f, MinTabExtent: 0f, MaxTabExtent: float.MaxValue)
    {
        Gap = 1f,
    };
}

/// <summary>What a strip does with a tab that does not fit the extent it was given.</summary>
public enum TabStripOverflow
{
    /// <summary>
    /// Lay it out anyway and let it clip. What a document strip wants: tabs keep their positions, and the
    /// last one being half-visible is a legible "there is more".
    /// </summary>
    Clip,

    /// <summary>
    /// Leave it out of the strip entirely — not drawn, and therefore not hit-testable. What a terminal
    /// tab bar wants, and the distinction is not cosmetic: a clipped tab leaves a region that is hit but
    /// not visible, so a press lands on something the user cannot see.
    /// </summary>
    Drop,
}

/// <summary>
/// Text wrapped around a tab's label to mark the active one without relying on colour.
/// </summary>
/// <remarks>
/// A terminal has colour, so this looks redundant next to an active plate — and on a monochrome
/// terminal, or a theme whose background is washed out by the user's own palette, the brackets are the
/// ONLY thing saying which tab is active. Dropping them would be a silent bet on the reader's terminal.
/// </remarks>
public sealed record TabLabelDecoration
{
    /// <summary>Wraps the active tab's label.</summary>
    public string ActiveOpen { get; init; } = "";

    /// <inheritdoc cref="ActiveOpen"/>
    public string ActiveClose { get; init; } = "";

    /// <summary>Wraps every other tab's label, so the two stay the same width and do not shift.</summary>
    public string IdleOpen { get; init; } = "";

    /// <inheritdoc cref="IdleOpen"/>
    public string IdleClose { get; init; } = "";

    /// <summary>No wrapping: a pixel strip marks the active tab with a plate and an accent.</summary>
    public static TabLabelDecoration None { get; } = new();

    /// <summary><c>[label]</c> active, <c> label </c> idle — the terminal convention.</summary>
    public static TabLabelDecoration Brackets { get; } = new()
    {
        ActiveOpen = "[",
        ActiveClose = "]",
        IdleOpen = " ",
        IdleClose = " ",
    };

    /// <summary>The label as this decoration would draw it.</summary>
    public string Apply(string label, bool active)
        => active ? $"{ActiveOpen}{label}{ActiveClose}" : $"{IdleOpen}{label}{IdleClose}";
}
