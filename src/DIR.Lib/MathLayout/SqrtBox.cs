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
///   vinculum rightward across the radicand as a thin rectangle of the
///   font's <c>RadicalRuleThickness</c> via <see cref="BoxStyle.RadicalRuleThickness"/>,
///   which falls back to the generic <see cref="BoxStyle.RuleThickness"/> when
///   the font has no MATH table. STIX Two Math, Latin Modern Math,
///   Cambria Math etc. take this path.</item>
///
///   <item><b>Scaled base glyph (good)</b> — when the font has the U+221A
///   glyph but no MATH stretch coverage (e.g. DejaVu Sans). The √ is
///   re-rasterized at a font size scaled so the glyph height roughly
///   matches the radicand; vinculum extends from the glyph's top-right.
///   Bitmap-scaling the font's own radical glyph beats hand-drawn lines for
///   any moderate stretch ratio.</item>
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
/// <see cref="MathConstants.RadicalDegreeBottomRaisePercent"/> of the radical's
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

    /// <summary>Top row of the scaled glyph's "flag" (the horizontal stroke
    /// at the top-right where the vinculum continues), measured in pixels
    /// from the bitmap's top edge. Used to align the vinculum's top edge
    /// pixel-perfectly with the flag.</summary>
    private readonly int _flagTopRow;

    /// <summary>Pixel thickness of the V's diagonal stroke just below the
    /// flag. Used as the vinculum thickness — the flag itself is usually
    /// drawn thicker by the font designer (a localized cap at the join),
    /// but extending that heavier stroke across the whole vinculum looks
    /// out of proportion vs the rest of the radical. The V's diagonal
    /// thickness is what we want the vinculum to match visually.</summary>
    private readonly int _vDiagonalThickness;

    /// <summary>Rightmost inked column of the flag, used as the vinculum's
    /// left edge so the rule starts where the flag ends, not where the
    /// bitmap's bounding box ends (which often has a few pixels of right
    /// padding past the last ink).</summary>
    private readonly int _flagRightCol;

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
        const float pathOneOvershootCap = 1.1f;
        var radical = new StretchyVerticalBox(RadicalCodepoint, requiredHeight, style);
        var pathOneOk = radical.IsAvailable
                        && radical.Bitmap.Height <= requiredHeight * pathOneOvershootCap;
        if (pathOneOk)
        {
            _radicalFont = radical;
            // Path 1 uses the same flag-measurement trick as path 2: scan the
            // composed bitmap to find where the radical's flag actually sits,
            // so the rightward vinculum continuation aligns to the glyph's
            // ink rather than to the bitmap's bounding-box top (which often
            // has a row or two of transparent padding above the flag, putting
            // the vinculum visibly above the V's hook).
            (_flagTopRow, _vDiagonalThickness, _flagRightCol) = MeasureTopFlag(radical.Bitmap);
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
                _radicalScaled = rasterizer.RasterizeGlyph(style.FontPath, style.FontSize * scale, rune);
                _radicalScaledFactor = scale;
                (_flagTopRow, _vDiagonalThickness, _flagRightCol) = MeasureTopFlag(_radicalScaled);
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
            var bitmapHeight = _radicalFont.Bitmap.Height;
            _radicalH = radicand.Height + _gap + _ruleThickness + _flagTopRow;
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
            // vinculum to sit a `gap` above the radicand-top, the
            // glyph-top must be at radicand.Height + gap + VinculumThickness
            // − flagTopRow above baseline. So _radicalH = that:
            _radicalH = radicand.Height + _gap + VinculumThickness;
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

    /// <summary>
    /// Scan the rasterized √ glyph to figure out (a) where the flag's top
    /// edge is — that's where the vinculum should start vertically, (b) the
    /// V's diagonal stroke thickness — that's how thick the vinculum should
    /// be, and (c) the rightmost inked column of the flag — that's where
    /// the vinculum should start horizontally.
    /// <para>
    /// Why measure the V diagonal instead of the flag itself: the flag is
    /// usually drawn thicker by the font designer (a localised cap that
    /// terminates the V into a horizontal stroke at the radicand's top).
    /// Extending that heavier weight across the whole vinculum looks
    /// out-of-proportion vs the V; the eye reads the long horizontal as
    /// "the same stroke" as the V diagonals, so it should match THEIR
    /// weight, not the cap's.
    /// </para>
    /// </summary>
    private static (int flagTop, int vDiagonalThickness, int flagRight) MeasureTopFlag(GlyphBitmap bm)
    {
        if (bm.Rgba is null || bm.Width <= 0 || bm.Height <= 0)
            return (0, (int)MathF.Max(1f, bm.Height * 0.05f), bm.Width);

        const byte alphaThreshold = 32;  // ignore faint AA pixels
        var w = bm.Width;
        var h = bm.Height;
        var data = bm.Rgba;

        // Step 1: find the flag's vertical bounds by sampling the rightmost
        // few columns. Far enough right that the V's diagonal hasn't reached
        // — that area is pure flag.
        var sampleCol = MathF.Min(w - 2, w * 0.92f);
        var x = (int)sampleCol;
        if (x < 0) x = 0;

        int flagTop = -1, flagBottom = -1;
        for (var y = 0; y < h; y++)
        {
            var alpha = data[(y * w + x) * 4 + 3];
            if (alpha >= alphaThreshold)
            {
                if (flagTop < 0) flagTop = y;
                flagBottom = y + 1;
            }
            else if (flagTop >= 0)
            {
                break;
            }
        }
        if (flagTop < 0) return (0, (int)MathF.Max(1f, h * 0.05f), w);

        // Step 2: rightmost inked column of the flag — the vinculum starts
        // here so it joins the flag's right end without a gap.
        var flagRight = 0;
        for (var y = flagTop; y < flagBottom; y++)
        for (var col = w - 1; col >= 0; col--)
        {
            if (data[(y * w + col) * 4 + 3] >= alphaThreshold)
            {
                if (col + 1 > flagRight) flagRight = col + 1;
                break;
            }
        }
        if (flagRight == 0) flagRight = w;

        // Step 3: V diagonal thickness — sample the row immediately below
        // the flag, find the leftmost contiguous inked stripe (that's the
        // V's diagonal continuing down from the flag's left underside).
        // Width of that stripe ≈ the diagonal stroke thickness, which is
        // what the vinculum should match. Falls back to flag thickness if
        // there's no row below the flag (tiny glyphs).
        var diagonalThickness = flagBottom - flagTop;
        if (flagBottom < h)
        {
            int dStart = -1, dEnd = -1;
            for (var col = 0; col < w; col++)
            {
                if (data[(flagBottom * w + col) * 4 + 3] >= alphaThreshold)
                {
                    if (dStart < 0) dStart = col;
                    dEnd = col;
                }
                else if (dStart >= 0)
                {
                    break;  // end of the first (left-side) inked stripe
                }
            }
            if (dStart >= 0)
            {
                var width = dEnd - dStart + 1;
                // Guard against pathological measurements (e.g. flagBottom
                // landing on the V's tip row where two diagonals merge):
                // never report a diagonal thicker than the flag itself.
                if (width > 0 && width <= flagBottom - flagTop)
                    diagonalThickness = width;
            }
        }

        return (flagTop, diagonalThickness, flagRight);
    }

    private bool HasScaledGlyph => _radicalScaled.Rgba is not null && _radicalScaled.Width > 0;

    /// <summary>Width of whichever radical-rendering path is active.</summary>
    private float RadicalWidth =>
        _radicalFont is not null ? _radicalFont.Width :
        HasScaledGlyph ? _radicalScaled.Width :
        _hookWidth;

    /// <summary>Vinculum thickness for the active path. Paths 1 and 2 both
    /// use the measured V-diagonal thickness (from <see cref="MeasureTopFlag"/>)
    /// so the vinculum visually matches the V's strokes — see that method's
    /// docstring for why we don't use the (heavier) flag thickness. Path 3
    /// (parametric) has no bitmap to measure, so it falls back to
    /// <see cref="BoxStyle.RadicalRuleThickness"/>.</summary>
    private float VinculumThickness =>
        _vDiagonalThickness > 0 ? _vDiagonalThickness : _ruleThickness;

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
        renderer.DrawGlyphBitmap(
            (int)MathF.Floor(penX),
            (int)MathF.Floor(glyphTop),
            _radicalScaled,
            style.Foreground);

        var radicandX = penX + _radicalScaled.Width;
        _radicand.Draw(renderer, radicandX, baselineY, style);

        // Vinculum continuation: pixel-aligned to the glyph.
        //   Y-top    = the flag's measured top row, so the vinculum sits
        //              flush with the top edge of the flag.
        //   Thickness = V's diagonal stroke thickness (NOT the flag's full
        //              thickness — see MeasureTopFlag for why).
        //   X-left   = the flag's rightmost inked column, so the rule is
        //              contiguous with the flag's right end (no gap, no
        //              overlap step).
        // Net effect: the long horizontal vinculum reads as "the same
        // stroke" as the V diagonals, while the font's heavier flag
        // remains as a small visible cap at the join.
        var vincTop = (int)MathF.Floor(glyphTop) + _flagTopRow;
        var vincBottom = vincTop + _vDiagonalThickness;
        var vincLeft = (int)MathF.Floor(penX) + _flagRightCol;
        var vincRight = (int)MathF.Ceiling(radicandX + _radicand.Width + _gap);
        var vinc = new RectInt(
            new PointInt(vincRight, vincBottom),
            new PointInt(vincLeft, vincTop));
        renderer.FillRectangle(vinc, style.Foreground);
    }

    /// <summary>
    /// MATH-driven path: blit the stretchy radical glyph to the left of the
    /// radicand, then extend the vinculum across the radicand's width as a
    /// thin horizontal rule continuous with the glyph's own top stroke.
    /// Vinculum Y/thickness/left-edge are measured from the composed bitmap
    /// (see <see cref="MeasureTopFlag"/>) so the rule visually joins the
    /// glyph's flag without a step or gap, regardless of how many transparent
    /// rows the bitmap has above the actual ink.
    /// </summary>
    private void DrawWithFontGlyph(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        // Bottom-anchored radical: position the bitmap so its bottom edge
        // lands at baselineY + _radicalGlyphDepth (= radicand.Depth +
        // extraDescender). That keeps the V's tip just below the radicand
        // even when the picked variant overshoots requiredHeight. The
        // bitmap top therefore sits at (radicalGlyphDepth - bitmap.Height)
        // pixels relative to baseline.
        var bitmap = _radicalFont!.Bitmap;
        var glyphTop = baselineY + _radicalGlyphDepth - bitmap.Height;
        renderer.DrawGlyphBitmap(
            (int)MathF.Floor(penX),
            (int)MathF.Floor(glyphTop),
            bitmap,
            style.Foreground);

        var radicandX = penX + _radicalFont.Width;
        _radicand.Draw(renderer, radicandX, baselineY, style);

        // Vinculum continuation: aligned to the bitmap's measured flag —
        // top edge at flag's first inked row, thickness matched to the V's
        // diagonal stroke, left edge at the flag's rightmost inked column
        // so the rule joins the flag's right end without a gap or step.
        var vincTop = (int)MathF.Floor(glyphTop) + _flagTopRow;
        var vincBottom = vincTop + _vDiagonalThickness;
        var vincLeft = (int)MathF.Floor(penX) + _flagRightCol;
        var vincRight = (int)MathF.Ceiling(radicandX + _radicand.Width + _gap);
        var vinc = new RectInt(
            new PointInt(vincRight, vincBottom),
            new PointInt(vincLeft, vincTop));
        renderer.FillRectangle(vinc, style.Foreground);
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
