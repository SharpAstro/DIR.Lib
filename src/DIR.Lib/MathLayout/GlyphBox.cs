using System.Text;


namespace DIR.Lib.MathLayout;

/// <summary>
/// A leaf box wrapping a string of text rasterized at a fixed font size. The
/// box's <see cref="Box.Width"/> is the advance width of the text;
/// <see cref="Box.Height"/> is the ascent; <see cref="Box.Depth"/> is the
/// descent. Layout.Sizing uses <see cref="RgbaImageRenderer.MeasureText"/> against
/// the same renderer that will eventually paint, so cache hits are reused.
/// </summary>
public sealed class GlyphBox : Box
{
    private readonly string _text;
    private readonly float _fontSize;
    private readonly float _width;
    private readonly float _height;
    private readonly float _depth;

    public GlyphBox(string text, BoxStyle style)
        : this(text, style, style.FontSize)
    { }

    public GlyphBox(string text, BoxStyle style, float fontSize)
    {
        _text = text;
        _fontSize = fontSize;

        // Per-rune metrics from the shared rasterizer: take maxAscent
        // (= max BearingY across the runes) and maxDescent (= max
        // (Height − BearingY), clamped at zero — "+", "=", "−" sit
        // entirely above baseline so their nominal "descent" is negative
        // and shouldn't push the box's reported Depth into the negative).
        // This matches *exactly* the loop DrawText uses to position the
        // baseline; same numbers in / same baseline out, so a string of
        // any glyphs renders at the requested baselineY without clipping
        // and without the per-string fudge that an 0.8/0.2 split causes.
        var rasterizer = BoxStyle.SharedRasterizer;
        int maxAscent = 0, maxDescent = 0;
        float advance = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var g = rasterizer.RasterizeGlyph(style.FontPath, fontSize, rune);
            advance += g.AdvanceX;
            if (g.BearingY > maxAscent) maxAscent = g.BearingY;
            var descent = g.Height - g.BearingY;
            if (descent > maxDescent) maxDescent = descent;
        }
        _width = advance;
        _height = maxAscent;
        _depth = maxDescent;
    }

    public override float Width => _width;
    public override float Height => _height;
    public override float Depth => _depth;

    /// <summary>Raw text rendered by this glyph box — exposed so callers can
    /// rebuild the same glyph at a different font size without losing the
    /// source string. Used by the LaTeX visitor's script-shrinking path.</summary>
    public string Text => _text;

    /// <summary>Font size (pixels) this glyph box was constructed with —
    /// exposed so wrappers like <see cref="AccentBox"/> can query font
    /// metrics at the same scale the box was actually rasterized at.
    /// Distinct from <c>BoxStyle.FontSize</c>, which is the surrounding
    /// layout's size and may differ for scripts.</summary>
    public float FontSize => _fontSize;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        // DrawText computes baseline = rectTop + (lineHeight + maxAscent
        // − maxDescent) / 2 where maxAscent/maxDescent are the per-rune
        // values DrawText itself derives from the rasterizer (lineHeight
        // = fontSize * 1.3 is the renderer's per-line padding). Our ctor
        // computes _height = maxAscent and _depth = maxDescent from the
        // SAME rasterizer + fontSize, so they match DrawText's internal
        // values 1:1. Solving for rectTop such that DrawText's baseline
        // equals the caller's baselineY:
        //     baselineY = rectTop + (lineHeight + _height − _depth) / 2
        //   ⇒ rectTop  = baselineY − (lineHeight + _height − _depth) / 2
        // This makes "y" (descender pulls _depth up) and "+" (no descender
        // → _depth ≈ 0) land at the SAME baselineY in an HBox — a
        // descender-having glyph no longer renders _depth pixels above
        // where the caller asked, which was the bug producing the
        // visible misalignment in x²+y², e^ip, matrix cells, etc.
        const float DrawTextLineHeightFactor = 1.3f;
        var lineHeight = _fontSize * DrawTextLineHeightFactor;
        var rectTop = baselineY - (lineHeight + _height - _depth) / 2f;

        var rect = new RectInt(
            new PointInt((int)MathF.Ceiling(penX + _width), (int)MathF.Ceiling(rectTop + lineHeight)),
            new PointInt((int)MathF.Floor(penX), (int)MathF.Floor(rectTop)));
        renderer.DrawText(_text, style.FontPath, _fontSize, style.Foreground, rect,
            horizAlignment: TextAlign.Near, vertAlignment: TextAlign.Near);
    }
}
