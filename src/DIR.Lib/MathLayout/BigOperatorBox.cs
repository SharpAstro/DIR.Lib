using System.Text;

namespace DIR.Lib.MathLayout;

/// <summary>
/// A display-style big operator (∫, ∑, ∏, ⋃, ∮, …) sized via the
/// font's OpenType MATH table. Tries the proper path first —
/// <see cref="StretchyVerticalBox"/> picks a pre-drawn variant glyph
/// from <c>MathVariants</c> whose height meets
/// <see cref="BoxStyle.DisplayOperatorMinHeightPx"/> — and falls back
/// to a scaled <see cref="GlyphBox"/> when the font has no MATH
/// variants for this codepoint (DejaVu, body fonts).
///
/// <para>Why not just <c>StretchyVerticalBox</c> directly: that class
/// returns an empty box when the codepoint isn't in vertical coverage,
/// which is correct for stretchy delimiters (caller falls back to
/// parametric drawing) but wrong for big operators where we always
/// want *some* glyph rendered. <see cref="BigOperatorBox"/> bundles
/// the fallback so scenes can write
/// <c>new BigOperatorBox(0x222B, style)</c> without conditioning on
/// font support.</para>
///
/// <para>The variant path produces the font designer's intended
/// displaystyle shape (STIX has dedicated stretched integrals, sums,
/// etc., often with different proportions from the base glyph). The
/// scale-fallback path produces a uniformly enlarged base glyph,
/// which looks fine but isn't the typographic ideal.</para>
/// </summary>
public sealed class BigOperatorBox : Box
{
    private readonly Box _inner;

    public BigOperatorBox(int codepoint, BoxStyle style)
    {
        var stretchy = new StretchyVerticalBox(codepoint, style.DisplayOperatorMinHeightPx, style);
        if (stretchy.IsAvailable)
        {
            _inner = stretchy;
            return;
        }
        // Fallback: scale the base glyph to the target font size. The
        // result isn't a "designed" big-operator glyph but the scene
        // gets a recognizable big ∫ / ∑ even on body fonts without
        // MATH variant coverage.
        _inner = new GlyphBox(new Rune(codepoint).ToString(), style, style.DisplayOperatorFontSize);
    }

    public override float Width => _inner.Width;
    public override float Height => _inner.Height;
    public override float Depth => _inner.Depth;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
        => _inner.Draw(renderer, penX, baselineY, style);
}
