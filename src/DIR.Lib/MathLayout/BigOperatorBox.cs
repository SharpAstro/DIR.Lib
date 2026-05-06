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
    private readonly float _inkRight;
    private readonly StretchyVerticalBox? _stretchy;

    /// <summary>The codepoint this box was constructed for.</summary>
    public int Codepoint { get; }

    /// <summary>Glyph id of the variant the font designer picked for
    /// this operator at displaystyle size — drives precise font-metric
    /// lookups (italic correction, corner kerns) against the variant
    /// itself rather than the base codepoint. Zero when the font has
    /// no MATH variants for this codepoint and we fell back to a
    /// scaled base glyph (the fallback path's metric lookup happens
    /// against the base codepoint via <see cref="Codepoint"/>).</summary>
    public uint VariantGlyphId { get; }

    /// <summary>Font size (pixels) at which the underlying glyph was
    /// rendered — used by <see cref="SupSubBox"/> to query font
    /// metrics at the right scale. For the variant path this is the
    /// displaystyle size (variant glyph has its own correction
    /// designed at this size); for the fallback scale-the-base path
    /// it's also the displaystyle size since the base glyph was
    /// scaled to fit.</summary>
    public float RenderFontSize { get; }

    public BigOperatorBox(int codepoint, BoxStyle style)
    {
        Codepoint = codepoint;
        RenderFontSize = style.DisplayOperatorFontSize;
        var stretchy = new StretchyVerticalBox(codepoint, style.DisplayOperatorMinHeightPx, style);
        if (stretchy.IsAvailable)
        {
            _inner = stretchy;
            _stretchy = stretchy;
            VariantGlyphId = stretchy.VariantGlyphId;
            _inkRight = stretchy.InkRightX;
            return;
        }
        // Fallback: scale the base glyph to the target font size. The
        // result isn't a "designed" big-operator glyph but the scene
        // gets a recognizable big ∫ / ∑ even on body fonts without
        // MATH variant coverage. No variant glyph id — caller looks
        // up metrics via Codepoint instead. The fallback GlyphBox is
        // already tight to its advance, so InkRight = Width here.
        _inner = new GlyphBox(new Rune(codepoint).ToString(), style, RenderFontSize);
        VariantGlyphId = 0;
        _inkRight = _inner.Width;
    }

    /// <summary>True when this big-operator was rendered via a
    /// pre-drawn <see cref="StretchyVerticalBox"/> variant (the font
    /// has MATH variant data for the codepoint at this size).
    /// False = scaled-base GlyphBox fallback for body fonts. Lets
    /// <see cref="SupSubBox"/> distinguish the script-placement
    /// strategy: variant-path uses bitmap-scanned ink anchors and
    /// no italic correction; fallback uses advance + italic correction.</summary>
    internal bool HasVariant => _stretchy is not null;

    /// <summary>X position (pixels from this box's left edge) of the
    /// rightmost ink column across the whole glyph — anchor for sub/super
    /// when the operator's bitmap is uniformly slanted (or upright). For
    /// strongly slanted operators (∫) callers prefer
    /// <see cref="InkRightAtY"/> which returns a height-aware kern.</summary>
    internal float InkRight => _inkRight;

    /// <summary>Rightmost-ink anchor (pixels from this box's left edge)
    /// restricted to the y-band <c>[yMinFromTop, yMaxFromTop)</c>, with y
    /// measured downward from this box's top edge (= baseline − Height).
    /// For ∫'s slanted body the script's anchor at sub-position differs
    /// from the anchor at super-position; this lets <see cref="SupSubBox"/>
    /// place each script flush to the operator's visible ink at that
    /// script's own height. Returns <see cref="InkRight"/> when the
    /// fallback path produced a plain <see cref="GlyphBox"/> (no bitmap
    /// to scan) — that path renders an upright glyph where uniform
    /// kerning is fine.</summary>
    internal float InkRightAtY(int yMinFromTop, int yMaxFromTop)
        => _stretchy is null ? _inkRight : _stretchy.InkRightAtY(yMinFromTop, yMaxFromTop);

    public override float Width => _inner.Width;
    public override float Height => _inner.Height;
    public override float Depth => _inner.Depth;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
        => _inner.Draw(renderer, penX, baselineY, style);
}
