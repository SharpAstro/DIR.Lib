using System.Text;

namespace DIR.Lib.MathLayout;

/// <summary>
/// Square-root construction over a radicand box. Three rendering paths,
/// tried in order at construction time:
///
/// <list type="bullet">
///   <item><b>MATH-stretchy (best)</b> — when the loaded font ships an
///   OpenType MATH table with a vertical-stretch construction for U+221A
///   (the radical sign). Pulls the radical glyph (hook + vinculum-start) at
///   the right size via <see cref="StretchyVerticalBox"/>, then extends the
///   vinculum rightward across the radicand as the glyph's own flag
///   continued (see <see cref="DrawVinculum"/>). The font's
///   <see cref="BoxStyle.RadicalRuleThickness"/> sets the gap above the
///   radicand. Taken by STIX Two Math, Latin Modern Math, Cambria Math etc.
///   for tall radicands, where a size variant fits within 10% or the
///   assembly is needed.</item>
///
///   <item><b>Scaled base glyph (good)</b> — when the font has the U+221A
///   glyph but no MATH stretch coverage (e.g. DejaVu Sans), or when the
///   MATH size variant would overshoot the radicand by more than 10% (STIX
///   Two Math for most everyday radicands). The √ is re-rasterized at a
///   font size scaled so the glyph height roughly matches the radicand;
///   the vinculum continues the glyph's flag the same way. Bitmap-scaling
///   the font's own radical glyph beats hand-drawn lines for any moderate
///   stretch ratio.</item>
///
///   <item><b>Parametric fallback (last resort)</b> — when neither MATH nor
///   the base glyph is available, the radical is drawn as two straight
///   strokes (steep upstroke into the top corner, plus a left-shoulder
///   stroke down to the bottom tip) with the vinculum continuing right
///   across the radicand.</item>
/// </list>
///
/// Layout in all three paths: vinculum sits a small gap above the
/// radicand's top; the radical sits to the radicand's left and reaches
/// down to roughly <c>baselineY + radicand.Depth</c> so any descender
/// inside the radicand still fits.
///
/// <para>An optional <c>index</c> box (the small "n" in <c>ⁿ√x</c>) is
/// rendered tucked into the radical's hook, raised by
/// <c>RadicalDegreeBottomRaisePercent</c> of the radical's
/// total height when MATH metrics are available, with horizontal positioning
/// driven by <c>RadicalKernBeforeDegree</c> / <c>RadicalKernAfterDegree</c>.
/// Falls back to TeX-style heuristics (60% raise, ~half-radical-width
/// negative kern) when the font ships no MATH table. Pass <c>null</c> for
/// a plain square root.</para>
/// </summary>
public sealed class SqrtBox : Box
{
    private const int RadicalCodepoint = 0x221A; // U+221A √

    private readonly Box _radicand;
    private readonly Box? _index;
    private readonly float _hookWidth;
    private readonly float _gap;
    private readonly float _ruleThickness;
    private readonly float _fontSize;

    /// <summary>X offset (relative to <c>penX</c>) where the index draws.
    /// Zero when there's no index. May be negative-equivalent (i.e. small)
    /// since the index sits to the left of the radical glyph.</summary>
    private readonly float _indexX;

    /// <summary>How far the radical (and everything after) shifts right to
    /// accommodate the index. Equals <c>max(0, kernBefore + index.Width + kernAfter)</c> —
    /// i.e. zero when the negative <c>kernAfter</c> swallows the index entirely
    /// into the hook area, otherwise the leftover that pokes out the left side.</summary>
    private readonly float _radicalShiftX;

    /// <summary>Distance above <c>baselineY</c> where the index's own
    /// baseline sits. Computed so the index's bottom edge lands at
    /// <c>raisePct · radicalTotalHeight</c> above the radical's bottom
    /// (which is itself at <c>baselineY + radicand.Depth</c>).</summary>
    private readonly float _indexBaselineLift;

    /// <summary>Box height (above baseline) including any index extension —
    /// <c>max(radicand.Height + gap + vinculumThickness, indexTopAboveBaseline)</c>.</summary>
    private readonly float _heightWithIndex;

