namespace DIR.Lib.Layout;

/// <summary>
/// The paintable + hit-testable payload of a <see cref="Node.Leaf"/>. Surface-neutral: it says
/// <i>what</i> to measure/draw, not <i>how</i> -- a per-surface painter interprets the concrete record.
/// The engine only needs <see cref="Text"/>/<see cref="Box"/>/<see cref="Fill"/> to compute intrinsic
/// (Auto) sizes; <see cref="Node.Hit"/> is the click region the painter auto-binds to the arranged rect.
/// </summary>
public abstract record Content
{
    /// <summary>A text run. Intrinsic size = the measure context's glyph metrics (px) / char count (cells).</summary>
    public sealed record Text(string Value, float FontSize = 14f) : Content
    {
        /// <summary>Glyph colour (default white). Surface-neutral -- Vulkan uses it directly, the TUI maps it to the nearest SGR.</summary>
        public RGBAColor32 Color { get; init; } = new(0xff, 0xff, 0xff, 0xff);

        /// <summary>Horizontal alignment of the text within the leaf's arranged rect.</summary>
        public TextAlign HAlign { get; init; } = TextAlign.Near;

        /// <summary>
        /// Measure the node as if it held this text instead of <see cref="Value"/>. For a readout whose
        /// content changes while its box must not: a zoom percentage reserves the room "1000%" needs and
        /// stops the legend beside it shuffling sideways on every scroll notch.
        /// <para>
        /// Without it the caller measures a sample string itself, converts to a fixed width, and caches
        /// that against every input that could invalidate it — re-deriving what the measure pass does,
        /// in the one place that cannot see the font the painter will actually use. Pair it with
        /// <see cref="HAlign"/> = <see cref="TextAlign.Center"/>, or the shorter live value sits at one
        /// end of the room it reserved.
        /// </para>
        /// </summary>
        public string? WidthSample { get; init; }

        /// <summary>Vertical alignment of the text within the leaf's arranged rect.</summary>
        public TextAlign VAlign { get; init; } = TextAlign.Center;

        /// <summary>
        /// Which end to sacrifice when the run does not fit its arranged rect. Intrinsic to the run for the
        /// same reason <see cref="Color"/> and <see cref="HAlign"/> are: only the author knows which half
        /// carries the meaning. See <see cref="TextTrim"/>.
        /// <para>
        /// Honoured by painters that ellipsize. Console.Lib's <c>CellLayout</c> does, because a cell surface
        /// measures in whole characters and has to cut somewhere. The pixel painter currently does NOT
        /// ellipsize — an overlong run is clipped by its rect — so this is inert there until it grows one
        /// (<see cref="FontFallbackResolver.FitEllipsis"/> is the measure-driven primitive it would use, and
        /// is End-only today).
        /// </para>
        /// </summary>
        public TextTrim Trim { get; init; } = TextTrim.End;
    }

    /// <summary>A fixed-size piece (icon, swatch, separator, spacer) -- intrinsic size is <paramref name="Width"/> x <paramref name="Height"/> design units. The painter fills it only when <see cref="Color"/> is non-transparent, so a transparent Box is a pure spacer.</summary>
    public sealed record Box(float Width, float Height) : Content
    {
        /// <summary>Fill colour. Default transparent => the painter draws nothing (spacer).</summary>
        public RGBAColor32 Color { get; init; }
    }

    /// <summary>
    /// A small pictogram named by MEANING, so that each surface can draw it the way that surface actually
    /// can: the pixel painter constructs it from rectangles, a cell painter picks a block-element glyph.
    /// <para>
    /// A <see cref="Text"/> run carrying a symbol character looks simpler and is the wrong tool, because the
    /// two surfaces fail in opposite directions. On a pixel surface the glyph has to exist in the bound
    /// font, and a missing one arrives as .notdef -- an empty box exactly where the icon should be -- which
    /// is why apps that care draw these from rectangles instead. On a cell surface rectangles are not
    /// available at all, but the box-drawing and block-element ranges are precisely what a terminal font is
    /// relied on to carry. Naming the meaning rather than the drawing lets both be right at once.
    /// </para>
    /// </summary>
    /// <param name="Size">
    /// The mark's size in design units. This is both the intrinsic (Auto) size AND the size it is DRAWN at,
    /// centred in whatever rect it is arranged into and clamped by it -- so an icon in a taller button stays
    /// the size it asked for instead of growing to fill the button. That matters most beside a text run,
    /// where a mark scaled to its cell rather than to its declared size overshoots the word's cap height and
    /// reads as vertically misaligned even when the two are centred on the same row.
    /// </param>
    public sealed record Icon(IconKind Kind, float Size = 14f) : Content
    {
        /// <summary>Ink colour (default white), the same convention as <see cref="Text.Color"/>.</summary>
        public RGBAColor32 Color { get; init; } = new(0xff, 0xff, 0xff, 0xff);
    }

