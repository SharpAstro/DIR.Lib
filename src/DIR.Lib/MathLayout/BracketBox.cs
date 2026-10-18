

namespace DIR.Lib.MathLayout;

public enum BracketKind { Paren, Square, Curly }

/// <summary>
/// A scalable bracket pair wrapping content. Two rendering paths:
///
/// <list type="bullet">
///   <item><b>Font-driven (preferred)</b> — when the loaded font ships an
///   OpenType MATH table and has a vertical-stretch construction for the
///   chosen delimiter codepoints. The brackets are rasterized via
///   <see cref="StretchyVerticalBox"/>: pre-drawn variants for common sizes,
///   assembly recipes (top hook + extender + bottom hook) for arbitrary
///   heights. STIX Two Math, Latin Modern Math, Cambria Math, DejaVu Sans's
///   bundled MATH build and similar fonts all hit this path.</item>
///
///   <item><b>Parametric fallback</b> — when no MATH coverage exists for
///   the codepoint, brackets are drawn as Bezier-ish strokes sized to the
///   inner content. Keeps the matrix case (tall content needing tall
///   brackets) working with general-purpose UI fonts that ship no MATH.</item>
/// </list>
///
/// The parametric three shapes:
/// <list type="bullet">
///   <item><c>Paren</c>: smooth crescents drawn as ellipse arcs.</item>
///   <item><c>Square</c>: two vertical strokes plus short horizontal serifs.</item>
///   <item><c>Curly</c>: per half, two quarter-curves joined by a straight
///   spine, the halves meeting at a sharp cusp.</item>
/// </list>
/// </summary>
public sealed class BracketBox : Box
{
    private readonly Box _inner;
    private readonly BracketKind _kind;
    private readonly float _bracketWidth;
    private readonly float _padding;
    private readonly float _ruleThickness;

    /// <summary>Left/right delimiter glyphs from the font's MATH table.
    /// Both non-null when the font-driven path is in use; both null when
    /// falling back to parametric drawing.</summary>
    private readonly StretchyVerticalBox? _leftFont;
    private readonly StretchyVerticalBox? _rightFont;

    public BracketBox(Box inner, BracketKind kind, BoxStyle style)
    {
        _inner = inner;
        _kind = kind;
        _ruleThickness = style.RuleThickness;
        // Bracket width grows slightly with content height so tall brackets
        // don't look pinched. Matches what TeX's \big/\Big variants do.
        // Used by the parametric fallback path.
        _bracketWidth = style.FontSize * 0.3f + inner.TotalHeight * 0.04f;
        // Horizontal padding between bracket glyphs and inner content. Set
        // to ~0.15 em so brackets visibly breathe around the content (they
        // were 0.08 em — visibly tight against letters in (x), [a,b], and
        // pmatrix). Matches MathJax/TeX visual spacing where the inner
        // baseline width does not equal the bracket-to-bracket interior.
        // An empty pair, (), has no content to set off, so its delimiters
        // sit side by side as TeX sets them.
        _padding = inner.Width <= 0 && inner.TotalHeight <= 0 ? 0 : style.FontSize * 0.15f;

        // Try MATH-driven brackets first. The delimiters are centred on the
        // math axis, so they must reach as far from it as the content does on
        // its farther side: requiredHeight = 2 * max(height - axis, depth +
        // axis), as TeX sizes a \left..\right pair. The content's total height
        // is the same only for content centred on the axis; a fraction whose
        // numerator is itself a fraction reaches further above the axis than
        // below, and asking for its total height left its numerator above the
        // delimiters. No padding is added to the request. A font's delimiters
        // come in steps (DejaVu Sans ships no size variants, and its assembly
        // grows by more than an em a copy), so asking for more than the content
        // reaches only takes a larger step than TeX would. The visual padding
        // around the delimiters comes from Width and Height, not from the
        // request.
        var axis = style.AxisHeight;
        var requiredHeight = 2 * MathF.Max(inner.Height - axis, inner.Depth + axis);
        var (leftCp, rightCp) = BracketCodepoints(kind);
        // As TeX's \left..\right, a delimiter may fall short of the request by
        // \delimiterfactor and \delimitershortfall (StretchyVerticalBox.Delimiter).
        var left = StretchyVerticalBox.Delimiter(leftCp, requiredHeight, style);
        var right = StretchyVerticalBox.Delimiter(rightCp, requiredHeight, style);
        if (left.IsAvailable && right.IsAvailable)
        {
            _leftFont = left;
            _rightFont = right;
        }
    }

