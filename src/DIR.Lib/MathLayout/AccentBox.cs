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
/// <para><b>Vertical placement:</b> the accent's baseline is raised
/// <c>max(0, base.Height − AccentBaseHeight)</c> above the base's, the
/// OpenType MATH rule: a math font draws its accents to sit over an
/// x-height base with the baselines level. So accents stay at one height
/// over short bases (x̄ and ε̄ get the same macron position), and ride
/// above the glyph top of a tall base (a hat over a big summation clears
/// the operator).</para>
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

        // Vertical, as the OpenType MATH table defines it and MathJax and TeX
        // set it: an accent glyph is drawn to sit over a base no taller than
        // AccentBaseHeight (an x-height letter) with the two baselines level,
        // so it is raised only by however much taller its base is.
        //
        // This used to put the accent's TOP a 5% em gap above
        // max(base.Height, AccentBaseHeight). An accent's ink is a band at
        // the top of its box (STIX's U+02C6 at 32 px: 5 rows at the top of a
        // 22 px box), so the band landed inside the base: a hat over x was
        // drawn across the x's top rows and could not be seen. It never met a
        // real formula, since nothing but a hand-built test reached this box.
        _accentBaselineDrop = MathF.Max(0f, @base.Height - style.AccentBaseHeight);

        // Height runs from the base baseline to whichever top is higher.
        _height = MathF.Max(@base.Height, _accentBaselineDrop + accent.Height);
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