    /// <summary>The radical's own top distance above the baseline. In path
    /// 1 this is the actual bitmap-flag-top position (computed by anchoring
    /// the bitmap's bottom to the radicand's bottom, so the V's tip stays
    /// near the radicand instead of dangling below). In paths 2/3 this is
    /// <c>radicand.Height + gap + vinculumThickness</c>. Equals
    /// <see cref="Height"/> when there's no index, but can be smaller when
    /// the index is what dominates the box's top extent. Used by the draw
    /// paths to place the radical glyph and its vinculum, since those should
    /// always anchor to the radical's actual top, not the box's outer top.</summary>
    private readonly float _radicalH;

    /// <summary>Path-1 only: depth (pixels below baseline) where the radical
    /// glyph bitmap's bottom edge sits. Equals
    /// <c>radicand.Depth + RadicalExtraDescender</c> by construction so the
    /// V's tip lands just below the radicand's bottom — the
    /// "bottom-anchored" placement that proper math typography expects for
    /// asymmetric stretchy delimiters. Zero in paths 2/3 (where the radical
    /// is positioned by other means).</summary>
    private readonly float _radicalGlyphDepth;

    /// <summary>MATH-stretchy radical glyph; non-null when path 1 wins.</summary>
    private readonly StretchyVerticalBox? _radicalFont;

    /// <summary>Scaled base √ glyph; non-default when path 2 wins (font has
    /// a U+221A glyph but no MATH stretch coverage). Default GlyphBitmap
    /// (Rgba == null) means we'll fall through to the parametric path.</summary>
    private readonly GlyphBitmap _radicalScaled;

    /// <summary>Scale factor applied to the base √ glyph in path 2.</summary>
    private readonly float _radicalScaledFactor;

    /// <summary>The radical glyph as paths 1 and 2 draw it: the rasterized
    /// bitmap with the flag's antialiased right end filled in, so the flag
    /// runs at full coverage to the bitmap's right edge and the vinculum
    /// carries on from there (see <see cref="JoinFlag"/>).</summary>
    private readonly GlyphBitmap _joinedRadical;

    /// <summary>Top row of the glyph's "flag" (the horizontal stroke at the
    /// top-right where the vinculum continues), in pixels from the bitmap's
    /// top edge. Taken from the outline, not scanned: the radical is
    /// rasterized with its top snapped to a pixel row, and the flag is the
    /// top of the glyph, so this is the row the outline's top begins at.</summary>
    private readonly int _flagTopRow;

    /// <summary>The flag's own column, row by row from <see cref="_flagTopRow"/>:
    /// the alpha of its rightmost full-coverage column. The vinculum is this
    /// column repeated, not a rule of some thickness of its own. A rule
    /// matched to the V's diagonal (what this used to do, on the grounds
    /// that the flag is a heavier cap the long bar should not copy) meets
    /// the flag with a step: a bar thinner or thicker than the flag, or an
    /// opaque row against the flag's antialiased one, reads at the join as
    /// the two being out of line, which is what was reported. Repeating the
    /// flag's column makes the bar the flag's own stroke, so the two share
    /// a top edge, a bottom edge and a weight, and nothing marks the join.</summary>
    private readonly byte[] _flagProfile = [];

    /// <summary>The flag's rightmost full-coverage column, in bitmap pixels:
    /// the column <see cref="_flagProfile"/> is read from. Everything right
    /// of it, in the bitmap and beyond, is drawn with that profile.</summary>
    private readonly int _flagLastFullCol;

    public SqrtBox(Box radicand, BoxStyle style)
        : this(radicand, null, style)
    { }

