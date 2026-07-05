using System;
using System.Collections.Generic;
using System.Text;

namespace DIR.Lib;

/// <summary>
/// One positioned glyph produced by an <see cref="ITextShaper"/>. This is the seam A3's
/// HarfBuzz satellite plugs into (it emits substituted glyph ids, cluster maps, and GPOS
/// offsets); A4's caret math walks the <see cref="Cluster"/> indices.
///
/// <para><b>Advances stay with the renderer.</b> The renderer looks up each glyph's <i>base</i>
/// advance and bearings from its own glyph cache — which is raster-size- and atlas-specific
/// (the SDF atlas scales from a fixed raster size; the software renderer rounds the font size;
/// each derives whitespace advance from the <c>'n'</c> reference glyph). No single font-table
/// advance reproduces both bit-for-bit, so the shaper does <em>not</em> own the advance. Instead
/// it contributes only <see cref="XAdvanceAdjust"/> (kerning / GPOS advance adjustment) on top of
/// the cache advance, plus <see cref="XOffset"/>/<see cref="YOffset"/> positioning shifts. Under
/// the default <see cref="AdvanceShaper"/> all three are zero and clusters are 1:1 with runes, so
/// glyph placement is byte-identical to the pre-seam per-rune loop.</para>
///
/// <para><see cref="Source"/> is retained (rather than reduced to a glyph id) because the GPU
/// renderer routes color glyphs — emoji, symbols — by Unicode codepoint range, independently of
/// glyph identity; that routing needs the codepoint, not the id. <see cref="Glyph"/> is
/// <c>null</c> under <see cref="AdvanceShaper"/> (the renderer resolves identity from
/// <see cref="Source"/> exactly as before) and is set only by a substituting shaper (A3), where
/// the substituted id is authoritative and the renderer must key by it.</para>
/// </summary>
public readonly record struct ShapedGlyph(
    Rune Source,
    GlyphIdentity? Glyph,
    int Cluster,
    float XAdvanceAdjust,
    float XOffset,
    float YOffset);

/// <summary>
/// Turns a run of text into a sequence of positioned <see cref="ShapedGlyph"/>s. The default
/// implementation (<see cref="AdvanceShaper"/>) reproduces today's per-rune advance summation
/// exactly; a HarfBuzz-backed satellite (A3) can substitute ligatures, apply complex-script
/// reordering, and emit GPOS positioning behind the same interface.
/// </summary>
public interface ITextShaper
{
    /// <summary>
    /// Shape a single line of text (no embedded <c>'\n'</c> — callers split first) into
    /// <paramref name="output"/>. The list is cleared and refilled, so a caller can reuse one
    /// buffer across calls to stay allocation-free. <paramref name="rasterizer"/> supplies glyph
    /// identity / metrics for shapers that need them (the default shaper only touches it when
    /// kerning is enabled); <paramref name="fontSize"/> is the requested display size, used only
    /// to scale positioning adjustments into pixels.
    /// </summary>
    void Shape(ReadOnlySpan<char> text, string fontPath, float fontSize,
        ManagedFontRasterizer rasterizer, List<ShapedGlyph> output);
}

/// <summary>
/// The default, no-shaping shaper: one <see cref="ShapedGlyph"/> per Unicode rune, in input
/// order, clusters 1:1 with the runes, and — unless <see cref="AppliesKerning"/> — zero
/// positioning adjustments. This makes <see cref="Renderer{TSurface}.DrawText"/> /
/// <see cref="Renderer{TSurface}.MeasureText"/> byte-identical to the pre-seam per-rune loops:
/// the runes come from <see cref="MemoryExtensions.EnumerateRunes"/> (same source, same order,
/// same U+FFFD substitution for ill-formed input), and the renderer still sources every advance
/// and bearing from its own glyph cache.
///
/// <para><b>Optional kerning</b> (opt-in via the constructor) applies the font's GPOS pair
/// adjustment / legacy <c>kern</c> table as an <see cref="ShapedGlyph.XAdvanceAdjust"/> on the
/// left glyph of each pair. This is the "kerned text before any HarfBuzz work" win from the plan.
/// It is <b>off by default</b> deliberately: it changes rendered pixels, and two consumers assume
/// the un-kerned per-rune model — <c>MathLayout.GlyphBox</c> (which mirrors DrawText's positioning
/// math to place formula baselines) and <c>TextInputRenderer</c> (whose caret/selection X comes
/// from re-measuring substrings, which drifts by one pair's kern at the boundary once kerning is
/// on). Enable it for static display text, not for math layout or editable fields, until A4 lands
/// cluster-aware caret mapping.</para>
/// </summary>
public sealed class AdvanceShaper : ITextShaper
{
    /// <summary>Shared instance with kerning off — the default for every <see cref="Renderer{TSurface}"/>.</summary>
    public static readonly AdvanceShaper Default = new(applyKerning: false);

    private readonly bool _applyKerning;

    public AdvanceShaper(bool applyKerning = false) => _applyKerning = applyKerning;

    /// <summary>Whether this shaper folds the font's kerning into glyph advances (see the type remarks).</summary>
    public bool AppliesKerning => _applyKerning;

    public void Shape(ReadOnlySpan<char> text, string fontPath, float fontSize,
        ManagedFontRasterizer rasterizer, List<ShapedGlyph> output)
    {
        output.Clear();

        // Cluster is the UTF-16 offset of the rune within this run — the identity map A4 needs and
        // the same index MeasureText substrings are cut at. Placement never reads it, so it can't
        // perturb pixels; it is bookkeeping for callers that map screen positions back to text.
        var cluster = 0;

        // Kerning bookkeeping (only used when _applyKerning). We adjust the LEFT glyph of each pair
        // after we've seen the right one, so we remember the previous glyph's slot + gid. Type1 has
        // no gid-based kerning, so a Type1 glyph breaks the kerning chain (prevKernable = false).
        var prevGid = 0u;
        var prevKernable = false;
        var prevIndex = -1;

        foreach (var rune in text.EnumerateRunes())
        {
            output.Add(new ShapedGlyph(rune, Glyph: null, cluster, XAdvanceAdjust: 0f, XOffset: 0f, YOffset: 0f));

            if (_applyKerning)
            {
                // Resolve identity ONLY on the kerning path — the default (no-kern) shaper never
                // touches the rasterizer, so the common case stays zero-overhead.
                var id = rasterizer.ResolveGlyphIdentity(fontPath, rune, charCode: -1, GlyphMapHint.Auto);
                var kernable = !id.IsType1;
                if (prevKernable && kernable)
                {
                    var kern = rasterizer.GetKerningPx(fontPath, fontSize, prevGid, id.Gid);
                    if (kern != 0f)
                    {
                        var g = output[prevIndex];
                        output[prevIndex] = g with { XAdvanceAdjust = g.XAdvanceAdjust + kern };
                    }
                }
                prevGid = id.Gid;
                prevKernable = kernable;
                prevIndex = output.Count - 1;
            }

            cluster += rune.Utf16SequenceLength;
        }
    }
}
