

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

    public FracBox(Box numerator, Box denominator, BoxStyle style)
    {
        _num = numerator;
        _den = denominator;
        _ruleThickness = style.RuleThickness;
        _gap = style.FontSize * 0.18f;

        // Add a half-em margin on each side so the rule visibly extends past
        // the numerator/denominator like in proper math typography.
        var margin = style.FontSize * 0.1f;
        _width = MathF.Max(numerator.Width, denominator.Width) + 2 * margin;

        // Distance from baseline (= centre of rule) to top of numerator.
        _height = _num.TotalHeight + _gap + _ruleThickness / 2f;
        // Distance from baseline down to bottom of denominator.
        _depth = _den.TotalHeight + _gap + _ruleThickness / 2f;
    }

    public override float Width => _width;
    public override float Height => _height;
    public override float Depth => _depth;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        float ruleY = baselineY;
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