    public SqrtBox(Box radicand, Box? index, BoxStyle style)
    {
        _radicand = radicand;
        _index = index;
        _hookWidth = style.FontSize * 0.45f;
        _gap = style.FontSize * 0.12f;
        _ruleThickness = style.RadicalRuleThickness;
        _fontSize = style.FontSize;

        // Required radical height = radicand TotalHeight + gap above + a sliver
        // of vinculum thickness. The MATH glyph centres itself on the math
        // axis, so we ask for the full vertical extent the radical needs to
        // reach: from radicand bottom (baseline + Depth) up past radicand top
        // (baseline - Height) plus the gap.
        var requiredHeight = radicand.TotalHeight + _gap * 2f + _ruleThickness;

        // Path 1 (MATH-stretchy variant or assembly) is the right choice
        // for LARGE stretches — the font's hand-designed bigger variants
        // beat naive scaling. But fonts often ship a coarse variant ladder
        // (e.g. natural / 1.5× / 2× / 3×), so for moderate stretches the
        // smallest variant ≥ requiredHeight can overshoot dramatically —
        // the picked V dangles way below the radicand even with top-anchor
        // placement (since the V tail naturally extends with the variant's
        // own size). We detect overshoot and fall through to path 2 (re-
        // rasterise the natural glyph at a slightly bigger font size),
        // which gives a snug, crisp fit. Threshold tuned to ~1.1 — accept
        // only up to 10% overshoot before preferring scaled-glyph path.
        //
        // Both glyph paths rasterize the radical with its top snapped to a
        // pixel row (an opt-in in ManagedFontRasterizer: nothing else asks for
        // it). The flag is the glyph's top, so its top edge is then a crisp,
        // fully covered row the vinculum can share; unsnapped, it lands
        // wherever the outline puts it and its first row is a partial one.
        // The snap moves the glyph within its own bitmap, and every placement
        // below anchors on the flag's top row in that bitmap, so the radical's
        // height and depth follow the shifted glyph rather than drifting.
        //
        // The cap is tested on the UNSNAPPED glyph: the snap can drop a row,
        // and a radical one row over the cap would otherwise flip from path 2
        // to path 1 (STIX Two Math \sqrt{\left(\frac{a}{b}\right)} at 72 px:
        // 172 rows unsnapped, 171 snapped, against a cap of 171.03; the
        // variant's tail then hangs 21 px deeper). The snapped glyph is built
        // only once path 1 has been chosen.
        const float pathOneOvershootCap = 1.1f;
        var unsnapped = new StretchyVerticalBox(RadicalCodepoint, requiredHeight, style);
        var pathOneOk = unsnapped.IsAvailable
                        && unsnapped.Bitmap.Height <= requiredHeight * pathOneOvershootCap;
        if (pathOneOk)
        {
            var radical = new StretchyVerticalBox(RadicalCodepoint, requiredHeight, style, snapTopEdge: true);
            _radicalFont = radical;
            (_flagTopRow, _flagProfile, _flagLastFullCol) = MeasureFlag(radical.Bitmap, radical.InkTopRow);
            _joinedRadical = JoinFlag(radical.Bitmap, _flagTopRow, _flagProfile, _flagLastFullCol);
        }
        else
        {
            // Path 2: scale the base √ glyph to match requiredHeight. Glyph
            // height is roughly proportional to font size, so we render once at
            // the natural size to measure, then re-render at
            // <c>fontSize * (requiredHeight / nativeHeight)</c>. We allow scale
            // < 1 (shrink below natural size) for small radicands — the
            // natural √ glyph at fontSize=96 is ~115 px tall, way bigger than
            // a single-letter radicand of ~50 px, so leaving it at scale=1
            // produces the visibly-oversized V seen in 4th-root and similar
            // scenes. MathJax shrinks the radical analogously. Floor at 0.5
            // so we never disappear; the typical case lands around 0.7.
            var rune = new Rune(RadicalCodepoint);
            var rasterizer = BoxStyle.SharedRasterizer;
            var native = rasterizer.RasterizeGlyph(style.FontPath, style.FontSize, rune);
            if (native.Rgba is not null && native.Height > 0)
            {
                var scale = MathF.Max(0.5f, requiredHeight / native.Height);
                _radicalScaled = rasterizer.RasterizeGlyphTopSnapped(style.FontPath, style.FontSize * scale, rune, out var inkTopRow);
                _radicalScaledFactor = scale;
                (_flagTopRow, _flagProfile, _flagLastFullCol) = MeasureFlag(_radicalScaled, inkTopRow);
                _joinedRadical = JoinFlag(_radicalScaled, _flagTopRow, _flagProfile, _flagLastFullCol);
            }
        }

        // Radical metrics. The radical is asymmetric (flag at top, tip at
        // bottom). We TOP-anchor: the flag-top row of the bitmap lands at
        // (radicand.Height + gap + ruleThickness) above the baseline, so the
        // vinculum hugs the radicand's top with a small visible gap above it.
        // Any extra height in the picked stretchy variant lets the V's tip
        // dangle below the radicand's bottom — that's what TeX and MathJax
        // do; the alternative (bottom-anchor) leaves a visibly large gap
        // above the radicand when the variant overshoots requiredHeight.
        float radicalD;
        if (_radicalFont is not null)
        {
            // flag-top-Y = baselineY − radicand.Height − gap − ruleThickness
            // bitmap-top-Y = flag-top-Y − flagTopRow
            // _radicalH (= baselineY − bitmap-top-Y) =
            //     radicand.Height + gap + ruleThickness + flagTopRow
            // _radicalGlyphDepth (= bitmap-bottom-Y − baselineY) =
            //     bitmap.Height − _radicalH
            // The thickness reserved is the font's rule or the rows the bar
            // is drawn on, whichever is more, as path 2 reserves them: the
            // bar is the flag's own column, and where that is thicker than
            // the rule (DejaVu Sans \sqrt{x} at 20 px: three rows against
            // one) reserving only the rule puts the bar in the gap and the
            // radicand's top against it.
            var bitmapHeight = _radicalFont.Bitmap.Height;
            _radicalH = radicand.Height + _gap + MathF.Max(_ruleThickness, VinculumThickness) + _flagTopRow;
            // Draw uses (baselineY + _radicalGlyphDepth − bitmap.Height) for
            // glyph top, so this MUST equal (bitmap.Height − _radicalH) to
            // keep top-anchor invariant. If the picked variant overshoots,
            // the V's tip dangles deep below baseline — that's deliberate.
            _radicalGlyphDepth = bitmapHeight - _radicalH;
            radicalD = _radicalGlyphDepth;
        }
        else if (HasScaledGlyph)
        {
            // Path 2 (scaled glyph): the bitmap's flag-top row lands at
            // glyphTop + flagTopRow (see DrawWithScaledGlyph). For the
            // vinculum to sit a `gap` above the radicand-top, the flag's top
            // must be radicand.Height + gap + VinculumThickness above the
            // baseline, and the glyph's top flagTopRow above that (zero
            // whenever the snap applied). So _radicalH = that:
            _radicalH = radicand.Height + _gap + VinculumThickness + _flagTopRow;
            // The V-tip is at glyphTop + scaled.Height = baselineY −
            // _radicalH + scaled.Height below baseline. Reporting that
            // depth lets a parent FracBox leave room for the V tail —
            // without it, a fraction's bar can clip through the V's
            // bottom (the visible bug in `\frac{\sqrt{\pi}}{2}`).
            radicalD = MathF.Max(radicand.Depth, _radicalScaled.Height - _radicalH);
        }
        else
        {
            // Path 3 (parametric): V is hand-drawn between vinculumY and
            // baselineY + radicand.Depth — it doesn't extend below the
            // radicand, so depth = radicand.Depth.
            _radicalH = radicand.Height + _gap + VinculumThickness;
            radicalD = radicand.Depth;
        }
        if (index is not null)
        {
            float kernBefore, kernAfter, raisePct;
            var info = BoxStyle.SharedRasterizer.GetMathConstants(style.FontPath);
            if (info is not null)
            {
                var c = info.Value.constants;
                var u = info.Value.unitsPerEm;
                kernBefore = c.RadicalKernBeforeDegree * style.FontSize / u;
                kernAfter  = c.RadicalKernAfterDegree  * style.FontSize / u;
                raisePct   = c.RadicalDegreeBottomRaisePercent / 100f;
                // Some fonts ship MATH RadicalKern values that produce a
                // degree position WAY off from the V's upper-left "hook"
                // area where it visually belongs. DejaVu Sans is a known
                // offender: kernBefore≈27 px, kernAfter≈-53 px at
                // FontSize=96, which lands the 4 over the V's right half
                // (between the V and the radicand) instead of in the
                // upper-left. We test the values against a reasonableness
                // bound — kernBefore should be a small left margin, not a
                // full-em push — and snap to the spec-typical ratios when
                // they're out of range. This is a capability/value-range
                // test, not a font-name test: any font whose MATH values
                // happen to fit the bound gets used as-is.
                var maxKernBefore = style.FontSize * 0.1f;
                if (kernBefore > maxKernBefore || -kernAfter > RadicalWidth * 0.8f)
                {
                    kernBefore = 0f;
                    kernAfter  = -RadicalWidth * 0.6f;
                }
            }
            else
            {
                // No MATH table at all: pure fallback. Same shape as the
                // MATH-clamped path above — keep the index left-tucked.
                kernBefore = 0f;
                kernAfter  = -RadicalWidth * 0.6f;
                raisePct   = 0.5f;
            }

            // OpenType MATH index/degree positioning. Reading left to right:
            //   [box.left] [kernBefore] [index] [kernAfter] [radical]
            // kernAfter is typically negative — the radical's left edge sits
            // *behind* the index's right edge so the index visually tucks
            // into the radical's hook. The natural position of radical.left
            // = kernBefore + index.Width + kernAfter; if that's negative,
            // radical.left would be outside the box, so we clamp to 0 and
            // push the index right enough to keep the same overlap relation.
            //   index.right − radical.left = −kernAfter (always)
            // ⇒ index.left = radical.left − kernAfter − index.Width
            // For wide indices this collapses back to index.left = kernBefore;
            // for the typical narrow degree (e.g. "4"), it correctly seats
            // the index over the radical's hook instead of leaving the
            // radical floating to the index's *left* (the previous formula
            // mistakenly placed the radical at x=0 with the index at
            // x=kernBefore, so the radical sat under the index's left edge).
            _radicalShiftX = MathF.Max(0f, kernBefore + index.Width + kernAfter);
            _indexX = _radicalShiftX - kernAfter - index.Width;

            // Index bottom should be raisePct of total radical height above
            // the radical's bottom. Convert to "lift above box baseline":
            //   indexBottomLift = raisePct·(H+D) − D
            //   indexBaselineLift = indexBottomLift + index.Depth
            var totalH = _radicalH + radicalD;
            var indexBottomLift = raisePct * totalH - radicalD;
            _indexBaselineLift = indexBottomLift + index.Depth;

            // Cap the lift so the index's top edge sits at-or-below the
            // ACTUAL vinculum (which is at _radicalH − flagTopRow above
            // baseline for paths 1/2 — the bitmap's flag-top row sits
            // flagTopRow pixels below the bitmap's top edge). Without
            // this cap, fonts with no MATH table get the heuristic 60%
            // raise + standard index sizing, and the index's top pokes
            // ABOVE the vinculum line — visible in DejaVu's ⁴√x where
            // the "4" crossed the bar. Using `_radicalH` directly was
            // wrong because that's the bitmap top, not the vinculum top;
            // it left a flagTopRow-px gap where the index could still
            // visibly overshoot. The cap is a no-op for fonts whose MATH
            // constants already keep the index inside the V.
            var vinculumTopAboveBaseline = _radicalH - _flagTopRow;
            var maxBaselineLift = vinculumTopAboveBaseline - index.Height;
            if (_indexBaselineLift > maxBaselineLift)
                _indexBaselineLift = maxBaselineLift;

            var indexTopLift = _indexBaselineLift + index.Height;
            _heightWithIndex = MathF.Max(_radicalH, indexTopLift);
        }
        else
        {
            _heightWithIndex = _radicalH;
        }
        _depthValue = radicalD;
    }

