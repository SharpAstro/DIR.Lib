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

    /// <summary>The codepoint this box was constructed for. Exposed so
    /// <see cref="SupSubBox"/> can look up <c>MathItalicsCorrection</c>
    /// and corner kerns against the base codepoint when this box is
    /// the script's parent — wrapper boxes don't otherwise expose
    /// their underlying glyph.</summary>
    public int Codepoint { get; }

    /// <summary>Font size (pixels) at which <see cref="SupSubBox"/>
    /// should look up italic-correction and corner-kern values for
    /// scripts attached to this operator. Held at the <i>surrounding</i>
    /// font size, NOT the displaystyle render size: the stretchy
    /// variant glyph used to draw the operator has its own
    /// MathItalicsCorrection in the font, generally smaller in
    /// proportion than scaling the base glyph's correction would
    /// suggest. We don't have the variant's glyph id plumbed back
    /// out of StretchyVerticalBox yet, so we approximate by reading
    /// the BASE codepoint's correction at the surrounding size —
    /// gives a reasonable post-script offset without ballooning at
    /// big render sizes.</summary>
    public float MetricFontSize { get; }

    public BigOperatorBox(int codepoint, BoxStyle style)
    {
        Codepoint = codepoint;
        MetricFontSize = style.FontSize;
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
