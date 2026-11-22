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
    private readonly RGBAColor32? _selectionText;
    private readonly RGBAColor32? _success;
    private readonly RGBAColor32? _raisedBg;
    private readonly RGBAColor32? _edge;
    private readonly RGBAColor32? _controlFill;
    private readonly RGBAColor32? _controlFillHover;
    private readonly RGBAColor32? _controlFillPressed;
    private readonly RGBAColor32? _controlEdge;

    // Steps (fractions towards white on a dark palette, black on a light one) of the neutral control roles.
    private const float ControlFillStep = 0.06f;
    private const float ControlHoverStep = 0.06f;
    private const float ControlEdgeStep = 0.08f;
    private const float ControlEdgeMinContrast = 3f;
    private const int ControlEdgeMaxSteps = 12;

    private RGBAColor32 StepFrom(RGBAColor32 from, float fraction)
        => RGBAColor32.Mix(from, IsDark ? new RGBAColor32(0xff, 0xff, 0xff, 0xff) : new RGBAColor32(0, 0, 0, 0xff), fraction);

    // ---- surfaces, back to front -------------------------------------------------------------

    /// <summary>The window's own backdrop, behind every panel.</summary>
    public required RGBAColor32 ContentBg { get; init; }

    /// <summary>A panel, card or list sitting on <see cref="ContentBg"/>.</summary>
    public required RGBAColor32 PanelBg { get; init; }

    /// <summary>A header strip, tab bar or status bar.</summary>
    public required RGBAColor32 HeaderBg { get; init; }

    /// <summary>
    /// A surface raised above <see cref="PanelBg"/>: a toast, a popover, a menu card. Defaults to
    /// <see cref="PanelBg"/>, so a palette written before this role existed compiles and looks as it did.
    /// </summary>
    public RGBAColor32 RaisedBg
    {
        get => _raisedBg ?? PanelBg;
        init => _raisedBg = value;
    }

    // ---- neutral controls --------------------------------------------------------------------

    /// <summary>
    /// Resting fill of a standard button. Defaults to one step off <see cref="PanelBg"/>: lighter when <see cref="IsDark"/>,
    /// darker when not. Never falls back to <see cref="Accent"/>.
    /// </summary>
    public RGBAColor32 ControlFill
    {
        get => _controlFill ?? StepFrom(PanelBg, ControlFillStep);
        init => _controlFill = value;
    }

    /// <summary>Fill under the pointer. Defaults to one step further from <see cref="ControlFill"/> in the same direction.</summary>
    public RGBAColor32 ControlFillHover
    {
        get => _controlFillHover ?? StepFrom(ControlFill, ControlHoverStep);
        init => _controlFillHover = value;
    }

    /// <summary>Fill while pressed. Defaults to one step further from <see cref="ControlFillHover"/> in the same direction.</summary>
    public RGBAColor32 ControlFillPressed
    {
        get => _controlFillPressed ?? StepFrom(ControlFillHover, ControlHoverStep);
        init => _controlFillPressed = value;
    }

    /// <summary>
    /// Edge of a standard button. Defaults to <see cref="ControlFill"/> stepped further in the same direction until it
    /// reaches 3:1 against <see cref="PanelBg"/> (at most 12 steps, then the last value; no loop waits on anything).
    /// </summary>
    public RGBAColor32 ControlEdge
    {
        get
        {
            if (_controlEdge is { } e) return e;
            var c = ControlFill;
            for (int i = 0; i < ControlEdgeMaxSteps && RGBAColor32.ContrastRatio(c, PanelBg) < ControlEdgeMinContrast; i++)
            {
                c = StepFrom(c, ControlEdgeStep);
            }
            return c;
        }
        init => _controlEdge = value;
    }

    // ---- rules -------------------------------------------------------------------------------

    /// <summary>The edge drawn round a raised surface. Defaults to <see cref="Separator"/>.</summary>
    public RGBAColor32 Edge
    {
        get => _edge ?? Separator;
        init => _edge = value;
    }

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
    /// Glyph colour drawn on the <see cref="Selection"/> fill by the text selection (<see cref="TextSelection{TSurface}"/>).
    /// Defaults to <see cref="BodyText"/>. Init-only with a default, so a palette written before it existed still compiles.
    /// </summary>
    public RGBAColor32 SelectionText
    {
        get => _selectionText ?? BodyText;
        init => _selectionText = value;
    }

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

    /// <summary>
    /// The ink that reads on <paramref name="fill"/>: <see cref="BodyText"/> or <see cref="ContentBg"/>, whichever has the
    /// higher <see cref="RGBAColor32.ContrastRatio"/> against it. The one rule for the text and icon on an
    /// <see cref="Accent"/> (or any other solid) fill, so a chosen segment or an on-state toggle reads in every palette
    /// and no call site picks a colour by eye. Ties go to <see cref="BodyText"/>.
    /// </summary>
    public RGBAColor32 InkOn(RGBAColor32 fill)
        => RGBAColor32.ContrastRatio(ContentBg, fill) > RGBAColor32.ContrastRatio(BodyText, fill) ? ContentBg : BodyText;

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

    /// <summary>
    /// The one-hue rule: true when the palette cannot tell severities apart by hue, so a consumer must not tint
    /// by severity (pilot's night palette, red on black, is the case). A colour counts as chromatic when its
    /// channel spread is at least 24; the reference hue is <see cref="BodyText"/>'s when that is chromatic, else the
    /// first chromatic severity's. The rule holds when every chromatic one of <see cref="Info"/>, <see cref="Warn"/>,
    /// <see cref="Error"/> and <see cref="Success"/> lies within 30 degrees of the reference. A palette with no
    /// chromatic severity at all is one-hue too. Computed, never stored, so it cannot disagree with the colours.
    /// </summary>
    public bool IsOneHue
    {
        get
        {
            const int MinChroma = 24;
            const float MaxHueDelta = 30f;
            Span<RGBAColor32> sev = [Info, Warn, Error, Success];
            float? reference = HueOf(BodyText, MinChroma);
            foreach (var c in sev)
            {
                var h = HueOf(c, MinChroma);
                if (h is null) continue;
                reference ??= h;
                var d = MathF.Abs(h.Value - reference.Value) % 360f;
                if (d > 180f) d = 360f - d;
                if (d > MaxHueDelta) return false;
            }
            return true;
        }
    }

    private static float? HueOf(RGBAColor32 c, int minChroma)
    {
        int max = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
        int min = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
        int chroma = max - min;
        if (chroma < minChroma) return null;
        float h;
        if (max == c.Red) h = ((c.Green - c.Blue) / (float)chroma) % 6f;
        else if (max == c.Green) h = (c.Blue - c.Red) / (float)chroma + 2f;
        else h = (c.Red - c.Green) / (float)chroma + 4f;
        h *= 60f;
        return h < 0 ? h + 360f : h;
    }
}