    /// <summary>Box depth — pixels below baseline where the radical glyph's
    /// bottom (V-tip) lands. For path 1 this is the bottom-anchored bitmap's
    /// excursion; for path 2 it's how far the scaled glyph's bitmap extends
    /// below the baseline (often a few pixels past the radicand's own depth);
    /// for path 3 it equals radicand.Depth. Reporting the V-tip's true
    /// position lets parent FracBox / HBox containers leave room — otherwise
    /// a fraction bar can cut through a deep V tail.</summary>
    private readonly float _depthValue;

    /// <summary>How far, in alpha, a column's top row may fall short of the
    /// top row's maximum and still count as the flag at full coverage.</summary>
    private const int FlagProfileTolerance = 8;

    /// <summary>The faintest trailing row the flag's band keeps. At small sizes
    /// the flag's column ends in antialiasing tail rows (STIX Two Math
    /// <c>\sqrt{2}</c> at 10 px: 158, 112, 46, 1), and each row kept is a row
    /// the vinculum is drawn on and the layout reserves, so a near-invisible
    /// tail pushes the radical a whole pixel taller. Trailing rows fainter than
    /// this are left out of both; the top row never is. It must stay at or
    /// below 32, where a dropped row would start to show as a thinner bar.</summary>
    private const int FlagTailMinAlpha = 16;

