

namespace DIR.Lib.MathLayout;

public enum BracketKind { Paren, Square, Curly }

/// <summary>
/// A scalable bracket pair wrapping content. Brackets are drawn
/// parametrically as Bezier-ish strokes sized to the inner content, so they
/// stretch arbitrarily — the matrix case (tall content needing tall
/// brackets) works without needing OpenType MATH or font-specific tricks.
///
/// The three shapes:
/// <list type="bullet">
///   <item><c>Paren</c>: smooth crescents drawn as ellipse arcs.</item>
///   <item><c>Square</c>: two vertical strokes plus short horizontal serifs.</item>
///   <item><c>Curly</c>: two-stroke S-curves meeting at a centre tip.</item>
/// </list>
/// </summary>
public sealed class BracketBox : Box
{
    private readonly Box _inner;
    private readonly BracketKind _kind;
    private readonly float _bracketWidth;
    private readonly float _padding;
    private readonly float _ruleThickness;

    public BracketBox(Box inner, BracketKind kind, BoxStyle style)
    {
        _inner = inner;
        _kind = kind;
        _ruleThickness = style.RuleThickness;
        // Bracket width grows slightly with content height so tall brackets
        // don't look pinched. Matches what TeX's \big/\Big variants do.
        _bracketWidth = style.FontSize * 0.3f + inner.TotalHeight * 0.04f;
        _padding = style.FontSize * 0.08f;
    }

    public override float Width => 2 * _bracketWidth + 2 * _padding + _inner.Width;
    public override float Height => _inner.Height + _padding * 0.5f;
    public override float Depth  => _inner.Depth + _padding * 0.5f;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        float top = baselineY - Height;
        float bottom = baselineY + Depth;

        DrawBracket(renderer, penX, top, bottom, openLeft: true, style);
        _inner.Draw(renderer, penX + _bracketWidth + _padding, baselineY, style);
        DrawBracket(renderer, penX + _bracketWidth + _padding * 2 + _inner.Width, top, bottom, openLeft: false, style);
    }

    private void DrawBracket(RgbaImageRenderer renderer, float xOrigin, float top, float bottom, bool openLeft, BoxStyle style)
    {
        // Curved/vertical bracket strokes are drawn ~2× the rule thickness
        // so they don't read as a single-pixel hairline at typical font
        // sizes — the fraction bar / sqrt vinculum naturally look heavier
        // because their length amortizes the same thickness, but a short
        // paren stroke at 1 px reads as much thinner. Square brackets'
        // serifs share the same thickness as the rule (these are short
        // horizontal strokes, same role as the fraction bar).
        int curveThickness = (int)MathF.Max(2f, _ruleThickness * 2f);
        int ruleThickness = (int)MathF.Max(1f, _ruleThickness);
        var color = style.Foreground;
        float bw = _bracketWidth;

        switch (_kind)
        {
            case BracketKind.Square:
            {
                // Vertical stroke at the inner edge, plus top/bottom serifs.
                // The vertical stroke is the main visual weight — give it the
                // curve thickness; serifs stay at rule thickness like a
                // fraction bar.
                float vx = openLeft ? xOrigin + bw * 0.55f : xOrigin + bw * 0.45f;
                float serifLeft = openLeft ? xOrigin + bw * 0.55f : xOrigin + bw * 0.2f;
                float serifRight = openLeft ? xOrigin + bw * 0.85f : xOrigin + bw * 0.45f;
                renderer.DrawLine(vx, top, vx, bottom, color, curveThickness);
                renderer.DrawLine(serifLeft, top, serifRight, top, color, ruleThickness);
                renderer.DrawLine(serifLeft, bottom, serifRight, bottom, color, ruleThickness);
                break;
            }
            case BracketKind.Curly:
            {
                // Two-stroke S: top half curls in to a centre tip, bottom
                // half curls back out. Approximated as four straight strokes
                // (the renderer doesn't expose Bezier curves; if it ever
                // does, swap these for a proper cubic).
                float midY = (top + bottom) / 2;
                float tipX = openLeft ? xOrigin + bw * 0.85f : xOrigin + bw * 0.15f;
                float farX = openLeft ? xOrigin + bw * 0.30f : xOrigin + bw * 0.70f;
                float spineX = openLeft ? xOrigin + bw * 0.55f : xOrigin + bw * 0.45f;
                renderer.DrawLine(farX, top, spineX, top + (midY - top) * 0.3f, color, curveThickness);
                renderer.DrawLine(spineX, top + (midY - top) * 0.3f, tipX, midY, color, curveThickness);
                renderer.DrawLine(tipX, midY, spineX, midY + (bottom - midY) * 0.7f, color, curveThickness);
                renderer.DrawLine(spineX, midY + (bottom - midY) * 0.7f, farX, bottom, color, curveThickness);
                break;
            }
            case BracketKind.Paren:
            default:
            {
                // Ellipse-arc paren: approximate by a vertical-ish stroke that
                // bulges out at the middle. We sample the curve as a polyline
                // through 5 points (top, upper-mid, mid, lower-mid, bottom)
                // and join with straight strokes — visually indistinguishable
                // from a true Bezier at this resolution.
                float midY = (top + bottom) / 2;
                float upperMidY = top + (midY - top) * 0.5f;
                float lowerMidY = midY + (bottom - midY) * 0.5f;
                float tipX = openLeft ? xOrigin + bw * 0.20f : xOrigin + bw * 0.80f;
                float midX = openLeft ? xOrigin + bw * 0.05f : xOrigin + bw * 0.95f;
                float[] xs = [tipX, (tipX + midX) / 2, midX, (tipX + midX) / 2, tipX];
                float[] ys = [top, upperMidY, midY, lowerMidY, bottom];
                for (int i = 0; i < xs.Length - 1; i++)
                    renderer.DrawLine(xs[i], ys[i], xs[i + 1], ys[i + 1], color, curveThickness);
                break;
            }
        }
    }
}
