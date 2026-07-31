using System.Collections.Concurrent;
using System.Text;
using SharpAstro.Fonts;

namespace DIR.Lib;

/// <summary>
/// Per-script font fallback for UI text. A primary font rarely covers every script (a Latin UI
/// face has no CJK/Arabic/Hebrew/Indic glyphs), so text in another script would render as
/// <c>.notdef</c> boxes. This resolver splits a string into consecutive runs that each render with
/// a font that actually covers them, picking from a caller-supplied ordered list of fallback fonts
/// (e.g. the per-script Noto Sans family the host bundles). Coverage is read straight from each
/// candidate's cmap via <see cref="OpenTypeFont.GetGlyphId(uint)"/>; faces are loaded lazily, so a
/// large CJK font isn't touched unless a codepoint actually needs it.
///
/// <para>Renderer-agnostic: <see cref="CoverageRuns"/> is pure; the <see cref="Measure"/>/
/// <see cref="Draw"/>/<see cref="FitEllipsis"/> helpers are generic over the backend's
/// <see cref="Renderer{TSurface}"/>. Caches are concurrent; intended for render-thread UI text.</para>
/// </summary>
public sealed class FontFallbackResolver
{
    private readonly string _primaryFontPath;
    private readonly List<string> _fallbackPaths = new();
    // Lazily-loaded face per path for cmap coverage checks. Null = load failed / not present.
    private readonly ConcurrentDictionary<string, OpenTypeFont?> _faces = new();
    // codepoint -> the font that covers it, or null when none of the available faces does.
    // The hot cache: each codepoint is classified once, for both the drawing path (which
    // substitutes the primary on a miss) and TryResolveFont (which reports the miss).
    private readonly ConcurrentDictionary<int, string?> _fontByCodepoint = new();

    /// <param name="primaryFontPath">The default font; used wherever it covers the codepoint.</param>
    /// <param name="fallbackFontPaths">Fallback fonts in priority order — the first that covers a
    /// codepoint the primary lacks wins. Missing files are skipped.</param>
    public FontFallbackResolver(string primaryFontPath, IEnumerable<string> fallbackFontPaths)
    {
        _primaryFontPath = primaryFontPath;
        foreach (var p in fallbackFontPaths)
            if (!string.IsNullOrEmpty(p) && FaceFileExists(p))
                _fallbackPaths.Add(p);
        _primaryCoversAscii = new Lazy<bool>(PrimaryCoversAsciiRange);
    }

    // The ASCII shortcut, valid only once the primary is known to cover ASCII.
    private bool IsPlainAscii(ReadOnlySpan<char> text) => IsAllAscii(text) && _primaryCoversAscii.Value;

    /// <summary>
    /// Build a resolver from the roles a UI actually has, rather than from an anonymous list.
    ///
    /// <para>The order is primary → symbol → emoji → per-script, and it matters: several script
    /// faces incidentally carry a few symbols (the Noto CJK faces cover ▶ ◀ ✓), so without the
    /// symbol face ahead of them a caret would be drawn from a multi-megabyte CJK font. Roles
    /// are <em>declared</em> here because they cannot be detected — a font full of symbols is
    /// metadata-identical to a text font, and only its cmap tells the truth.</para>
    /// </summary>
    /// <param name="primaryFontPath">The UI's text face.</param>
    /// <param name="symbolFontPath">Face carrying arrows, geometric shapes, ballot boxes, …</param>
    /// <param name="emojiFontPath">Face carrying (usually colour) emoji.</param>
    /// <param name="scriptFontPaths">Per-script faces — CJK, Arabic, Hebrew, Indic, …</param>
    public static FontFallbackResolver FromRoles(string primaryFontPath,
        string? symbolFontPath = null, string? emojiFontPath = null,
        IEnumerable<string>? scriptFontPaths = null)
    {
        List<string> ordered = [];
        if (!string.IsNullOrEmpty(symbolFontPath)) ordered.Add(symbolFontPath);
        if (!string.IsNullOrEmpty(emojiFontPath)) ordered.Add(emojiFontPath);
        if (scriptFontPaths is not null) ordered.AddRange(scriptFontPaths);

        return new FontFallbackResolver(primaryFontPath, ordered)
        {
            // Recorded post-construction so the accessors report only what actually resolved —
            // the constructor drops paths that aren't on disk.
            SymbolFontPath = Available(symbolFontPath),
            EmojiFontPath = Available(emojiFontPath),
        };

        static string? Available(string? path)
            => !string.IsNullOrEmpty(path) && FaceFileExists(path) ? path : null;
    }

    /// <summary>The default font — used wherever it covers the codepoint.</summary>
    public string PrimaryFontPath => _primaryFontPath;

    /// <summary>The declared symbol face, if one was given to <see cref="FromRoles"/> and exists.</summary>
    public string? SymbolFontPath { get; private init; }

