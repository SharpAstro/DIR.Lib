using System;

namespace DIR.Lib;

/// <summary>
/// Making a text run fit a width on a surface that can measure glyphs — the two things such a surface can
/// do about a run that is too wide, and the one honest way to decide it is too wide at all.
///
/// <para><b>Why this has to exist somewhere.</b> Neither pixel text path bounds a run to the rect it was
/// given: <see cref="Renderer{TSurface}.DrawText"/> starts at the rect's edge and keeps going, and the
/// layout painter hands it a rect the engine resolved. So a run wider than its rect draws over whatever is
/// beside it — silently, and only on the surface sizes where it happens not to fit, which is the worst way
/// to find out. Every consumer that shares a strip between two runs was therefore rolling its own
/// measure-and-scale loop; three of them lived in one chess file alone, and none of them measured through
/// the caller's <see cref="FontFallbackResolver"/>, so any run needing a fallback face was fitted against
/// the wrong width.</para>
///
/// <para><b>Design unit vs. surface unit.</b> Everything here is in the caller's DRAWING units — the same
/// <c>fontSize</c> and width it would pass to <c>DrawText</c>, already through any DPI/measure scale. This
/// is deliberately below the <see cref="Layout"/> engine's design-unit convention: the engine's job ends
/// when it hands out a rect, and a rect is in surface units.</para>
///
/// <para><b>An empty font is the only "cannot measure" this handles</b> — it returns the run untouched, which
/// keeps <see cref="PixelWidgetBase{TSurface}.FontPath"/>'s documented unresolved-font contract (empty font =
/// no text, never a throw). A NON-empty font that the renderer cannot load throws here exactly as it already
/// threw from <c>DrawText</c>; a renderer that can draw a font but not measure it is outside the
/// <see cref="Renderer{TSurface}"/> contract, so nothing is swallowed to accommodate one.</para>
/// </summary>
public static class TextFit
{
    /// <summary>The default floor for <see cref="TextTrim.Shrink"/>: below this a UI run is decoration, not
    /// text, and shrinking further trades one unreadable outcome for another.</summary>
    public const float DefaultMinFontSize = 6f;

    /// <summary>
    /// How to draw <paramref name="text"/> so it occupies no more than <paramref name="maxWidth"/>, per the
    /// run's own <paramref name="trim"/> policy: the (possibly shortened) string and the (possibly reduced)
    /// size to draw it at. A run that already fits comes back untouched, which is the common case and costs
    /// exactly one measurement.
    /// </summary>
    /// <param name="renderer">The width oracle; also the surface the result will be drawn on.</param>
    /// <param name="text">The run.</param>
    /// <param name="fontPath">Font for measuring — the same one the caller will draw with.</param>
    /// <param name="fallback">The caller's per-codepoint fallback, or null. When set, widths are measured
    /// across coverage runs, exactly as <c>DrawText</c> will draw them.</param>
    /// <param name="fontSize">The size the run wants to be drawn at.</param>
    /// <param name="maxWidth">The width available. Zero or negative means "unconstrained" — nothing is
    /// known about the space, so the run is returned as-is rather than reduced to nothing.</param>
    /// <param name="trim">Which end to sacrifice, or to scale instead. See <see cref="TextTrim"/>.</param>
    /// <param name="minFontSize">Floor for <see cref="TextTrim.Shrink"/>; see <see cref="DefaultMinFontSize"/>.</param>
    public static (string Text, float FontSize) ForWidth<TSurface>(
        Renderer<TSurface> renderer, string text, string fontPath, FontFallbackResolver? fallback,
        float fontSize, float maxWidth, TextTrim trim, float minFontSize = DefaultMinFontSize)
    {
        if (trim == TextTrim.None || maxWidth <= 0f || string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fontPath))
        {
            return (text, fontSize);
        }