/// <summary>
/// Base (unscaled, pixel) layout metrics shared across chrome. Callers still multiply
/// by their own DPI scale; these are the logical base sizes.
/// </summary>
/// <remarks>
/// Positional, so a parameter added to the constructor is a binary break (the 9.1 lesson in CLAUDE.md): the five
/// parameters stay, and later fields are init-only properties whose default survives <c>default(UiMetrics)</c>
/// (a nullable backing field with a fallback getter). Pinned by <c>InputEventCompatibilityTests</c>.
/// </remarks>
public readonly record struct UiMetrics(
    float BaseFontSize,
    float Padding,
    float HeaderHeight,
    float ItemHeight,
    float ButtonHeight)
{
    private readonly float? _cornerRadius;
    private readonly float? _controlRadius;
    private readonly float? _controlEdgeWidth;
    private readonly float? _linkArrowGap;
    private readonly Layout.ShadowStyle? _shadow;

    /// <summary>The toast's key shadow: no offset across, 1 down, blur 2, black at 55%.</summary>
    public static Layout.ShadowStyle DefaultShadow { get; } = new() { OffsetX = 0, OffsetY = 1, Blur = 2, Color = new RGBAColor32(0, 0, 0, 140) };

    /// <summary>Corner radius of a raised surface, in design units. Defaults to 6.</summary>
    public float CornerRadius
    {
        get => _cornerRadius ?? 6f;
        init => _cornerRadius = value;
    }

    /// <summary>Corner radius of a button or other control, in design units. Defaults to 4.</summary>
    public float ControlRadius
    {
        get => _controlRadius ?? 4f;
        init => _controlRadius = value;
    }

    /// <summary>Width of a control's edge ring, in design units. Defaults to 1.</summary>
    public float ControlEdgeWidth
    {
        get => _controlEdgeWidth ?? 1f;
        init => _controlEdgeWidth = value;
    }

    /// <summary>Gap between a link's text and its trailing arrow, in design units. Defaults to 2.</summary>
    public float LinkArrowGap
    {
        get => _linkArrowGap ?? 2f;
        init => _linkArrowGap = value;
    }

    /// <summary>
    /// The shadow of a raised surface. Defaults to <see cref="DefaultShadow"/>; a colour with alpha 0 states none.
    /// </summary>
    public Layout.ShadowStyle Shadow
    {
        get => _shadow ?? DefaultShadow;
        init => _shadow = value;
    }
}

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

    private readonly ControlStyles? _controls;

    // The derived styles are cached outside the record, keyed on the instance: a record field would be copied by
    // `with` (a stale cache for the new palette or metrics) and compared by Equals.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UiTheme, ControlStyles> DerivedControls = new();

    /// <summary>
    /// The control styles. Defaults to <see cref="ControlStyles.For"/> of <see cref="Palette"/> and <see cref="Metrics"/>,
    /// derived once per instance (never carried over by <c>with</c>). Override one kind with <c>theme with { Controls = theme.Controls with { Standard = ... } }</c>.
    /// </summary>
    public ControlStyles Controls
    {
        get => _controls ?? DerivedControls.GetValue(this, static t => ControlStyles.For(t.Palette, t.Metrics));
        init => _controls = value;
    }
}
