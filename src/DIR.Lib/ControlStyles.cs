using System;

namespace DIR.Lib;

/// <summary>
/// How one kind of button looks: the fill at rest, hovered and pressed, the edge, the radius, the horizontal padding and
/// the ink. A null <see cref="RestFill"/> is no fill at rest (a subtle button), a null <see cref="Edge"/> is no edge.
/// Values are design units and palette colours, never literals, so a theme restyles every button by one
/// <c>with</c> on <see cref="UiTheme.Controls"/>.
/// </summary>
public sealed record ButtonStyle
{
    /// <summary>Fill at rest; null draws none.</summary>
    public RGBAColor32? RestFill { get; init; }

    /// <summary>Fill while the pointer is over the button.</summary>
    public required RGBAColor32 HoverFill { get; init; }

    /// <summary>Fill while the primary button is held down on it.</summary>
    public required RGBAColor32 PressedFill { get; init; }

    /// <summary>Edge colour; null draws no edge.</summary>
    public RGBAColor32? Edge { get; init; }

    /// <summary>Width of the edge ring in design units (used when <see cref="Edge"/> is set).</summary>
    public required float EdgeWidth { get; init; }

    /// <summary>Corner radius in design units.</summary>
    public required float Radius { get; init; }

    /// <summary>Horizontal padding in design units.</summary>
    public required float PaddingX { get; init; }

    /// <summary>Text and icon colour.</summary>
    public required RGBAColor32 Ink { get; init; }

    /// <summary>Text and icon colour when the button is disabled; null means the host dims <see cref="Ink"/> itself.</summary>
    public RGBAColor32? DisabledInk { get; init; }
}

/// <summary>How a hyperlink looks: its colour, and whether it is underlined on hover.</summary>
public sealed record HyperlinkStyle
{
    /// <summary>Link colour.</summary>
    public required RGBAColor32 Color { get; init; }

    /// <summary>Gap between the text and the trailing arrow of an external link, in design units.</summary>
    public required float ArrowGap { get; init; }

    /// <summary>Whether the text is underlined while the pointer is over it.</summary>
    public bool UnderlineOnHover { get; init; } = true;
}

/// <summary>
/// How a filter chip looks: a fully round pill that is either on (filled with the accent, no ring) or off (the neutral
/// control fill with the control edge ring). Values are palette colours and design units, never literals.
/// </summary>
public sealed record ChipStyle
{
    /// <summary>Fill of a chip that is on, at rest.</summary>
    public required RGBAColor32 OnFill { get; init; }

    /// <summary>Fill of a chip that is on, while the pointer is over it.</summary>
    public required RGBAColor32 OnHoverFill { get; init; }

    /// <summary>Fill of a chip that is on, while the primary button is held down on it.</summary>
    public required RGBAColor32 OnPressedFill { get; init; }

    /// <summary>Text and icon colour of a chip that is on.</summary>
    public required RGBAColor32 OnInk { get; init; }

    /// <summary>Fill of a chip that is off, at rest.</summary>
    public required RGBAColor32 OffFill { get; init; }

    /// <summary>Fill of a chip that is off, while the pointer is over it.</summary>
    public required RGBAColor32 OffHoverFill { get; init; }

    /// <summary>Fill of a chip that is off, while the primary button is held down on it.</summary>
    public required RGBAColor32 OffPressedFill { get; init; }

    /// <summary>Text and icon colour of a chip that is off.</summary>
    public required RGBAColor32 OffInk { get; init; }

    /// <summary>Ring colour of a chip that is off; null draws no ring. A chip that is on never has a ring.</summary>
    public RGBAColor32? OffEdge { get; init; }

    /// <summary>Width of the ring in design units (used when <see cref="OffEdge"/> is set).</summary>
    public required float EdgeWidth { get; init; }

    /// <summary>Corner radius in design units; null means fully round (half of <see cref="UiMetrics.ButtonHeight"/>).</summary>
    public float? Radius { get; init; }

    /// <summary>Horizontal padding in design units.</summary>
    public required float PaddingX { get; init; }

    /// <summary>Text and icon colour when the chip is disabled; null means the host dims the ink itself.</summary>
    public RGBAColor32? DisabledInk { get; init; }

    /// <summary>Derives a chip style from the Accent and Standard button styles of a <see cref="ControlStyles"/>.</summary>
    internal static ChipStyle From(ButtonStyle accent, ButtonStyle standard) => new()
    {
        OnFill = accent.RestFill ?? accent.HoverFill,
        OnHoverFill = accent.HoverFill,
        OnPressedFill = accent.PressedFill,
        OnInk = accent.Ink,
        OffFill = standard.RestFill ?? standard.HoverFill,
        OffHoverFill = standard.HoverFill,
        OffPressedFill = standard.PressedFill,
        OffInk = standard.Ink,
        OffEdge = standard.Edge,
        EdgeWidth = standard.EdgeWidth,
        PaddingX = standard.PaddingX,
        DisabledInk = standard.DisabledInk,
    };
}

