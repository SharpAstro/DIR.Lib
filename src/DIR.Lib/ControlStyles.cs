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
/// How a themed on/off switch looks: a rounded track and a round knob. On the track is the accent with the knob at the
/// end; off it is the neutral control fill with the control edge ring and the knob at the start. The track is
/// <see cref="HeightScale"/> times <see cref="UiMetrics.ButtonHeight"/> high and <see cref="WidthScale"/> times its own
/// height wide, so it lines up with a button beside it. Values are palette colours and design units, never literals.
/// </summary>
public sealed record SwitchStyle
{
    /// <summary>Track fill when on.</summary>
    public required RGBAColor32 OnFill { get; init; }

    /// <summary>Track fill when on, under the pointer.</summary>
    public required RGBAColor32 OnHoverFill { get; init; }

    /// <summary>Track fill when on, while pressed.</summary>
    public required RGBAColor32 OnPressedFill { get; init; }

    /// <summary>Knob colour when on: the ink that reads on <see cref="OnFill"/>.</summary>
    public required RGBAColor32 OnKnob { get; init; }

    /// <summary>Track fill when off.</summary>
    public required RGBAColor32 OffFill { get; init; }

    /// <summary>Track fill when off, under the pointer.</summary>
    public required RGBAColor32 OffHoverFill { get; init; }

    /// <summary>Track fill when off, while pressed.</summary>
    public required RGBAColor32 OffPressedFill { get; init; }

    /// <summary>Knob colour when off.</summary>
    public required RGBAColor32 OffKnob { get; init; }

    /// <summary>Ring colour of the off track; null draws none.</summary>
    public RGBAColor32? OffEdge { get; init; }

    /// <summary>Width of the ring in design units.</summary>
    public required float EdgeWidth { get; init; }

    /// <summary>Colour of the label beside the switch.</summary>
    public required RGBAColor32 LabelInk { get; init; }

    /// <summary>Knob and label colour when disabled; null means the host dims them itself.</summary>
    public RGBAColor32? DisabledInk { get; init; }

    /// <summary>Track height as a multiple of <see cref="UiMetrics.ButtonHeight"/>. Defaults to 0.75.</summary>
    public float HeightScale { get; init; } = 0.75f;

    /// <summary>Track width as a multiple of its height. Defaults to 1.8.</summary>
    public float WidthScale { get; init; } = 1.8f;

    /// <summary>Gap between the track edge and the knob, in design units. Defaults to 3.</summary>
    public float KnobInset { get; init; } = 3f;

    // DSN-1 v4 (11.105): the states the sized switch draws. Init-only and optional, so a style written against 11.104 still
    // compiles; each falls back as its OrDerived accessor says, and ControlStyles.For sets them all from palette roles.

    /// <summary>Knob of an off switch while the pointer is over its track; null keeps <see cref="OffKnob"/>.</summary>
    public RGBAColor32? OffHoverKnob { get; init; }

    /// <summary>Outline of the off knob; null draws none (a light palette outlines it).</summary>
    public RGBAColor32? OffKnobEdge { get; init; }

    /// <summary>Track of a locked switch that is on; null is <see cref="OffFill"/> (flat, as a disabled switch is).</summary>
    public RGBAColor32? LockedOnFill { get; init; }

    /// <summary>Knob of a locked switch that is on; null is the disabled ink.</summary>
    public RGBAColor32? LockedOnKnob { get; init; }

    /// <summary>Track of a locked switch that is off; null is <see cref="OffFill"/>.</summary>
    public RGBAColor32? LockedOffFill { get; init; }

    /// <summary>Edge of a locked switch that is off; null is <see cref="OffEdge"/>.</summary>
    public RGBAColor32? LockedOffEdge { get; init; }

    /// <summary>Knob of a locked switch that is off; null is the disabled ink.</summary>
    public RGBAColor32? LockedOffKnob { get; init; }

    /// <summary>Edge (and mark) of a switch whose last change was refused; null is <see cref="LabelInk"/>.</summary>
    public RGBAColor32? RefusedEdge { get; init; }

    /// <summary>The ring of a switch that is asking for confirmation; null is <see cref="LabelInk"/>.</summary>
    public RGBAColor32? AskingRing { get; init; }

    /// <summary>The disabled knob and label ink: <see cref="DisabledInk"/>, else <see cref="OffKnob"/> halfway to <see cref="OffFill"/>.</summary>
    public RGBAColor32 DisabledInkOrDerived => DisabledInk ?? RGBAColor32.Mix(OffKnob, OffFill, 0.5f);