        // The early-out that keeps this affordable per-leaf per-frame: one measurement answers the
        // overwhelmingly common "it fits" case, and neither branch below is entered.
        if (Measure(renderer, text, fontPath, fallback, fontSize) <= maxWidth)
        {
            return (text, fontSize);
        }

        return trim == TextTrim.Shrink
            ? (text, ShrinkToWidth(renderer, text, fontPath, fallback, fontSize, maxWidth, minFontSize))
            : (TrimToWidth(renderer, text, fontPath, fallback, fontSize, maxWidth, trim), fontSize);
    }

    /// <summary>
    /// The largest size at or below <paramref name="fontSize"/> at which <paramref name="text"/> measures no
    /// wider than <paramref name="maxWidth"/>, floored at <paramref name="minFontSize"/>.
    ///
    /// <para>Advance widths scale linearly with the size, so one division lands on the answer; hinting and
    /// integer rounding can still leave a pixel or two over, so the estimate is verified and refined a
    /// bounded few times rather than trusted outright.</para>
    ///
    /// <para><b>The returned size is always one that was MEASURED to fit</b> (or the floor — see below). That
    /// needs saying because the ratio alone cannot promise it: a rasterizer quantizes the pixel size, so the
    /// measured width is a STEP function of the requested one. Every estimate that lands inside one step
    /// measures identically, the ratio then shrinks by the same factor on the next pass, and the iteration
    /// converges to a fixed point that is still ABOVE <paramref name="maxWidth"/> — so the refined estimate
    /// was returned unverified and the run drew wider than the rect it had just been fitted to. Once the
    /// ratio stops converging, the search moves to the grid the rasterizer actually has (whole sizes) and
    /// keeps stepping down until a measurement fits.</para>
    ///
    /// <para>The floor wins over the fit: a run that cannot fit
    /// even at <paramref name="minFontSize"/> comes back AT the floor and overflows visibly, because
    /// overflow a reader can see beats text scaled to nothing.</para>
    /// </summary>
    public static float ShrinkToWidth<TSurface>(
        Renderer<TSurface> renderer, ReadOnlySpan<char> text, string fontPath, FontFallbackResolver? fallback,
        float fontSize, float maxWidth, float minFontSize = DefaultMinFontSize)
    {
        if (maxWidth <= 0f || text.IsEmpty || string.IsNullOrEmpty(fontPath))
        {
            return fontSize;
        }

        var size = fontSize;
        for (var pass = 0; pass < 4; pass++)
        {
            var measured = Measure(renderer, text, fontPath, fallback, size);
            if (measured <= maxWidth || size <= minFontSize)
            {
                return size;
            }

            size = MathF.Max(minFontSize, size * (maxWidth / measured));
        }

        // The ratio has stopped making progress — the estimates are landing inside one quantization step, so
        // they all measure the same and the factor no longer moves the result (see the remarks). Step down
        // whole sizes from here: that is the grid the rasterizer quantizes onto, so each step is the next
        // distinct width available rather than another estimate inside the same one.
        for (var whole = MathF.Floor(size); whole >= minFontSize; whole -= 1f)
        {
            if (Measure(renderer, text, fontPath, fallback, whole) <= maxWidth)
            {
                return whole;
            }
        }

        // Not even the floor fits, so the floor is the answer and the run overflows where a reader can see it.
        return minFontSize;
    }

    /// <summary>
    /// <paramref name="text"/> truncated with an ellipsis at the <paramref name="trim"/> end so it measures
    /// no wider than <paramref name="maxWidth"/> at <paramref name="fontSize"/>.
    ///
    /// <para>The resolver-free counterpart to <see cref="FontFallbackResolver.FitEllipsis"/>, which this
    /// delegates to when a resolver IS supplied -- one implementation of the End case, so the fallback and
    /// no-fallback paths cannot cut at different lengths. <see cref="TextTrim.Start"/> and
    /// <see cref="TextTrim.Middle"/> are handled here because the resolver only ever grew the End
    /// form; Middle has its own <see cref="TrimMiddleToWidth"/> because its search shape differs.</para>
    /// </summary>
    public static string TrimToWidth<TSurface>(
        Renderer<TSurface> renderer, string text, string fontPath, FontFallbackResolver? fallback,
        float fontSize, float maxWidth, TextTrim trim)
    {
        if (maxWidth <= 0f) return "";
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fontPath)) return text;
        if (Measure(renderer, text, fontPath, fallback, fontSize) <= maxWidth) return text;

        if (trim is TextTrim.Middle)
        {
            return TrimMiddleToWidth(renderer, text, fontPath, fallback, fontSize, maxWidth);
        }

        if (trim != TextTrim.Start && fallback is { } resolver)
        {
            return resolver.FitEllipsis(renderer, text, fontSize, maxWidth);
        }

        // Shortest-first would be O(n) measurements from the wrong end; walking down from the full run stops
        // at the first candidate that fits, which is the longest one — the answer, and usually within a few
        // steps of where it started.
        for (var len = text.Length - 1; len > 0; len--)
        {
            var candidate = trim == TextTrim.Start
                ? string.Concat("…", text.AsSpan(text.Length - len))
                : string.Concat(text.AsSpan(0, len), "…");
            if (Measure(renderer, candidate, fontPath, fallback, fontSize) <= maxWidth) return candidate;
        }

        return "…";
    }

    /// <summary>
    /// <paramref name="text"/> with its MIDDLE replaced by an ellipsis, keeping an equal number of
    /// characters from each end, so it measures no wider than <paramref name="maxWidth"/>.
    ///
    /// <para>Binary search over the kept-end length rather than the descending walk the Start/End
    /// case uses, because this one is called from per-FRAME paint paths on runs that can be several
    /// times too wide (a 120-character install path in a narrow panel). The walk stops at the first
    /// fit, which is cheap when a run barely overflows and O(n) measurements when it badly does;
    /// halving is O(log n) either way. Measured width is not linear in the character count on a
    /// proportional face, so trimming by ratio would over- or under-shoot -- hence a search and not
    /// arithmetic.</para>
    ///
    /// <para>Symmetric by construction: both ends keep the same count. An asymmetric split would
    /// need a rule for which end deserves the odd character, and no such rule generalises past the
    /// one case that suggested it.</para>
    /// </summary>
    public static string TrimMiddleToWidth<TSurface>(
        Renderer<TSurface> renderer, string text, string fontPath, FontFallbackResolver? fallback,
        float fontSize, float maxWidth)
    {
        if (maxWidth <= 0f) return "";
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fontPath)) return text;
        if (Measure(renderer, text, fontPath, fallback, fontSize) <= maxWidth) return text;

        const string Gap = "…";
        var lo = 0;
        var hi = text.Length / 2;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            var candidate = string.Concat(text.AsSpan(0, mid), Gap, text.AsSpan(text.Length - mid));
            if (Measure(renderer, candidate, fontPath, fallback, fontSize) <= maxWidth)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo == 0
            ? Gap
            : string.Concat(text.AsSpan(0, lo), Gap, text.AsSpan(text.Length - lo));
    }

    /// <summary>
    /// The one width oracle every branch here uses — through the fallback resolver when there is one, so a
    /// run is measured with the faces it will actually be drawn with.
    /// <para>Takes a span because the no-fallback path is the hot one (a layout paint calls it once per text
    /// leaf per frame) and needs no allocation at all. The resolver's own API is string-based, so the
    /// fallback path materializes one; that is the path already paying for per-codepoint coverage lookups.</para>
    /// </summary>
    private static float Measure<TSurface>(Renderer<TSurface> renderer, ReadOnlySpan<char> text, string fontPath,
        FontFallbackResolver? fallback, float fontSize)
        => fallback is { } resolver
            ? resolver.Measure(renderer, text.ToString(), fontSize).Width
            : renderer.MeasureText(text, fontPath, fontSize).Width;
}
