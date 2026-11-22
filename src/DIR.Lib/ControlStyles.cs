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

    /// <summary>Whether the text is underlined while the pointer is over it.</summary>
    public bool UnderlineOnHover { get; init; } = true;
}

/// <summary>
/// The control kinds a theme styles: <see cref="Accent"/> (the one primary action), <see cref="Standard"/>,
/// <see cref="Subtle"/> (no fill at rest) and <see cref="Hyperlink"/>. <see cref="For"/> derives all four from a palette
/// and metrics, so every existing theme gets them without stating anything.
/// </summary>
public sealed record ControlStyles
{
    // Fractions of the step towards the opposite extreme, not colours.
    private const float AccentHoverStep = 0.12f;
    private const float AccentPressedStep = 0.24f;
    private const float LinkStep = 0.1f;
    private const int LinkMaxSteps = 10;
    private const float LinkMinContrast = 4.5f;

    /// <summary>The primary action: filled with the accent, no edge.</summary>
    public required ButtonStyle Accent { get; init; }

    /// <summary>The ordinary button: neutral control fill with an edge.</summary>
    public required ButtonStyle Standard { get; init; }

    /// <summary>A quiet button: no fill at rest, neutral fill on hover.</summary>
    public required ButtonStyle Subtle { get; init; }

    /// <summary>An inline link.</summary>
    public required HyperlinkStyle Hyperlink { get; init; }

    /// <summary>Derives the four defaults from <paramref name="palette"/> and <paramref name="metrics"/>.</summary>
    public static ControlStyles For(UiPalette palette, UiMetrics metrics)
    {
        var accentInk = palette.InkOn(palette.Accent);
        // Move away from the ink so hover and pressed never lose contrast with it.
        var away = accentInk == palette.BodyText ? palette.ContentBg : palette.BodyText;
        return new ControlStyles
        {
            Accent = new ButtonStyle
            {
                RestFill = palette.Accent,
                HoverFill = RGBAColor32.Mix(palette.Accent, away, AccentHoverStep),
                PressedFill = RGBAColor32.Mix(palette.Accent, away, AccentPressedStep),
                Radius = metrics.ControlRadius,
                PaddingX = metrics.Padding,
                Ink = accentInk,
                DisabledInk = palette.DimText,
            },
            Standard = new ButtonStyle
            {
                RestFill = palette.ControlFill,
                HoverFill = palette.ControlFillHover,
                PressedFill = palette.ControlFillPressed,
                Edge = palette.ControlEdge,
                Radius = metrics.ControlRadius,
                PaddingX = metrics.Padding,
                Ink = palette.BodyText,
                DisabledInk = palette.DimText,
            },
            Subtle = new ButtonStyle
            {
                HoverFill = palette.ControlFillHover,
                PressedFill = palette.ControlFillPressed,
                Radius = metrics.ControlRadius,
                PaddingX = metrics.Padding,
                Ink = palette.BodyText,
                DisabledInk = palette.DimText,
            },
            Hyperlink = new HyperlinkStyle { Color = LinkColor(palette) },
        };
    }

    // The accent when it reaches 4.5:1 on PanelBg, else the accent stepped towards BodyText until it does (bounded;
    // BodyText itself is the last resort).
    private static RGBAColor32 LinkColor(UiPalette p)
    {
        var c = p.Accent;
        for (int i = 0; i < LinkMaxSteps && RGBAColor32.ContrastRatio(c, p.PanelBg) < LinkMinContrast; i++)
        {
            c = RGBAColor32.Mix(c, p.BodyText, LinkStep);
        }
        return RGBAColor32.ContrastRatio(c, p.PanelBg) < LinkMinContrast ? p.BodyText : c;
    }
}