    /// <summary><see cref="OffHoverKnob"/>, else <see cref="OffKnob"/>.</summary>
    public RGBAColor32 OffHoverKnobOrDerived => OffHoverKnob ?? OffKnob;

    /// <summary><see cref="LockedOnFill"/>, else <see cref="OffFill"/>.</summary>
    public RGBAColor32 LockedOnFillOrDerived => LockedOnFill ?? OffFill;

    /// <summary><see cref="LockedOnKnob"/>, else the disabled ink.</summary>
    public RGBAColor32 LockedOnKnobOrDerived => LockedOnKnob ?? DisabledInkOrDerived;

    /// <summary><see cref="LockedOffFill"/>, else <see cref="OffFill"/>.</summary>
    public RGBAColor32 LockedOffFillOrDerived => LockedOffFill ?? OffFill;

    /// <summary><see cref="LockedOffEdge"/>, else <see cref="OffEdge"/>.</summary>
    public RGBAColor32? LockedOffEdgeOrDerived => LockedOffEdge ?? OffEdge;

    /// <summary><see cref="LockedOffKnob"/>, else the disabled ink.</summary>
    public RGBAColor32 LockedOffKnobOrDerived => LockedOffKnob ?? DisabledInkOrDerived;

    /// <summary><see cref="RefusedEdge"/>, else <see cref="LabelInk"/>.</summary>
    public RGBAColor32 RefusedEdgeOrDerived => RefusedEdge ?? LabelInk;

    /// <summary><see cref="AskingRing"/>, else <see cref="LabelInk"/>.</summary>
    public RGBAColor32 AskingRingOrDerived => AskingRing ?? LabelInk;

    /// <summary>
    /// The DSN-1 v4 switch from palette roles: on is the accent track with the lighter of body text and page as knob, off a
    /// <see cref="UiPalette.HeaderBg"/> track with a <see cref="UiPalette.SeparatorStrong"/> edge, the off knob lightening
    /// under the pointer, locked dimmed with its side kept, refused in Error and asking in Warn. The pressed fills come from
    /// the button styles.
    /// </summary>
    internal static SwitchStyle For(UiPalette p, ButtonStyle accent, ButtonStyle standard, float edgeWidth)
    {
        // The lighter of BodyText and ContentBg, unless that colour is chromatic (a one-hue palette), then the darker.
        var lighter = p.BodyText.RelativeLuminance >= p.ContentBg.RelativeLuminance ? p.BodyText : p.ContentBg;
        var darker = lighter == p.BodyText ? p.ContentBg : p.BodyText;
        var onKnob = p.IsOneHue ? darker : lighter;
        var offTrack = p.HeaderBg;
        // DSN-1 v4 wins over the 11.104 3:1 knob rule (the person's choice, 11.105): white on tianwen's orange is about
        // 2.9:1, and on and off are told apart by the knob's side and the track's fill, never by the knob's colour alone.
        return From(accent, standard) with
        {
            OnFill = p.Accent,
            OnHoverFill = RGBAColor32.Mix(p.Accent, onKnob, V4HoverStep),
            OnKnob = onKnob,
            OffFill = offTrack,
            OffHoverFill = RGBAColor32.Mix(offTrack, p.SeparatorStrong, V4OffHoverStep),
            OffEdge = p.SeparatorStrong,
            OffKnob = p.IsDark ? p.DimText : onKnob,
            OffHoverKnob = onKnob,
            OffKnobEdge = p.IsDark ? null : p.SeparatorStrong,
            LockedOnFill = RGBAColor32.Mix(p.Accent, p.PanelBg, V4LockedMix),
            LockedOnKnob = p.DimText,
            LockedOffFill = p.PanelBg,
            LockedOffEdge = p.Separator,
            LockedOffKnob = p.SeparatorStrong,
            RefusedEdge = p.Error,
            AskingRing = p.Warn,
            EdgeWidth = edgeWidth,
            LabelInk = p.BodyText,
            DisabledInk = p.DimText,
        };
    }

    // Fractions of the way to the other colour, not colours.
    private const float V4HoverStep = 0.12f;
    private const float V4OffHoverStep = 0.4f;
    private const float V4LockedMix = 0.55f;

