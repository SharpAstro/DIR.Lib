

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
    private readonly float _italicCorrection;

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

        // Italic correction (pixels): the horizontal shift to apply to
        // a superscript anchored on a slanted base. The integral sign
        // ∫ has a large value (~0.2 em); italic letters like 𝑓 have
        // smaller values; upright glyphs have zero. The subscript
        // doesn't get the correction — only the super shifts right
        // because the slope's top is to the right of its bottom. Looked
        // up only when the base is a single-rune GlyphBox; otherwise
        // we leave it at zero (no correction).
        _italicCorrection = TryGetItalicsCorrection(@base, style);
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
            // The reported box width omits the italic-correction shift on
            // both scripts: the super and sub are drawn shifted right /
            // left by italic correction (TeX Rule 18a), but the box's
            // advance only counts the script size + kern. The result is
            // that a super on a strongly slanted base (∫, big radicals)
            // is allowed to *overhang* the box's right edge into the
            // following sibling's space — same convention as MathJax /
            // TeX's "italic correction is added to scripts but not to
            // the layout extent". Without this, "∫_0^∞ e" leaves a wide
            // empty gap between the integral and the e because the box
            // had to be wide enough to contain the right-shifted ∞.
            float supRight = _sup is null ? 0 : _scriptKern + _sup.Width;
            float subRight = _sub is null ? 0 : _scriptKern + _sub.Width;
            return _base.Width + MathF.Max(supRight, subRight);
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
        // Super shifts +italic_correction; sub shifts -italic_correction
        // (TeX Rule 18a). For upright bases both correction = 0 and the
        // two scripts share an x. For ∫ the correction is large so the
        // scripts spread visibly: super lands above the integral's top-
        // right hook, sub tucks under its bottom-left curl.
        float anchor = penX + _base.Width + _scriptKern;
        float supX = anchor + _italicCorrection;
        float subX = anchor - _italicCorrection;
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
