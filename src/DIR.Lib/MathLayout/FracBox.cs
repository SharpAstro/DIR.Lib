

namespace DIR.Lib.MathLayout;

/// <summary>
/// A built-up fraction: numerator stacked above a horizontal rule, denominator
/// below it. The fraction's overall width is the max of numerator and
/// denominator widths plus a small margin for the rule overshoot. The
/// box's baseline sits at the middle of the rule (TeX's "axis height"),
/// which means a fraction inside <c>a + \frac{b}{c}</c> aligns visually
/// with the surrounding text.
/// </summary>
public sealed class FracBox : Box
{
    private readonly Box _num;
    private readonly Box _den;
    private readonly float _width;
    private readonly float _height;
    private readonly float _depth;
    private readonly float _ruleThickness;
    private readonly float _gap;
    private readonly float _mathAxis;

    public FracBox(Box numerator, Box denominator, BoxStyle style)
    {
        _num = numerator;
        _den = denominator;
        _ruleThickness = style.FractionRuleThickness;
        _gap = style.FontSize * 0.18f;
        // The fraction rule sits on the math axis — same horizontal level as
        // '=' / '+' / '−' glyph centres and a centred LimitsBox(∫). Without
        // this lift the rule sits flush on the baseline, which leaves it
        // visually below adjacent inline-math glyphs. BoxStyle.AxisHeight is
        // currently the historical 0.25·em magic number; see its docstring
        // for why we don't read MATH.AxisHeight directly yet.
        _mathAxis = style.AxisHeight;

        // Add a half-em margin on each side so the rule visibly extends past
        // the numerator/denominator like in proper math typography.
        var margin = style.FontSize * 0.1f;
        _width = MathF.Max(numerator.Width, denominator.Width) + 2 * margin;

        // Height = distance from line baseline up to top of numerator. With
        // the rule lifted to the math axis, that's mathAxis + halfRule + gap
        // + numerator's full visual height.
        _height = _mathAxis + _ruleThickness / 2f + _gap + _num.TotalHeight;
        // Depth  = distance from baseline down to bottom of denominator,
        // measured the symmetric way: rule centre is mathAxis above baseline,
        // so the rule's bottom edge is mathAxis − halfRule above baseline,
        // and denominator extends below by gap + den.TotalHeight − mathAxis.
        _depth = MathF.Max(0f, _gap + _den.TotalHeight + _ruleThickness / 2f - _mathAxis);
    }

    public override float Width => _width;
    public override float Height => _height;
    public override float Depth => _depth;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        // Rule centred on the math axis (above the line baseline) — see ctor.
        float ruleY = baselineY - _mathAxis;
        float ruleLeft = penX;
        float ruleRight = penX + _width;

        // Numerator: centre horizontally, baseline = top of rule - gap - num.depth.
        float numX = penX + (_width - _num.Width) / 2f;
        float numBaseline = ruleY - _ruleThickness / 2f - _gap - _num.Depth;
        _num.Draw(renderer, numX, numBaseline, style);

        // Rule.
        var ruleRect = new RectInt(
            new PointInt((int)MathF.Ceiling(ruleRight), (int)MathF.Ceiling(ruleY + _ruleThickness / 2f)),
            new PointInt((int)MathF.Floor(ruleLeft), (int)MathF.Floor(ruleY - _ruleThickness / 2f)));
        renderer.FillRectangle(ruleRect, style.Foreground);

        // Denominator: centre horizontally, baseline = bottom of rule + gap + den.height.
        float denX = penX + (_width - _den.Width) / 2f;
        float denBaseline = ruleY + _ruleThickness / 2f + _gap + _den.Height;
        _den.Draw(renderer, denX, denBaseline, style);
    }
}