    /// <summary>The declared emoji face, if one was given to <see cref="FromRoles"/> and exists.</summary>
    public string? EmojiFontPath { get; private init; }

    /// <summary>True if at least one fallback font is available (else this is a pass-through).</summary>
    public bool HasFallbacks => _fallbackPaths.Count > 0;

    // Whether the primary covers printable ASCII. Nearly every primary does, which is what makes
    // "all-ASCII ⇒ no fallback needed" a sound shortcut — but only once it has been checked. It is
    // computed lazily (constructing a resolver must not touch the disk) and exactly once, so the
    // shortcut stays a plain char scan rather than a per-character cmap lookup.
    private readonly Lazy<bool> _primaryCoversAscii;

    private bool PrimaryCoversAsciiRange()
    {
        for (var c = 0x20; c < 0x7F; c++)
            if (!Covers(_primaryFontPath, new Rune(c))) return false;
        return true;
    }

    /// <summary>
    /// The font that can actually draw <paramref name="rune"/>, or <c>null</c> when none of the
    /// available faces covers it.
    ///
    /// <para>This is the question <see cref="CoverageRuns"/> deliberately cannot answer: it
    /// falls back to the primary so an uncovered glyph degrades to <c>.notdef</c> rather than
    /// vanishing, which is right for drawing but hides the miss from the caller. Ask here when
    /// the answer changes what you draw — substituting an ASCII spelling for a symbol the
    /// machine has no face for, say.</para>
    /// </summary>
    public string? TryResolveFont(Rune rune)
    {
        if (_fontByCodepoint.TryGetValue(rune.Value, out var cached)) return cached;

        string? chosen = null;
        if (Covers(_primaryFontPath, rune))
        {
            chosen = _primaryFontPath;
        }
        else
        {
            foreach (var fb in _fallbackPaths)
            {
                if (Covers(fb, rune)) { chosen = fb; break; }
            }
        }
        _fontByCodepoint[rune.Value] = chosen;
        return chosen;
    }

    /// <summary>True if any available face covers <paramref name="rune"/>.</summary>
    public bool CanRender(Rune rune) => TryResolveFont(rune) is not null;

    /// <summary>
    /// True if every rune in <paramref name="text"/> can be drawn by some available face. Use it
    /// to choose between a symbol spelling and an ASCII one at the point the string is picked.
    /// </summary>
    public bool CanRender(string text)
    {
        foreach (var rune in text.EnumerateRunes())
            if (TryResolveFont(rune) is null) return false;
        return true;
    }

    /// <summary>A maximal slice of a string that draws with one font.</summary>
    /// <param name="Start">UTF-16 offset of the run within the source text.</param>
    /// <param name="Length">UTF-16 length of the run.</param>
    /// <param name="FontPath">The font to draw it with.</param>
    public readonly record struct FontRun(int Start, int Length, string FontPath);

    /// <summary>
    /// True if the primary font alone can draw every rune of <paramref name="text"/> — i.e. no
    /// run splitting is needed. Allocation-free, and the answer for essentially all UI chrome,
    /// so callers can gate the run machinery behind it and pay nothing in the common case.
    /// </summary>
    public bool PrimaryCoversAll(ReadOnlySpan<char> text)
    {
        // With no fallbacks the primary is the only font there is, so it "covers" by definition —
        // the same pass-through CoverageRuns applies.
        if (_fallbackPaths.Count == 0 || IsPlainAscii(text)) return true;
        foreach (var rune in text.EnumerateRunes())
            if (!Covers(_primaryFontPath, rune)) return false;
        return true;
    }

