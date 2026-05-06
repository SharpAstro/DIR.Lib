

namespace DIR.Lib.MathLayout;

/// <summary>
/// Display-style limits attachment: <paramref name="lower"/> stacks
/// directly below the base, <paramref name="upper"/> directly above —
/// both centred on the base's horizontal axis. Used for big-operator
/// limits in math display mode (<c>\int_0^\infty</c>, <c>\sum_{i=0}^n</c>,
/// <c>\lim_{x \to 0}</c>) where TeX puts the limits above and below
/// instead of as scripts to the right.
///
/// Contrast with <see cref="SupSubBox"/>, which places sup/sub to the
/// right of the base (text/script style). Callers shrink the limit
/// boxes themselves via <see cref="BoxStyle.Smaller"/> before passing
/// them in, exactly like SupSubBox; the layout doesn't know about the
/// font scale.
/// </summary>
public sealed class LimitsBox : Box
{
    private readonly Box _base;
    private readonly Box? _lower;
    private readonly Box? _upper;
    private readonly float _gap;
    private readonly float _baseShift;
    private readonly float _baseHalf;
    private readonly float _mathAxis;
    private readonly float _width;
    private readonly float _height;
    private readonly float _depth;

    public LimitsBox(Box @base, Box? lower, Box? upper, BoxStyle style)
    {
        _base = @base;
        _lower = lower;
        _upper = upper;
        // Visible separation between base and each limit. TeX's
        // \displaystyle uses upper/lower limit gaps in the 0.15–0.20·em range
        // depending on the font's MATH constants. The previous 0.1·em was
        // tuned against an older GlyphBox bug that accidentally added extra
        // padding above/below glyphs; with that fixed the prescribed gap
        // shows up as-is, and 0.1·em looked too tight (the upper limit's
        // bottom edge sat almost on the operator's top hook). 0.2·em
        // matches the OpenType MATH UpperLimitGapMin / LowerLimitGapMin
        // typical values for STIX, Latin Modern, etc.
        _gap = style.FontSize * 0.2f;

        // Centre the base on the *math axis*, not the parent baseline.
        // That's where '=' / '+' / '−' glyphs visually centre, where fraction
        // bars land, and where a centred operator should sit too. Without
        // this upward offset, a tall LimitsBox(∫) inside an HBox alongside
        // '=' looks low — its visual centre stuck on the line baseline while
        // surrounding inline-math glyphs sit on the axis. BoxStyle.AxisHeight
        // is currently the historical 0.25·em magic number; see its docstring
        // for why we don't read MATH.AxisHeight directly yet.
        _mathAxis = style.AxisHeight;
        _baseShift = (_base.Height - _base.Depth) / 2f - _mathAxis;
        _baseHalf = _base.TotalHeight / 2f;

        _width = MathF.Max(_base.Width, MathF.Max(lower?.Width ?? 0, upper?.Width ?? 0));
        // Box extends mathAxis higher (top side) and mathAxis less (bottom
        // side) than a baseline-centred version would, since we lifted the
        // whole base by mathAxis above the line baseline.
        _height = _baseHalf + _mathAxis + (upper is not null ? _gap + upper.TotalHeight : 0);
        _depth  = MathF.Max(0f, _baseHalf - _mathAxis) + (lower is not null ? _gap + lower.TotalHeight : 0);
    }

    public override float Width => _width;
    public override float Height => _height;
    public override float Depth => _depth;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        float centerX = penX + _width / 2f;

        // Draw the base shifted so its visual centre lands at parent
        // baselineY — see ctor for the math-axis alignment rationale.
        float baseX = centerX - _base.Width / 2f;
        _base.Draw(renderer, baseX, baselineY + _baseShift, style);

        if (_upper is not null)
        {
            // Upper sits above the base. After the math-axis lift, base's
            // top = baselineY − _baseHalf − _mathAxis. Upper's bottom edge
            // = top of base − _gap; upper's own baseline is _upper.Depth
            // above its bottom.
            float upperX = centerX - _upper.Width / 2f;
            float upperBaseline = baselineY - _baseHalf - _mathAxis - _gap - _upper.Depth;
            _upper.Draw(renderer, upperX, upperBaseline, style);
        }

        if (_lower is not null)
        {
            // Lower sits below the base. After the math-axis lift, base's
            // bottom = baselineY + _baseHalf − _mathAxis. Lower's top edge
            // = bottom of base + _gap; lower's baseline is _lower.Height
            // below its top.
            float lowerX = centerX - _lower.Width / 2f;
            float lowerBaseline = baselineY + _baseHalf - _mathAxis + _gap + _lower.Height;
            _lower.Draw(renderer, lowerX, lowerBaseline, style);
        }
    }
}
