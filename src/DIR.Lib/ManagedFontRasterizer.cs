using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using SharpAstro.Fonts;
using SharpAstro.Fonts.Outlines;
using FontsHint = SharpAstro.Fonts.Tables.Cmap.GlyphMapHint;
using Tables = SharpAstro.Fonts.Tables;
using T1 = SharpAstro.Fonts.Type1;
using Rast = SharpAstro.Fonts.Rasterizer;

namespace DIR.Lib;

/// <summary>
/// Pure-managed glyph rasterizer backed by SharpAstro.Fonts.
/// Took over from the FreeType-backed rasterizer this library used to carry,
/// keeping its public API — no native dependencies, no GC pinning, AOT-compatible.
///
/// <para>Loaded fonts are cached per (path or memory-id). Cache lookup is
/// lock-free (<see cref="ConcurrentDictionary{TKey,TValue}"/>); per-glyph
/// rendering allocates only the result bitmap.</para>
/// </summary>
public sealed class ManagedFontRasterizer : IDisposable
{
    private readonly ConcurrentDictionary<string, OpenTypeFont> _fonts = new();

    // Embedded Adobe Type1 (/FontFile, PFB) fonts, keyed by the same id as _fonts. Type1 is a
    // different container than SFNT (PostScript dict + eexec-encrypted charstrings) so it can't go
    // through OpenTypeFont; it's looked up by char code → glyph name via the font's own Encoding.
    private readonly ConcurrentDictionary<string, T1.Type1Font> _type1Fonts = new();

    // Per-font char code → PostScript glyph name from the PDF /Encoding (base encoding +
    // /Differences). Authoritative over anything inside the font program — §9.6.6.2 has a named
    // base encoding REPLACE the built-in encoding, and /Differences override individual codes.
    // Consulted for every embedded simple font: Type1 resolves the name against its charstrings,
    // OpenType via GetGlyphIdByName (the CFF charset — the only route into a bare name-keyed CFF
    // with no cmap and no Encoding operator, e.g. the Canon manual's Lithos-Bold subset).
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<int, string>> _pdfEncoding = new();

    // Session-stable memo for ResolveGlyphIdentity: the same (font, codepoint, charCode, hint)
    // always resolves to the same identity once the font is registered, but atlas consumers that
    // key by identity hit the resolve on every draw — per glyph, per frame, even on atlas hits
    // (the multi-subtable probing in GetGlyphIdHinted is the cost). Misses are deliberately NOT
    // cached: an unregistered "mem:" font racing its first draw resolves to default and must
    // re-resolve once the bytes land, not pin a permanent blank. RegisterType1Encoding
    // invalidates its font's entries — /Differences overrides win over the built-in encoding
    // and may be registered after a glyph was already resolved.
    private readonly ConcurrentDictionary<(string Font, int Codepoint, int CharCode, GlyphMapHint Hint), GlyphIdentity> _identityMemo = new();

    // Char-code → gid for a loaded OpenType face, the PDF's named glyph first: a registered
    // /Encoding designates glyphs BY NAME (code → name → glyph), that designation is
    // authoritative over any cmap strategy, and for a bare name-keyed CFF with no cmap it is
    // the only route that exists. TrueType faces return 0 from the name lookup and fall
    // through to the cmap/char-code strategies unchanged.
    private uint ResolveOpenTypeGid(string fontPath, OpenTypeFont font, uint codepoint, uint charCode, FontsHint hint)
    {
        if (charCode <= int.MaxValue && _pdfEncoding.TryGetValue(fontPath, out var enc)
            && enc.TryGetValue((int)charCode, out var name))
        {
            var byName = font.GetGlyphIdByName(name);
            if (byName != 0) return byName;
        }
        return font.GetGlyphId(codepoint, charCode, hint);
    }

    /// <summary>
    /// Rasterize a glyph with PDF char-code + cmap lookup hint.
    /// </summary>
    public GlyphBitmap RasterizeGlyphWithCharCode(string fontPath, float fontSize,
        Rune codepoint, uint charCode, GlyphMapHint hint = GlyphMapHint.Auto)
    {
        // Embedded Type1: the char code resolves to a glyph name (PDF /Differences override first).
        if (_type1Fonts.TryGetValue(fontPath, out var t1))
            return RenderType1(t1, ResolveType1Name(fontPath, t1, charCode), fontSize);
        var font = GetOrLoad(fontPath);
        // DIR.Lib.GlyphMapHint and SharpAstro.Fonts' enum share value layout
        // (Auto=0, EmbeddedSubset=1, CharCodeIsGID=2, Unicode=3) so the cast
        // is a no-op at runtime.
        var gid = ResolveOpenTypeGid(fontPath, font, (uint)codepoint.Value, charCode, (FontsHint)hint);
        if (gid == 0) return default;
        return Render(font, gid, fontSize);
    }

    /// <summary>
    /// Rasterize a glyph by Unicode codepoint via the preferred Unicode cmap.
    /// </summary>
    public GlyphBitmap RasterizeGlyph(string fontPath, float fontSize, Rune codepoint)
    {
        // Type1 has no Unicode cmap; for the ASCII range the code point equals the char code, which
        // is enough for the resolver's "render 'A'" probe and any Unicode-keyed draw of basic Latin.
        if (_type1Fonts.TryGetValue(fontPath, out var t1))
            return RenderType1(t1, ResolveType1Name(fontPath, t1, (uint)codepoint.Value), fontSize);
        var font = GetOrLoad(fontPath);
        var gid = font.GetGlyphId((uint)codepoint.Value);
        if (gid == 0) return default;
        return Render(font, gid, fontSize);
    }

    /// <summary>
    /// Rasterize a glyph directly by glyph id, bypassing cmap lookup. Useful
    /// when the caller already has a gid in hand — e.g. an OpenType MATH
    /// variant or assembly piece, where the relevant glyphs are referenced by
    /// id and may not have any Unicode codepoint mapped to them at all.
    /// </summary>
    public GlyphBitmap RasterizeGlyphByGid(string fontPath, float fontSize, uint gid)
    {
        if (gid == 0) return default;
        var font = GetOrLoad(fontPath);
        return Render(font, gid, fontSize);
    }

    /// <summary>
    /// <see cref="RasterizeGlyph"/> with the glyph moved vertically, by at most half a pixel, so the top of
    /// its outline lands exactly on a pixel row and the bitmap's first row is fully covered rather than
    /// antialiased. Opt-in, and used only by <see cref="MathLayout.SqrtBox"/>: the radical's flag is the
    /// topmost part of the glyph, and the vinculum continuing it can only share a crisp top edge with it if
    /// that edge is on a row (see <see cref="RenderTopSnapped"/>).
    /// </summary>
    /// <param name="snap">Where the outline's top begins in the bitmap (row 0 after a snap) and how many
    /// rows the stroke at its top-right touches; <see cref="TopSnap.None"/> when nothing was snapped: a
    /// colour glyph, or an embedded Type 1 face.</param>
    internal GlyphBitmap RasterizeGlyphTopSnapped(string fontPath, float fontSize, Rune codepoint, out TopSnap snap)
    {
        snap = TopSnap.None;
        if (_type1Fonts.ContainsKey(fontPath)) return RasterizeGlyph(fontPath, fontSize, codepoint);
        var font = GetOrLoad(fontPath);
        var gid = font.GetGlyphId((uint)codepoint.Value);
        if (gid == 0) return default;
        return RenderTopSnapped(font, gid, fontSize, out snap);
    }

    /// <summary>
    /// Look up the OpenType MATH vertical-stretch construction for a Unicode
    /// codepoint — the recipe used to build a scalable radical, paren, brace,
    /// or other delimiter at an arbitrary height. Returns null if the font
    /// has no MATH table, or if this codepoint is not in the table's vertical
    /// coverage. Use the returned <see cref="Tables.OpenTypeMath.MathGlyphConstruction"/> to walk
    /// pre-drawn variants and (beyond the largest variant) the assembly recipe.
    /// </summary>
    public Tables.OpenTypeMath.MathGlyphConstruction? GetVerticalMathConstruction(string fontPath, Rune codepoint)
    {
        var font = GetOrLoad(fontPath);
        var math = font.Math;
        if (math is null) return null;
        var gid = font.GetGlyphId((uint)codepoint.Value);
        if (gid == 0) return null;
        return math.GetVerticalConstruction((ushort)gid);
    }

    /// <summary>
    /// MATH-table units-per-em + global stretch metadata for a font, exposed
    /// so callers doing assembly composition can convert the part records'
    /// FUnit measurements (full advance, connector lengths, MinConnectorOverlap)
    /// to pixel space at the desired font size. Returns null if the font has
    /// no MATH table.
    /// </summary>
    public (ushort unitsPerEm, ushort minConnectorOverlap)? GetMathStretchInfo(string fontPath)
    {
        var font = GetOrLoad(fontPath);
        return font.Math is null ? null : (font.UnitsPerEm, font.Math.MinConnectorOverlap);
    }