    /// <summary>
    /// Find the radical's flag in its rasterized glyph: the row its top
    /// begins at, its alpha profile down that row band, and the rightmost
    /// column that still has that profile (the flag at full coverage, before
    /// its antialiased right end).
    /// <para>
    /// <paramref name="inkTopRow"/> is where the outline's top lands, from
    /// the snapped rasterization; the flag's top is the glyph's top, so that
    /// is the flag's top row and nothing is scanned for it. It is -1 only
    /// when the glyph could not be snapped (a colour glyph), and the first
    /// inked row is used instead.
    /// </para>
    /// <para>
    /// The last full column is the rightmost one whose top-row alpha is on
    /// the top row's plateau: right of it is only the antialiased right end.
    /// The flag's right end is square (checked for STIX Two Math and DejaVu
    /// Sans), so that column is full on every row of the band, and it is the
    /// column furthest from the V's diagonal, which on a narrow glyph (a
    /// small size, or a MATH size variant) reaches almost to the right edge
    /// a few rows down. The band runs down that column while it has ink, so
    /// a partially covered bottom row belongs to it.
    /// </para>
    /// </summary>
    private static (int TopRow, byte[] Profile, int LastFullCol) MeasureFlag(GlyphBitmap bm, int inkTopRow)
    {
        if (bm.Rgba is null || bm.Width <= 0 || bm.Height <= 0)
            return (0, [], bm.Width - 1);

        var w = bm.Width;
        var h = bm.Height;
        var data = bm.Rgba;
        byte Alpha(int col, int row) => data[(row * w + col) * 4 + 3];
        int RowMax(int row)
        {
            var max = 0;
            for (var col = 0; col < w; col++) max = Math.Max(max, Alpha(col, row));
            return max;
        }

        var top = Math.Max(0, inkTopRow);
        while (top < h && RowMax(top) == 0) top++;
        if (top == h) return (0, [], w - 1);

        var topMax = RowMax(top);
        var last = w - 1;
        while (last > 0 && Alpha(last, top) < topMax - FlagProfileTolerance) last--;

        var bottom = top;
        while (bottom < h && Alpha(last, bottom) > 0) bottom++;
        while (bottom - 1 > top && Alpha(last, bottom - 1) < FlagTailMinAlpha) bottom--;

        var profile = new byte[bottom - top];
        for (var y = top; y < bottom; y++) profile[y - top] = Alpha(last, y);
        return (top, profile, last);
    }

