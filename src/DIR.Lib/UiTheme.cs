namespace DIR.Lib;

/// <summary>
/// Shared chrome colour roles for cross-surface UI. Surface-agnostic: the same
/// <see cref="RGBAColor32"/> values drive the Vulkan GPU renderer and the terminal
/// (Console.Lib maps each colour to nearest-SGR / truecolor via its <c>VtStyle</c>).
/// Consuming apps supply their own <see cref="UiTheme"/> instance; these roles are
/// the chrome the apps share, not the full palette (app/tab-specific colours -- sky
/// map, charts, overlays -- stay local to their owner).
/// </summary>
/// <remarks>
/// <para>
/// A <b>reference</b> type, and deliberately not the <c>readonly record struct</c> it used to be.
/// A record struct always has an implicit parameterless constructor that property initializers do
/// not run, so <c>default(UiPalette)</c> yields all-zero -- which for a palette is transparent
/// black, painted silently everywhere, with no exception and nothing on screen to say why. As a
/// <c>sealed record</c> the omission is a compile error (<c>required</c>) and a null is a clean
/// throw. It also grows without breaking call sites, which a positional record cannot, and it is
/// cheaper to pass than fifteen inline fields.
/// </para>
/// <para>
/// <b>Derive it when the theme MOVES, not per frame.</b> Building one allocates; a consumer that
/// rebuilds a palette (or anything projected from it, such as <see cref="TabBarColors"/>) inside
/// its draw call turns a once-per-theme-change cost into a per-frame one.
/// </para>
/// </remarks>
public sealed record UiPalette
{
    private readonly RGBAColor32? _separatorStrong;
    private readonly RGBAColor32? _headerText;
    private readonly RGBAColor32? _accentAlt;
    private readonly RGBAColor32? _focus;
    private readonly RGBAColor32? _success;

    // ---- surfaces, back to front -------------------------------------------------------------

    /// <summary>The window's own backdrop, behind every panel.</summary>
    public required RGBAColor32 ContentBg { get; init; }

    /// <summary>A panel, card or list sitting on <see cref="ContentBg"/>.</summary>
    public required RGBAColor32 PanelBg { get; init; }

    /// <summary>A header strip, tab bar or status bar.</summary>
    public required RGBAColor32 HeaderBg { get; init; }

    // ---- rules -------------------------------------------------------------------------------

    /// <summary>Hairline divider between regions.</summary>
    public required RGBAColor32 Separator { get; init; }

    /// <summary>
    /// The heavier of the two rule weights, for a division that carries more meaning than a row
    /// gap -- a chart axis, a section break. Defaults to <see cref="Separator"/>, so a palette with
    /// only one rule weight need not state it.
    /// </summary>
    public RGBAColor32 SeparatorStrong
    {
        get => _separatorStrong ?? Separator;
        init => _separatorStrong = value;
    }

    // ---- text --------------------------------------------------------------------------------

    /// <summary>Primary readable text.</summary>
    public required RGBAColor32 BodyText { get; init; }

    /// <summary>
    /// Secondary text: labels, units, hints. Anything the reader can skip.
    /// <para>
    /// Note this is a <i>de-emphasis</i> role, and a palette with no luminance headroom to spare
    /// cannot honour it. Where the whole ramp is squeezed into a narrow band, state the label in
    /// <see cref="BodyText"/> and let position or weight carry the hierarchy instead.
    /// </para>
    /// </summary>
    public required RGBAColor32 DimText { get; init; }

    /// <summary>
    /// Text on a header strip. Defaults to <see cref="Accent"/>, which is what most chrome wants
    /// and what the two roles collapsed to when they shared one field.
    /// </summary>
    public RGBAColor32 HeaderText
    {
        get => _headerText ?? Accent;
        init => _headerText = value;
    }

    // ---- marks -------------------------------------------------------------------------------

    /// <summary>
    /// The colour that says "this one": the active tab's stripe, a progress fill, a live value.
    /// Kept separate from <see cref="HeaderText"/> on purpose -- when they were one field an app
    /// could set a header colour and end up with no accent anywhere.
    /// </summary>
    public required RGBAColor32 Accent { get; init; }

    /// <summary>
    /// A second mark, for the cases that genuinely need two at once (a two-trace chart). Defaults
    /// to <see cref="Accent"/>, so a single-accent palette need not invent one -- such a palette
    /// must then separate the two by dash or weight rather than by hue.
    /// </summary>
    public RGBAColor32 AccentAlt
    {
        get => _accentAlt ?? Accent;
        init => _accentAlt = value;
    }

    /// <summary>Fill behind the selected row or item.</summary>
    public required RGBAColor32 Selection { get; init; }

    /// <summary>
    /// Keyboard focus ring. Distinct from <see cref="Selection"/> because focus and selection can
    /// sit on the same row, and a focus ring that matches the selection fill vanishes exactly
    /// there. Defaults to <see cref="Accent"/>.
    /// </summary>
    public RGBAColor32 Focus
    {
        get => _focus ?? Accent;
        init => _focus = value;
    }

    // ---- semantic ----------------------------------------------------------------------------

    // Severity is generic UI, not a notification concept -- a notification feed is merely the
    // first consumer. It lives here rather than in each app because the moment a palette has more
    // than one state these three must switch in lockstep with everything else, and holding them
    // outside means every consumer re-derives them per state by hand.

    /// <summary>Informational: something happened, nothing to do.</summary>
    public required RGBAColor32 Info { get; init; }

    /// <summary>Warning: worth a look.</summary>
    public required RGBAColor32 Warn { get; init; }

    /// <summary>Error: needs attention.</summary>
    public required RGBAColor32 Error { get; init; }

    /// <summary>
    /// Success: connected, healthy, done. Defaults to <see cref="Accent"/>, which is not a
    /// placeholder -- the conventional green is unavailable to some palettes (a dark-adaptation
    /// scheme cannot spend the green channel), and for those the accent IS the right positive
    /// mark. A palette with the headroom states a green and gets one.
    /// </summary>
    public RGBAColor32 Success
    {
        get => _success ?? Accent;
        init => _success = value;
    }

    // ---- derived -----------------------------------------------------------------------------

    /// <summary>
    /// Whether this palette paints on a dark ground, so a consumer can pick an overlay alpha, a
    /// shadow direction or an icon variant without being handed a separate flag.
    /// <para>
    /// Computed from <see cref="ContentBg"/> rather than stored, which is the point: a stored flag
    /// can disagree with the colours it describes, and this cannot.
    /// </para>
    /// </summary>
    public bool IsDark => ContentBg.Luminance < 0x80;
}

/// <summary>
/// Base (unscaled, pixel) layout metrics shared across chrome. Callers still multiply
/// by their own DPI scale; these are the logical base sizes.
/// </summary>
public readonly record struct UiMetrics(
    float BaseFontSize,
    float Padding,
    float HeaderHeight,
    float ItemHeight,
    float ButtonHeight);

/// <summary>
/// A complete UI theme: a colour <see cref="Palette"/> plus base <see cref="Metrics"/>.
/// One instance is the single source of truth for an app's chrome, replacing per-tab
/// duplicated colour/size constants.
/// </summary>
public sealed record UiTheme
{
    /// <summary>The colour roles.</summary>
    public required UiPalette Palette { get; init; }

    /// <summary>The base metrics.</summary>
    public required UiMetrics Metrics { get; init; }
}
