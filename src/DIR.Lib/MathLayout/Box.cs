

namespace DIR.Lib.MathLayout;

/// <summary>
/// A rectangular box with a baseline, in the TeX sense:
/// <list type="bullet">
///   <item><c>Width</c>: total horizontal extent in pixels.</item>
///   <item><c>Height</c>: pixels from the box's top edge down to the baseline
///     (i.e. the "ascent" plus everything that sits above the baseline).</item>
///   <item><c>Depth</c>: pixels from the baseline down to the bottom edge
///     (i.e. the "descent" — what hangs below for letters like 'g', or
///     what a denominator drops below the fraction bar).</item>
/// </list>
/// The total visual height is <c>Height + Depth</c>. Boxes always paint
/// themselves relative to a (penX, baselineY) the parent provides; sizing is
/// computed eagerly so parents can lay out children before any rasterization.
/// </summary>
public abstract class Box
{
    /// <summary>Horizontal extent, pixels.</summary>
    public abstract float Width { get; }

    /// <summary>Pixels above the baseline.</summary>
    public abstract float Height { get; }

    /// <summary>Pixels below the baseline.</summary>
    public abstract float Depth { get; }

    /// <summary>Total visual height (= Height + Depth).</summary>
    public float TotalHeight => Height + Depth;

    /// <summary>
    /// Paint this box into <paramref name="renderer"/> with the box's left
    /// edge at <paramref name="penX"/> and the baseline at
    /// <paramref name="baselineY"/>. The box is allowed to occupy the
    /// rectangle [penX, penX+Width] × [baselineY-Height, baselineY+Depth].
    /// </summary>
    public abstract void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style);
}

/// <summary>
/// Rendering parameters threaded through the box layout. Kept as a record so
/// callers can produce variants (smaller font for scripts, for example)
/// without mutating shared state.
///
/// <para>Pixel-valued math metrics (<see cref="AxisHeight"/>,
/// <see cref="FractionRuleThickness"/>, <see cref="RadicalRuleThickness"/>)
/// are read from the font's OpenType MATH table when available, falling back
/// to TeX-style ratios of <see cref="FontSize"/> when the font has no MATH
/// table. This means a BoxStyle pointed at a math font (STIX Two, Latin Modern
/// Math, Cambria Math) lays out exactly to that font's design, while a
/// BoxStyle pointed at a general-purpose UI font (DejaVu, Roboto) still gets
/// reasonable defaults — the parametric fallback paths in
/// <see cref="BracketBox"/> / <see cref="SqrtBox"/> use the same metrics.</para>
/// </summary>
public sealed record BoxStyle(string FontPath, float FontSize, RGBAColor32 Foreground)
{
    /// <summary>
    /// Shared rasterizer used for all metric lookups (AxisHeight,
    /// RadicalRuleThickness, FractionRuleThickness) and for stretchy delimiter
    /// rasterization in <see cref="StretchyVerticalBox"/>. Single instance so
    /// the per-font OpenType cache is hit across every BoxStyle.
    /// </summary>
    internal static readonly ManagedFontRasterizer SharedRasterizer = new();

    public BoxStyle(string fontPath, float fontSize)
        : this(fontPath, fontSize, new RGBAColor32(255, 255, 255, 255))
    { }

    /// <summary>Smaller-em-size variant used for super/subscripts.</summary>
    public BoxStyle Smaller(float scale = 0.7f) => this with { FontSize = FontSize * scale };

    /// <summary>Stroke thickness in pixels for generic strokes (parametric
    /// bracket strokes, etc.) when no font-specific value applies. Math
    /// rules use the more specific <see cref="FractionRuleThickness"/> /
    /// <see cref="RadicalRuleThickness"/> instead.</summary>
    public float RuleThickness => MathF.Max(1f, FontSize / 18f);

    /// <summary>The "ex height" — used for vertical positioning of operators.</summary>
    public float ExHeight => FontSize * 0.5f;

    /// <summary>
    /// Distance (pixels, positive up) from the baseline to the math axis —
    /// the level on which fraction bars, '+' / '=' / '−' centres, and big-
    /// operator centres sit.
    ///
    /// <para><b>Why this is a magic number, not <c>MathConstants.AxisHeight</c>:</b>
    /// MATH.AxisHeight in real fonts (DejaVu = ~13.7%) sits notably lower than
    /// where text-style operator glyphs ('=', '+', '−') actually centre — those
    /// are designed assuming axis ≈ ex-height/2 ≈ <c>FontSize * 0.25</c>. Using
    /// the true MATH.AxisHeight without first wrapping operator glyphs in an
    /// axis-centring box (so '=' shifts down to meet the bar) decouples the
    /// fraction bar from '=' visually. Until that wrapper exists, structural
    /// layout pins to the same magic number that GlyphBox-rendered operators
    /// naturally sit at.</para>
    /// </summary>
    public float AxisHeight => FontSize * 0.25f;

