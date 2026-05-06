using System.Text;

namespace DIR.Lib.MathLayout;

/// <summary>
/// A vertically-stretchy delimiter box backed by the loaded font's OpenType
/// MATH table — the same recipe TeX/MathML engines use to render scalable
/// parens, brackets, braces, radicals, and other delimiters at any required
/// height. Wraps <see cref="ManagedFontRasterizer.RasterizeStretchyVertical"/>:
/// at construction time the bitmap is composed (variant or assembly), then
/// <see cref="Draw"/> blits the cached bitmap with the box's foreground tint.
///
/// <para>The composed bitmap is centered on the math axis (≈ <c>FontSize/4</c>
/// above the baseline by TeX convention). <see cref="Height"/> reports the
/// pixels above the baseline (axis + half the bitmap), <see cref="Depth"/>
/// reports what hangs below (half the bitmap minus axis).</para>
///
/// <para>Use <see cref="IsAvailable"/> to check whether MATH-driven rendering
/// succeeded — false when the font has no MATH table, the codepoint isn't in
/// the vertical coverage, or the codepoint isn't mapped at all. Callers
/// should fall back to a parametric drawing path in that case (e.g.
/// <see cref="BracketBox"/>'s ellipse-arc, <see cref="SqrtBox"/>'s hook +
/// vinculum).</para>
/// </summary>
public sealed class StretchyVerticalBox : Box
{
    /// <summary>
    /// Shared rasterizer so font cache hits accumulate across StretchyVerticalBox
    /// instances and across <see cref="BoxStyle"/> metric lookups. Same lifetime
    /// as the AppDomain — fonts are pure-managed and hold no native handles, so
    /// a static cache costs only a few KB per loaded font (the parsed OpenType
    /// tables). Reuses the singleton owned by <see cref="BoxStyle"/>.
    /// </summary>
    private static ManagedFontRasterizer SharedRasterizer => BoxStyle.SharedRasterizer;

    private readonly GlyphBitmap _bitmap;
    private readonly float _height;
    private readonly float _depth;
    private readonly uint _variantGlyphId;

    /// <summary>
    /// Compose a stretchy delimiter for <paramref name="codepoint"/> covering
    /// at least <paramref name="requiredHeightPx"/> using <paramref name="style"/>'s
    /// font + size. If the font has no MATH coverage for this codepoint the
    /// box reports <see cref="IsAvailable"/> = false and zero metrics — the
    /// caller should pick another rendering path.
    /// </summary>
    public StretchyVerticalBox(int codepoint, float requiredHeightPx, BoxStyle style)
    {
        var rune = new Rune(codepoint);
        _bitmap = SharedRasterizer.RasterizeStretchyVertical(style.FontPath, style.FontSize, rune, requiredHeightPx, out _variantGlyphId);

        if (_bitmap.Rgba is null || _bitmap.Width == 0 || _bitmap.Height == 0)
        {
            _height = 0;
            _depth = 0;
            return;
        }

        // Centre the composed bitmap on the math axis. BoxStyle.AxisHeight
        // is currently FontSize/4 (TeX convention) — see the docstring there
        // for why we don't read MATH.AxisHeight directly today. Keeps brackets
        // visually centred on the same level that '+', '=', '−' GlyphBoxes
        // naturally land at.
        var axis = style.AxisHeight;
        var halfHeight = _bitmap.Height / 2f;
        _height = axis + halfHeight;
        _depth = halfHeight - axis;
        if (_depth < 0) _depth = 0;
    }

    /// <summary>True when the font produced a real bitmap for this codepoint.
    /// False = no MATH table, or codepoint isn't covered, or isn't mapped at
    /// all. <see cref="Width"/> / <see cref="Height"/> / <see cref="Depth"/>
    /// will all be zero in that case.</summary>
    public bool IsAvailable => _bitmap.Rgba is not null && _bitmap.Width > 0;

    /// <summary>The glyph id of the variant the font picked to satisfy
    /// <c>requiredHeightPx</c>. Non-zero when <see cref="IsAvailable"/>;
    /// equal to the BASE glyph's id when the bitmap was composed via
    /// MATH assembly (no single id covers the result, so the base is
    /// the sensible fallback for metric lookups). Used by
    /// <see cref="SupSubBox"/> to query the variant's own
    /// <c>MathItalicsCorrection</c> rather than scaling the base's
    /// — designers tune the value per variant size.</summary>
    public uint VariantGlyphId => _variantGlyphId;

    /// <summary>The composed RGBA bitmap (variant or assembly). Exposed so
    /// e.g. <see cref="SqrtBox"/> can scan the top rows to find the radical
    /// flag's actual y-offset and diagonal thickness — needed to align a
    /// continuation rule (vinculum) with the glyph's real ink, not its
    /// bounding-box top edge (which usually has a row or two of transparent
    /// padding).</summary>
    internal GlyphBitmap Bitmap => _bitmap;

    public override float Width => _bitmap.Width;
    public override float Height => _height;
    public override float Depth => _depth;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        if (!IsAvailable) return;

        // Top edge = baselineY − Height. Blit with the box's foreground tint;
        // grayscale glyph alpha is multiplied by the color, color glyphs (rare
        // for delimiters) blit as-is.
        var top = baselineY - _height;
        renderer.DrawGlyphBitmap(
            (int)MathF.Floor(penX),
            (int)MathF.Floor(top),
            _bitmap,
            style.Foreground);
    }
}