    /// <summary>Derives a switch style from the accent and standard button styles.</summary>
    public static SwitchStyle From(ButtonStyle accent, ButtonStyle standard) => new()
    {
        OffKnob = OffKnobFor(standard),
        OnFill = accent.RestFill ?? accent.HoverFill,
        OnHoverFill = accent.HoverFill,
        OnPressedFill = accent.PressedFill,
        OnKnob = accent.Ink,
        OffFill = standard.RestFill ?? standard.HoverFill,
        OffHoverFill = standard.HoverFill,
        OffPressedFill = standard.PressedFill,
        OffEdge = standard.Edge,
        EdgeWidth = standard.EdgeWidth,
        LabelInk = standard.Ink,
        DisabledInk = standard.DisabledInk,
    };

    // The control edge, stepped towards the ink until it keeps 3:1 on the off track at rest, hovered and pressed
    // (the edge alone can fall just short on the hover fill). Bounded; the ink itself is the last resort.
    private static RGBAColor32 OffKnobFor(ButtonStyle standard)
    {
        var knob = standard.Edge ?? standard.Ink;
        var rest = standard.RestFill ?? standard.HoverFill;
        for (int i = 0; i < 10 && Least(knob, rest, standard) < 3f; i++)
        {
            knob = RGBAColor32.Mix(knob, standard.Ink, 0.1f);
        }
        return Least(knob, rest, standard) < 3f ? standard.Ink : knob;
    }

    private static float Least(RGBAColor32 knob, RGBAColor32 rest, ButtonStyle standard)
        => MathF.Min(RGBAColor32.ContrastRatio(knob, rest),
            MathF.Min(RGBAColor32.ContrastRatio(knob, standard.HoverFill), RGBAColor32.ContrastRatio(knob, standard.PressedFill)));
}

/// <summary>
/// How a themed slider looks: the part of the track up to the thumb (<see cref="Fill"/>), the rest of the track
/// (<see cref="Track"/>) and a round thumb with an accent centre and a ring. Values are palette colours and design units.
/// </summary>
public sealed record SliderStyle
{
    /// <summary>The track from its start up to the thumb.</summary>
    public required RGBAColor32 Fill { get; init; }

    /// <summary>The track beyond the thumb.</summary>
    public required RGBAColor32 Track { get; init; }

    /// <summary>The thumb's centre disc.</summary>
    public required RGBAColor32 ThumbCentre { get; init; }

    /// <summary>The thumb's ring.</summary>
    public required RGBAColor32 ThumbRing { get; init; }

    /// <summary>Width of the thumb's ring in design units.</summary>
    public required float ThumbRingWidth { get; init; }

    /// <summary>The thumb's diameter, and so the slider's height, in design units.</summary>
    public required float ThumbSize { get; init; }
}

/// <summary>
/// How a themed dropdown looks: the closed box (fill, ring, radius, ink) and the menu it opens (a raised surface with
/// rows in <see cref="RowInk"/> and a hover fill). Values are palette colours and design units, never literals.
/// </summary>
public sealed record DropdownStyle
{
    /// <summary>Fill of the closed box.</summary>
    public required RGBAColor32 BoxFill { get; init; }

    /// <summary>Fill of the closed box under the pointer.</summary>
    public required RGBAColor32 BoxHoverFill { get; init; }

    /// <summary>Ring colour of the closed box.</summary>
    public required RGBAColor32 BoxEdge { get; init; }

    /// <summary>Width of the box's ring in design units.</summary>
    public required float EdgeWidth { get; init; }

    /// <summary>Corner radius of the closed box in design units.</summary>
    public required float BoxRadius { get; init; }

    /// <summary>Horizontal padding of the closed box in design units.</summary>
    public required float PaddingX { get; init; }

    /// <summary>Text and caret colour of the closed box.</summary>
    public required RGBAColor32 BoxInk { get; init; }

    /// <summary>Fill of the menu: a raised surface.</summary>
    public required RGBAColor32 MenuBg { get; init; }

    /// <summary>Corner radius of the menu in design units.</summary>
    public required float MenuRadius { get; init; }

    /// <summary>Shadow of the menu.</summary>
    public required Layout.ShadowStyle MenuShadow { get; init; }

    /// <summary>Text colour of a menu row.</summary>
    public required RGBAColor32 RowInk { get; init; }

    /// <summary>Fill of the menu row under the pointer.</summary>
    public required RGBAColor32 RowHover { get; init; }
}

/// <summary>
/// How a themed setting row looks: a whole card row on the panel (<see cref="RestFill"/>), a hover fill, the corner
/// radius, the padding and gaps, and the title and subtitle inks. Values are palette colours and design units.
/// </summary>
public sealed record SettingRowStyle
{
    /// <summary>Fill of the row at rest.</summary>
    public required RGBAColor32 RestFill { get; init; }

