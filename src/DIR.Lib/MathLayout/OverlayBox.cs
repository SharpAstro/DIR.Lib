namespace DIR.Lib.MathLayout;

/// <summary>
/// A base box with an overlay (slash, stroke, vertical bar, dot, …)
/// drawn through it. Sister primitive to <see cref="AccentBox"/>:
/// <list type="bullet">
/// <item><see cref="AccentBox"/> stacks an accent <i>above</i> the base —
/// the macron, hat, tilde, vector arrow.</item>
/// <item><see cref="OverlayBox"/> draws the overlay <i>through</i> the
/// base — the Dirac slash D̸, the negation slash ≠, the cancel-stroke,
/// the divisor bar of a slashed letter.</item>
/// </list>
///
/// <para>The overlay is centred on the base both horizontally and
/// vertically: its visual middle aligns with the base's visual middle
/// (= <c>(base.Height − base.Depth) / 2</c> above the base's baseline,
/// the same point a math-font designer treats as the glyph's optical
/// centre). For a slash glyph designed at letter height — like the
/// '/' or U+2215 in any text font — that puts the cross-stroke
/// straight through the base's body.</para>
///
/// <para>The reported <see cref="Box.Width"/>, <see cref="Box.Height"/>,
/// and <see cref="Box.Depth"/> are the base's. The overlay is allowed
/// to overhang in any direction — TeX/MathJax convention for negation
/// and cancel marks. If the overlay is taller than the base (a slash
/// drawn larger than the letter it crosses), it visually pokes above
/// and below but doesn't push the surrounding HBox apart.</para>
///
/// <para>Compared to passing a combining-overlay rune (U+0338 long
/// solidus, U+20D2 short vertical line) to <see cref="GlyphBox"/>:
/// our rasterizer sums per-rune advances and doesn't apply OpenType
/// GPOS mark anchoring, so a combining overlay lands at its own pen
/// position — typically ZERO advance — which puts it on the <i>next</i>
/// glyph rather than over the previous one. <see cref="OverlayBox"/>
/// places the overlay explicitly so that quirk doesn't bite.</para>
/// </summary>
public sealed class OverlayBox : Box
{
    private readonly Box _base;
    private readonly Box _overlay;
    private readonly float _overlayXOffset;       // overlay left edge relative to base left edge
    private readonly float _overlayBaselineDrop;  // overlay baseline above base baseline (signed)

    public OverlayBox(Box @base, Box overlay)
    {
        _base = @base;
        _overlay = overlay;

        // Horizontal centre on centre.
        _overlayXOffset = (@base.Width - overlay.Width) / 2f;

        // Vertical centre on centre. The "centre" of a box (above its
        // baseline) is (Height − Depth) / 2 — halfway between the top
        // and bottom edges of the box's bounding rectangle. Aligning
        // these two means: place the overlay's baseline so that its
        // own centre lands at the base's centre.
        var baseCentre = (@base.Height - @base.Depth) / 2f;
        var overlayCentre = (overlay.Height - overlay.Depth) / 2f;
        _overlayBaselineDrop = baseCentre - overlayCentre;
    }

    public override float Width => _base.Width;
    public override float Height => _base.Height;
    public override float Depth => _base.Depth;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        _base.Draw(renderer, penX, baselineY, style);
        _overlay.Draw(renderer, penX + _overlayXOffset, baselineY - _overlayBaselineDrop, style);
    }
}
