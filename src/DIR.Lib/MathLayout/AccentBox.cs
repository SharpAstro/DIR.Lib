using System.Text;

namespace DIR.Lib.MathLayout;

/// <summary>
/// A base box with a top accent (macron, hat, tilde, dot, vector arrow, …)
/// stacked above. Replaces the naive <c>GlyphBox("ψ̄")</c> approach: the
/// rasterizer sums per-rune advances and doesn't apply OpenType GPOS mark
/// anchoring, so combining marks land at the *next* glyph's pen position
/// rather than over the previous letter. <see cref="AccentBox"/> places
/// the accent explicitly using the OpenType MATH
/// <c>MathTopAccentAttachment</c> table when available — the same data
/// MathJax / TeX consume — and falls back to centred placement when the
/// font lacks the metadata.
///
/// <para><b>Horizontal alignment:</b> the accent is centred such that its
/// own attachment point sits over the base's attachment point. For each
/// of the two boxes the attachment x is read from the font (FUnit value
/// converted to pixels) when the box is a single-rune <see cref="GlyphBox"/>;
/// otherwise it defaults to half the box's advance — the spec-default
/// centre.</para>
///
/// <para><b>Vertical placement:</b> the accent's <i>top</i> sits at
/// <c>max(base.Height, AccentBaseHeight)</c> above the base's baseline.
/// We anchor the top rather than the bottom because real macron / hat /
/// tilde glyphs put their visible mark near the top of the glyph cell
/// with empty space below — anchoring on the bottom makes the bar float
/// <c>accent.Height</c> too far above the base. Two effects still fall
/// out: (1) accents stay at a consistent height over
/// short bases (x̄ and ε̄ get the same macron position), and (2) accents
/// ride above the actual glyph top for tall bases (so a hat over a
/// big-summation doesn't intersect the operator).</para>
///
/// <para>The box's reported <see cref="Box.Width"/> is the base's width —
/// the accent is allowed to overhang horizontally without pushing
/// neighbours, matching TeX/MathJax convention.</para>
/// </summary>
public sealed class AccentBox : Box
{
    private readonly Box _base;
    private readonly Box _accent;
    private readonly float _accentXOffset;       // accent left edge relative to base left edge
    private readonly float _accentBaselineDrop;  // distance from base baseline up to accent baseline (positive)
    private readonly float _height;
    private readonly float _depth;

    public AccentBox(Box @base, Box accent, BoxStyle style)
    {
        _base = @base;
        _accent = accent;

        // Horizontal: accent attachment over base attachment.
        var baseAttach = TryGetTopAccentAttachment(@base, style) ?? @base.Width / 2f;
        var accentAttach = TryGetTopAccentAttachment(accent, style) ?? accent.Width / 2f;
        _accentXOffset = baseAttach - accentAttach;

        // Vertical: place the accent's TOP at (refTop + gap) above base
        // baseline. The accent's top in its own coords is
        // (accent_baseline - accent.Height), so:
        //     accent_baseline = base_baseline - (refTop + gap) + accent.Height
        // i.e. the baseline drop (positive = above base baseline) is
        //     refTop + gap - accent.Height.
        //
        // Without the gap, the visible mark inside the accent glyph (which
        // sits near the cell's top edge in any sensible font design) lands
        // exactly at base.Height above baseline — i.e., ON the base's top
        // edge — and disappears into the base's hairline. The gap is a
        // small fraction of font size; OpenType MATH supplies no per-
        // accent constant for this (only OverbarVerticalGap, which the
        // explicit \overline primitive uses), so we fall back to a TeX-
        // style heuristic of 5% em. Visually that's ~5 px at the 96 px
        // baseline render and stays proportional at smaller sizes.
        var refTop = MathF.Max(@base.Height, style.AccentBaseHeight);
        var gap = style.FontSize * 0.05f;
        _accentBaselineDrop = refTop + gap - accent.Height;

        // Height extends from base baseline up to top of accent — which is
        // exactly refTop + gap by construction.
        _height = refTop + gap;
        _depth = @base.Depth;
    }

    public override float Width => _base.Width;
    public override float Height => _height;
    public override float Depth => _depth;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        _base.Draw(renderer, penX, baselineY, style);
        _accent.Draw(renderer, penX + _accentXOffset, baselineY - _accentBaselineDrop, style);
    }

    /// <summary>
    /// Resolve a box's top-accent attachment x in pixels — the canonical
    /// MATH-table value when the box is a single-rune <see cref="GlyphBox"/>
    /// and the font supplies it, else null (caller falls back to advance/2).
    /// Multi-rune or non-glyph boxes always return null since the MATH
    /// table is per-glyph and there's no obvious aggregation.
    /// </summary>
    private static float? TryGetTopAccentAttachment(Box box, BoxStyle style)
    {
        if (box is not GlyphBox gb) return null;
        var text = gb.Text;
        if (text.Length == 0) return null;
        // Only single-rune glyphs have an unambiguous attachment lookup.
        var enumerator = text.EnumerateRunes();
        if (!enumerator.MoveNext()) return null;
        var rune = enumerator.Current;
        if (enumerator.MoveNext()) return null;
        // Query at the box's own rasterization font size — the accent and
        // base may be at different sizes (e.g. an accent over a script).
        return BoxStyle.SharedRasterizer.GetTopAccentAttachmentPx(style.FontPath, gb.FontSize, rune);
    }
}
