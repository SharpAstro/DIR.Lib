namespace DIR.Lib.Layout;

/// <summary>The sides of a node's box a <see cref="BorderStyle"/> draws and reserves.</summary>
[System.Flags]
public enum BorderSides
{
    /// <summary>No side: the border reserves and draws nothing.</summary>
    None = 0,
    /// <summary>The top edge.</summary>
    Top = 1,
    /// <summary>The right edge.</summary>
    Right = 2,
    /// <summary>The bottom edge.</summary>
    Bottom = 4,
    /// <summary>The left edge.</summary>
    Left = 8,
    /// <summary>All four edges.</summary>
    All = Top | Right | Bottom | Left,
}

/// <summary>
/// A border a <see cref="Node"/> declares: colour, width and corner radius in design units, and the sides it covers.
/// The engine reserves it inside the node's extent, outside the padding, on every surface: a pixel surface reserves
/// <see cref="Width"/>, a cell surface one cell per side (see <see cref="ExtentKind.Border"/>).
/// <para>
/// A record with init-only properties, not positional parameters, so a field added later is additive.
/// </para>
/// </summary>
public sealed record BorderStyle
{
    /// <summary>The stroke colour. A fully transparent colour reserves the border but paints nothing.</summary>
    public RGBAColor32 Color { get; init; }

    /// <summary>The stroke width in design units; 0 or less reserves nothing.</summary>
    public float Width { get; init; } = 1f;

    /// <summary>The corner radius in design units; 0 is square. Painting only: it reserves no room. On all four sides a pixel surface also rounds the node's background to it.</summary>
    public float Radius { get; init; }

    /// <summary>The sides drawn and reserved. Default <see cref="BorderSides.All"/>.</summary>
    public BorderSides Sides { get; init; } = BorderSides.All;
}
