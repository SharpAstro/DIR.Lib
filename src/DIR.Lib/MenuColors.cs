namespace DIR.Lib;

/// <summary>
/// Default color palette for the vertical menu widget. Defaults match VkMenuWidget's original
/// palette so the migration is a visual no-op on the GPU path.
/// </summary>
public record MenuColors
{
    /// <summary>Color for the title text. Default: warm orange-white (#ffce9e).</summary>
    public RGBAColor32 TitleColor { get; init; } = new(0xff, 0xce, 0x9e, 0xff);

    /// <summary>Color for the prompt text. Default: light grey (#dddddd).</summary>
    public RGBAColor32 PromptColor { get; init; } = new(0xdd, 0xdd, 0xdd, 0xff);

    /// <summary>Color for unselected item text. Default: mid-grey (#cccccc).</summary>
    public RGBAColor32 ItemColor { get; init; } = new(0xcc, 0xcc, 0xcc, 0xff);

    /// <summary>Background fill for the selected item row. Default: slate blue (#305090).</summary>
    public RGBAColor32 SelectedBackground { get; init; } = new(0x30, 0x50, 0x90, 0xff);

    /// <summary>Foreground text for the selected item row. Default: gold (#ffd700).</summary>
    public RGBAColor32 SelectedForeground { get; init; } = new(0xff, 0xd7, 0x00, 0xff);

    /// <summary>
    /// Project a <see cref="UiPalette"/> onto the menu's roles, so a themed app drives the menu
    /// from the same source as its panels instead of living with the defaults above -- which are
    /// <c>VkMenuWidget</c>'s original scheme and belong to no palette at all.
    /// </summary>
    /// <remarks>
    /// The title takes <see cref="UiPalette.Accent"/> and the selected row keeps a separate
    /// foreground, because a selection fill dark enough to sit under body text and a mark bright
    /// enough to be seen are different jobs. Derive when the theme MOVES, not per frame.
    /// </remarks>
    public static MenuColors FromPalette(UiPalette palette) => new()
    {
        TitleColor = palette.Accent,
        PromptColor = palette.BodyText,
        ItemColor = palette.DimText,
        SelectedBackground = palette.Selection,
        SelectedForeground = palette.BodyText,
    };
}