    /// <summary>
    /// An app-drawn escape hatch (chart, sky map, custom widget, text input). Carries only a minimum intrinsic
    /// size in design units; pair with <c>Star</c> sizing to fill available space. The painter draws it via an
    /// app <c>drawFill</c> callback, which receives this instance back -- so when one tree contains several
    /// <see cref="Fill"/> leaves (e.g. a panel with multiple inputs), set <see cref="Key"/> to route each to its
    /// own draw closure (e.g. <c>map[fill.Key]?.Invoke(rect)</c>) without a central switch.
    /// </summary>
    public sealed record Fill(float MinWidth = 0f, float MinHeight = 0f, string? Key = null) : Content;
}

/// <summary>
/// The pictograms a <see cref="Content.Icon"/> can name. Deliberately a CLOSED and tiny set: every kind
/// costs a drawing in the pixel painter AND a glyph choice in every cell painter, so a kind earns its place
/// by having a consumer on both. A one-off pictogram belongs in a <see cref="Content.Fill"/> the app draws
/// itself, which is what that escape hatch is for.
/// </summary>
public enum IconKind
{
    /// <summary>A 2x2 of squares: "lay these out as a grid of tiles".</summary>
    Grid,

    /// <summary>
    /// A solid triangle pointing up: "this opens upward", the mark on a control whose menu drops UP out
    /// of a bar at the foot of a window. <see cref="CaretDown"/> is the same mark inverted, for the
    /// opened state or a menu that drops down.
    /// <para>
    /// Filled rather than a chevron of two strokes because at the size a chip affords it -- ten pixels,
    /// often fewer -- a stroked mark is two hairlines with a hole between them, and the hole is the part
    /// that disappears first. Every consumer that wanted one was drawing its own triangle from raw
    /// vertices, which is the tell that the family was missing a member rather than that the mark was
    /// app-specific.
    /// </para>
    /// </summary>
    CaretUp,

    /// <summary><see cref="CaretUp"/> inverted: "this opens downward", or "this is already open".</summary>
    CaretDown,

    /// <summary>Three stacked bars: "lay these out as a list of rows".</summary>
    List,

    /// <summary>
    /// An <c>A</c> inside viewfinder corner brackets: "decide this for me". The camera convention for an
    /// automatic mode, which is where a reader most likely met it (every body prints some version of it), and
    /// it composes from the two things that are always safe: brackets are rectangles, and <c>A</c> is ASCII.
    /// The other common rendering wraps the A in cyclic arrows, which a ring cannot say legibly at icon size.
    /// </summary>
    Auto,

    /// <summary>
    /// A disc half filled and half outlined: "follow whatever the desktop is set to".
    /// <para>
    /// The three theme marks are one family and earn their place together: an app with a light/dark setting
    /// needs all three or none, and both surfaces can say each one. The divided disc is the long-standing
    /// contrast mark, and the outlined half is load-bearing rather than decoration -- a bare half-disc reads
    /// as a moon, which is the one neighbour it must not be confused with.
    /// </para>
    /// </summary>
    ThemeSystem,

    /// <summary>A rayed disc: "light, whatever the desktop says". The sun, drawn with the rays clear of the
    /// disc so the two read as separate marks rather than one soft blob at icon size.</summary>
    ThemeLight,

    /// <summary>A crescent: "dark, whatever the desktop says".</summary>
    ThemeDark,
}
