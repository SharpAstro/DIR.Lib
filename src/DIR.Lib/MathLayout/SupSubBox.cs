using System.Text;
using SharpAstro.Fonts.Tables.OpenTypeMath;

namespace DIR.Lib.MathLayout;

/// <summary>
/// Postfix script attached to a base box. Either a superscript (raised) or
/// a subscript (lowered), or both. The script's font is shrunk
/// (<see cref="BoxStyle.Smaller"/>) before its sub-tree is constructed —
/// callers pass already-rasterized smaller boxes via
/// <paramref name="sup"/> / <paramref name="sub"/>.
/// </summary>
public sealed class SupSubBox : Box
{
    private readonly Box _base;
    private readonly Box? _sup;
    private readonly Box? _sub;
    private readonly float _supShift;
    private readonly float _subShift;
    private readonly float _scriptKern;
    private readonly float _supXShift;  // horizontal shift on super (corner kern OR italic correction)
    private readonly float _subXShift;  // horizontal shift on sub (corner kern OR -italic correction)

    public SupSubBox(Box @base, Box? sup, Box? sub, BoxStyle style)
    {
        _base = @base;
        _sup = sup;
        _sub = sub;

        // Per TeX Rule 18 / OpenType MATH:
        //   supShift = max(SuperscriptShiftUp, base.Height + SuperscriptBaselineDropMax)
        //   subShift = max(SubscriptShiftDown,  base.Depth  + SubscriptBaselineDropMin)
        // The shift values come from MATH constants when the font has
        // them; we fall back to TeX-style heuristics (0.45·em, 0.18·em,
        // and base-relative drops of 0.7 / 0.85) for non-math fonts so
        // ad-hoc layouts under DejaVu / Roboto stay reasonable.
        var c = SharedMathConstants(style);
        var supShiftUp = c?.supShiftUp     ?? style.FontSize * 0.45f;
        var supDropMax = c?.supDropMax     ?? _base.Height * 0.7f;
        var subShiftDown = c?.subShiftDown ?? style.FontSize * 0.18f;
        var subDropMin = c?.subDropMin     ?? _base.Depth * 0.85f;
        _supShift = MathF.Max(supShiftUp, _base.Height - supDropMax);
        _subShift = MathF.Max(subShiftDown, _base.Depth + subDropMin);
        _scriptKern = style.FontSize * 0.04f;

        // Per-corner horizontal shifts. The OpenType MATH table can
        // supply per-glyph corner kerns (TopRight for sup, BottomRight
        // for sub) that are evaluated at the script's contact height —
        // a strictly more precise placement than italic correction,
        // which is a single global value for the whole glyph. We
        // prefer corner kerns when present, fall back to italic
        // correction otherwise.
        //
        // <para>For ordinary slanted bases (italic letters) the sign
        // convention is symmetric: super shifts right by +italic, sub
        // shifts left by −italic — places both at the slope's contact
        // corners.</para>
        //
        // <para>For <see cref="BigOperatorBox"/> the placement is
        // asymmetric: super shifts right by +italic, sub stays at
        // advance (no leftward pull). Empirical match to MathJax —
        // ∫'s top curl sits roughly at the advance position, and
        // MathJax aligns the sub with the top curl rather than the
        // bottom one. Pulling sub left to the slope's bottom-left
        // corner detaches it from the rest of the formula and
        // overlaps the bottom curl's ink.</para>
        var italic = TryGetItalicsCorrection(@base, style);
        // Lookup heights for the corner kern step functions — the
        // sub/super's contact y above the main baseline. We pass these
        // in pixels; the rasterizer converts to FU for the lookup.
        var supContactY = _supShift - (_sup?.Depth ?? 0);
        var subContactY = -_subShift + (_sub?.Height ?? 0);
        _supXShift = TryGetCornerKern(@base, style, MathKernCorner.TopRight, supContactY) ?? italic;
        _subXShift = TryGetCornerKern(@base, style, MathKernCorner.BottomRight, subContactY)
            ?? (@base is BigOperatorBox ? 0f : -italic);
    }