    /// <summary>Fill of the row under the pointer or with the keyboard.</summary>
    public required RGBAColor32 HoverFill { get; init; }

    /// <summary>Fill of the row while pressed.</summary>
    public required RGBAColor32 PressedFill { get; init; }

    /// <summary>Corner radius in design units.</summary>
    public required float Radius { get; init; }

    /// <summary>Horizontal padding in design units.</summary>
    public required float PaddingX { get; init; }

    /// <summary>Vertical padding in design units.</summary>
    public required float PaddingY { get; init; }

    /// <summary>Gap between the text block and the trailing mark, and between title and subtitle, in design units.</summary>
    public required float Gap { get; init; }

    /// <summary>Title colour.</summary>
    public required RGBAColor32 TitleInk { get; init; }

    /// <summary>Subtitle colour.</summary>
    public required RGBAColor32 SubtitleInk { get; init; }

    /// <summary>Colour of the trailing chevron, caret or arrow.</summary>
    public required RGBAColor32 MarkInk { get; init; }
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
    // The thumb is as wide as a button's side padding times this (a slider has no ButtonHeight to follow when derived
    // without metrics); For states it from the metrics instead.
    private const float SliderThumbPaddingScale = 3f;
    private const float SliderThumbHeightScale = 0.6f;

    /// <summary>The primary action: filled with the accent, no edge.</summary>
    public required ButtonStyle Accent { get; init; }

    /// <summary>The ordinary button: neutral control fill with an edge.</summary>
    public required ButtonStyle Standard { get; init; }

    /// <summary>A quiet button: no fill at rest, neutral fill on hover.</summary>
    public required ButtonStyle Subtle { get; init; }

    /// <summary>An inline link.</summary>
    public required HyperlinkStyle Hyperlink { get; init; }

    /// <summary>
    /// A destroying action's rest look: the standard button with its label in <see cref="UiPalette.Error"/>. Init-only and
    /// not required, so an existing <c>new ControlStyles { ... }</c> still compiles; when unset, <see cref="DestructiveOrDerived"/>
    /// is <see cref="Standard"/> (a bare <c>ControlStyles</c> has no Error role to colour it with). <see cref="For"/> sets it.
    /// </summary>
    public ButtonStyle? Destructive { get; init; }

    /// <summary>The destroying button style: <see cref="Destructive"/> when set, else <see cref="Standard"/>.</summary>
    public ButtonStyle DestructiveOrDerived => Destructive ?? Standard;

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

    /// <summary>
    /// The slider. Init-only and not required, so an existing <c>new ControlStyles { ... }</c> still compiles; when unset,
    /// <see cref="SliderOrDerived"/> derives it from <see cref="Accent"/> and <see cref="Standard"/>. <see cref="For"/> sets it.
    /// </summary>
    public SliderStyle? Slider { get; init; }

    /// <summary>
    /// The on/off switch. Init-only and not required; when unset, <see cref="SwitchOrDerived"/> derives it from
    /// <see cref="Accent"/> and <see cref="Standard"/>. <see cref="For"/> sets it.
    /// </summary>
    public SwitchStyle? Switch { get; init; }

    /// <summary>The switch style: <see cref="Switch"/> when set, else derived from <see cref="Accent"/> and <see cref="Standard"/>.</summary>
    public SwitchStyle SwitchOrDerived => Switch ?? SwitchStyle.From(Accent, Standard);

    /// <summary>
    /// The dropdown. Init-only and not required; when unset, <see cref="DropdownOrDerived"/> derives it from
    /// <see cref="Standard"/> and its host's raised surface is the panel. <see cref="For"/> sets it.
    /// </summary>
    public DropdownStyle? Dropdown { get; init; }

    /// <summary>
    /// The setting row. Init-only and not required; when unset, <see cref="SettingRowOrDerived"/> derives it from
    /// <see cref="Standard"/> (its hover and pressed fills) over the standard fill. <see cref="For"/> sets it.
    /// </summary>
    public SettingRowStyle? SettingRow { get; init; }

    /// <summary>The setting row style: <see cref="SettingRow"/> when set, else derived from <see cref="Standard"/>.</summary>
    public SettingRowStyle SettingRowOrDerived => SettingRow ?? DeriveSettingRow(Standard,
        Standard.RestFill ?? Standard.HoverFill, Standard.Ink, Standard.DisabledInk ?? Standard.Ink, Standard.Radius, Standard.PaddingX, Standard.PaddingX / 2f);