    /// <summary>
    /// The FACE's own vertical metrics at <paramref name="fontSize"/>, in pixels: how far this font says
    /// its glyphs rise above and fall below the baseline, independent of which glyphs a caller happens to
    /// be drawing. <c>Descent</c> is a positive DEPTH, not hhea's negative offset.
    /// </summary>
    /// <remarks>
    /// <para>This exists so a renderer can place a baseline WITHOUT measuring the run. Deriving it from the
    /// run's own ink -- the tallest ascender and deepest descender actually present -- makes the baseline
    /// depend on the text, and independently drawn labels of the SAME font and size then fail to share one:
    /// "a" lands at one height, "b" lower because its ascender inflates the box, "g" higher because its
    /// descender does. A row of chess file letters a-h visibly stair-steps at b, d and g.</para>
    /// <para>hhea rather than OS/2: it is the table every SFNT face carries and the one classically meant
    /// for line layout. Null when the face declares none -- a caller then has nothing better than the run's
    /// own extents, and should fall back to them rather than invent a ratio.</para>
    /// </remarks>
    public (float Ascent, float Descent, float LineGap)? GetVerticalMetrics(string fontPath, float fontSize)
    {
        var font = GetOrLoad(fontPath);
        if (font.Hhea is not { } hhea || font.UnitsPerEm == 0) return null;

        var scale = fontSize / font.UnitsPerEm;
        // hhea states the descender as a negative offset FROM the baseline; every caller wants a depth.
        return (hhea.Ascender * scale, -hhea.Descender * scale, hhea.LineGap * scale);
    }

    /// <summary>
    /// Global OpenType MATH metrics (axis height, fraction/radical rule thickness, etc.)
    /// for a font, paired with the font's UnitsPerEm so callers can convert the
    /// FUnit values to pixels via <c>fUnits * fontSize / unitsPerEm</c>. Returns
    /// null if the font has no MATH table — caller should fall back to ad-hoc
    /// defaults (e.g. <see cref="MathLayout.BoxStyle"/>'s magic-number defaults).
    /// </summary>
    public (Tables.OpenTypeMath.MathConstants constants, ushort unitsPerEm)? GetMathConstants(string fontPath)
    {
        var font = GetOrLoad(fontPath);
        return font.Math is null ? null : (font.Math.Constants, font.UnitsPerEm);
    }

    /// <summary>
    /// Try to return the styled-variant rune (italic, bold, script, …)
    /// for <paramref name="codepoint"/> in the font at
    /// <paramref name="fontPath"/>. Returns the rune corresponding to
    /// the Unicode math-alphanumeric codepoint when (a) a Unicode
    /// mapping exists for (codepoint, style) and (b) the font's cmap
    /// covers that mapped codepoint. Returns null otherwise — caller
    /// should fall back to the original codepoint, which is exactly
    /// what <see cref="MathLayout.GlyphBox"/> does at construction
    /// time when this helper is wrapped behind a styled-glyph
    /// constructor.
    /// </summary>
    public Rune? TryGetMathStyledRune(string fontPath, Rune codepoint, MathStyle style)
    {
        if (style == MathStyle.Normal) return codepoint;
        var font = GetOrLoad(fontPath);
        var gid = font.GetMathVariantGlyphId((uint)codepoint.Value, style);
        if (gid == 0) return null;
        // The styled-variant codepoint is what the cmap matched against;
        // recompute it (cheap pure lookup) so the caller can rebuild text.
        var mapped = MathAlphanumerics.MapCodepoint((uint)codepoint.Value, style);
        return mapped.HasValue ? new Rune((int)mapped.Value) : null;
    }

    /// <summary>
    /// Math corner kern (pixels at the requested font size) for the
    /// given codepoint at correction <paramref name="heightPx"/> —
    /// the sub/super's bottom-y above the math axis (for top corners)
    /// or its top-y above the math axis (for bottom corners). The
    /// kern's step function is keyed on this height; positive results
    /// shift the script right, negative shift left. Returns null when
    /// the font has no MATH table, no <c>MathGlyphInfo</c>, no kern
    /// coverage for this glyph, or no data for the requested corner —
    /// caller falls back to italic correction or zero as appropriate.
    /// Internally converts the pixel height to FU before lookup, then
    /// converts the kern back to pixels at the same scale.
    /// </summary>
    public float? GetMathCornerKernPx(string fontPath, float fontSize,
        Rune codepoint, Tables.OpenTypeMath.MathKernCorner corner, float heightPx)
    {
        var font = GetOrLoad(fontPath);
        var kern = font.GetMathCornerKern((uint)codepoint.Value, corner);
        if (kern is null) return null;
        var heightFU = (short)MathF.Round(heightPx * font.UnitsPerEm / fontSize);
        var kernFU = kern.Lookup(heightFU);
        return kernFU * fontSize / font.UnitsPerEm;
    }

    /// <summary>
    /// Italic correction (pixels at the requested font size) for the
    /// given codepoint — the extra horizontal space a slanted glyph's
    /// top extends past its advance width. OpenType MATH supplies this
    /// in <c>MathItalicsCorrectionInfo</c>; it's the canonical input
    /// for placing a superscript on a slanted base (italic <c>f</c>,
    /// big integral, big radical) so the script clears the slope.
    /// Returns null when the font has no MATH table, no
    /// <c>MathGlyphInfo</c> subtable, or the glyph isn't in the italic-
    /// correction coverage — caller treats that as "no correction"
    /// (zero shift, same as upright glyphs).
    /// </summary>
    public float? GetItalicsCorrectionPx(string fontPath, float fontSize, Rune codepoint)
    {
        var font = GetOrLoad(fontPath);
        var info = font.Math?.GlyphInfo;
        if (info is null) return null;
        var gid = font.GetGlyphId((uint)codepoint.Value);
        if (gid == 0) return null;
        var fu = info.GetItalicsCorrection((ushort)gid);
        if (fu == 0) return null;
        return fu * fontSize / font.UnitsPerEm;
    }

    /// <summary>
    /// Italic correction (pixels at the requested font size) for a
    /// glyph identified directly by its glyph id. Used to look up
    /// metrics on stretchy variant glyphs that aren't reachable via
    /// the cmap — the variant's id comes from
    /// <see cref="RasterizeStretchyVertical(string, float, Rune, float, out uint)"/>'s
    /// out parameter, then this accessor finds the correction the
    /// font designer set for that specific size of the glyph.
    /// Returns null when the glyph isn't in the italic-correction
    /// coverage; caller should fall back to the base codepoint's
    /// correction (or zero for upright glyphs).
    /// </summary>
    public float? GetItalicsCorrectionByGidPx(string fontPath, float fontSize, uint glyphId)
    {
        var font = GetOrLoad(fontPath);
        var info = font.Math?.GlyphInfo;
        if (info is null || glyphId == 0) return null;
        var fu = info.GetItalicsCorrection((ushort)glyphId);
        if (fu == 0) return null;
        return fu * fontSize / font.UnitsPerEm;
    }

    /// <summary>
    /// Top-accent attachment x-coordinate (pixels, measured from the
    /// glyph's left edge at the requested font size) for the given
    /// codepoint. Drives where a math accent (macron, hat, tilde, …)
    /// anchors over a base — without this the accent floats at
    /// <c>advance / 2</c>, which lands off-centre on slanted letters
    /// (italic ψ leans, so its visual centre isn't at half the advance).
    /// Returns null when the font has no MATH table, no <c>MathGlyphInfo</c>
    /// subtable, or the glyph isn't in the <c>MathTopAccentAttachment</c>
    /// coverage. Callers should fall back to <c>advance / 2</c>.
    /// </summary>
    public float? GetTopAccentAttachmentPx(string fontPath, float fontSize, Rune codepoint)
    {
        var font = GetOrLoad(fontPath);
        var info = font.Math?.GlyphInfo;
        if (info is null) return null;
        var gid = font.GetGlyphId((uint)codepoint.Value);
        if (gid == 0) return null;
        var fu = info.GetTopAccentAttachment((ushort)gid);
        if (fu is null) return null;
        return fu.Value * fontSize / font.UnitsPerEm;
    }

    /// <summary>
    /// Rasterize a stretchy delimiter (paren, bracket, brace, radical, etc.)
    /// at a height that covers <paramref name="requiredHeightPx"/> using the
    /// font's OpenType MATH vertical-construction recipe. Algorithm:
    /// <list type="number">
    /// <item>If the unstretched base glyph is already tall enough, use it.</item>
    /// <item>Else walk the construction's pre-drawn variants and pick the
    /// smallest whose advance covers the request.</item>
    /// <item>Else compose the assembly: each non-extender part appears once,
    /// extender parts repeat as many times as needed to reach the request.</item>
    /// </list>
    /// Returns a default <see cref="GlyphBitmap"/> (Rgba == null, Width == 0)
    /// when the result wouldn't be useful — codepoint not in the font, or the
    /// font has no MATH data and the unstretched base glyph isn't tall
    /// enough to cover the request. Callers must handle this by falling back
    /// to a non-MATH path (e.g. parametric drawing in SqrtBox / BracketBox).
    /// When the base glyph IS tall enough, it's returned unchanged regardless
    /// of MATH presence.
    /// </summary>
    public GlyphBitmap RasterizeStretchyVertical(string fontPath, float fontSize,
        Rune codepoint, float requiredHeightPx)
        => RasterizeStretchyVertical(fontPath, fontSize, codepoint, requiredHeightPx, out _);