/// <summary>
/// How a toggle tile looks: a square icon tile that is either on (filled with the accent, no ring) or off (the neutral
/// control fill with the control edge ring), with its label under it. The side is <see cref="SideScale"/> times
/// <see cref="UiMetrics.ButtonHeight"/>, so it follows the metrics. Values are palette colours and design units, never literals.
/// </summary>
public sealed record ToggleTileStyle
{
    /// <summary>Fill of a tile that is on, at rest.</summary>
    public required RGBAColor32 OnFill { get; init; }

    /// <summary>Fill of a tile that is on, while the pointer is over it.</summary>
    public required RGBAColor32 OnHoverFill { get; init; }

    /// <summary>Fill of a tile that is on, while the primary button is held down on it.</summary>
    public required RGBAColor32 OnPressedFill { get; init; }

    /// <summary>Icon colour of a tile that is on.</summary>
    public required RGBAColor32 OnInk { get; init; }

    /// <summary>Fill of a tile that is off, at rest.</summary>
    public required RGBAColor32 OffFill { get; init; }

    /// <summary>Fill of a tile that is off, while the pointer is over it.</summary>
    public required RGBAColor32 OffHoverFill { get; init; }

    /// <summary>Fill of a tile that is off, while the primary button is held down on it.</summary>
    public required RGBAColor32 OffPressedFill { get; init; }

    /// <summary>Icon colour of a tile that is off.</summary>
    public required RGBAColor32 OffInk { get; init; }

    /// <summary>Ring colour of a tile that is off; null draws no ring. A tile that is on never has a ring.</summary>
    public RGBAColor32? OffEdge { get; init; }

    /// <summary>Width of the ring in design units (used when <see cref="OffEdge"/> is set).</summary>
    public required float EdgeWidth { get; init; }

    /// <summary>Corner radius of the tile in design units.</summary>
    public required float Radius { get; init; }

    /// <summary>The tile's side as a multiple of <see cref="UiMetrics.ButtonHeight"/>.</summary>
    public required float SideScale { get; init; }

    /// <summary>Gap between the tile and its label, in design units.</summary>
    public required float LabelGap { get; init; }

    /// <summary>Colour of the label under the tile.</summary>
    public required RGBAColor32 LabelInk { get; init; }

    /// <summary>Icon and label colour when the tile is disabled; null means the host dims the ink itself.</summary>
    public RGBAColor32? DisabledInk { get; init; }
}

/// <summary>
/// The control kinds a theme styles: <see cref="Accent"/> (the one primary action), <see cref="Standard"/>,
/// <see cref="Subtle"/> (no fill at rest), <see cref="Hyperlink"/>, the filter <see cref="Chip"/> and the toggle <see cref="Tile"/>. <see cref="For"/> derives them from a palette
/// and metrics, so every existing theme gets them without stating anything.
/// </summary>
public sealed record ControlStyles
{
    // Fractions of the step towards the opposite extreme, not colours.
    private const float AccentHoverStep = 0.12f;
    private const float AccentPressedStep = 0.24f;
    private const float LinkStep = 0.1f;
    private const int LinkMaxSteps = 10;
    // A tile is one and a half buttons square; its label gap is half a button's side padding, set or derived alike.
    private const float TileSideScale = 1.5f;
    private const float LinkMinContrast = 4.5f;

    /// <summary>The primary action: filled with the accent, no edge.</summary>
    public required ButtonStyle Accent { get; init; }

    /// <summary>The ordinary button: neutral control fill with an edge.</summary>
    public required ButtonStyle Standard { get; init; }

    /// <summary>A quiet button: no fill at rest, neutral fill on hover.</summary>
    public required ButtonStyle Subtle { get; init; }

    /// <summary>An inline link.</summary>
    public required HyperlinkStyle Hyperlink { get; init; }

    /// <summary>
    /// The filter chip. Init-only and not required, so a <c>new ControlStyles { Accent, Standard, Subtle, Hyperlink }</c>
    /// written before chips existed still compiles; when unset, <see cref="ChipOrDerived"/> derives it from
    /// <see cref="Accent"/> and <see cref="Standard"/>, and <c>Builder.Chip</c> takes the radius from the metrics.
    /// <see cref="For"/> sets it.
    /// </summary>
    public ChipStyle? Chip { get; init; }

    /// <summary>
    /// The toggle tile. Init-only and not required, so an existing <c>new ControlStyles { ... }</c> still compiles; when
    /// unset, <see cref="TileOrDerived"/> derives it from <see cref="Accent"/> and <see cref="Standard"/>. <see cref="For"/> sets it.
    /// </summary>
    public ToggleTileStyle? Tile { get; init; }

    /// <summary>The tile style: <see cref="Tile"/> when set, else derived from <see cref="Accent"/> and <see cref="Standard"/>.</summary>
    public ToggleTileStyle TileOrDerived => Tile ?? DeriveTile(Accent, Standard, TileSideScale, Standard.PaddingX / 2f, Standard.Ink);

