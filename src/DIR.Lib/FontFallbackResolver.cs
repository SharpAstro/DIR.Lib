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
    // codepoint -> resolved font path. The hot cache: each codepoint is classified once.
    private readonly ConcurrentDictionary<int, string> _fontByCodepoint = new();

    /// <param name="primaryFontPath">The default font; used wherever it covers the codepoint.</param>
    /// <param name="fallbackFontPaths">Fallback fonts in priority order — the first that covers a
    /// codepoint the primary lacks wins. Missing files are skipped.</param>
    public FontFallbackResolver(string primaryFontPath, IEnumerable<string> fallbackFontPaths)
    {
        _primaryFontPath = primaryFontPath;
        foreach (var p in fallbackFontPaths)
            if (!string.IsNullOrEmpty(p) && File.Exists(p))
                _fallbackPaths.Add(p);
    }

    /// <summary>True if at least one fallback font is available (else this is a pass-through).</summary>
    public bool HasFallbacks => _fallbackPaths.Count > 0;

    /// <summary>
    /// Split <paramref name="text"/> into consecutive runs that each render with one font: the
    /// primary where it covers the codepoint, else the first fallback that does, else the primary
    /// (so the glyph degrades to <c>.notdef</c> rather than vanishing).
    /// </summary>
    public List<(string Text, string FontPath)> CoverageRuns(string text)
    {
        var runs = new List<(string, string)>();
        if (string.IsNullOrEmpty(text)) return runs;
        // Fast path: pure-ASCII (the common case) never needs fallback.
        if (_fallbackPaths.Count == 0 || IsAllAscii(text))
        {
            runs.Add((text, _primaryFontPath));
            return runs;
        }

        var sb = new StringBuilder();
        string? curFont = null;
        foreach (var rune in text.EnumerateRunes())
        {
            var font = ResolveFont(rune);
            if (curFont is null)
            {
                curFont = font;
            }
            else if (font != curFont)
            {
                runs.Add((sb.ToString(), curFont));
                sb.Clear();
                curFont = font;
            }
            sb.Append(rune.ToString());
        }
        if (sb.Length > 0 && curFont is not null) runs.Add((sb.ToString(), curFont));
        return runs;
    }

    private string ResolveFont(Rune rune)
    {
        if (_fontByCodepoint.TryGetValue(rune.Value, out var cached)) return cached;

        var chosen = _primaryFontPath;
        if (!Covers(_primaryFontPath, rune))
        {
            foreach (var fb in _fallbackPaths)
            {
                if (Covers(fb, rune)) { chosen = fb; break; }
            }
        }
        _fontByCodepoint[rune.Value] = chosen;
        return chosen;
    }

    private bool Covers(string fontPath, Rune rune)
    {
        var face = _faces.GetOrAdd(fontPath, LoadFace);
        return face is not null && face.GetGlyphId((uint)rune.Value) != 0;
    }

    private static OpenTypeFont? LoadFace(string path)
    {
        try { return OpenTypeFont.LoadFromFile(path); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FontFallback] failed to load '{path}': {ex.Message}");
            return null;
        }
    }

    private static bool IsAllAscii(string s)
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
