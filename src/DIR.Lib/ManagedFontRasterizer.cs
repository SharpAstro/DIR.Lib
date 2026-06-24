using System.Collections.Concurrent;
using System.Text;
using SharpAstro.Fonts;
using FontsHint = SharpAstro.Fonts.Tables.Cmap.GlyphMapHint;
using Tables = SharpAstro.Fonts.Tables;
using T1 = SharpAstro.Fonts.Type1;
using Rast = SharpAstro.Fonts.Rasterizer;

namespace DIR.Lib;

/// <summary>
/// Pure-managed glyph rasterizer backed by SharpAstro.Fonts.
/// Drop-in replacement for <see cref="FreeTypeGlyphRasterizer"/> — same
/// public API, no native dependencies, no GC pinning, AOT-compatible.
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

    // Per-font char code → glyph name overrides from the PDF /Encoding /Differences (same id as
    // _type1Fonts). Authoritative over the font's built-in encoding; this is how a PDF reaches a
    // glyph (e.g. the fi/fl ligatures) it places at a code the built-in encoding doesn't cover.
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<int, string>> _type1Encoding = new();

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
        var gid = font.GetGlyphId((uint)codepoint.Value, charCode, (FontsHint)hint);
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
    /// Look up the OpenType MATH vertical-stretch construction for a Unicode
    /// codepoint — the recipe used to build a scalable radical, paren, brace,
    /// or other delimiter at an arbitrary height. Returns null if the font
    /// has no MATH table, or if this codepoint is not in the table's vertical
    /// coverage. Use the returned <see cref="MathGlyphConstruction"/> to walk
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
    {
        variantGlyphId = 0;
        var font = GetOrLoad(fontPath);
        var baseGid = font.GetGlyphId((uint)codepoint.Value);
        if (baseGid == 0) return default;

        // 0) Base glyph already covers the request — no stretching needed.
        //    This branch fires for short content even on math-less fonts and
        //    is the cheap path: just render the unstretched glyph and return.
        var baseGlyph = Render(font, baseGid, fontSize);
        if (baseGlyph.Height >= requiredHeightPx)
        {
            variantGlyphId = baseGid;
            return baseGlyph;
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
        var requiredFUnits = (int)Math.Ceiling(requiredHeightPx * unitsPerEm / fontSize);
        foreach (var v in construction.Variants)
        {
            if (v.AdvanceMeasurement >= requiredFUnits)
            {
                variantGlyphId = v.GlyphId;
                return Render(font, v.GlyphId, fontSize);
            }
        }

        // 2) Assembly path — composed from multiple parts; no single glyph
        //    id. Report the base glyph id so callers can still look up
        //    metrics (italic correction, etc.) against something sensible.
        if (construction.Assembly is { } asm)
        {
            variantGlyphId = baseGid;
            return ComposeVerticalAssembly(font, asm, math.MinConnectorOverlap, unitsPerEm, fontSize, requiredFUnits);
        }

        // 3) Largest variant if any (still possibly short of the request, but
        //    closer than the base). If the construction has neither variants
        //    nor assembly we already returned default above.
        if (construction.Variants.Count > 0)
        {
            variantGlyphId = construction.Variants[^1].GlyphId;
            return Render(font, construction.Variants[^1].GlyphId, fontSize);
        }
        return default;
    }

    /// <summary>
    /// Compose a stretchy vertical glyph from an OT MATH assembly recipe.
    /// Stacks the parts (extenders repeated as needed) into a single RGBA
    /// bitmap, using the maximum permissible overlap between adjacent pieces
    /// — clamped to the table's <c>MinConnectorOverlap</c>. Parts are listed
    /// bottom-up by spec; we draw from the bottom of the canvas upward.
    ///
    /// <para>This is a pragmatic "stack the rasterized bitmaps with FUnit-derived
    /// overlap" implementation. Visible seams between extender repeats may
    /// occur because the part bitmaps are positioned by their visible-ink
    /// extents rather than by FullAdvance + bearings — the parts were designed
    /// to mate seamlessly, but the math depends on subpixel-precise placement.
    /// Adequate for renderer-grade output at typical math sizes.</para>
    /// </summary>
    private static GlyphBitmap ComposeVerticalAssembly(
        OpenTypeFont font, Tables.OpenTypeMath.MathGlyphAssembly asm,
        ushort minOverlap, ushort unitsPerEm, float fontSize, int requiredFUnits)
    {
        if (asm.Parts.Count == 0) return default;

        // Compute the total FUnit-space advance with one copy of each part
        // and max overlap between adjacent pairs.
        var advanceWithOnce = 0;
        for (var i = 0; i < asm.Parts.Count; i++) advanceWithOnce += asm.Parts[i].FullAdvance;

        var startOverlap = 0;
        for (var i = 0; i < asm.Parts.Count - 1; i++)
            startOverlap += ClampedOverlap(asm.Parts[i], asm.Parts[i + 1], minOverlap);
        var totalFUnits = advanceWithOnce - startOverlap;

        // Repeat the first extender until we reach the target.
        var extenderRepeats = new int[asm.Parts.Count];
        for (var i = 0; i < asm.Parts.Count; i++) extenderRepeats[i] = 1;

        var firstExtender = -1;
        for (var i = 0; i < asm.Parts.Count; i++)
            if (asm.Parts[i].IsExtender) { firstExtender = i; break; }

        if (firstExtender >= 0)
        {
            const int safetyCap = 64;  // assemblies of >64 extenders are pathological
            while (totalFUnits < requiredFUnits && extenderRepeats[firstExtender] < safetyCap)
            {
                var p = asm.Parts[firstExtender];
                var selfOv = ClampedOverlap(p, p, minOverlap);
                totalFUnits += p.FullAdvance - selfOv;
                extenderRepeats[firstExtender]++;
            }
        }

        // Materialise the runtime sequence (extenders unrolled) and rasterize each
        // unique part once — extenders share a single bitmap across copies.
        var totalParts = 0;
        for (var i = 0; i < asm.Parts.Count; i++) totalParts += extenderRepeats[i];
        var sequence = new Tables.OpenTypeMath.MathGlyphPart[totalParts];
        var sIdx = 0;
        for (var i = 0; i < asm.Parts.Count; i++)
            for (var r = 0; r < extenderRepeats[i]; r++)
                sequence[sIdx++] = asm.Parts[i];

        var bitmapCache = new Dictionary<ushort, GlyphBitmap>();
        var bitmaps = new GlyphBitmap[sequence.Length];
        for (var i = 0; i < sequence.Length; i++)
        {
            var gid = sequence[i].GlyphId;
            if (!bitmapCache.TryGetValue(gid, out var bm))
            {
                bm = Render(font, gid, fontSize);
                bitmapCache[gid] = bm;
            }
            bitmaps[i] = bm;
        }

        // Pixel-space overlaps between adjacent sequence positions.
        var overlapPx = new int[Math.Max(0, sequence.Length - 1)];
        for (var i = 0; i < sequence.Length - 1; i++)
        {
            var ovFunits = ClampedOverlap(sequence[i], sequence[i + 1], minOverlap);
            overlapPx[i] = (int)Math.Round(ovFunits * fontSize / unitsPerEm);
        }

        // Canvas dimensions:
        //   width  = the bbox spanning every part's inked extent — for each
        //            part, its ink lives in [BearingX, BearingX + Width).
        //            Different parts often have different BearingX values
        //            (extender bitmaps are narrower than hook bitmaps), so
        //            we take the union, not just the max width.
        //   height = sum of part heights minus per-pair overlaps.
        // Parts are positioned by their BearingX: the font designer assigned
        // each part a BearingX that makes the parts mate seamlessly when
        // drawn at the same glyph-origin. Centring bitmaps horizontally
        // (the older bug) misaligned wider hooks against narrower extenders,
        // producing two visible vertical strokes for paren assemblies — the
        // hook's ink at canvas-x=0 and the extender's ink at canvas-x=~half-width.
        var height = 0;
        var minBearing = int.MaxValue;
        var maxRight = int.MinValue;
        for (var i = 0; i < bitmaps.Length; i++)
        {
            height += bitmaps[i].Height;
            if (bitmaps[i].BearingX < minBearing) minBearing = bitmaps[i].BearingX;
            var right = bitmaps[i].BearingX + bitmaps[i].Width;
            if (right > maxRight) maxRight = right;
        }
        for (var i = 0; i < overlapPx.Length; i++) height -= overlapPx[i];
        var width = maxRight - minBearing;
        if (height <= 0) height = 1;
        if (width <= 0) width = 1;

        // Composite bottom-up: parts[0] sits at the bottom of the canvas.
        var canvas = new RgbaImage(width, height);
        canvas.Clear(new RGBAColor32(0, 0, 0, 0));

        var pen = height;  // bottom edge
        for (var i = 0; i < sequence.Length; i++)
        {
            var bm = bitmaps[i];
            pen -= bm.Height;
            // Translate by minBearing so the smallest BearingX lands at x=0.
            var x = bm.BearingX - minBearing;
            if (bm.Rgba is { Length: > 0 })
                canvas.BlitRgba(x, pen, bm.Rgba, bm.Width, bm.Height);
            // Slide back down by overlap so the next part sits inside this one's footprint.
            if (i < overlapPx.Length) pen += overlapPx[i];
        }

        // BearingY = height keeps the whole thing above the baseline by default;
        // callers that want a math-axis-centred placement adjust their pen.
        return new GlyphBitmap(canvas.Pixels, width, height, BearingX: 0, BearingY: height, AdvanceX: 0f);
    }

    /// <summary>
    /// Permissible overlap between part <paramref name="lower"/> (below) and
    /// part <paramref name="upper"/> (above), in FUnits. Per OT MATH spec the
    /// overlap may be in [MinConnectorOverlap, max], where
    /// max = min(lower.EndConnectorLength, upper.StartConnectorLength). We
    /// pick the maximum to keep the assembly compact.
    /// </summary>
    private static int ClampedOverlap(
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
        if (_fonts.ContainsKey(fontId) || _type1Fonts.ContainsKey(fontId)) return true;
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
    /// Install the PDF <c>/Encoding /Differences</c> char code → glyph name overrides for the Type1
    /// font registered under <paramref name="fontId"/>. These win over the font's built-in encoding,
    /// so a code the embedded font maps to <c>.notdef</c> (e.g. a remapped <c>fi</c> ligature) still
    /// resolves to its named glyph. No-op for non-Type1 fonts (the override is only consulted there).
    /// </summary>
    public void RegisterType1Encoding(string fontId, IReadOnlyDictionary<int, string> differences)
    {
        if (differences.Count > 0) _type1Encoding[fontId] = differences;
    }

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
        var gid = font.GetGlyphId((uint)codepoint.Value, charCode, (FontsHint)hint);
        if (gid == 0) return default;
        return RenderSdf(font, gid, fontSize, spread);
    }

    public void Dispose()
    {
        // Managed fonts don't own native resources — clearing the cache is
        // sufficient. Method retained for API parity with FreeType path.
        _fonts.Clear();
    }

    // ---- Internals ---------------------------------------------------------

    private OpenTypeFont GetOrLoad(string fontPath)
    {
        if (_fonts.TryGetValue(fontPath, out var existing)) return existing;
        if (fontPath.StartsWith("mem:", StringComparison.Ordinal))
            throw new InvalidOperationException($"Memory font not registered: '{fontPath}'");

        var font = OpenTypeFont.LoadFromFile(fontPath);
        return _fonts.GetOrAdd(fontPath, font);
    }

    /// <summary>
    /// Convert a SharpAstro.Fonts render result into DIR.Lib's
    /// <see cref="GlyphBitmap"/> shape. Tries the color path first
    /// (COLR/CBDT) and falls back to grayscale-as-white.
    /// </summary>
    private static GlyphBitmap Render(OpenTypeFont font, uint gid, float pixelsPerEm)
    {
        var color = font.RenderColor(gid, pixelsPerEm);
        if (color is { IsEmpty: false })
        {
            var advance = font.Hmtx is not null
                ? font.Hmtx.GetAdvanceWidth(gid) * pixelsPerEm / font.UnitsPerEm
                : 0f;
            return new GlyphBitmap(color.Pixels, color.Width, color.Height,
                color.Left, color.Top, advance, IsColored: true);
        }

        var gray = font.RenderGlyphHinted(gid, pixelsPerEm);
        if (gray.IsEmpty) return default;

        // Expand grayscale alpha to white-RGBA for compositing parity with
        // FreeType's grayscale path in the existing renderer.
        var rgba = new byte[gray.Width * gray.Height * 4];
        for (var i = 0; i < gray.Alpha.Length; i++)
        {
            var di = i * 4;
            rgba[di] = 255;
            rgba[di + 1] = 255;
            rgba[di + 2] = 255;
            rgba[di + 3] = gray.Alpha[i];
        }
        var advanceX = font.Hmtx is not null
            ? font.Hmtx.GetAdvanceWidth(gid) * pixelsPerEm / font.UnitsPerEm
            : 0f;
        return new GlyphBitmap(rgba, gray.Width, gray.Height,
            gray.Left, gray.Top, advanceX, IsColored: false);
    }

    private static SdfGlyphBitmap RenderSdf(OpenTypeFont font, uint gid, float pixelsPerEm, float spread)
    {
        var sdf = font.RenderSdf(gid, pixelsPerEm, spread);
        if (sdf.IsEmpty) return default;

        var advanceX = font.Hmtx is not null
            ? font.Hmtx.GetAdvanceWidth(gid) * pixelsPerEm / font.UnitsPerEm
            : 0f;
        return new SdfGlyphBitmap(sdf.Alpha, sdf.Width, sdf.Height,
            sdf.Left, sdf.Top, advanceX, spread);
    }

    // ---- Type1 (PFB) glyph rendering -------------------------------------

    /// <summary>
    /// Resolve a Type1 char code to a glyph name via the font's built-in Encoding. Returns null for
    /// out-of-range codes or the .notdef slot, so callers render nothing rather than a notdef box.
    /// </summary>
    // Resolve a Type1 char code to a glyph name: the PDF /Encoding /Differences override wins (when
    // it names a glyph the font actually has), otherwise the font's built-in encoding.
    private string? ResolveType1Name(string fontPath, T1.Type1Font font, uint charCode)
    {
        if (_type1Encoding.TryGetValue(fontPath, out var diffs)
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
}
