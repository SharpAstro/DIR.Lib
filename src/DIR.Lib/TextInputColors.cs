namespace DIR.Lib;

/// <summary>
/// Colour palette for <see cref="TextInputRenderer"/>. Defaults are the values the field has always
/// drawn, so a consumer that sets nothing looks exactly as before.
/// </summary>
/// <remarks>
/// Same shape and the same reason as <see cref="TabBarColors"/>: the renderer had eight
/// <c>private static readonly</c> literals, which are the first consumer's dark scheme frozen into a
/// shared widget. A themed app could restyle every surface it drew itself and still get a slate-blue
/// box in the middle of it, which is exactly what happened to TianWen's night mode.
/// </remarks>
public record TextInputColors
{
    /// <summary>Fill of an unfocused field. Default: slate (#282832).</summary>
    public RGBAColor32 Background { get; init; } = new(40, 40, 50, 255);

    /// <summary>Fill of the focused field, lifted so focus reads without a border change alone. Default: #323241.</summary>
    public RGBAColor32 BackgroundActive { get; init; } = new(50, 50, 65, 255);

    /// <summary>Border of an unfocused field. Default: mid slate (#505064).</summary>
    public RGBAColor32 Border { get; init; } = new(80, 80, 100, 255);

    /// <summary>Border of the focused field. Default: blue (#648cc8).</summary>
    public RGBAColor32 BorderActive { get; init; } = new(100, 140, 200, 255);

    /// <summary>The entered text. Default: near-white (#dcdcdc).</summary>
    public RGBAColor32 Text { get; init; } = new(220, 220, 220, 255);

    /// <summary>Placeholder text shown while the field is empty. Default: grey (#78788c).</summary>
    public RGBAColor32 Placeholder { get; init; } = new(120, 120, 140, 255);

    /// <summary>The blinking caret. Default: pale blue (#c8c8ff).</summary>
    public RGBAColor32 Cursor { get; init; } = new(200, 200, 255, 255);

    /// <summary>Fill behind selected text. Default: translucent blue (#3c5a96b4).</summary>
    public RGBAColor32 Selection { get; init; } = new(60, 90, 150, 180);

    /// <summary>
    /// Derives a field palette from the shared chrome roles in <paramref name="palette"/>, so an app
    /// that already holds a <see cref="UiTheme"/> drives its inputs from that one source instead of
    /// restating eight colours next to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A field sits ON a panel and must read as recessed into it, so the ground comes from
    /// <see cref="UiPalette.ContentBg"/> rather than <see cref="UiPalette.PanelBg"/>; focus lifts it
    /// halfway back toward the panel. Both borders and the caret come from the accent, which is what
    /// keeps focus legible on a palette whose accent is the only saturated colour it has.
    /// </para>
    /// <para>
    /// The caret takes the accent rather than the text colour deliberately. On a palette with a single
    /// hue to spend, a caret drawn in the text colour is invisible against the text it sits beside.
    /// </para>
    /// </remarks>
    public static TextInputColors FromPalette(UiPalette palette) => new()
    {
        Background = palette.ContentBg,
        BackgroundActive = Blend(palette.ContentBg, palette.PanelBg, 0.5f),
        Border = palette.Separator,
        BorderActive = palette.Accent,
        Text = palette.BodyText,
        Placeholder = palette.DimText,
        Cursor = palette.Accent,
        Selection = palette.Selection,
    };

    private static RGBAColor32 Blend(RGBAColor32 a, RGBAColor32 b, float t)
    {
        static byte Lerp(byte x, byte y, float u) => (byte)(x + ((y - x) * u));
        return new RGBAColor32(Lerp(a.Red, b.Red, t), Lerp(a.Green, b.Green, t), Lerp(a.Blue, b.Blue, t), a.Alpha);
    }
}