    /// <summary>
    /// Pull the four script-shift MATH constants for the given style's
    /// font and convert them to pixels at the layout font size. Returns
    /// null when the font has no MATH table — caller falls back to
    /// TeX-style heuristics so non-math fonts (DejaVu, Roboto) still
    /// produce sensible script placement.
    /// </summary>
    private static (float supShiftUp, float supDropMax, float subShiftDown, float subDropMin)?
        SharedMathConstants(BoxStyle style)
    {
        var info = BoxStyle.SharedRasterizer.GetMathConstants(style.FontPath);
        if (info is null) return null;
        var c = info.Value.constants;
        float scale = style.FontSize / info.Value.unitsPerEm;
        return (
            c.SuperscriptShiftUp * scale,
            c.SuperscriptBaselineDropMax * scale,
            c.SubscriptShiftDown * scale,
            c.SubscriptBaselineDropMin * scale);
    }

    /// <summary>
    /// Resolve the (rune, render-font-size) pair for a base box that
    /// represents a single glyph at a known size. Covers single-rune
    /// <see cref="GlyphBox"/> and <see cref="BigOperatorBox"/>'s
    /// fallback path. The variant path of <see cref="BigOperatorBox"/>
    /// is handled separately because its variant glyph id can't be
    /// reached by codepoint via the cmap.
    /// </summary>
    private static (Rune rune, float fontSize)? TryGetSingleGlyph(Box @base)
    {
        switch (@base)
        {
            case GlyphBox gb:
                return TryFromText(gb.Text, gb.FontSize);
            // MathGlyphBox wraps a GlyphBox after remapping its runes to
            // a math-alphanumeric codepoint (italic / bold / script / …).
            // Look the metrics up against the *remapped* rune so the
            // italic correction comes from the styled glyph, not the
            // upright fallback — italic 𝑥 (U+1D465) carries an italics
            // correction in STIX while plain 'x' does not.
            case MathGlyphBox mgb:
                return TryFromText(mgb.Text, mgb.FontSize);
            case BigOperatorBox big:
                return (new Rune(big.Codepoint), big.RenderFontSize);
            default:
                return null;
        }

        static (Rune rune, float fontSize)? TryFromText(string text, float fontSize)
        {
            if (text.Length == 0) return null;
            var e = text.EnumerateRunes();
            if (!e.MoveNext()) return null;
            var rune = e.Current;
            if (e.MoveNext()) return null;
            return (rune, fontSize);
        }
    }

    /// <summary>
    /// Resolve the corner kern (pixels at the base's render size) for
    /// a slanted base when the font supplies <c>MathKernInfo</c> for
    /// the underlying glyph. Returns null when no kern data — caller
    /// falls back to italic correction.
    /// </summary>
    private static float? TryGetCornerKern(Box @base, BoxStyle style, MathKernCorner corner, float heightPx)
    {
        var glyph = TryGetSingleGlyph(@base);
        if (glyph is null) return null;
        return BoxStyle.SharedRasterizer.GetMathCornerKernPx(
            style.FontPath, glyph.Value.fontSize, glyph.Value.rune, corner, heightPx);
    }

    /// <summary>
    /// Resolve the italic correction (pixels) for the underlying glyph,
    /// to be applied as ±shift on the script x positions per TeX Rule 18a.
    ///
    /// <para>For ordinary slanted bases — italic letters in math context —
    /// italic correction places the script at the slope's top-right
    /// corner: super at advance + correction, sub at advance − correction.
    /// Returns the codepoint's value via cmap, scaled at the base's own
    /// render size.</para>
    ///
    /// <para>For <see cref="BigOperatorBox"/> the variant glyph (∫ at
    /// displaystyle) carries its own italic correction (540 FU on the
    /// STIX variant) — but the spec value applied at the variant's
    /// displaystyle size produces a shift large enough to detach the
    /// super from the operator and pull the sub on top of its ink. We
    /// look up the value (by glyph id when the variant is in coverage,
    /// by codepoint otherwise) but evaluate it at the surrounding
    /// <see cref="BoxStyle.FontSize"/> rather than the displaystyle
    /// render size. The result is a script shift that scales with the
    /// formula's body size — empirically the placement MathJax produces
    /// — instead of growing with the operator glyph itself.</para>
    /// </summary>
    private static float TryGetItalicsCorrection(Box @base, BoxStyle style)
    {
        if (@base is BigOperatorBox big)
        {
            // Look up the FU value via the variant gid (preferred — the
            // variant is the actual rendered glyph, isn't in the cmap)
            // or the base codepoint as fallback. Scale by style.FontSize,
            // not big.RenderFontSize, so the shift magnitude tracks the
            // surrounding script-size context, not the inflated
            // displaystyle operator size. Only super uses this value —
            // the sub stays at advance for big operators (see ctor).
            if (big.VariantGlyphId != 0)
            {
                return BoxStyle.SharedRasterizer.GetItalicsCorrectionByGidPx(
                    style.FontPath, style.FontSize, big.VariantGlyphId) ?? 0f;
            }
            return BoxStyle.SharedRasterizer.GetItalicsCorrectionPx(
                style.FontPath, style.FontSize, new Rune(big.Codepoint)) ?? 0f;
        }
        var glyph = TryGetSingleGlyph(@base);
        if (glyph is null) return 0f;
        return BoxStyle.SharedRasterizer.GetItalicsCorrectionPx(
            style.FontPath, glyph.Value.fontSize, glyph.Value.rune) ?? 0f;
    }