    /// <summary>
    /// Split <paramref name="text"/> into consecutive runs that each render with one font, into
    /// <paramref name="output"/> (cleared first, so one list can be reused across calls). Runs
    /// are offsets into the source rather than substrings, so a draw loop allocates nothing.
    /// </summary>
    public void CoverageRuns(ReadOnlySpan<char> text, List<FontRun> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        if (text.IsEmpty) return;

        // Fast path: pure-ASCII text under a primary that covers ASCII (the common case) is one run.
        if (_fallbackPaths.Count == 0 || IsPlainAscii(text))
        {
            output.Add(new FontRun(0, text.Length, _primaryFontPath));
            return;
        }

        string? curFont = null;
        var runStart = 0;
        var pos = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var font = ResolveFont(rune);
            if (curFont is null)
            {
                curFont = font;
            }
            else if (!ReferenceEquals(font, curFont) && font != curFont)
            {
                output.Add(new FontRun(runStart, pos - runStart, curFont));
                runStart = pos;
                curFont = font;
            }
            pos += rune.Utf16SequenceLength;
        }
        if (pos > runStart && curFont is not null) output.Add(new FontRun(runStart, pos - runStart, curFont));
    }

    /// <summary>
    /// Split <paramref name="text"/> into consecutive runs that each render with one font: the
    /// primary where it covers the codepoint, else the first fallback that does, else the primary
    /// (so the glyph degrades to <c>.notdef</c> rather than vanishing).
    /// </summary>
    public List<(string Text, string FontPath)> CoverageRuns(string text)
    {
        var runs = new List<(string, string)>();
        if (string.IsNullOrEmpty(text)) return runs;

        var spans = new List<FontRun>();
        CoverageRuns(text.AsSpan(), spans);
        foreach (var (start, length, font) in spans)
            runs.Add((text.Substring(start, length), font));
        return runs;
    }

    // The drawing path's answer: a font is always named, so an uncovered glyph degrades to the
    // primary's .notdef rather than vanishing from the line.
    private string ResolveFont(Rune rune) => TryResolveFont(rune) ?? _primaryFontPath;

    private bool Covers(string fontPath, Rune rune)
    {
        var face = _faces.GetOrAdd(fontPath, LoadFace);
        return face is not null && face.GetGlyphId((uint)rune.Value) != 0;
    }

    private static OpenTypeFont? LoadFace(string fontId)
    {
        try
        {
            // Fallback fonts are named by FontFaceId, so a '#N' suffix picks a face out of a
            // collection — without this a .ttc fallback would silently always be face 0.
            return FontFaceId.TryParse(fontId, out var path, out var faceIndex) && !File.Exists(fontId)
                ? OpenTypeFont.LoadFromFile(path, faceIndex)
                : OpenTypeFont.LoadFromFile(fontId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FontFallback] failed to load '{fontId}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Whether the file behind a <see cref="FontFaceId"/> is present. The id may carry a
    /// <c>#N</c> face suffix, which <see cref="File.Exists"/> would reject outright.
    /// </summary>
    private static bool FaceFileExists(string fontId)
        => File.Exists(fontId)
           || (FontFaceId.TryParse(fontId, out var path, out _) && File.Exists(path));

    private static bool IsAllAscii(ReadOnlySpan<char> s)
    {
        foreach (var c in s) if (c > 0x7F) return false;
        return true;
    }

    // ---- Renderer-coupled measure / draw / fit (shared by any widget) ----

    /// <summary>Total advance width + max height of <paramref name="text"/> across its coverage runs.</summary>
    public (float Width, float Height) Measure<TSurface>(Renderer<TSurface> renderer, string text, float fontSize)
    {
        float w = 0f, h = 0f;
        foreach (var (runText, font) in CoverageRuns(text))
        {
            var (rw, rh) = renderer.MeasureText(runText.AsSpan(), font, fontSize);
            w += rw;
            if (rh > h) h = rh;
        }
        return (w, h);
    }

    /// <summary>
    /// Draw <paramref name="text"/> into <paramref name="rect"/> with per-run font fallback. Supports
    /// horizontal Near (left) and Center; vertical alignment is delegated to each run's DrawText.
    /// Runs lay out left→right by measured advance.
    /// </summary>
    public void Draw<TSurface>(Renderer<TSurface> renderer, string text, float fontSize, RGBAColor32 color,
        RectInt rect, TextAlign hAlign, TextAlign vAlign)
    {
        var runs = CoverageRuns(text);
        if (runs.Count == 0) return;

        var left = Math.Min(rect.UpperLeft.X, rect.LowerRight.X);
        var right = Math.Max(rect.UpperLeft.X, rect.LowerRight.X);
        var top = Math.Min(rect.UpperLeft.Y, rect.LowerRight.Y);
        var bottom = Math.Max(rect.UpperLeft.Y, rect.LowerRight.Y);

        var startX = (float)left;
        if (hAlign == TextAlign.Center)
        {
            var total = 0f;
            foreach (var (rt, f) in runs) total += renderer.MeasureText(rt.AsSpan(), f, fontSize).Width;
            startX = left + ((right - left) - total) * 0.5f;
        }

        var x = startX;
        foreach (var (runText, font) in runs)
        {
            var rw = renderer.MeasureText(runText.AsSpan(), font, fontSize).Width;
            var runRect = new RectInt(((int)MathF.Ceiling(x + rw), bottom), ((int)x, top));
            renderer.DrawText(runText.AsSpan(), font, fontSize, color, runRect, TextAlign.Near, vAlign);
            x += rw;
        }
    }

    /// <summary>
    /// Truncate <paramref name="text"/> with a trailing ellipsis so its fallback-measured width fits
    /// <paramref name="maxW"/>. Measured across coverage runs, so non-Latin widths are accounted for.
    /// </summary>
    public string FitEllipsis<TSurface>(Renderer<TSurface> renderer, string text, float fontSize, float maxW)
    {
        if (maxW <= 0) return "";
        if (Measure(renderer, text, fontSize).Width <= maxW) return text;
        for (var len = text.Length - 1; len > 0; len--)
        {
            var cand = string.Concat(text.AsSpan(0, len), "…");
            if (Measure(renderer, cand, fontSize).Width <= maxW) return cand;
        }
        return "…";
    }
}