    /// <summary>
    /// The radical bitmap with its flag's right end filled in: every column
    /// right of the flag's last full-coverage column gets that column's
    /// pixels down the flag's band, so the flag runs at full coverage to the
    /// bitmap's right edge, where the vinculum (the same column, repeated)
    /// takes over. Filling the end inside the bitmap, rather than drawing the
    /// bar over it, writes each pixel of the join once: blending the bar over
    /// the antialiased end would darken it past the flag's own coverage.
    /// </summary>
    private static GlyphBitmap JoinFlag(GlyphBitmap bm, int topRow, byte[] profile, int lastFullCol)
    {
        if (bm.Rgba is null || profile.Length == 0 || lastFullCol >= bm.Width - 1) return bm;
        var w = bm.Width;
        var rgba = (byte[])bm.Rgba.Clone();
        for (var y = topRow; y < topRow + profile.Length; y++)
        {
            var src = (y * w + lastFullCol) * 4;
            for (var col = lastFullCol + 1; col < w; col++)
                Array.Copy(rgba, src, rgba, (y * w + col) * 4, 4);
        }
        return bm with { Rgba = rgba };
    }

    private bool HasScaledGlyph => _radicalScaled.Rgba is not null && _radicalScaled.Width > 0;

    /// <summary>Which of the three rendering paths drew the radical (see the
    /// class summary): 1 MATH-stretchy, 2 scaled base glyph, 3 parametric.</summary>
    internal int Path => _radicalFont is not null ? 1 : HasScaledGlyph ? 2 : 3;

    /// <summary>The pixels-per-em the radical's glyph was rasterized at: the
    /// box's font size on path 1, that size times the scale on path 2.</summary>
    internal float RadicalGlyphSize => _radicalFont is not null ? _fontSize : _fontSize * _radicalScaledFactor;

    /// <summary>Width of whichever radical-rendering path is active.</summary>
    private float RadicalWidth =>
        _radicalFont is not null ? _radicalFont.Width :
        HasScaledGlyph ? _radicalScaled.Width :
        _hookWidth;

