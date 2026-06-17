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
}
