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
        /// Honoured by BOTH painters. Console.Lib's <c>CellLayout</c> cuts because a cell surface measures
        /// in whole characters and has to; the pixel painter fits the run to its arranged rect through
        /// <see cref="TextFit.ForWidth"/>, which also implements <see cref="TextTrim.Shrink"/> (scale the
        /// run down) and <see cref="TextTrim.None"/> (let it overhang).
        /// <para>
        /// It is not cosmetic on either surface: <c>DrawText</c> starts at the rect edge and keeps going,
        /// so an unfitted over-wide run draws straight over its neighbour on whichever sizes happen not to
        /// fit.
        /// </para>
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
    /// <para>
    /// <b>Always a concrete number, whoever supplied it.</b> A caller that states no size gets one derived
    /// from the text beside it -- see <see cref="MatchesText"/> -- so a painter reads this field and never
    /// has to reconstruct a size of its own.
    /// </para>
    /// </param>
    public sealed record Icon(IconKind Kind, float Size = Icon.DefaultSize) : Content
    {
        /// <summary>
        /// The size a mark falls back to when nothing in scope names one: no text to match, and no size
        /// stated. Deliberately the same as <see cref="Text.FontSize"/>'s default, so a lone icon and a lone
        /// run are the same size for the same reason -- neither was told anything.
        /// </summary>
        public const float DefaultSize = 14f;

        /// <summary>
        /// Fraction of a text run's em that a mark beside it takes, when its size comes from that run
        /// (<see cref="MatchesText"/>).
        /// <para>
        /// It is an <b>x-height</b>, not a cap height. A kind inks the FULL square it declares where a glyph
        /// inks perhaps 80% of its em box, so a mark sized to cap height (~0.71 em on a humanist sans) reads
        /// distinctly heavier than the letters beside it; sized to the x-height it matches the visual weight
        /// of the lowercase body, which is what the eye reads as "the same size as that word". 0.54 is
        /// Noto Sans's x-height (0.536 em), and it is also where three chips in one consumer's window had
        /// independently converged by hand, at 7 units against a 13-unit label.
        /// </para>
        /// <para>
        /// A constant rather than a question put to the bound font, for two reasons that both have to hold:
        /// the size is resolved while the tree is being BUILT, before any font is bound or any measure
        /// context exists, and a cell surface has no x-height to report at all. The cost is that a face with
        /// an unusually small or large x-height is matched to the typical one rather than to itself.
        /// </para>
        /// </summary>
        public const float TextSizeRatio = 0.54f;

        /// <summary>Ink colour (default white), the same convention as <see cref="Text.Color"/>.</summary>
        public RGBAColor32 Color { get; init; } = new(0xff, 0xff, 0xff, 0xff);

        /// <summary>
        /// The caller stated no size, so <see cref="Size"/> was derived from a text run in the same
        /// container rather than declared -- <see cref="Builder.Icon"/> sets this, and the container
        /// factories (<see cref="Builder.HStack"/> and friends) do the deriving.
        /// <para>
        /// <b>Why the size cannot be the caller's to state.</b> A caret in a row next to a label is sized BY
        /// that label; every consumer call site was passing the same expression, spelled out a second time,
        /// and the two copies drift in silence -- nothing warns, and the mark simply looks wrong beside its
        /// own text. Worse, the fallback is unusable as a default: <see cref="DefaultSize"/> is a bare
        /// constant, so a consumer whose tree is authored in device pixels and takes the default gets a mark
        /// that does not scale with anything. Derived from the sibling run, the size is expressed in
        /// whatever unit that run is, so it is right under either convention without being told which.
        /// </para>
        /// <para>
        /// It records INTENT and stays set after resolution: <see cref="Size"/> is the resolved value either
        /// way, and this says where it came from -- which is what a layout dump needs in order to explain a
        /// mark nobody sized. The resolution reaches an icon that is a DIRECT child of the container holding
        /// the run (the run itself may be nested as deep as it likes); an icon wrapped in a container of its
        /// own is out of scope of the search and keeps <see cref="DefaultSize"/>, so state a size there.
        /// </para>
        /// </summary>
        public bool MatchesText { get; init; }
    }

    /// <summary>
    /// An editable single-line text field: the node IS the control.
    /// <para>
    /// A field can be expressed as a <see cref="Fill"/> plus a draw closure, and every consumer did exactly
    /// that before this leaf existed. The cost was never the closure -- it was the IDENTITY. A
    /// <see cref="Fill.Key"/> string is shared between the tree and a painter dictionary that nothing checks,
    /// so a key with no entry is a silently blank field rather than an error, and the closure re-states the
    /// font and size the tree already knows. <see cref="Fill"/> means "the app paints something arbitrary
    /// here", which describes a chart exactly and a text box not at all: DIR.Lib already owns a field's
    /// painting, its hit region, its focus semantics and its key handling.
    /// </para>
    /// <para>
    /// A painter meeting this leaf therefore renders the field AND registers a
    /// <see cref="HitResult.TextInputHit"/> over the arranged rect, which it cannot forget to do.
    /// Click-to-focus, blur-on-outside-click, Tab cycling (whose order is derived from region paint order,
    /// so it is the visual order automatically) and the I-beam cursor all follow from that one registration
    /// with no per-field wiring.
    /// </para>
    /// <para>
    /// <b>The node carries a reference to caller-owned mutable state.</b> That is the precedent
    /// <see cref="Node.OnClick"/> already sets by carrying a delegate closing over live state, and the tree is
    /// rebuilt per frame anyway. Ownership does not move: the consumer still owns the
    /// <see cref="TextInputState"/> and its commit wiring. It is also what makes fields created per camera or
    /// per OTA fall out for free -- they appear as hardware does, so they can never be statically declared
    /// controls the way a form designer emits them, but they are an ordinary loop in a per-frame tree.
    /// </para>
    /// </summary>
    /// <param name="State">The caller-owned field state: text, caret, selection and the commit callbacks.</param>
    /// <param name="FontSize">Text size in design units, as <see cref="Text.FontSize"/>.</param>
    public sealed record TextInput(TextInputState State, float FontSize = 14f) : Content
    {
        /// <summary>
        /// Palette for THIS field, or null for the shared <see cref="TextInputRenderer.Colors"/> -- the same
        /// per-call escape hatch <c>TextInputRenderer.Render</c> takes, for the field that genuinely differs
        /// (one inlaid in a toolbar chip too short to afford a fill, a border and a selection at once).
        /// </summary>
        public TextInputColors? Colors { get; init; }

        /// <summary>
        /// Measure the field as if it held this text, the same reservation
        /// <see cref="Text.WidthSample"/> makes and for a sharper reason: <b>a box that resizes while you
        /// type is a bug</b>. So the intrinsic width comes from this sample, or from
        /// <see cref="TextInputState.Placeholder"/> when it is null -- never from the live
        /// <see cref="TextInputState.Text"/>, which would relayout the row on every keystroke.
        /// <para>
        /// Intrinsic sizing is the fallback rather than the normal case: a field almost always takes its
        /// width from its row (<c>.Stretch()</c> inside a labelled row) and its height from the row's own
        /// height, so the measured size only decides anything under <c>Auto</c>.
        /// </para>
        /// </summary>
        public string? WidthSample { get; init; }

        /// <summary>
        /// A mark drawn INSIDE the field at its leading edge, with the text starting after it — the
        /// affordance that says what a box is for before anything has been typed in it. Null (the default)
        /// is a plain field, measured and painted exactly as before.
        /// <para>
        /// Inside rather than beside, which is the whole reason it belongs to the field: the field paints
        /// its own background and border, so a mark placed as a sibling in the row lands outside the box
        /// and reads as a button next to an input. Putting it here also means the room it needs is
        /// reserved by the MEASURE pass and left by the PAINT — see
        /// <see cref="TextInputRenderer.LeadingRoom"/>, which is the one place that number lives, for the
        /// same reason <see cref="TextInputRenderer.HorizontalPadding"/> is.
        /// </para>
        /// <para>
        /// A <see cref="TextInputState.Placeholder"/> says the same thing in words and is not a substitute: it is gone the
        /// moment the field has content, and this is exactly when a reader glancing back at a bar full of
        /// results needs to know which box was the query.
        /// </para>
        /// </summary>
        public IconKind? LeadingIcon { get; init; }
    }

    /// <summary>
    /// An app-drawn escape hatch (chart, sky map, custom widget). Carries only a minimum intrinsic
    /// size in design units; pair with <c>Star</c> sizing to fill available space. The painter draws it via an
    /// app <c>drawFill</c> callback, which receives this instance back -- so when one tree contains several
    /// <see cref="Fill"/> leaves (e.g. a panel with several charts), set <see cref="Key"/> to route each to its
    /// own draw closure (e.g. <c>map[fill.Key]?.Invoke(rect)</c>) without a central switch.
    /// <para>
    /// A text field used to be listed here as an example and is no longer one: it has its own
    /// <see cref="TextInput"/> leaf. This stays the escape hatch for genuinely bespoke content, which is a
    /// narrower set than it looks -- reach for it when no surface could know how to draw the thing.
    /// </para>
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

    /// <summary>
    /// A cross of two bars: "add one". The mark on a new-tab button, and the increment half of a stepper.
    /// <para>
    /// Unlike most of this set it has a perfectly safe ASCII spelling, so it looks like the one kind that
    /// did not need naming -- and it is the one where the pixel side gains the most. A typeset <c>+</c> is
    /// drawn by whichever face the host resolved, at that face's stroke weight, sitting on the TEXT
    /// baseline rather than centred in its box; two rectangles are crisp at 30 px, weight-matched to
    /// whatever sits beside them, and centred on the rect they were arranged into. Every consumer that
    /// wanted one had already reached that conclusion and was drawing its own two rectangles.
    /// </para>
    /// </summary>
    Plus,

    /// <summary>
    /// One bar: "remove one", the decrement half of a stepper and the mark on an unpin or collapse control.
    /// <para>
    /// The pair is why both earn their place: a stepper is <c>[-] value [+]</c> and needs the two marks to
    /// share a stroke weight and a centre line, which is exactly what two independently-drawn glyphs (or
    /// two hand-rolled rectangle sets) drift apart on. This is also the ONE kind that cannot ink its full
    /// square -- a horizontal bar has no height to give -- so it inks the full WIDTH and takes
    /// <see cref="Plus"/>'s bar thickness verbatim, which is what makes the two line up.
    /// </para>
    /// </summary>
    Minus,

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

    /// <summary>
    /// A lens with a handle: "search", or "this box is where you type a query".
    /// <para>
    /// It earns its place the way <see cref="Plus"/> did — by consumers already drawing their own. One had
    /// hand-rolled a lens from an ellipse and a line for a marquee-zoom tool, and wanted a second for the
    /// field its search bar is built around; on a cell surface the shape is one of the few pictograms a
    /// terminal font is genuinely relied on to carry (U+1F50D, with U+2315 as the narrow-cell fallback).
    /// </para>
    /// <para>
    /// Outlined rather than filled, unlike the carets: a filled blob at chip size is a dot with a stalk,
    /// and the ring is the whole reading. That makes it the one kind whose weight comes from a pen rather
    /// than from its coverage, so it thickens with size instead of staying a hairline.
    /// </para>
    /// </summary>
    Search,

    /// <summary>
    /// Four arrows from a common centre: "drag to move this", the pan tool every canvas app has.
    /// <para>
    /// Stroked rather than filled, like <see cref="Search"/> and unlike the carets: the mark reads by its
    /// arms reaching out, and a filled version is a blob with four bumps.
    /// </para>
    /// </summary>
    Pan,
    /// <summary>
    /// An I-beam: "this selects text". The text cursor, and a tool button's mark for the same.
    /// <para>
    /// It is a mark, not the platform cursor — <see cref="CursorKind.Text"/> is what a surface applies to
    /// the POINTER, and a tool palette needs the same idea as ink inside a button. The serifs are
    /// load-bearing: a bare vertical bar at chip size is a separator, which is the neighbour it must not be
    /// mistaken for.
    /// </para>
    /// </summary>
    IBeam,
}