    /// <summary>Vinculum thickness for the active path's layout. Paths 1 and 2
    /// draw the vinculum as the flag's own column repeated (see
    /// <see cref="_flagProfile"/> for why not the V's diagonal), and the
    /// layout counts every pixel row that bar touches, its partially covered
    /// bottom row included. Counting the coverage instead, or rounding it,
    /// lets that bottom row sit in the gap: at 24 px the radicand's own
    /// antialiased top then lands on the row directly under it, and the two
    /// touch. Whole rows also keep the radical's height an integer away from
    /// the integer-row rule this replaced, so its floored placement moves
    /// by whole rows and the flag still starts on its snapped row. Path 3
    /// (parametric) has no glyph to take it from, so it falls back to
    /// <see cref="BoxStyle.RadicalRuleThickness"/>.</summary>
    private float VinculumThickness =>
        _flagProfile.Length > 0 ? _flagProfile.Length : _ruleThickness;

    public override float Width => _radicalShiftX + RadicalWidth + _radicand.Width + _gap;
    public override float Height => _heightWithIndex;
    public override float Depth => _depthValue;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        if (_index is not null)
        {
            // Index draws first, at its raised position. The index uses its
            // own (caller-supplied) BoxStyle — typically the parent style's
            // Smaller().Smaller() — so the small "n" is rasterized at the
            // appropriate em size; we just place the box.
            _index.Draw(renderer, penX + _indexX, baselineY - _indexBaselineLift, style);
        }

