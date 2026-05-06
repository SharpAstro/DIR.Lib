using System.Text;
using SharpAstro.Fonts;

namespace DIR.Lib.MathLayout;

/// <summary>
/// A <see cref="GlyphBox"/> that renders its text in a math style —
/// italic, bold, script, fraktur, double-struck, sans-serif, monospace.
/// Each rune is independently remapped at construction time to its
/// Unicode math-alphanumeric variant (U+1D400–U+1D7FF) when (a) the
/// Unicode mapping exists for the rune+style pair and (b) the font's
/// cmap covers the styled codepoint. Runes with no styled variant —
/// digits in italic style, Greek in monospace, anything outside the
/// supported ranges (operators, brackets, CJK) — pass through unchanged
/// in the same string.
///
/// <para><b>Why this is a separate class from <see cref="GlyphBox"/>:</b>
/// the rune remap has to happen <i>before</i> the rasterizer measures
/// glyphs, since the styled codepoint may have a different advance and
/// different glyph metrics from the upright form. By rebuilding the
/// underlying string in the constructor and delegating to
/// <see cref="GlyphBox"/>, all the existing layout math (kerning,
/// SupSubBox script-shrinking, AccentBox attachment lookup) keeps
/// working unchanged — it just sees the styled codepoints.</para>
///
/// <para>In published math typography, plain Latin and Greek letters
/// in expressions are conventionally italic (<i>F</i>, <i>D</i>,
/// <i>μ</i>, <i>x</i>). Greek lowercase glyphs in math fonts usually
/// already render as italic by font-design convention even at their
/// base codepoints (U+03B1–U+03C9), but Latin letters typed at U+0041–
/// U+007A render upright unless explicitly remapped — that's the
/// asymmetry this class flattens.</para>
///
/// <para>For non-math fonts (DejaVu, Roboto) without the U+1D400 block
/// in their cmap, every rune falls back to the upright original. The
/// formula renders the same as a plain <see cref="GlyphBox"/> — no
/// crash, no '.notdef' boxes, just no italic. Math fonts (STIX Two
/// Math, Latin Modern Math, Cambria Math) get the proper styled
/// glyphs.</para>
/// </summary>
public sealed class MathGlyphBox : Box
{
    private readonly GlyphBox _inner;

    public MathGlyphBox(string text, BoxStyle style, MathStyle mathStyle)
        : this(text, style, mathStyle, style.FontSize)
    { }

    public MathGlyphBox(string text, BoxStyle style, MathStyle mathStyle, float fontSize)
    {
        _inner = new GlyphBox(Restyle(text, style.FontPath, mathStyle), style, fontSize);
    }

    public override float Width => _inner.Width;
    public override float Height => _inner.Height;
    public override float Depth => _inner.Depth;

    /// <summary>The (possibly remapped) text the underlying
    /// <see cref="GlyphBox"/> ended up rendering. Useful for tests
    /// that need to verify which runes actually got styled.</summary>
    public string Text => _inner.Text;

    /// <summary>Forwarded so <see cref="AccentBox"/> can read the
    /// rasterization size when computing accent attachment.</summary>
    public float FontSize => _inner.FontSize;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
        => _inner.Draw(renderer, penX, baselineY, style);

    /// <summary>
    /// Remap each rune to its styled variant when the font supports
    /// it; pass through otherwise. Returns a brand-new string so the
    /// inner <see cref="GlyphBox"/> sees a styled codepoint sequence.
    /// </summary>
    private static string Restyle(string text, string fontPath, MathStyle mathStyle)
    {
        if (mathStyle == MathStyle.Normal || text.Length == 0) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var styled = BoxStyle.SharedRasterizer.TryGetMathStyledRune(fontPath, rune, mathStyle);
            sb.Append((styled ?? rune).ToString());
        }
        return sb.ToString();
    }
}