    private static (int left, int right) BracketCodepoints(BracketKind kind) => kind switch
    {
        BracketKind.Paren  => ('(', ')'),
        BracketKind.Square => ('[', ']'),
        BracketKind.Curly  => ('{', '}'),
        _ => ('(', ')'),
    };

    public override float Width => _leftFont is not null
        ? _leftFont.Width + _padding + _inner.Width + _padding + _rightFont!.Width
        : 2 * _bracketWidth + 2 * _padding + _inner.Width;

    public override float Height => _leftFont is not null
        ? MathF.Max(_inner.Height + _padding * 0.5f, MathF.Max(_leftFont.Height, _rightFont!.Height))
        : _inner.Height + _padding * 0.5f;

    public override float Depth => _leftFont is not null
        ? MathF.Max(_inner.Depth + _padding * 0.5f, MathF.Max(_leftFont.Depth, _rightFont!.Depth))
        : _inner.Depth + _padding * 0.5f;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        if (_leftFont is not null)
        {
            // MATH-driven path: each StretchyVerticalBox knows how to centre
            // itself on the math axis, so passing the same baselineY to all
            // three sub-boxes keeps the brackets visually centred on the
            // axis along with the inner content's relational/operator glyphs.
            _leftFont.Draw(renderer, penX, baselineY, style);
            _inner.Draw(renderer, penX + _leftFont.Width + _padding, baselineY, style);
            _rightFont!.Draw(renderer, penX + _leftFont.Width + _padding + _inner.Width + _padding, baselineY, style);
            return;
        }

        // Parametric fallback for fonts without MATH coverage.
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
                // Each half: a quarter-curve from the end, which lies towards
                // the content, out to a straight vertical spine; the spine; and
                // a quarter-curve from the spine out to the cusp at mid-height,
                // which points away from the content. Both curves meet the cusp
                // running horizontally, which is what makes it sharp, so the
                // left one reads as { and the right one as }. Each curve is a
                // quarter ellipse sampled into short strokes, as the paren's
                // arc is (the renderer has no curve primitive).
                float midY = (top + bottom) / 2;
                float inward = openLeft ? 1f : -1f;
                float spineX = xOrigin + bw * 0.5f;
                float reach = bw * 0.35f;
                float halfHeight = (bottom - top) / 2;
                float curl = MathF.Min(MathF.Max(halfHeight * 0.25f, reach), halfHeight * 0.5f);
                int steps = Math.Max(6, (int)MathF.Ceiling(MathF.Max(curl, reach) / 1.5f));

                // The top half, end to cusp; the bottom half mirrors it.
                var points = new (float X, float Y)[4 * steps + 3];
                var n = 0;
                for (int i = 0; i <= steps; i++)
                {
                    float a = MathF.PI / 2 * i / steps;
                    points[n++] = (spineX + inward * reach * (1 - MathF.Sin(a)), top + curl * (1 - MathF.Cos(a)));
                }
                for (int i = 0; i <= steps; i++)
                {
                    float a = MathF.PI / 2 * i / steps;
                    points[n++] = (spineX - inward * reach * (1 - MathF.Cos(a)), midY - curl * (1 - MathF.Sin(a)));
                }
                for (int i = n - 2; i >= 0; i--)
                    points[n++] = (points[i].X, 2 * midY - points[i].Y);
                renderer.DrawPolyline(points.AsSpan(0, n), color, curveThickness);
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
