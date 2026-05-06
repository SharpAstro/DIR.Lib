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
        // prefer corner kerns when present, fall back to ±italic
        // correction otherwise. The TeX-style sign convention applies
        // either way: super shifts right, sub shifts left, on a slanted
        // base (italic letters, big integrals).
        var italic = TryGetItalicsCorrection(@base, style);
        // Lookup heights for the corner kern step functions — the
        // sub/super's contact y above the main baseline. We pass these
        // in pixels; the rasterizer converts to FU for the lookup.
        var supContactY = _supShift - (_sup?.Depth ?? 0);
        var subContactY = -_subShift + (_sub?.Height ?? 0);
        _supXShift = TryGetCornerKern(@base, style, MathKernCorner.TopRight, supContactY) ?? italic;
        _subXShift = TryGetCornerKern(@base, style, MathKernCorner.BottomRight, subContactY) ?? -italic;
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
    /// Resolve the corner kern (pixels at the base's render size) for
    /// a slanted base when it's a single-rune <see cref="GlyphBox"/>
    /// and the font supplies <c>MathKernInfo</c> for that glyph. The
    /// height parameter is the script's contact y above the main
    /// baseline (positive). Returns null when the font has no kern
    /// data — caller falls back to italic correction.
    /// </summary>
    private static float? TryGetCornerKern(Box @base, BoxStyle style, MathKernCorner corner, float heightPx)
    {
        if (@base is not GlyphBox gb) return null;
        var text = gb.Text;
        if (text.Length == 0) return null;
        var enumerator = text.EnumerateRunes();
        if (!enumerator.MoveNext()) return null;
        var rune = enumerator.Current;
        if (enumerator.MoveNext()) return null;
        return BoxStyle.SharedRasterizer.GetMathCornerKernPx(style.FontPath, gb.FontSize, rune, corner, heightPx);
    }

    /// <summary>
    /// Resolve the italic correction (pixels at the box's render size)
    /// for the base when it's a single-rune <see cref="GlyphBox"/> and
    /// the font supplies the metric; otherwise zero.
    /// </summary>
    private static float TryGetItalicsCorrection(Box @base, BoxStyle style)
    {
        if (@base is not GlyphBox gb) return 0f;
        var text = gb.Text;
        if (text.Length == 0) return 0f;
        var enumerator = text.EnumerateRunes();
        if (!enumerator.MoveNext()) return 0f;
        var rune = enumerator.Current;
        if (enumerator.MoveNext()) return 0f; // multi-rune — no single italic correction
        return BoxStyle.SharedRasterizer.GetItalicsCorrectionPx(style.FontPath, gb.FontSize, rune) ?? 0f;
    }

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