    /// <summary>
    /// Same as <see cref="RasterizeStretchyVertical(string, float, Rune, float)"/>
    /// but also returns the glyph id of the variant that was rendered.
    /// <list type="bullet">
    /// <item>Variant path or "base already tall enough" path → the
    /// chosen single glyph's id (caller can look up its
    /// <c>MathItalicsCorrection</c>, corner kerns, etc.).</item>
    /// <item>Assembly path → the BASE glyph's id (assembly-composed
    /// glyphs don't have a single glyph id, so the base is the only
    /// reasonable fallback for metric lookup).</item>
    /// <item>No coverage → 0 (caller should use base codepoint).</item>
    /// </list>
    /// </summary>
    public GlyphBitmap RasterizeStretchyVertical(string fontPath, float fontSize,
        Rune codepoint, float requiredHeightPx, out uint variantGlyphId)
        => RasterizeStretchyVertical(fontPath, fontSize, codepoint, requiredHeightPx,
            snapTop: false, out variantGlyphId, out _);

    /// <summary>
    /// <see cref="RasterizeStretchyVertical(string, float, Rune, float, out uint)"/> with an opt-in
    /// <paramref name="snapTop"/>: the chosen glyph is moved vertically by at most half a pixel so the top
    /// of its outline lands on a pixel row, as <see cref="RasterizeGlyphTopSnapped"/> does. Of an assembly
    /// only the topmost part is snapped (see <see cref="ComposeVerticalAssembly"/> for why). Which glyph is
    /// chosen does not depend on <paramref name="snapTop"/>; with it false the result is the public
    /// method's, byte for byte, which is what every caller but the radical gets.
    /// </summary>
    /// <param name="snap">Where the outline's top begins in the bitmap and how many rows the stroke at
    /// its top-right touches; <see cref="TopSnap.None"/> when nothing was snapped
    /// (<paramref name="snapTop"/> false, or a colour glyph).</param>
    internal GlyphBitmap RasterizeStretchyVertical(string fontPath, float fontSize,
        Rune codepoint, float requiredHeightPx, bool snapTop, out uint variantGlyphId, out TopSnap snap)
        => RasterizeStretchyVerticalCore(fontPath, fontSize, codepoint, requiredHeightPx, requiredHeightPx, snapTop,
            out variantGlyphId, out snap, out _);

    /// <summary>
    /// <see cref="RasterizeStretchyVertical(string, float, Rune, float)"/>, also saying where each part of an
    /// assembly landed in the composed bitmap; <paramref name="parts"/> is null when no assembly was built
    /// (the base glyph or a size variant covered the request).
    /// </summary>
    internal GlyphBitmap RasterizeStretchyVerticalWithParts(string fontPath, float fontSize,
        Rune codepoint, float requiredHeightPx, out AssemblyPart[]? parts)
        => RasterizeStretchyVerticalCore(fontPath, fontSize, codepoint, requiredHeightPx, requiredHeightPx, snapTop: false,
            out _, out _, out parts);

    /// <summary>
    /// The least height a <c>\left..\right</c> delimiter may be for a request of <paramref name="requestPx"/>:
    /// max(0.901 x request, request - 0.5 em), TeX's <c>\delimiterfactor</c> 901 and <c>\delimitershortfall</c>
    /// 5 pt at a 10 pt design size.
    /// </summary>
    internal static float DelimiterCovers(float requestPx, float fontSize) =>
        MathF.Max(0.901f * requestPx, requestPx - 0.5f * fontSize);

    /// <summary>
    /// <see cref="RasterizeStretchyVertical(string, float, Rune, float, out uint)"/> for a <c>\left..\right</c>
    /// delimiter, which may fall short of its request by TeX's rule (<see cref="DelimiterCovers"/>): the
    /// unstretched glyph or a size variant covers it from that height. An assembly is still built to the
    /// whole request, since it can reach it.
    /// </summary>
    internal GlyphBitmap RasterizeStretchyDelimiter(string fontPath, float fontSize,
        Rune codepoint, float requiredHeightPx, out uint variantGlyphId, out AssemblyPart[]? parts)
        => RasterizeStretchyVerticalCore(fontPath, fontSize, codepoint, requiredHeightPx,
            DelimiterCovers(requiredHeightPx, fontSize), snapTop: false, out variantGlyphId, out _, out parts);

    /// <summary>One part of a composed assembly as drawn: its glyph, and the rows of the composed bitmap its
    /// ink box spans, Y down, as fractions of a pixel.</summary>
    internal readonly record struct AssemblyPart(uint GlyphId, bool IsExtender, float Top, float Bottom);

    /// <param name="coversFromPx">The least height at which the unstretched glyph or a size variant counts
    /// as covering the request: the request itself, or less for a delimiter (<see cref="DelimiterCovers"/>).</param>
    private GlyphBitmap RasterizeStretchyVerticalCore(string fontPath, float fontSize,
        Rune codepoint, float requiredHeightPx, float coversFromPx, bool snapTop, out uint variantGlyphId,
        out TopSnap snap, out AssemblyPart[]? parts)
    {
        variantGlyphId = 0;
        snap = TopSnap.None;
        parts = null;
        var font = GetOrLoad(fontPath);
        var baseGid = font.GetGlyphId((uint)codepoint.Value);
        if (baseGid == 0) return default;

        // 0) Base glyph already covers the request — no stretching needed.
        //    This branch fires for short content even on math-less fonts and
        //    is the cheap path: just render the unstretched glyph and return.
        //    The decision is taken on the unsnapped bitmap, so snapping never
        //    changes which glyph a request gets.
        var baseGlyph = Render(font, baseGid, fontSize);
        if (baseGlyph.Height >= coversFromPx)
        {
            variantGlyphId = baseGid;
            return snapTop ? RenderTopSnapped(font, baseGid, fontSize, out snap) : baseGlyph;
        }

        // The base glyph isn't tall enough. From here we need MATH data — if
        // the font has none (or doesn't cover this codepoint vertically),
        // return empty so the caller can pick a non-MATH fallback (e.g.
        // parametric drawing in SqrtBox / BracketBox). Returning the too-
        // short base glyph would silently produce visually wrong output for
        // tall content like matrices and big radicands.
        var math = font.Math;
        if (math is null) return default;

        var construction = math.GetVerticalConstruction((ushort)baseGid);
        if (construction is null) return default;

        // 1) Variants — pick smallest whose AdvanceMeasurement (FUnits) covers the request.
        var unitsPerEm = font.UnitsPerEm;
        var requiredFUnits = (int)Math.Ceiling(coversFromPx * unitsPerEm / fontSize);
        foreach (var v in construction.Variants)
        {
            if (v.AdvanceMeasurement >= requiredFUnits)
            {
                variantGlyphId = v.GlyphId;
                return RenderMaybeTopSnapped(font, v.GlyphId, fontSize, snapTop, out snap);
            }
        }

        // 2) Assembly path — composed from multiple parts; no single glyph
        //    id. Report the base glyph id so callers can still look up
        //    metrics (italic correction, etc.) against something sensible.
        if (construction.Assembly is { } asm)
        {
            variantGlyphId = baseGid;
            return ComposeVerticalAssembly(font, asm, math.MinConnectorOverlap, unitsPerEm, fontSize,
                requiredHeightPx * unitsPerEm / fontSize,
                snapTop, out snap, out parts);
        }

        // 3) Largest variant if any (still possibly short of the request, but
        //    closer than the base). If the construction has neither variants
        //    nor assembly we already returned default above.
        if (construction.Variants.Count > 0)
        {
            variantGlyphId = construction.Variants[^1].GlyphId;
            return RenderMaybeTopSnapped(font, construction.Variants[^1].GlyphId, fontSize, snapTop, out snap);
        }
        return default;
    }

    private static GlyphBitmap RenderMaybeTopSnapped(OpenTypeFont font, uint gid, float pixelsPerEm, bool snapTop, out TopSnap snap)
    {
        if (snapTop) return RenderTopSnapped(font, gid, pixelsPerEm, out snap);
        snap = TopSnap.None;
        return Render(font, gid, pixelsPerEm);
    }