    /// <summary>Horizontal shift applied to the super relative to
    /// (base.advance + scriptKern). Positive = right, negative = left.
    /// Driven by the per-corner kern (TopRight) when the font supplies
    /// one, otherwise +italic correction. Exposed for layout tests so
    /// the script-positioning math can be asserted numerically rather
    /// than only via baseline image diffs.</summary>
    internal float SupXShift => _supXShift;

    /// <summary>Horizontal shift applied to the sub relative to
    /// (base.advance + scriptKern). Per-corner BottomRight kern when
    /// the font supplies one, otherwise −italic correction.</summary>
    internal float SubXShift => _subXShift;

    /// <summary>Vertical baseline shift up for the super (pixels).</summary>
    internal float SupShift => _supShift;

    /// <summary>Vertical baseline shift down for the sub (pixels).</summary>
    internal float SubShift => _subShift;

    /// <summary>Horizontal gap between base.advance and the unshifted
    /// script anchor (before <see cref="SupXShift"/> / <see cref="SubXShift"/>
    /// apply).</summary>
    internal float ScriptKern => _scriptKern;

    public override float Width
    {
        get
        {
            // Box width includes the per-corner horizontal shift on
            // each script — the super at +shift may extend past the
            // unshifted right edge; we need canvas room for it so a
            // standalone SupSubBox doesn't clip and an HBox sibling
            // doesn't overlap. Sub at -shift may pull left of the
            // unshifted edge but doesn't reduce width below base.Width
            // (script.Width overhangs cleanly into the base's advance).
            float supRight = _sup is null ? 0 : _supXShift + _scriptKern + _sup.Width;
            float subRight = _sub is null ? 0 : _subXShift + _scriptKern + _sub.Width;
            float scriptExtent = MathF.Max(supRight, subRight);
            return _base.Width + MathF.Max(0, scriptExtent);
        }
    }

    public override float Height
    {
        get
        {
            float h = _base.Height;
            if (_sup is not null) h = MathF.Max(h, _supShift + _sup.Height);
            return h;
        }
    }

    public override float Depth
    {
        get
        {
            float d = _base.Depth;
            if (_sub is not null) d = MathF.Max(d, _subShift + _sub.Depth);
            return d;
        }
    }

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        _base.Draw(renderer, penX, baselineY, style);
        // Per-corner shifts: TopRight kern (or +italic correction
        // fallback) for super, BottomRight kern (or −italic correction)
        // for sub. For upright bases both shifts = 0; for ∫ the corner
        // kerns pull the sub under the bottom curl and push the super
        // past the top hook. For italic letters the smaller italic-
        // correction fallback applies when the font has no per-glyph
        // kern data.
        float anchor = penX + _base.Width + _scriptKern;
        float supX = anchor + _supXShift;
        float subX = anchor + _subXShift;
        if (_sup is not null)
        {
            // Sup baseline sits at (baseline - shift) — sup.Height is its
            // ascent, so the sup glyph occupies [baseline-shift-sup.Height,
            // baseline-shift+sup.Depth].
            _sup.Draw(renderer, supX, baselineY - _supShift, style);
        }
        if (_sub is not null)
        {
            _sub.Draw(renderer, subX, baselineY + _subShift, style);
        }
    }
}