    /// <summary>The slider style: <see cref="Slider"/> when set, else derived from <see cref="Accent"/> and <see cref="Standard"/>.</summary>
    public SliderStyle SliderOrDerived => Slider ?? DeriveSlider(Accent, Standard, Standard.PaddingX * SliderThumbPaddingScale);

    /// <summary>The dropdown style: <see cref="Dropdown"/> when set, else derived from <see cref="Standard"/> (the menu is the box's fill).</summary>
    public DropdownStyle DropdownOrDerived => Dropdown ?? DeriveDropdown(Standard, Standard.RestFill ?? Standard.HoverFill, Standard.Radius, UiMetrics.DefaultShadow);

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
            Destructive = standardStyle with { Ink = ErrorInk(palette, standardStyle) },
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
            Switch = SwitchStyle.For(palette, accentStyle, standardStyle, metrics.ControlEdgeWidth),
            Slider = new SliderStyle
            {
                Fill = palette.Accent,
                Track = palette.ControlFill,
                ThumbCentre = palette.Accent,
                ThumbRing = palette.ControlEdge,
                ThumbRingWidth = metrics.ControlEdgeWidth * 2f,
                ThumbSize = metrics.ButtonHeight * SliderThumbHeightScale,
            },
            Dropdown = DeriveDropdown(standardStyle, palette.RaisedBg, metrics.CornerRadius, metrics.Shadow),
            SettingRow = DeriveSettingRow(standardStyle, palette.PanelBg, palette.BodyText, palette.DimText, metrics.ControlRadius, metrics.Padding * 2f, metrics.Padding),
        };
    }

    private static SettingRowStyle DeriveSettingRow(ButtonStyle standard, RGBAColor32 rest, RGBAColor32 title, RGBAColor32 subtitle,
        float radius, float paddingX, float paddingY) => new()
    {
        RestFill = rest,
        HoverFill = standard.HoverFill,
        PressedFill = standard.PressedFill,
        Radius = radius,
        PaddingX = paddingX,
        PaddingY = paddingY,
        Gap = paddingY,
        TitleInk = title,
        SubtitleInk = subtitle,
        MarkInk = subtitle,
    };

    private static SliderStyle DeriveSlider(ButtonStyle accent, ButtonStyle standard, float thumbSize) => new()
    {
        Fill = accent.RestFill ?? accent.HoverFill,
        Track = standard.RestFill ?? standard.HoverFill,
        ThumbCentre = accent.RestFill ?? accent.HoverFill,
        ThumbRing = standard.Edge ?? standard.HoverFill,
        ThumbRingWidth = standard.EdgeWidth * 2f,
        ThumbSize = thumbSize,
    };

    private static DropdownStyle DeriveDropdown(ButtonStyle standard, RGBAColor32 menuBg, float menuRadius, Layout.ShadowStyle shadow) => new()
    {
        BoxFill = standard.RestFill ?? standard.HoverFill,
        BoxHoverFill = standard.HoverFill,
        BoxEdge = standard.Edge ?? standard.HoverFill,
        EdgeWidth = standard.EdgeWidth,
        BoxRadius = standard.Radius,
        PaddingX = standard.PaddingX,
        BoxInk = standard.Ink,
        MenuBg = menuBg,
        MenuRadius = menuRadius,
        MenuShadow = shadow,
        RowInk = standard.Ink,
        RowHover = standard.HoverFill,
    };

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

    // Error as the label of a destroying button, when it reaches 4.5:1 on the button's three fills, else stepped towards
    // BodyText until it does (bounded; BodyText itself is the last resort), as LinkColor does for the accent.
    private static RGBAColor32 ErrorInk(UiPalette p, ButtonStyle standard)
    {
        var c = p.Error;
        for (int i = 0; i < LinkMaxSteps && LeastContrast(c, standard) < LinkMinContrast; i++)
        {
            c = RGBAColor32.Mix(c, p.BodyText, LinkStep);
        }
        return LeastContrast(c, standard) < LinkMinContrast ? p.BodyText : c;
    }

    private static float LeastContrast(RGBAColor32 c, ButtonStyle s)
        => MathF.Min(RGBAColor32.ContrastRatio(c, s.RestFill ?? s.HoverFill),
            MathF.Min(RGBAColor32.ContrastRatio(c, s.HoverFill), RGBAColor32.ContrastRatio(c, s.PressedFill)));

    private static float LeastContrast(RGBAColor32 c, UiPalette p)
        => MathF.Min(RGBAColor32.ContrastRatio(c, p.PanelBg), RGBAColor32.ContrastRatio(c, p.ContentBg));
}
