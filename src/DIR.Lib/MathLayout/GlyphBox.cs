using System.Text;


namespace DIR.Lib.MathLayout;

/// <summary>
/// A leaf box wrapping a string of text rasterized at a fixed font size. The
/// box's <see cref="Box.Width"/> is the advance width of the text;
/// <see cref="Box.Height"/> is the ascent; <see cref="Box.Depth"/> is the
/// descent. Sizing uses <see cref="RgbaImageRenderer.MeasureText"/> against
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

        // We need a temporary renderer to measure — MeasureText is an
        // instance method on RgbaImageRenderer, but it doesn't depend on the
        // surface dimensions, only on the cached glyph metrics. Construct a
        // 1×1 throwaway just to get the rasterizer; the cache is per-instance
        // so this allocates a tiny new font cache. For the demo's small
        // formula corpus that's fine; if it ever matters, we'd thread a
        // shared rasterizer through BoxStyle instead.
        using var measurer = new RgbaImageRenderer(1, 1);
        var (w, h) = measurer.MeasureText(text, style.FontPath, fontSize);
        _width = w;

        // Tight TeX-style metrics: report the glyph's actual ascent/descent
        // as Height/Depth instead of inflating to DrawText's per-line
        // padding (lineHeight = fontSize * 1.3). MeasureText returns
        // combined visual height (ascent + descent); split 0.8/0.2 — close
        // enough for typical Latin/Greek/digits. (Letters with true
        // descenders like g/y/p get the ~20% descent they need; capitals
        // and digits over-claim a tiny amount of depth, but never collide
        // because nothing renders below the baseline for them.)
        //
        // The Draw() method below compensates for DrawText's internal
        // lineHeight padding by shifting rect.UpperLeft.Y, so the actual
        // glyph baseline still lands at the caller's baselineY even though
        // our reported Height is smaller than the rect we pass.
        _height = h * 0.8f;
        _depth  = h * 0.2f;
    }

    public override float Width => _width;
    public override float Height => _height;
    public override float Depth => _depth;

    /// <summary>Raw text rendered by this glyph box — exposed so callers can
    /// rebuild the same glyph at a different font size without losing the
    /// source string. Used by the LaTeX visitor's script-shrinking path.</summary>
    public string Text => _text;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        // DrawText computes baseline = rectTop + (lineHeight + ascent -
        // descent) / 2 with lineHeight = fontSize * 1.3 (a per-line
        // padding constant baked into the renderer). For our actual glyph
        // baseline to land at the caller's baselineY despite reporting
        // tight Height/Depth, we shift the rect-top up by half the slack
        // (lineHeight - actualHeight) / 2. The rect bounds otherwise don't
        // affect positioning under Near/Near alignment — DrawText doesn't
        // clip painted glyphs to the rect.
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