    /// <summary>The chip style: <see cref="Chip"/> when set, else derived from <see cref="Accent"/> and <see cref="Standard"/>.</summary>
    public ChipStyle ChipOrDerived => Chip ?? ChipStyle.From(Accent, Standard);

    /// <summary>Derives the control styles from <paramref name="palette"/> and <paramref name="metrics"/>.</summary>
    public static ControlStyles For(UiPalette palette, UiMetrics metrics)
    {
        var accentInk = palette.InkOn(palette.Accent);
        // Move away from the ink so hover and pressed never lose contrast with it.
        var away = accentInk == palette.BodyText ? palette.ContentBg : palette.BodyText;
        var accentStyle = new ButtonStyle
        {
                RestFill = palette.Accent,
                HoverFill = RGBAColor32.Mix(palette.Accent, away, AccentHoverStep),
                PressedFill = RGBAColor32.Mix(palette.Accent, away, AccentPressedStep),
                EdgeWidth = metrics.ControlEdgeWidth,
                Radius = metrics.ControlRadius,
                PaddingX = metrics.Padding,
                Ink = accentInk,
                DisabledInk = palette.DimText,
        };
        var standardStyle = new ButtonStyle
        {
                RestFill = palette.ControlFill,
                HoverFill = palette.ControlFillHover,
                PressedFill = palette.ControlFillPressed,
                Edge = palette.ControlEdge,
                EdgeWidth = metrics.ControlEdgeWidth,
                Radius = metrics.ControlRadius,
                PaddingX = metrics.Padding,
                Ink = palette.BodyText,
                DisabledInk = palette.DimText,
        };
        return new ControlStyles
        {
            Accent = accentStyle,
            Standard = standardStyle,
            Subtle = new ButtonStyle
            {
                HoverFill = palette.ControlFillHover,
                PressedFill = palette.ControlFillPressed,
                EdgeWidth = metrics.ControlEdgeWidth,
                Radius = metrics.ControlRadius,
                PaddingX = metrics.Padding,
                Ink = palette.BodyText,
                DisabledInk = palette.DimText,
            },
            Hyperlink = new HyperlinkStyle { Color = LinkColor(palette), ArrowGap = metrics.LinkArrowGap },
            Chip = new ChipStyle
            {
                OnFill = palette.Accent,
                OnHoverFill = RGBAColor32.Mix(palette.Accent, away, AccentHoverStep),
                OnPressedFill = RGBAColor32.Mix(palette.Accent, away, AccentPressedStep),
                OnInk = accentInk,
                OffFill = palette.ControlFill,
                OffHoverFill = palette.ControlFillHover,
                OffPressedFill = palette.ControlFillPressed,
                OffInk = palette.BodyText,
                OffEdge = palette.ControlEdge,
                EdgeWidth = metrics.ControlEdgeWidth,
                Radius = metrics.ButtonHeight / 2f,
                PaddingX = metrics.Padding,
                DisabledInk = palette.DimText,
            },
            Tile = DeriveTile(accentStyle, standardStyle, TileSideScale, standardStyle.PaddingX / 2f, palette.BodyText),
        };
    }

    private static ToggleTileStyle DeriveTile(ButtonStyle accent, ButtonStyle standard, float sideScale, float labelGap, RGBAColor32 labelInk) => new()
    {
        OnFill = accent.RestFill ?? accent.HoverFill,
        OnHoverFill = accent.HoverFill,
        OnPressedFill = accent.PressedFill,
        OnInk = accent.Ink,
        OffFill = standard.RestFill ?? standard.HoverFill,
        OffHoverFill = standard.HoverFill,
        OffPressedFill = standard.PressedFill,
        OffInk = standard.Ink,
        OffEdge = standard.Edge,
        EdgeWidth = standard.EdgeWidth,
        Radius = standard.Radius,
        SideScale = sideScale,
        LabelGap = labelGap,
        LabelInk = labelInk,
        DisabledInk = standard.DisabledInk,
    };

    // The accent when it reaches 4.5:1 on both grounds a link sits on (PanelBg and ContentBg, cards included), else the
    // accent stepped towards BodyText until it does on the lesser of the two (bounded; BodyText itself is the last resort).
    private static RGBAColor32 LinkColor(UiPalette p)
    {
        var c = p.Accent;
        for (int i = 0; i < LinkMaxSteps && LeastContrast(c, p) < LinkMinContrast; i++)
        {
            c = RGBAColor32.Mix(c, p.BodyText, LinkStep);
        }
        return LeastContrast(c, p) < LinkMinContrast ? p.BodyText : c;
    }

    private static float LeastContrast(RGBAColor32 c, UiPalette p)
        => MathF.Min(RGBAColor32.ContrastRatio(c, p.PanelBg), RGBAColor32.ContrastRatio(c, p.ContentBg));
}