    /// <summary>
    /// Compose a stretchy vertical glyph from an OpenType MATH assembly recipe, as the specification (and
    /// MathML Core after it) lays one out. Parts are listed bottom-up.
    /// <list type="number">
    /// <item>Every extender appears the same number of times, r, from none upward: the fewest whose height
    /// reaches the request with every join at the table's <c>MinConnectorOverlap</c>, the least it may
    /// overlap. Taking the most the connectors allow instead (what this did) lets an extender whose
    /// connectors are as long as itself add next to nothing a copy: STIX Two Math's radical extender
    /// (FullAdvance 651, connectors 650) added one font unit, so the loop met its cap a little over two em
    /// tall and the radical came out shorter than its radicand. And always one copy of each extender put
    /// DejaVu Sans's parenthesis at 3.5 em where the two ends alone make 2.4.</item>
    /// <item>The overlap left over, the height past the request, is spread evenly across the joins, each
    /// held between <c>MinConnectorOverlap</c> and what its two connectors allow, so the glyph lands on the
    /// request. It is shorter only at the cap, and taller only where no extender at all already overshoots
    /// (the assembly's minimum).</item>
    /// <item>The parts are placed in font units, each one's ink box starting where the one below ends less
    /// the join's overlap, and drawn as one outline at those fractional offsets. A join is then a union of
    /// overlapping ink, never two antialiased edges stacked on whole pixels, so it shows no seam.</item>
    /// </list>
    /// The parts' design outlines are drawn, not hinted ones: a hinter fits each part to the pixel grid on
    /// its own, which moves its ends, and they would no longer meet where the font units say.
    /// <para>With <paramref name="snapTop"/> the whole outline moves up by under half a pixel so the top of
    /// the topmost part (the radical's flag) lands on a pixel row, and the stroke at its top-right is
    /// measured on that part alone, as <see cref="RenderTopSnapped"/> does for a single glyph. Moving every
    /// part by the one shift keeps the joins where they were.</para>
    /// </summary>
    /// <param name="requiredUnits">The height asked for, in font units.</param>
    /// <param name="placed">Each part as drawn, bottom-up: its ink box in the bitmap's rows (Y down).</param>
    private static GlyphBitmap ComposeVerticalAssembly(
        OpenTypeFont font, Tables.OpenTypeMath.MathGlyphAssembly asm,
        ushort minOverlap, ushort unitsPerEm, float fontSize, float requiredUnits,
        bool snapTop, out TopSnap snap, out AssemblyPart[]? placed)
    {
        snap = TopSnap.None;
        placed = null;
        if (asm.Parts.Count == 0) return default;

        var sequence = AssemblySequence(asm, minOverlap, requiredUnits);
        var overlaps = SpreadOverlaps(sequence, minOverlap, requiredUnits);

        // Each part's ink box, from its outline: the part is placed by where its ink starts, which for these
        // parts is its FullAdvance tall (STIX Two Math's and DejaVu Sans's alike, to a unit).
        var inkBottom = new float[sequence.Length];
        var bottoms = new float[sequence.Length];
        var boundsCache = new Dictionary<ushort, (float YMin, float YMax)>();
        for (var i = 0; i < sequence.Length; i++)
        {
            var gid = sequence[i].GlyphId;
            if (!boundsCache.TryGetValue(gid, out var bounds))
            {
                var sink = new TopEdgeSink();
                font.DrawGlyph(gid, sink);
                bounds = sink.HasPoints ? (sink.YMin, sink.YMax) : (0f, sequence[i].FullAdvance);
                boundsCache[gid] = bounds;
            }
            inkBottom[i] = bounds.YMin;
            bottoms[i] = i == 0 ? 0f : bottoms[i - 1] + sequence[i - 1].FullAdvance - overlaps[i - 1];
        }

        var scale = fontSize / unitsPerEm;
        var top = sequence.Length - 1;
        var topUnits = bottoms[top] + boundsCache[sequence[top].GlyphId].YMax - inkBottom[top];

        // An assembly whose least reachable height still overshoots the request past what a delimiter may
        // (the larger of 1.25 times the request and the request plus half an em) is squashed vertically to
        // the request. DejaVu Sans ships no size variants, and parts 1.2 em tall whose connectors leave no
        // room to overlap more, so its two-part parenthesis alone is 1.29 times a 1/2 fraction and its brace
        // 1.92 times. STIX Two Math, with variants and joins that stretch, never comes near the bound.
        var requestPx = requiredUnits * scale;
        var heightPx = topUnits * scale;
        var yScale = heightPx > MathF.Max(1.25f * requestPx, requestPx + 0.5f * fontSize)
            ? scale * (requestPx / heightPx)
            : scale;
        void Walk(IGlyphSink sink, float shift)
        {
            for (var i = 0; i < sequence.Length; i++)
                font.DrawGlyph(sequence[i].GlyphId,
                    new ScaleShiftSink(sink, scale, (bottoms[i] - inkBottom[i]) * yScale + shift, yScale));
        }

        // The bottom of the ink on a row boundary, or the top on one when snapping (see RenderTopSnapped).
        var shiftPx = 0f;
        float row = 0, strokeBottomPx = float.NaN;
        if (snapTop)
        {
            var topPx = topUnits * yScale;
            row = MathF.Round(topPx);
            if (!(row - topPx is >= 0f and <= SnapInsetPx)) shiftPx = row - SnapInsetPx - topPx;
            var stroke = new TopStrokeSink();
            font.DrawGlyph(sequence[top].GlyphId, stroke);
            var strokeUnits = stroke.TopRunBottom(stroke.XMax - 0.01f * unitsPerEm);
            strokeBottomPx = (strokeUnits + bottoms[top] - inkBottom[top]) * yScale + shiftPx;
        }

        var gray = Rast.SmoothRasterizer.Rasterize(sink => Walk(sink, shiftPx), pixelsPerEm: 1f, unitsPerEm: 1);
        if (gray.IsEmpty) return default;
        if (snapTop) snap = new TopSnap(gray.Top - (int)row, StrokeRows(row, strokeBottomPx));

        placed = new AssemblyPart[sequence.Length];
        for (var i = 0; i < sequence.Length; i++)
        {
            var bottomY = gray.Top - (bottoms[i] * yScale + shiftPx);
            placed[i] = new AssemblyPart(sequence[i].GlyphId, sequence[i].IsExtender,
                bottomY - sequence[i].FullAdvance * yScale, bottomY);
        }

        // BearingY = height keeps the whole thing above the baseline by default;
        // callers that want a math-axis-centred placement adjust their pen.
        var bitmap = FromGray(gray, advanceX: 0f);
        return bitmap with { BearingY = bitmap.Height };
    }

    /// <summary>The most copies of the extenders an assembly is given; past it the glyph is short of the
    /// request. A hundred copies of the shortest extender in the fonts here is over 30 em.</summary>
    private const int MaxExtenderRepeats = 100;

    /// <summary>
    /// The assembly's parts bottom-up with every extender repeated r times: the fewest, from none, whose
    /// height with every join at <paramref name="minOverlap"/> (the most an assembly can be stretched to)
    /// reaches <paramref name="requiredUnits"/>, up to <see cref="MaxExtenderRepeats"/>.
    /// </summary>
    private static Tables.OpenTypeMath.MathGlyphPart[] AssemblySequence(
        Tables.OpenTypeMath.MathGlyphAssembly asm, ushort minOverlap, float requiredUnits)
    {
        int fixedAdvance = 0, fixedParts = 0, extenderAdvance = 0, extenderParts = 0;
        foreach (var p in asm.Parts)
        {
            if (p.IsExtender) { extenderAdvance += p.FullAdvance; extenderParts++; }
            else { fixedAdvance += p.FullAdvance; fixedParts++; }
        }

        var repeats = 0;
        if (extenderParts > 0)
        {
            // Height at r copies, least overlap: fixed + r * ext - minOverlap * (parts(r) - 1).
            var perRepeat = extenderAdvance - minOverlap * extenderParts;
            var atNone = fixedAdvance - minOverlap * (fixedParts - 1);
            if (perRepeat > 0 && requiredUnits > atNone)
                repeats = Math.Min(MaxExtenderRepeats, (int)Math.Ceiling((requiredUnits - atNone) / perRepeat));
        }

        var sequence = new List<Tables.OpenTypeMath.MathGlyphPart>(fixedParts + repeats * extenderParts);
        foreach (var p in asm.Parts)
            for (var r = p.IsExtender ? repeats : 1; r > 0; r--)
                sequence.Add(p);
        return [.. sequence];
    }

    /// <summary>
    /// The overlap at each join of <paramref name="sequence"/>, in font units, so that the parts' advances
    /// less the overlaps come to <paramref name="requiredUnits"/>: the same at every join, each held between
    /// <paramref name="minOverlap"/> and what its two connectors allow (<see cref="MaxOverlap"/>). Where even
    /// the most overlap leaves the parts taller than the request, every join takes its most; where even the
    /// least leaves them shorter (at the cap), its least.
    /// </summary>
    private static float[] SpreadOverlaps(Tables.OpenTypeMath.MathGlyphPart[] sequence, ushort minOverlap, float requiredUnits)
    {
        var joins = Math.Max(0, sequence.Length - 1);
        var overlaps = new float[joins];
        if (joins == 0) return overlaps;

        var advance = 0f;
        foreach (var p in sequence) advance += p.FullAdvance;
        var wanted = advance - requiredUnits;

        // Every join at min(minOverlap + t, its most); find t by bisection, the total being monotonic in it.
        float Total(float t)
        {
            var sum = 0f;
            for (var i = 0; i < joins; i++) sum += MathF.Min(minOverlap + t, MaxOverlap(sequence[i], sequence[i + 1], minOverlap));
            return sum;
        }
        var widest = 0f;
        for (var i = 0; i < joins; i++) widest = MathF.Max(widest, MaxOverlap(sequence[i], sequence[i + 1], minOverlap) - minOverlap);
        float lo = 0f, hi = widest;
        if (Total(hi) <= wanted) lo = hi;
        else if (Total(0f) < wanted)
            for (var k = 0; k < 40; k++)
            {
                var mid = (lo + hi) / 2;
                if (Total(mid) < wanted) lo = mid; else hi = mid;
            }

        for (var i = 0; i < joins; i++)
            overlaps[i] = MathF.Min(minOverlap + lo, MaxOverlap(sequence[i], sequence[i + 1], minOverlap));
        return overlaps;
    }

