

namespace DIR.Lib.MathLayout;

/// <summary>
/// Square-root construction: a parametrically-drawn radical (left hook +
/// horizontal vinculum) over the radicand. The hook is drawn with two
/// straight strokes — a steep upstroke from baseline-low into the top
/// corner, then the vinculum extending right across the radicand's width.
/// This avoids needing a font with stretchable √ glyphs (most don't).
///
/// Layout: vinculum sits a small gap above the radicand's top; the hook's
/// upper-left corner aligns with the vinculum's left end; the hook's
/// lower point sits at <c>baselineY + radicand.Depth</c> so the descender
/// of any letter inside the radicand still fits.
/// </summary>
public sealed class SqrtBox : Box
{
    private readonly Box _radicand;
    private readonly float _hookWidth;
    private readonly float _gap;
    private readonly float _ruleThickness;

    public SqrtBox(Box radicand, BoxStyle style)
    {
        _radicand = radicand;
        _hookWidth = style.FontSize * 0.45f;
        _gap = style.FontSize * 0.12f;
        _ruleThickness = style.RuleThickness;
    }

    public override float Width => _hookWidth + _radicand.Width + _gap;
    public override float Height => _radicand.Height + _gap + _ruleThickness;
    public override float Depth => _radicand.Depth;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        float radicandX = penX + _hookWidth;
        float radicandTop = baselineY - _radicand.Height;
        float radicandBottom = baselineY + _radicand.Depth;
        float vinculumY = radicandTop - _gap;

        // Radicand.
        _radicand.Draw(renderer, radicandX, baselineY, style);

        // Vinculum: horizontal rule across the top of the radicand.
        var vinc = new RectInt(
            new PointInt((int)MathF.Ceiling(penX + Width), (int)MathF.Ceiling(vinculumY + _ruleThickness / 2f)),
            new PointInt((int)MathF.Floor(penX + _hookWidth - _ruleThickness / 2f), (int)MathF.Floor(vinculumY - _ruleThickness / 2f)));
        renderer.FillRectangle(vinc, style.Foreground);

        // Hook: two straight strokes forming a check-mark shape.
        // Top point of the hook = (penX + hookWidth, vinculumY).
        // Bottom-tip = ~one-third down from baseline (visual sweet spot).
        // Left-shoulder = (penX, vinculumY + 0.4*hookHeight).
        float hookTopX = penX + _hookWidth;
        float hookTopY = vinculumY;
        float hookTipX = penX + _hookWidth * 0.45f;
        float hookTipY = radicandBottom;
        float hookLeftX = penX;
        float hookLeftY = vinculumY + (radicandBottom - vinculumY) * 0.35f;

        int thickness = (int)MathF.Max(1f, _ruleThickness);
        renderer.DrawLine(hookTopX, hookTopY, hookTipX, hookTipY, style.Foreground, thickness);
        renderer.DrawLine(hookTipX, hookTipY, hookLeftX, hookLeftY, style.Foreground, thickness);
    }
}
