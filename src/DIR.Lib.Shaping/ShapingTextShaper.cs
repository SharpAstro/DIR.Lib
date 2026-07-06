using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using SharpAstro.Fonts;
using SharpAstro.Fonts.Shaping;

namespace DIR.Lib.Shaping;

/// <summary>
/// An <see cref="ITextShaper"/> backed by the pure-managed <c>SharpAstro.Fonts.Shaping</c> engine.
/// It itemizes a line into script/direction runs (<see cref="ScriptItemizer"/>), shapes each run
/// with the font's GSUB/GPOS (ligatures, contextual forms, Arabic joining, mark positioning, RTL),
/// and emits <see cref="ShapedGlyph"/>s carrying the substituted glyph identity and positioning.
///
/// <para>Per the A2 contract the shaper contributes only <em>adjustments</em>: the engine reports
/// each glyph's advance DELTA relative to its <c>hmtx</c> advance plus placement offsets, all in
/// font units; this adapter scales them to pixels by <c>fontSize / unitsPerEm</c>. The renderer
/// still sources every glyph's base advance from its own cache, keyed by the (possibly substituted)
/// <see cref="GlyphIdentity"/>. Fonts the engine can't read — Type1/PFB, or a not-yet-registered
/// memory font — fall back to the unshaped per-rune stream, identical to <see cref="AdvanceShaper"/>
/// with kerning off, so text still draws.</para>
///
/// <para>Thread-safe: the per-font <see cref="ShapingFont"/> cache is concurrent and the per-call
/// scratch (run list + shape buffer) is thread-local.</para>
/// </summary>
public sealed class ShapingTextShaper : ITextShaper
{
    /// <summary>Shared instance (paragraph direction = <see cref="ParagraphDirection.Auto"/>) — safe
    /// to assign to every renderer (no per-call mutable state beyond thread-locals).</summary>
    public static readonly ShapingTextShaper Instance = new();

    /// <summary>Base paragraph direction for the UAX #9 bidirectional algorithm.</summary>
    public enum ParagraphDirection
    {
        /// <summary>Resolve each line's base direction from its first strong character (P2/P3).</summary>
        Auto,
        /// <summary>Force a left-to-right base direction.</summary>
        LeftToRight,
        /// <summary>Force a right-to-left base direction.</summary>
        RightToLeft,
    }

    private readonly int _paragraphLevel;

    /// <summary>Create a shaper with a fixed paragraph <paramref name="direction"/>. The default
    /// (<see cref="ParagraphDirection.Auto"/>) resolves each line's base direction from its first
    /// strong character; pass a fixed direction for a known LTR- or RTL-context UI. Immutable, so an
    /// instance is safe to share across threads.</summary>
    public ShapingTextShaper(ParagraphDirection direction = ParagraphDirection.Auto)
        => _paragraphLevel = direction switch
        {
            ParagraphDirection.LeftToRight => 0,
            ParagraphDirection.RightToLeft => 1,
            _ => BidiAlgorithm.AutoLevel,
        };

    // Engine face per font id. A cached null memoizes "not shapeable" for Type1/PFB (permanent);
    // memory fonts that merely aren't registered yet are NOT memoized, so they shape once loaded.
    private readonly ConcurrentDictionary<string, ShapingFont?> _shapingFonts = new(StringComparer.Ordinal);

    [ThreadStatic] private static ShapeBuffer? _buffer;
    [ThreadStatic] private static List<ScriptRun>? _runs;

    /// <inheritdoc/>
    public void Shape(ReadOnlySpan<char> text, string fontPath, float fontSize,
        ManagedFontRasterizer rasterizer, List<ShapedGlyph> output)
    {
        ArgumentNullException.ThrowIfNull(rasterizer);
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        if (text.IsEmpty) return;

        var shapingFont = GetShapingFont(fontPath, rasterizer);
        if (shapingFont is null)
        {
            EmitUnshaped(text, output);
            return;
        }

        // FUnit → pixel scale for the positioning deltas.
        var scale = fontSize / shapingFont.Font.UnitsPerEm;

        var runs = _runs ??= [];
        var buffer = _buffer ??= new ShapeBuffer();
        BidiScriptItemizer.Itemize(text, _paragraphLevel, runs);

        // Runs come back in VISUAL (left-to-right) order (UAX #9 rule L2), and each RTL run's glyphs
        // are reversed to visual order by the shaper — so appending run after run gives the renderer's
        // pen loop the correct placement order, now for mixed LTR/RTL text too (not just single runs).
        foreach (var run in runs)
        {
            buffer.Clear();
            buffer.Direction = run.Direction;
            buffer.AddText(text.Slice(run.Start, run.Length), clusterOffset: run.Start);
            Shaper.Shape(shapingFont, buffer, run.Script);

            var gids = buffer.GlyphIds;
            var clusters = buffer.Clusters;
            var advanceDeltas = buffer.XAdvanceDeltas;
            var xOffsets = buffer.XOffsets;
            var yOffsets = buffer.YOffsets;
            for (var i = 0; i < gids.Length; i++)
            {
                output.Add(new ShapedGlyph(
                    SourceRune(text, clusters[i]),
                    new GlyphIdentity(gids[i], Type1Name: null),
                    clusters[i],
                    advanceDeltas[i] * scale,
                    xOffsets[i] * scale,
                    yOffsets[i] * scale));
            }
        }
    }

    private ShapingFont? GetShapingFont(string fontPath, ManagedFontRasterizer rasterizer)
    {
        if (_shapingFonts.TryGetValue(fontPath, out var cached))
            return cached;

        if (!rasterizer.TryGetOpenTypeFont(fontPath, out var font))
        {
            // Type1/PFB never has an OpenType face — memoize the null. A memory font may just not be
            // registered yet on an early draw, so don't memoize that; re-probe on the next call.
            if (!fontPath.StartsWith("mem:", StringComparison.Ordinal))
                _shapingFonts.TryAdd(fontPath, null);
            return null;
        }

        var shapingFont = ShapingFont.Create(font);
        _shapingFonts.TryAdd(fontPath, shapingFont);
        return shapingFont;
    }

    // The source rune for color-glyph routing: the codepoint at the glyph's cluster (the first
    // component of a ligature). U+FFFD for a malformed boundary, which shouldn't occur since
    // clusters fall on rune boundaries.
    private static Rune SourceRune(ReadOnlySpan<char> text, int cluster)
        => (uint)cluster < (uint)text.Length
           && Rune.DecodeFromUtf16(text[cluster..], out var rune, out _) == OperationStatus.Done
            ? rune
            : Rune.ReplacementChar;

    // Type1 / unshapeable fallback: one glyph per rune, no adjustments — matches AdvanceShaper with
    // kerning off, so DrawText/MeasureText stay byte-identical to the pre-shaper per-rune loop.
    private static void EmitUnshaped(ReadOnlySpan<char> text, List<ShapedGlyph> output)
    {
        var cluster = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            output.Add(new ShapedGlyph(rune, Glyph: null, cluster, XAdvanceAdjust: 0f, XOffset: 0f, YOffset: 0f));
            cluster += rune.Utf16SequenceLength;
        }
    }
}