    /// <summary>
    /// The most part <paramref name="lower"/> (below) and part <paramref name="upper"/> (above) may overlap,
    /// in font units: the shorter of the two connectors that meet, by the OpenType MATH specification, and
    /// never less than the table's <paramref name="minOverlap"/>.
    /// </summary>
    private static int MaxOverlap(
        Tables.OpenTypeMath.MathGlyphPart lower,
        Tables.OpenTypeMath.MathGlyphPart upper,
        ushort minOverlap)
    {
        var max = Math.Min((int)lower.EndConnectorLength, (int)upper.StartConnectorLength);
        return Math.Max(max, minOverlap);
    }

    /// <summary>
    /// Register a font from raw bytes under <paramref name="fontId"/>. The
    /// byte array is retained — do not mutate after passing in.
    /// </summary>
    public bool RegisterFontFromMemory(string fontId, byte[] fontData)
    {
        if (IsFontRegistered(fontId)) return true;
        try
        {
            // Adobe Type1 (/FontFile, PFB) is not SFNT — OpenTypeFont.Load can't read it. Route the
            // PFB bytes through the Type1 charstring engine instead. Without this, every LaTeX /
            // Computer-Modern PDF falls back to a system font and loses the fi/ff/fl ligatures (they
            // live at OT1 codes 11–15, which a Latin fallback maps to blank control chars).
            if (T1.Type1Font.IsType1(fontData))
            {
                _type1Fonts.TryAdd(fontId, T1.Type1Font.LoadType1(fontData));
                return true;
            }

            var font = OpenTypeFont.Load(fontData);
            _fonts.TryAdd(fontId, font);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="fontId"/> has a face loaded here — an SFNT in the OpenType cache, or an
    /// embedded Type1. This is the predicate every rasterize entry point implicitly fails on: a
    /// <c>mem:</c> id that was never registered (or whose bytes would not parse) throws from all of
    /// them, and a caller that queues glyph work on a background thread wants to SAY that in its own
    /// diagnostics rather than surface an exception message from three frames ago.
    /// <para>A real file path answers false until something loads it — files load on demand, and
    /// asking about one is not a request to load it. Only <c>mem:</c> ids, which cannot load on
    /// demand, make this the whole truth.</para>
    /// </summary>
    public bool IsFontRegistered(string fontId) =>
        _fonts.ContainsKey(fontId) || _type1Fonts.ContainsKey(fontId);

    /// <summary>
    /// Install the PDF <c>/Encoding</c> char code → glyph name map (named base encoding with
    /// <c>/Differences</c> layered on top) for the embedded font registered under
    /// <paramref name="fontId"/>. The map wins over the font program's built-in encoding —
    /// §9.6.6.2 has a named base encoding replace it outright — so a code the embedded font
    /// maps to <c>.notdef</c> (e.g. a remapped <c>fi</c> ligature) still resolves to its named
    /// glyph. Type1 fonts resolve the name against their charstrings; OpenType faces through
    /// <see cref="OpenTypeFont.GetGlyphIdByName"/> (name-keyed CFF charsets); TrueType outlines
    /// have no name authority and keep resolving by cmap/char-code, so the map is inert there.
    /// </summary>
    public void RegisterPdfEncoding(string fontId, IReadOnlyDictionary<int, string> encoding)
    {
        if (encoding.Count == 0) return;
        _pdfEncoding[fontId] = encoding;
        // The overrides change what a char code resolves to for this font — drop its memoized
        // identities so earlier built-in-encoding resolutions can't shadow the /Differences names.
        foreach (var key in _identityMemo.Keys)
            if (key.Font == fontId)
                _identityMemo.TryRemove(key, out _);
    }

    /// <inheritdoc cref="RegisterPdfEncoding"/>
    [Obsolete("Renamed: the encoding map applies to any embedded simple font, not just Type1. Use RegisterPdfEncoding.")]
    public void RegisterType1Encoding(string fontId, IReadOnlyDictionary<int, string> differences)
        => RegisterPdfEncoding(fontId, differences);

    /// <summary>
    /// Rasterize a glyph as a signed distance field by Unicode codepoint.
    /// Returns a single-channel SDF bitmap suitable for GPU SDF text rendering.
    /// </summary>
    public SdfGlyphBitmap RasterizeGlyphSdf(string fontPath, float fontSize, Rune codepoint, float spread = 4f)
    {
        if (_type1Fonts.TryGetValue(fontPath, out var t1))
            return RenderType1Sdf(t1, ResolveType1Name(fontPath, t1, (uint)codepoint.Value), fontSize, spread);
        var font = GetOrLoad(fontPath);
        var gid = font.GetGlyphId((uint)codepoint.Value);
        if (gid == 0) return default;
        return RenderSdf(font, gid, fontSize, spread);
    }

    /// <summary>
    /// Rasterize a glyph as a signed distance field using PDF char-code + cmap lookup hint.
    /// Mirrors <see cref="RasterizeGlyphWithCharCode"/> for CID and embedded-subset fonts
    /// whose Unicode cmap is absent or unreliable.
    /// </summary>
    public SdfGlyphBitmap RasterizeGlyphSdfWithCharCode(string fontPath, float fontSize,
        Rune codepoint, uint charCode, GlyphMapHint hint = GlyphMapHint.Auto, float spread = 4f)
    {
        if (_type1Fonts.TryGetValue(fontPath, out var t1))
            return RenderType1Sdf(t1, ResolveType1Name(fontPath, t1, charCode), fontSize, spread);
        var font = GetOrLoad(fontPath);
        var gid = ResolveOpenTypeGid(fontPath, font, (uint)codepoint.Value, charCode, (FontsHint)hint);
        if (gid == 0) return default;
        return RenderSdf(font, gid, fontSize, spread);
    }

    /// <summary>
    /// Rasterize a glyph as a multi-channel signed distance field (MTSDF) by
    /// Unicode codepoint. Corners stay sharp at any scale; the A channel is the
    /// same true-distance field <see cref="RasterizeGlyphSdf"/> produces.
    /// </summary>
    public MtsdfGlyphBitmap RasterizeGlyphMtsdf(string fontPath, float fontSize, Rune codepoint, float spread = 4f)
    {
        if (_type1Fonts.TryGetValue(fontPath, out var t1))
            return RenderType1Mtsdf(t1, ResolveType1Name(fontPath, t1, (uint)codepoint.Value), fontSize, spread);
        var font = GetOrLoad(fontPath);
        var gid = font.GetGlyphId((uint)codepoint.Value);
        if (gid == 0) return default;
        return RenderMtsdf(font, gid, fontSize, spread);
    }

    /// <summary>
    /// MTSDF variant of <see cref="RasterizeGlyphSdfWithCharCode"/> — PDF char-code
    /// + cmap lookup hint for CID and embedded-subset fonts.
    /// </summary>
    public MtsdfGlyphBitmap RasterizeGlyphMtsdfWithCharCode(string fontPath, float fontSize,
        Rune codepoint, uint charCode, GlyphMapHint hint = GlyphMapHint.Auto, float spread = 4f)
    {
        if (_type1Fonts.TryGetValue(fontPath, out var t1))
            return RenderType1Mtsdf(t1, ResolveType1Name(fontPath, t1, charCode), fontSize, spread);
        var font = GetOrLoad(fontPath);
        var gid = ResolveOpenTypeGid(fontPath, font, (uint)codepoint.Value, charCode, (FontsHint)hint);
        if (gid == 0) return default;
        return RenderMtsdf(font, gid, fontSize, spread);
    }

    /// <summary>
    /// MTSDF variant of <see cref="RasterizeGlyphByGid"/> — rasterize directly by
    /// glyph id, bypassing cmap lookup. (Not available for Type1 fonts, which are
    /// addressed by glyph name, not id.)
    /// </summary>
    public MtsdfGlyphBitmap RasterizeGlyphMtsdfByGid(string fontPath, float fontSize, uint gid, float spread = 4f)
    {
        if (gid == 0) return default;
        var font = GetOrLoad(fontPath);
        return RenderMtsdf(font, gid, fontSize, spread);
    }

    /// <summary>
    /// Resolve a (Unicode codepoint, PDF char-code, cmap hint) request to a stable glyph
    /// identity for atlas caching — the exact identity step that <see cref="RasterizeGlyph"/>,
    /// <see cref="RasterizeGlyphWithCharCode"/>, and the MTSDF variants perform internally,
    /// surfaced so a caller can key an atlas by identity and then rasterize by GID
    /// (<see cref="RasterizeGlyphMtsdfByGid"/> / <see cref="RasterizeGlyphByGid"/>) or, for
    /// Type1/PFB fonts, by glyph name (<see cref="RasterizeGlyphMtsdfByType1Name"/> /
    /// <see cref="RasterizeGlyphByType1Name"/>).
    ///
    /// <para>Same inputs → same <see cref="OpenTypeFont.GetGlyphId(uint,uint,FontsHint)"/> / Type1-name
    /// lookup → same glyph as the rasterize methods produce: this moves <em>where</em> resolution
    /// happens (to the cache-key boundary), not the resolution itself.</para>
    ///
    /// <para>Resolved identities are memoized per (font, codepoint, charCode, hint) — session-
    /// stable, unlike an atlas cache that evicts. Misses are not memoized (see
    /// <c>_identityMemo</c>), and <see cref="RegisterType1Encoding"/> invalidates its font's
    /// entries.</para>
    /// </summary>
    public GlyphIdentity ResolveGlyphIdentity(string fontPath, Rune codepoint, int charCode, GlyphMapHint hint)
    {
        var memoKey = (fontPath, codepoint.Value, charCode, hint);
        if (_identityMemo.TryGetValue(memoKey, out var memoized)) return memoized;

        var id = ResolveGlyphIdentityUncached(fontPath, codepoint, charCode, hint);
        if (id.Gid != 0 || id.Type1Name is not null)
            _identityMemo.TryAdd(memoKey, id);
        return id;
    }

    private GlyphIdentity ResolveGlyphIdentityUncached(string fontPath, Rune codepoint, int charCode, GlyphMapHint hint)
    {
        // Type1/PFB: identity is a PostScript glyph NAME — the PDF /Differences override wins,
        // else the font's built-in encoding. Mirrors the Type1 branch of the rasterize methods.
        if (_type1Fonts.TryGetValue(fontPath, out var t1))
        {
            var name = ResolveType1Name(fontPath, t1, charCode >= 0 ? (uint)charCode : (uint)codepoint.Value);
            return new GlyphIdentity(0, name);
        }

        // OpenType: an embedded "mem:" subset font may not be registered yet on a very early draw
        // (registration races the first glyph use). GetOrLoad would throw; treat that as an
        // unresolved miss so a synchronous draw-path caller degrades to a blank this frame and
        // re-resolves once the font lands — rather than throwing on the render thread. A genuinely
        // unloadable file font still surfaces its load error (rare, and only once at load time).
        OpenTypeFont font;
        try { font = GetOrLoad(fontPath); }
        catch (InvalidOperationException) { return default; }

        // The SAME resolution the rasterize methods make: the PDF encoding's named glyph first,
        // then charCode-aware cmap strategies for CID/subset fonts when a PDF char code is
        // supplied, plain Unicode cmap otherwise. GlyphMapHint casts to the Fonts enum (shared
        // value layout — see RasterizeGlyphWithCharCode).
        var gid = charCode >= 0
            ? ResolveOpenTypeGid(fontPath, font, (uint)codepoint.Value, (uint)charCode, (FontsHint)hint)
            : font.GetGlyphId((uint)codepoint.Value);
        return new GlyphIdentity(gid, null);
    }

    /// <summary>
    /// Kerning between two glyph ids, in pixels at <paramref name="fontSize"/>. Wraps
    /// <see cref="OpenTypeFont.GetKerning"/> (GPOS pair adjustment, else legacy <c>kern</c>) and
    /// scales its FUnit result by <c>fontSize / UnitsPerEm</c>. Used by <see cref="AdvanceShaper"/>'s
    /// opt-in kerning path — the only place a shaper contributes an advance adjustment on top of the
    /// renderer's cache advance. Returns 0 for Type1/PFB fonts (no numeric glyph ids, no gid-keyed
    /// kerning), an unregistered <c>mem:</c> font, or any pair the font has no kerning for.
    /// </summary>
    public float GetKerningPx(string fontPath, float fontSize, uint leftGid, uint rightGid)
    {
        if (_type1Fonts.ContainsKey(fontPath)) return 0f;
        OpenTypeFont font;
        try { font = GetOrLoad(fontPath); }
        catch (InvalidOperationException) { return 0f; }
        var fu = font.GetKerning(leftGid, rightGid);
        return fu == 0 ? 0f : fu * fontSize / font.UnitsPerEm;
    }

    /// <summary>
    /// MTSDF variant that rasterizes a Type1/PFB glyph directly by PostScript glyph name — the
    /// Type1 counterpart of <see cref="RasterizeGlyphMtsdfByGid"/> (Type1 fonts have no numeric
    /// glyph ids). Returns an empty bitmap for a null/unknown name or a non-Type1 font.
    /// </summary>
    public MtsdfGlyphBitmap RasterizeGlyphMtsdfByType1Name(string fontPath, float fontSize, string? glyphName, float spread = 4f)
    {
        if (glyphName is null || !_type1Fonts.TryGetValue(fontPath, out var t1)) return default;
        return RenderType1Mtsdf(t1, glyphName, fontSize, spread);
    }

    /// <summary>
    /// Color/grayscale variant that rasterizes a Type1/PFB glyph by PostScript glyph name — the
    /// Type1 counterpart of <see cref="RasterizeGlyphByGid"/> for the bitmap atlas path. Returns
    /// an empty bitmap for a null/unknown name or a non-Type1 font.
    /// </summary>
    public GlyphBitmap RasterizeGlyphByType1Name(string fontPath, float fontSize, string? glyphName)
    {
        if (glyphName is null || !_type1Fonts.TryGetValue(fontPath, out var t1)) return default;
        return RenderType1(t1, glyphName, fontSize);
    }

    /// <summary>
    /// Walks a glyph's OUTLINE into <paramref name="sink"/>, in the face's own design units with Y up, and
    /// says how many of those units make an em. For a consumer that needs the geometry rather than pixels:
    /// a vector export (XPS for a print job, PDF, SVG) draws each glyph as a path at whatever resolution
    /// its device has. Covers both kinds of face the rasterize methods draw, an SFNT by glyph id and an
    /// embedded Type 1 by glyph name, which a caller cannot reach on its own: the Type 1 faces live only
    /// here. <see cref="TryGetOpenTypeFont"/> answers the SFNT half and deliberately refuses Type 1.
    /// </summary>
    /// <remarks>
    /// The identity is the one <see cref="ResolveGlyphIdentity"/> returns, so a caller draws the same glyph
    /// the atlas would. A Type 1 face's em comes from its <c>FontMatrix</c>[0], as its rasterizer's does
    /// (<see cref="T1.Type1Font.UnitsPerEm"/>); a skewed <c>FontMatrix</c> is not applied, on either path.
    /// </remarks>
    /// <returns>
    /// False, with nothing emitted, when the face is not loaded here (a <c>mem:</c> id never registered),
    /// the identity is of the wrong kind for the face, or a Type 1 face has no glyph by that name.
    /// </returns>
    public bool TryDrawGlyphOutline(string fontPath, GlyphIdentity identity, IGlyphSink sink, out int unitsPerEm)
    {
        ArgumentNullException.ThrowIfNull(sink);
        unitsPerEm = 0;

        if (_type1Fonts.TryGetValue(fontPath, out var t1))
        {
            if (identity.Type1Name is not { } name || !t1.HasGlyph(name)) return false;
            unitsPerEm = t1.UnitsPerEm;
            return t1.DrawGlyph(name, sink);
        }

        if (identity.IsType1 || !TryGetOpenTypeFont(fontPath, out var font)) return false;
        unitsPerEm = font.UnitsPerEm;
        font.DrawGlyph(identity.Gid, sink);
        return true;
    }

    public void Dispose()
    {
        // Managed fonts don't own native resources — clearing the cache is
        // sufficient. Method retained for API parity with FreeType path.
        _fonts.Clear();
        _identityMemo.Clear();
    }

    // ---- Internals ---------------------------------------------------------

    /// <summary>
    /// The parsed OpenType face for <paramref name="fontPath"/>, loading and caching it if needed.
    /// This is the seam a text shaper uses to read the font's GSUB/GPOS/GDEF tables — the same face
    /// the rasterize methods draw from, so a shaper's substituted glyph ids stay consistent with
    /// what gets rendered. Returns <c>false</c> (and <paramref name="font"/> <c>null</c>) for
    /// Type1/PFB fonts, which are not SFNT and cannot be shaped, and for memory fonts not yet
    /// registered — callers fall back to unshaped per-rune layout in those cases.
    /// </summary>
    public bool TryGetOpenTypeFont(string fontPath, [NotNullWhen(true)] out OpenTypeFont? font)
    {
        font = null;
        if (_type1Fonts.ContainsKey(fontPath)) return false;      // Type1 has no OpenType face
        if (_fonts.TryGetValue(fontPath, out font)) return true;  // already loaded
        if (fontPath.StartsWith("mem:", StringComparison.Ordinal)) return false; // not registered yet
        font = GetOrLoad(fontPath); // resolves a '#N' collection-face id the same way drawing does
        return true;
    }

    private OpenTypeFont GetOrLoad(string fontPath)
    {
        if (_fonts.TryGetValue(fontPath, out var existing)) return existing;
        if (fontPath.StartsWith("mem:", StringComparison.Ordinal))
            throw new InvalidOperationException($"Memory font not registered: '{fontPath}'");

        // A '#N' suffix names a face inside a collection (see FontFaceId). Check the whole id as
        // a path first so a file whose name genuinely ends that way still opens.
        var font = FontFaceId.TryParse(fontPath, out var path, out var faceIndex) && !File.Exists(fontPath)
            ? OpenTypeFont.LoadFromFile(path, faceIndex)
            : OpenTypeFont.LoadFromFile(fontPath);
        return _fonts.GetOrAdd(fontPath, font);
    }

    /// <summary>
    /// Convert a SharpAstro.Fonts render result into DIR.Lib's
    /// <see cref="GlyphBitmap"/> shape. Tries the color path first
    /// (COLR/CBDT) and falls back to grayscale-as-white.
    /// </summary>
    private static GlyphBitmap Render(OpenTypeFont font, uint gid, float pixelsPerEm)
    {
        var advanceX = AdvancePx(font, gid, pixelsPerEm);

        var color = font.RenderColor(gid, pixelsPerEm);
        if (color is { IsEmpty: false })
            return new GlyphBitmap(color.Pixels, color.Width, color.Height,
                color.Left, color.Top, advanceX, IsColored: true);

        var gray = font.RenderGlyphHinted(gid, pixelsPerEm);
        if (gray.IsEmpty) return Blank(advanceX);
        return FromGray(gray, advanceX);
    }

    /// <summary>
    /// Expand grayscale alpha to white-RGBA for compositing parity with
    /// FreeType's grayscale path in the existing renderer.
    /// </summary>
    private static GlyphBitmap FromGray(Rast.GlyphBitmap gray, float advanceX)
    {
        var rgba = new byte[gray.Width * gray.Height * 4];
        for (var i = 0; i < gray.Alpha.Length; i++)
        {
            var di = i * 4;
            rgba[di] = 255;
            rgba[di + 1] = 255;
            rgba[di + 2] = 255;
            rgba[di + 3] = gray.Alpha[i];
        }
        return new GlyphBitmap(rgba, gray.Width, gray.Height,
            gray.Left, gray.Top, advanceX, IsColored: false);
    }

    /// <summary>How far below a row boundary a snapped top is placed, in pixels. Exactly on the
    /// boundary, float error in the shift can leave the top a hair above it, and the rasterizer then
    /// opens an extra, empty row. 1/256 px is far inside the first of its four vertical subsamples
    /// (at 1/8 px), so the row is still fully covered.</summary>
    private const float SnapInsetPx = 1f / 256f;

    /// <summary>
    /// <see cref="Render"/>, with the outline moved vertically so its top lands on a pixel row.
    /// <para>The outline is the one <see cref="Render"/> would rasterize: the hinted one where the face
    /// hints this glyph (already in pixels), the design outline scaled to pixels otherwise. Its top is
    /// the outline's own maximum Y, read with a bounds sink rather than scanned from a bitmap, so it is
    /// exact. The shift moves that top to the nearest row, so it is never more than half a pixel, and a
    /// top already on a row is rendered unshifted, which keeps a hinted glyph byte-identical to
    /// <see cref="Render"/>.</para>
    /// <para>It also measures, on the same outline, the stroke at the glyph's top-right: down a vertical
    /// line just inside the outline's rightmost point, where the topmost run of ink ends. For the radical
    /// that is its flag, and the count of rows it touches below the snapped top is exact at any size, where
    /// the bitmap cannot tell a flag thinner than a pixel from the stem or the curve it runs into.</para>
    /// <para>A colour glyph is returned unsnapped, with <paramref name="snap"/> <see cref="TopSnap.None"/>.</para>
    /// </summary>
    private static GlyphBitmap RenderTopSnapped(OpenTypeFont font, uint gid, float pixelsPerEm, out TopSnap snap)
    {
        snap = TopSnap.None;
        if (font.RenderColor(gid, pixelsPerEm) is { IsEmpty: false })
            return Render(font, gid, pixelsPerEm);

        Action<IGlyphSink> walk;
        float scale, em;
        if (font.LoadHintedOutline(gid, pixelsPerEm) is { IsEmpty: false } hinted)
        {
            walk = hinted.Walk;
            scale = 1f;
            em = pixelsPerEm;
        }
        else
        {
            walk = sink => font.DrawGlyph(gid, sink);
            scale = pixelsPerEm / font.UnitsPerEm;
            em = font.UnitsPerEm;
        }

        var bounds = new TopEdgeSink();
        walk(bounds);
        if (!bounds.HasPoints) return Render(font, gid, pixelsPerEm);
        var topPx = bounds.YMax * scale;

        // A hundredth of an em inside the right end: on the flag, clear of any rounding at its corner.
        var stroke = new TopStrokeSink();
        walk(stroke);
        var strokeBottomPx = stroke.TopRunBottom(stroke.XMax - 0.01f * em) * scale;

        var row = MathF.Round(topPx);
        if (row - topPx is >= 0f and <= SnapInsetPx)
        {
            // Already on a row (the hinter put it there): nothing to move.
            var plain = Render(font, gid, pixelsPerEm);
            snap = new TopSnap(plain.BearingY - (int)row, StrokeRows(row, strokeBottomPx));
            return plain;
        }
        var shift = row - SnapInsetPx - topPx;

        var gray = Rast.SmoothRasterizer.Rasterize(
            sink => walk(new ScaleShiftSink(sink, scale, shift)), pixelsPerEm: 1f, unitsPerEm: 1);
        var advanceX = AdvancePx(font, gid, pixelsPerEm);
        if (gray.IsEmpty) return Blank(advanceX);

        // The shifted top sits just under the row boundary `row`, so the bitmap's Top is that row.
        snap = new TopSnap(gray.Top - (int)row, StrokeRows(row, strokeBottomPx + shift));
        return FromGray(gray, advanceX);
    }

    /// <summary>Rows a stroke touches from the row boundary <paramref name="topRow"/> down to
    /// <paramref name="bottomPx"/> (both Y-up pixels), or 0 when there was no stroke to measure.</summary>
    private static int StrokeRows(float topRow, float bottomPx) =>
        float.IsNaN(bottomPx) ? 0 : Math.Max(1, (int)MathF.Ceiling(topRow - bottomPx - 1e-3f));

    /// <summary>
    /// What a top-snapped rasterization learned from the outline: the bitmap row the outline's top begins
    /// at, and how many rows from there the stroke at the glyph's top-right touches (the radical's flag),
    /// 0 when the outline gave no measure.
    /// </summary>
    internal readonly record struct TopSnap(int InkTopRow, int TopStrokeRows)
    {
        /// <summary>Nothing was snapped: a colour glyph, an embedded Type 1 face, or no snap asked for.</summary>
        public static TopSnap None => new(-1, 0);
    }

    /// <summary>Flattens an outline into line segments, to follow a vertical line through it: where the
    /// topmost run of ink on that line ends tells how thick the stroke at the outline's top is there.</summary>
    private sealed class TopStrokeSink : IGlyphSink
    {
        private const int CurveSteps = 16;
        private readonly List<(float X0, float Y0, float X1, float Y1)> _segments = [];
        private float _x, _y, _startX, _startY;

        public float XMax { get; private set; } = float.NegativeInfinity;

        public void MoveTo(float x, float y)
        {
            (_x, _y, _startX, _startY) = (x, y, x, y);
            XMax = MathF.Max(XMax, x);
        }

        public void LineTo(float x, float y) => Segment(x, y);

        public void QuadTo(float cx, float cy, float x, float y)
        {
            float x0 = _x, y0 = _y;
            for (var i = 1; i <= CurveSteps; i++)
            {
                var t = i / (float)CurveSteps;
                var u = 1f - t;
                Segment(u * u * x0 + 2f * u * t * cx + t * t * x, u * u * y0 + 2f * u * t * cy + t * t * y);
            }
        }

        public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
        {
            float x0 = _x, y0 = _y;
            for (var i = 1; i <= CurveSteps; i++)
            {
                var t = i / (float)CurveSteps;
                var u = 1f - t;
                Segment(u * u * u * x0 + 3f * u * u * t * c1x + 3f * u * t * t * c2x + t * t * t * x,
                    u * u * u * y0 + 3f * u * u * t * c1y + 3f * u * t * t * c2y + t * t * t * y);
            }
        }

        public void Close()
        {
            if (_x != _startX || _y != _startY) Segment(_startX, _startY);
        }

        /// <summary>The Y where the topmost run of ink down the vertical line at <paramref name="x"/> ends,
        /// by the non-zero winding rule the rasterizer fills with; NaN when the line meets no ink.</summary>
        public float TopRunBottom(float x)
        {
            var crossings = new List<(float Y, int Wind)>();
            foreach (var (x0, y0, x1, y1) in _segments)
            {
                if ((x0 <= x && x < x1) || (x1 <= x && x < x0))
                    crossings.Add((y0 + (x - x0) * (y1 - y0) / (x1 - x0), x1 > x0 ? 1 : -1));
            }
            crossings.Sort((a, b) => b.Y.CompareTo(a.Y));
            var winding = 0;
            foreach (var (y, wind) in crossings)
            {
                var was = winding;
                winding += wind;
                if (was != 0 && winding == 0) return y;
            }
            return float.NaN;
        }

        private void Segment(float x, float y)
        {
            _segments.Add((_x, _y, x, y));
            (_x, _y) = (x, y);
            XMax = MathF.Max(XMax, x);
        }
    }

    /// <summary>Collects the highest and lowest Y an outline reaches: its on-curve points and the interior
    /// extrema of its curves (a control point above the curve is not ink).</summary>
    internal sealed class TopEdgeSink : IGlyphSink
    {
        private float _y;

        public float YMax { get; private set; } = float.NegativeInfinity;

        /// <summary>The lowest Y, found the same way: an assembly part is placed by where its ink starts.</summary>
        public float YMin { get; private set; } = float.PositiveInfinity;

        public bool HasPoints => !float.IsNegativeInfinity(YMax);

        public void MoveTo(float x, float y) => Note(y);

        public void LineTo(float x, float y) => Note(y);

        public void QuadTo(float cx, float cy, float x, float y)
        {
            // B'(t) = 0 at t = (p0 - p1) / (p0 - 2 p1 + p2).
            var d = _y - 2f * cy + y;
            if (d != 0f)
            {
                var t = (_y - cy) / d;
                if (t is > 0f and < 1f)
                {
                    var u = 1f - t;
                    Note(u * u * _y + 2f * u * t * cy + t * t * y, keepCurrent: true);
                }
            }
            Note(y);
        }

        public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
        {
            // B'(t) / 3 = A t^2 + B t + C with a = c1 - p0, b = c2 - c1, c = p3 - c2.
            var a = c1y - _y;
            var b = c2y - c1y;
            var c = y - c2y;
            var qa = a - 2f * b + c;
            var qb = 2f * (b - a);
            if (MathF.Abs(qa) < 1e-6f)
            {
                if (qb != 0f) NoteCubic(-a / qb, c1y, c2y, y);
            }
            else
            {
                var disc = qb * qb - 4f * qa * a;
                if (disc >= 0f)
                {
                    var sq = MathF.Sqrt(disc);
                    NoteCubic((-qb + sq) / (2f * qa), c1y, c2y, y);
                    NoteCubic((-qb - sq) / (2f * qa), c1y, c2y, y);
                }
            }
            Note(y);
        }

        public void Close() { }

        private void NoteCubic(float t, float c1y, float c2y, float y)
        {
            if (t is not (> 0f and < 1f)) return;
            var u = 1f - t;
            Note(u * u * u * _y + 3f * u * u * t * c1y + 3f * u * t * t * c2y + t * t * t * y, keepCurrent: true);
        }

        private void Note(float y, bool keepCurrent = false)
        {
            if (y > YMax) YMax = y;
            if (y < YMin) YMin = y;
            if (!keepCurrent) _y = y;
        }
    }

    /// <summary>Scales an outline to pixels (its Y by <c>yScale</c>, the same as X unless an assembly is squashed)
    /// and moves it up by <c>dy</c> pixels (Y is up here), for the rasterizer to draw at identity scale.</summary>
    private sealed class ScaleShiftSink(IGlyphSink inner, float scale, float dy, float yScale) : IGlyphSink
    {
        public ScaleShiftSink(IGlyphSink inner, float scale, float dy) : this(inner, scale, dy, scale) { }

        public void MoveTo(float x, float y) => inner.MoveTo(x * scale, y * yScale + dy);

        public void LineTo(float x, float y) => inner.LineTo(x * scale, y * yScale + dy);

        public void QuadTo(float cx, float cy, float x, float y)
            => inner.QuadTo(cx * scale, cy * yScale + dy, x * scale, y * yScale + dy);

        public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
            => inner.CubicTo(c1x * scale, c1y * yScale + dy, c2x * scale, c2y * yScale + dy, x * scale, y * yScale + dy);

        public void Close() => inner.Close();
    }

    private static SdfGlyphBitmap RenderSdf(OpenTypeFont font, uint gid, float pixelsPerEm, float spread)
    {
        var advanceX = AdvancePx(font, gid, pixelsPerEm);
        var sdf = font.RenderSdf(gid, pixelsPerEm, spread);
        if (sdf.IsEmpty) return new SdfGlyphBitmap([], 0, 0, 0, 0, advanceX, spread);

        return new SdfGlyphBitmap(sdf.Alpha, sdf.Width, sdf.Height,
            sdf.Left, sdf.Top, advanceX, spread);
    }

    private static MtsdfGlyphBitmap RenderMtsdf(OpenTypeFont font, uint gid, float pixelsPerEm, float spread)
    {
        var advanceX = AdvancePx(font, gid, pixelsPerEm);
        var mtsdf = font.RenderMtsdf(gid, pixelsPerEm, spread);
        if (mtsdf.IsEmpty) return new MtsdfGlyphBitmap([], 0, 0, 0, 0, advanceX, spread);

        return new MtsdfGlyphBitmap(mtsdf.Rgba, mtsdf.Width, mtsdf.Height,
            mtsdf.Left, mtsdf.Top, advanceX, spread);
    }

    /// <summary>
    /// The glyph's horizontal advance in pixels at <paramref name="pixelsPerEm"/>, or 0 for a face
    /// with no <c>hmtx</c> table.
    /// </summary>
    private static float AdvancePx(OpenTypeFont font, uint gid, float pixelsPerEm) =>
        font.Hmtx is not null
            ? font.Hmtx.GetAdvanceWidth(gid) * pixelsPerEm / font.UnitsPerEm
            : 0f;

    /// <summary>
    /// A glyph with no pixels but a real advance. An empty outline is <em>not</em> a missing glyph:
    /// a space exists precisely to move the pen, so bailing out with <c>default</c> discarded the one
    /// piece of information it carries. Callers still distinguish the two cases, because a genuinely
    /// absent glyph resolves to gid 0 and is rejected before it ever reaches a render method.
    /// </summary>
    private static GlyphBitmap Blank(float advanceX) => new([], 0, 0, 0, 0, advanceX);

    // ---- Type1 (PFB) glyph rendering -------------------------------------

    /// <summary>
    /// Resolve a Type1 char code to a glyph name via the font's built-in Encoding. Returns null for
    /// out-of-range codes or the .notdef slot, so callers render nothing rather than a notdef box.
    /// </summary>
    // Resolve a Type1 char code to a glyph name: the PDF /Encoding /Differences override wins (when
    // it names a glyph the font actually has), otherwise the font's built-in encoding.
    private string? ResolveType1Name(string fontPath, T1.Type1Font font, uint charCode)
    {
        if (_pdfEncoding.TryGetValue(fontPath, out var diffs)
            && diffs.TryGetValue((int)charCode, out var name)
            && font.HasGlyph(name))
            return name;
        return Type1GlyphName(font, charCode);
    }

    private static string? Type1GlyphName(T1.Type1Font font, uint charCode)
    {
        var enc = font.Encoding;
        if (charCode >= (uint)enc.Count) return null;
        var name = enc[(int)charCode];
        return string.IsNullOrEmpty(name) || name == ".notdef" ? null : name;
    }

    /// <summary>
    /// Rasterize a Type1 glyph (by name) to DIR.Lib's white-RGBA <see cref="GlyphBitmap"/>, mirroring
    /// the grayscale branch of <see cref="Render"/>. Advance comes from the PDF /Widths array upstream,
    /// so AdvanceX is left 0 here (same as the assembly path).
    /// </summary>
    private static GlyphBitmap RenderType1(T1.Type1Font font, string? glyphName, float pixelsPerEm)
    {
        if (glyphName is null || !font.HasGlyph(glyphName)) return default;
        var gray = font.RenderGlyph(glyphName, pixelsPerEm);
        if (gray.Width == 0 || gray.Height == 0 || gray.Alpha.Length == 0) return default;

        var rgba = new byte[gray.Width * gray.Height * 4];
        for (var i = 0; i < gray.Alpha.Length; i++)
        {
            var di = i * 4;
            rgba[di] = 255;
            rgba[di + 1] = 255;
            rgba[di + 2] = 255;
            rgba[di + 3] = gray.Alpha[i];
        }
        return new GlyphBitmap(rgba, gray.Width, gray.Height, gray.Left, gray.Top, AdvanceX: 0f, IsColored: false);
    }

    /// <summary>
    /// Rasterize a Type1 glyph (by name) to a signed-distance field, driving the sink-based SDF
    /// rasterizer directly from the charstring interpreter — the Type1 analogue of <see cref="RenderSdf"/>.
    /// </summary>
    private static SdfGlyphBitmap RenderType1Sdf(T1.Type1Font font, string? glyphName, float pixelsPerEm, float spread)
    {
        if (glyphName is null || !font.HasGlyph(glyphName)) return default;
        var sdf = Rast.SdfRasterizer.RasterizeAuto(sink => font.DrawGlyph(glyphName, sink),
            pixelsPerEm, font.UnitsPerEm, spread);
        if (sdf.Width == 0 || sdf.Height == 0) return default;
        return new SdfGlyphBitmap(sdf.Alpha, sdf.Width, sdf.Height, sdf.Left, sdf.Top, AdvanceX: 0f, spread);
    }

    /// <summary>
    /// Rasterize a Type1 glyph (by name) to an MTSDF, driving the sink-based
    /// generator directly from the charstring interpreter — the Type1 analogue of
    /// <see cref="RenderMtsdf"/>.
    /// </summary>
    private static MtsdfGlyphBitmap RenderType1Mtsdf(T1.Type1Font font, string? glyphName, float pixelsPerEm, float spread)
    {
        if (glyphName is null || !font.HasGlyph(glyphName)) return default;
        var mtsdf = Rast.MsdfRasterizer.RasterizeAuto(sink => font.DrawGlyph(glyphName, sink),
            pixelsPerEm, font.UnitsPerEm, spread);
        if (mtsdf.Width == 0 || mtsdf.Height == 0) return default;
        return new MtsdfGlyphBitmap(mtsdf.Rgba, mtsdf.Width, mtsdf.Height, mtsdf.Left, mtsdf.Top, AdvanceX: 0f, spread);
    }
}
