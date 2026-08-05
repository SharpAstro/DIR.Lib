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
}