        // Everything radical-side shifts right by _radicalShiftX (zero in the
        // no-index case) so the existing draw logic stays untouched apart
        // from the pen position it gets handed.
        var radicalPenX = penX + _radicalShiftX;
        if (_radicalFont is not null)
        {
            DrawWithFontGlyph(renderer, radicalPenX, baselineY, style);
            return;
        }
        if (HasScaledGlyph)
        {
            DrawWithScaledGlyph(renderer, radicalPenX, baselineY, style);
            return;
        }
        DrawParametric(renderer, radicalPenX, baselineY, style);
    }

    /// <summary>
    /// Scaled-base-glyph path: blit the (re-rasterized at larger fontSize)
    /// √ glyph with its top edge at the vinculum line, then extend the
    /// vinculum across the radicand. Used for fonts with a √ glyph but no
    /// MATH stretch coverage (e.g. DejaVu Sans). The glyph's own bottom-
    /// hook tip lands somewhere near the radicand's bottom — exact
    /// alignment depends on the font's glyph design, but visually the
    /// result is much closer to a real radical than the parametric V.
    /// </summary>
    private void DrawWithScaledGlyph(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        // Anchor to the radical's own top (_radicalH), not the box's outer
        // Height — the latter includes any index lift and would float the
        // radical glyph upward when an index extends above.
        var glyphTop = baselineY - _radicalH;
        var (x0, y0) = DrawRadicalGlyph(renderer, penX, glyphTop, style);

        var radicandX = penX + _radicalScaled.Width;
        _radicand.Draw(renderer, radicandX, baselineY, style);

        DrawVinculum(renderer, x0, y0, radicandX, style);
    }

    /// <summary>Where the last <see cref="Draw"/> put the radical's flag and
    /// the vinculum continuing it, in surface pixels; null before the first
    /// draw and on the parametric path. Lets a test find the join from the
    /// layout's own numbers instead of searching the image for it.</summary>
    internal RadicalJoin? LastJoin { get; private set; }

    /// <summary>The flag's top row and band height, its rightmost
    /// full-coverage column, the vinculum's first column (the glyph's right
    /// edge) and one past its last, all in surface pixels.</summary>
    internal sealed record RadicalJoin(int FlagTopY, int FlagRows, int FlagLastFullX, int BarStartX, int BarEndX);

    /// <summary>Draw the radical glyph, its flag's end filled in (see
    /// <see cref="JoinFlag"/>), at the floored pen position, and return that
    /// position for <see cref="DrawVinculum"/>.</summary>
    private (int X0, int Y0) DrawRadicalGlyph(RgbaImageRenderer renderer, float penX, float glyphTop, BoxStyle style)
    {
        var x0 = (int)MathF.Floor(penX);
        var y0 = (int)MathF.Floor(glyphTop);
        renderer.DrawGlyphBitmap(x0, y0, _joinedRadical, style.Foreground);
        return (x0, y0);
    }

    /// <summary>
    /// The vinculum of paths 1 and 2: the flag's column repeated, row by row,
    /// from the glyph's right edge (where the filled-in flag ends) to past
    /// the radicand. Each row is the flag's own coverage on that row, so the
    /// bar has the flag's top edge, bottom edge and weight.
    /// </summary>
    private void DrawVinculum(RgbaImageRenderer renderer, int x0, int y0, float radicandX, BoxStyle style)
    {
        var fg = style.Foreground;
        var barTop = y0 + _flagTopRow;
        var barLeft = x0 + _joinedRadical.Width;
        var barRight = (int)MathF.Ceiling(radicandX + _radicand.Width + _gap);
        for (var r = 0; r < _flagProfile.Length; r++)
        {
            // The same tint the glyph blit applies to its own alpha, so a bar
            // row and the flag row beside it come out identical.
            var a = (byte)((_flagProfile[r] * fg.Alpha + 127) / 255);
            if (a == 0) continue;
            renderer.FillRectangle(
                new RectInt(new PointInt(barRight, barTop + r + 1), new PointInt(barLeft, barTop + r)),
                new RGBAColor32(fg.Red, fg.Green, fg.Blue, a));
        }
        LastJoin = new RadicalJoin(barTop, _flagProfile.Length, x0 + _flagLastFullCol, barLeft, Math.Max(barLeft, barRight));
    }

    /// <summary>
    /// MATH-driven path: blit the stretchy radical glyph to the left of the
    /// radicand, then extend the vinculum across the radicand's width as the
    /// glyph's own top stroke continued (see <see cref="DrawVinculum"/>):
    /// its top row comes from the snapped outline and its profile from the
    /// flag's last full-coverage column (see <see cref="MeasureFlag"/>), so
    /// the bar joins the flag without a step or gap.
    /// </summary>
    private void DrawWithFontGlyph(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        // Bottom-anchored radical: position the bitmap so its bottom edge
        // lands at baselineY + _radicalGlyphDepth (= radicand.Depth +
        // extraDescender). That keeps the V's tip just below the radicand
        // even when the picked variant overshoots requiredHeight. The
        // bitmap top therefore sits at (radicalGlyphDepth - bitmap.Height)
        // pixels relative to baseline.
        var glyphTop = baselineY + _radicalGlyphDepth - _joinedRadical.Height;
        var (x0, y0) = DrawRadicalGlyph(renderer, penX, glyphTop, style);

        var radicandX = penX + _radicalFont!.Width;
        _radicand.Draw(renderer, radicandX, baselineY, style);

        DrawVinculum(renderer, x0, y0, radicandX, style);
    }

    /// <summary>
    /// Parametric fallback for fonts with no MATH coverage of '√': two
    /// straight strokes forming a check-mark shape, plus the vinculum.
    /// </summary>
    private void DrawParametric(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        float radicandX = penX + _hookWidth;
        float radicandTop = baselineY - _radicand.Height;
        float radicandBottom = baselineY + _radicand.Depth;
        float vinculumY = radicandTop - _gap;

        // Radicand.
        _radicand.Draw(renderer, radicandX, baselineY, style);

        // Vinculum: horizontal rule across the top of the radicand. The
        // right edge anchors to the radicand (not "penX + Width") so we
        // don't double-count the index shift — penX here is already the
        // post-shift radicalPenX.
        var vincRight = (int)MathF.Ceiling(radicandX + _radicand.Width + _gap);
        var vinc = new RectInt(
            new PointInt(vincRight, (int)MathF.Ceiling(vinculumY + _ruleThickness / 2f)),
            new PointInt((int)MathF.Floor(penX + _hookWidth - _ruleThickness / 2f), (int)MathF.Floor(vinculumY - _ruleThickness / 2f)));
        renderer.FillRectangle(vinc, style.Foreground);

        // Hook: two straight strokes forming a check-mark shape.
        // Top point of the hook = (penX + hookWidth, vinculumY).
        // Bottom-tip = ~one-third down from baseline (visual sweet spot).
        // Left-shoulder = (penX, vinculumY + 0.4*hookHeight).
        float hookTopX = penX + _hookWidth;
        float hookTopY = vinculumY;
        float hookTipX = penX + _hookWidth * 0.45f;
        float hookTipY = radicandBottom;
        float hookLeftX = penX;
        float hookLeftY = vinculumY + (radicandBottom - vinculumY) * 0.35f;

        int thickness = (int)MathF.Max(1f, _ruleThickness);
        renderer.DrawLine(hookTopX, hookTopY, hookTipX, hookTipY, style.Foreground, thickness);
        renderer.DrawLine(hookTipX, hookTipY, hookLeftX, hookLeftY, style.Foreground, thickness);
    }
}
