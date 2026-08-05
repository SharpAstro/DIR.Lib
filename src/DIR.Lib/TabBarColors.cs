namespace DIR.Lib;

/// <summary>
/// Colour palette for <see cref="TabBar"/>. Defaults are the values the bar has always drawn, so a
/// consumer that sets nothing looks exactly as before.
/// </summary>
/// <remarks>
/// Split the way a themeable palette has to be: the surfaces and the text carry the theme, the accent
/// does not. <see cref="ActiveAccent"/> says "this is the tab you are on", and that meaning is the same
/// on a light strip as on a dark one — running it through a theme changes what it communicates rather
/// than how it reads. A consumer wanting a light bar overrides the six surface and text colours and
/// leaves the accent alone.
/// </remarks>
public record TabBarColors
{
    /// <summary>Fill behind the whole strip, including past the last tab. Default: near-black (#14141c).</summary>
    public RGBAColor32 BarBackground { get; init; } = new(0x14, 0x14, 0x1c, 0xff);

    /// <summary>Fill of the active tab. Default: lifted slate (#2c2c3c).</summary>
    public RGBAColor32 ActiveBackground { get; init; } = new(0x2c, 0x2c, 0x3c, 0xff);

    /// <summary>Fill of every other tab. Default: recessed slate (#1c1c26).</summary>
    public RGBAColor32 InactiveBackground { get; init; } = new(0x1c, 0x1c, 0x26, 0xff);

    /// <summary>Rule between tabs and along the bar's bottom edge. Default: mid slate (#3a3a48).</summary>
    public RGBAColor32 Separator { get; init; } = new(0x3a, 0x3a, 0x48, 0xff);

    /// <summary>Strip marking the active tab. Default: blue (#4488ff). Semantic, not decorative — see
    /// the remarks on <see cref="TabBarColors"/> before theming it.</summary>
    public RGBAColor32 ActiveAccent { get; init; } = new(0x44, 0x88, 0xff, 0xff);

    /// <summary>Label of the active tab. Default: near-white (#f0f0f0).</summary>
    public RGBAColor32 ActiveText { get; init; } = new(0xf0, 0xf0, 0xf0, 0xff);

    /// <summary>Label of every other tab, dimmed so the active one reads first. Default: grey (#9a9aa6).</summary>
    public RGBAColor32 InactiveText { get; init; } = new(0x9a, 0x9a, 0xa6, 0xff);

    /// <summary>The per-tab close mark. Default: light grey (#c0c0c8).</summary>
    public RGBAColor32 CloseMark { get; init; } = new(0xc0, 0xc0, 0xc8, 0xff);

    /// <summary>
    /// Derives a bar palette from the shared chrome roles in <paramref name="palette"/>, so an app that
    /// already holds a <see cref="UiTheme"/> drives the bar from that one source instead of restating
    /// eight colours next to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="UiPalette"/> names two chrome surfaces, and the bar draws three: strip, idle tab and
    /// active tab. The strip and idle tabs therefore share <see cref="UiPalette.PanelBg"/> and are told
    /// apart by the separator the bar already rules between them, while the active tab takes
    /// <see cref="UiPalette.HeaderBg"/> — it is the header of the content beneath it, which is what that
    /// role means. Inventing a third tone by blending the two was the alternative and it would put a
    /// colour on screen that the app's theme never chose.
    /// </para>
    /// <para>
    /// <see cref="ActiveAccent"/> is deliberately NOT taken from the palette; it keeps its default for
    /// the reason given on <see cref="TabBarColors"/>. Anything here can still be overridden after the
    /// fact — <c>FromPalette(p) with { InactiveBackground = … }</c> — which is why this is a record.
    /// </para>
    /// </remarks>
    public static TabBarColors FromPalette(UiPalette palette) => new()
    {
        BarBackground = palette.PanelBg,
        InactiveBackground = palette.PanelBg,
        ActiveBackground = palette.HeaderBg,
        Separator = palette.Separator,
        ActiveText = palette.HeaderText,
        InactiveText = palette.DimText,
        CloseMark = palette.BodyText,
    };
}