    /// <summary>
    /// Default fraction-rule thickness (pixels). Read from
    /// <c>MathConstants.FractionRuleThickness</c> when the font ships a MATH
    /// table; falls back to <see cref="RuleThickness"/> otherwise. The MATH
    /// value matches the font's stem thickness — designed to harmonise with
    /// the surrounding glyphs — so on math fonts this is visibly better than
    /// the generic <c>FontSize / 18</c> heuristic.
    /// </summary>
    public float FractionRuleThickness
    {
        get
        {
            var info = SharedRasterizer.GetMathConstants(FontPath);
            if (info is null) return RuleThickness;
            var px = info.Value.constants.FractionRuleThickness * FontSize / info.Value.unitsPerEm;
            // Always render at least one pixel so a small font size doesn't
            // collapse the bar to zero (matches RuleThickness's lower bound).
            return MathF.Max(1f, px);
        }
    }

    /// <summary>
    /// Default radical (sqrt) vinculum thickness (pixels). Read from
    /// <c>MathConstants.RadicalRuleThickness</c> when the font ships a MATH
    /// table; falls back to <see cref="RuleThickness"/> otherwise.
    /// </summary>
    public float RadicalRuleThickness
    {
        get
        {
            var info = SharedRasterizer.GetMathConstants(FontPath);
            if (info is null) return RuleThickness;
            var px = info.Value.constants.RadicalRuleThickness * FontSize / info.Value.unitsPerEm;
            return MathF.Max(1f, px);
        }
    }

    /// <summary>
    /// Font size (pixels) to render a display-style big operator (∫,
    /// ∑, ∏, ⋃, ∮, …) at, derived from the font's OpenType MATH
    /// <c>DisplayOperatorMinHeight</c> constant — the minimum height
    /// the spec asks display-style operators to reach. We convert from
    /// FUnits to pixels at the surrounding em and feed that as a font
    /// size to <see cref="GlyphBox"/>, which is approximately equivalent
    /// to "draw the operator's base glyph at the height the font wants
    /// for displaystyle". STIX gives ~2.4em, MathJax matches; body fonts
    /// without a MATH table fall back to 1.5em — same heuristic the
    /// scenes used before this property was wired up.
    ///
    /// <para>This is a heuristic: properly, displaystyle operators
    /// should be picked from <c>MathVariants</c> rather than scaled, so
    /// the font designer's specific large-operator glyph is used. Until
    /// <see cref="StretchyVerticalBox"/> handles big-operator dispatch
    /// for ordinary integrals, this property is what scenes use.</para>
    /// </summary>
    public float DisplayOperatorFontSize
    {
        get
        {
            // Always at least 1.5·em — a sanity floor for fonts whose
            // MATH table sets DisplayOperatorMinHeight to a value
            // smaller than the surrounding text (DejaVu's value works
            // out to less than 1·em in pixels at our render sizes).
            // Without the floor, the "big operator" ends up smaller
            // than the surrounding glyphs, making the script-size
            // bounds look enormous next to it.
            //
            // 1.3× scale on the font's spec value bumps the threshold
            // up enough to pick the more cursive next-up variant in
            // fonts whose chain is set up for MathJax-style displaystyle
            // (STIX2's ∫ has ~2.15em as MinHeight, and 1.3× lands on the
            // cursive variant the spec's bare value misses). The spec's
            // value is the *minimum*; designers expect typesetters to
            // go larger for displaystyle. Going much higher (1.8+) skips
            // past the curly variant to a taller-but-thinner one that
            // looks less cursive again.
            const float displayScale = 1.3f;
            var floor = FontSize * 1.5f;
            var info = SharedRasterizer.GetMathConstants(FontPath);
            if (info is null) return floor;
            ushort minH = info.Value.constants.DisplayOperatorMinHeight;
            if (minH == 0) return floor;
            return MathF.Max(floor, displayScale * FontSize * minH / info.Value.unitsPerEm);
        }
    }

    /// <summary>
    /// Minimum height (pixels) a display-style big operator should
    /// reach — the target passed to <see cref="StretchyVerticalBox"/>
    /// when picking a pre-drawn variant from the font's
    /// <c>MathVariants</c> table. Same source as
    /// <see cref="DisplayOperatorFontSize"/>; the difference is
    /// "give me the font size to scale to" vs. "give me the target
    /// pixel height". The latter is what the variant-picking path
    /// wants. 1.5·em floor applies to both.
    /// </summary>
    public float DisplayOperatorMinHeightPx => DisplayOperatorFontSize;

    /// <summary>
    /// Reference height (pixels above the baseline) at which a top accent
    /// anchors. Read from <c>MathConstants.AccentBaseHeight</c> when the
    /// font ships a MATH table; falls back to <see cref="ExHeight"/>
    /// otherwise — the same proxy MathJax uses for non-math fonts. Used
    /// by <see cref="AccentBox"/> to keep accents at a consistent height
    /// over short bases (so x̄ and ψ̄ have visually matching macrons),
    /// but to ride above the actual glyph top for tall bases.
    /// </summary>
    public float AccentBaseHeight
    {
        get
        {
            var info = SharedRasterizer.GetMathConstants(FontPath);
            if (info is null) return ExHeight;
            return info.Value.constants.AccentBaseHeight * FontSize / info.Value.unitsPerEm;
        }
    }
}
