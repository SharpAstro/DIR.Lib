namespace DIR.Lib.Layout;

/// <summary>
/// A focus ring a <see cref="Node"/> declares: a stroke of <see cref="Color"/> and <see cref="Width"/> design units drawn
/// OUTSIDE the node's (rounded) edge while the list cursor is on the node's hit. It takes no layout space (the engine
/// ignores it), a pixel surface paints it, and a cell surface draws none. A transparent colour or a zero width paints
/// nothing.
/// <para>
/// A record with init-only properties, not positional parameters, so a field added later is additive.
/// </para>
/// </summary>
public sealed record FocusRingStyle
{
    /// <summary>The ring colour. A fully transparent colour paints nothing.</summary>
    public RGBAColor32 Color { get; init; } = new RGBAColor32(0, 0, 0, 255);

    /// <summary>The ring's stroke width in design units (design-language: 2). Zero or less paints nothing.</summary>
    public float Width { get; init; } = 2f;

    /// <summary>The gap in design units between the node's edge and the ring's inner edge; what is behind the node shows
    /// through it, so the ring reads on a control filled in its own colour (an accent button under an accent ring). Zero
    /// (default) touches the edge. Negative counts as zero.</summary>
    public float Offset { get; init; }
}
