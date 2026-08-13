namespace DIR.Lib;

public abstract class Renderer<TSurface>(TSurface surface) : IDisposable
{
    public TSurface Surface { get; } = surface;

    public abstract uint Width { get; }
    public abstract uint Height { get; }

    public abstract void Resize(uint width, uint height);

    public abstract void DrawRectangle(in RectInt rect, RGBAColor32 strokeColor, int strokeWidth);
    public abstract void FillRectangle(in RectInt rect, RGBAColor32 fillColor);
    public abstract void FillEllipse(in RectInt rect, RGBAColor32 fillColor);

    /// <summary>
    /// Fills a triangle list: <paramref name="vertices"/> is x,y pairs, three vertices per triangle,
    /// in surface pixels. Winding is irrelevant — every triangle is filled.
    /// </summary>
    /// <remarks>
    /// <para>The default is a scanline fill written in terms of <see cref="FillRectangle"/>, so every
    /// backend has it whether or not it has a triangle pipeline; a GPU renderer overrides it with one
    /// draw call. Unantialiased, like the other constructed primitives here.</para>
    ///
    /// <para><b>Why a primitive rather than a caller's loop.</b> Anything not made of rectangles,
    /// ellipses and text — an arrowhead, a chevron, a chart's filled area — is a triangle list, and a
    /// widget that cannot say so has to reach past the abstraction to whichever backend can. One
    /// missing primitive is enough to pin a whole UI layer to one renderer.</para>
    /// </remarks>
    public virtual void DrawTriangles(ReadOnlySpan<float> vertices, RGBAColor32 color)
    {
        for (var i = 0; i + 6 <= vertices.Length; i += 6)
        {
            FillTriangle(vertices[i], vertices[i + 1], vertices[i + 2],
                         vertices[i + 3], vertices[i + 4], vertices[i + 5], color);
        }
    }

    /// <summary>
    /// One triangle, as horizontal spans. Each row is tested at its CENTRE (y + 0.5), which is what
    /// keeps two triangles sharing an edge from both claiming the row or neither doing.
    /// </summary>
    private void FillTriangle(float x0, float y0, float x1, float y1, float x2, float y2, RGBAColor32 color)
    {
        var top = (int)MathF.Floor(MathF.Min(y0, MathF.Min(y1, y2)));
        var bottom = (int)MathF.Ceiling(MathF.Max(y0, MathF.Max(y1, y2)));

        for (var y = top; y < bottom; y++)
        {
            var cy = y + 0.5f;
            var lo = float.MaxValue;
            var hi = float.MinValue;

            Cross(x0, y0, x1, y1, cy, ref lo, ref hi);
            Cross(x1, y1, x2, y2, cy, ref lo, ref hi);
            Cross(x2, y2, x0, y0, cy, ref lo, ref hi);

            if (hi <= lo) continue;

            var xa = (int)MathF.Round(lo);
            var xb = (int)MathF.Round(hi);
            // A span the rounding collapsed still had ink in it — a one-pixel tip is the whole point of
            // an arrowhead, and dropping it leaves the mark visibly blunt.
            if (xb <= xa) xb = xa + 1;
            FillRectangle(new RectInt(new PointInt(xb, y + 1), new PointInt(xa, y)), color);
        }

        // Where an edge crosses this row's centre line, if it does. Half-open in y (>= start, < end) so
        // a shared vertex is counted once rather than twice.
        static void Cross(float ax, float ay, float bx, float by, float cy, ref float lo, ref float hi)
        {
            if (ay > by)
            {
                (ax, ay, bx, by) = (bx, by, ax, ay);
            }
            if (cy < ay || cy >= by) return;

            var x = ax + (bx - ax) * (cy - ay) / (by - ay);
            if (x < lo) lo = x;
            if (x > hi) hi = x;
        }
    }

    /// <summary>The pushed clips, innermost last. Each entry is ALREADY intersected with the one
    /// below it, so the top is the effective region and a backend never combines anything.</summary>
    private readonly List<RectInt> _clipStack = [];

    /// <summary>How many clips are pushed. Zero means drawing is unrestricted. A widget that pushes
    /// and pops in pairs can assert on this to prove it left the renderer as it found it.</summary>
    public int ClipDepth => _clipStack.Count;

    /// <summary>
    /// Restricts subsequent drawing to <paramref name="rect"/> (pixels) until the matching
    /// <see cref="PopClip"/>. Widgets use this to keep content inside their bounds — a tab strip's
    /// overflowing labels, a sidebar's rows, a thumbnail inside that sidebar.
    /// </summary>
    /// <remarks>
    /// <para><b>Nests, and narrows.</b> A push inside a push draws in the INTERSECTION of the two, so
    /// an inner widget states only its own bounds and cannot escape its parent's. That is the point of
    /// the stack: the alternative — one level, where a second push replaces the first — makes every
    /// nested clip the caller's job to intersect by hand, and makes restoring the outer one a re-push
    /// of a rect the inner widget has no business knowing. Both were live in this family.</para>
    ///
    /// <para>Clipping is an optimization for some backends and correctness for others, so the region
    /// bookkeeping lives HERE and a backend implements only <see cref="ApplyClip"/> and
    /// <see cref="ClearClip"/> — one absolute rect, no history. A backend that ignores both still
    /// reports a correct <see cref="ClipDepth"/>.</para>
    /// </remarks>
    public void PushClip(in RectInt rect)
    {
        var region = rect.Normalized();
        if (_clipStack.Count > 0) region = _clipStack[^1].Intersect(region);
        _clipStack.Add(region);
        ApplyClip(region);
    }

    /// <summary>
    /// Removes the clip set by the matching <see cref="PushClip"/>, restoring the enclosing one — or
    /// the full surface at depth zero.
    /// </summary>
    /// <exception cref="InvalidOperationException">Nothing was pushed. Deliberately loud: a pop
    /// without a push is a caller using this as "reset the clip", which happens to work only while
    /// every backend sets the region absolutely, and silently unclips a nested draw the moment one
    /// does not.</exception>
    public void PopClip()
    {
        if (_clipStack.Count == 0)
            throw new InvalidOperationException(
                "PopClip with no clip pushed. Push and pop in pairs; there is no 'reset' form.");

        _clipStack.RemoveAt(_clipStack.Count - 1);
        if (_clipStack.Count > 0) ApplyClip(_clipStack[^1]);
        else ClearClip();
    }

    /// <summary>
    /// Drops every pushed clip and opens the surface back up. For a backend to call where its own frame
    /// boundary has already discarded the region — a Vulkan command buffer's scissor does not survive
    /// one — so a widget that threw mid-frame cannot leave the next frame clipped to its bounds.
    /// </summary>
    protected void ResetClipStack()
    {
        if (_clipStack.Count == 0) return;
        _clipStack.Clear();
        ClearClip();
    }

    /// <summary>Confines drawing to this absolute, already-normalized and already-intersected rect.
    /// The one thing a clipping backend implements; the default ignores it.</summary>
    protected virtual void ApplyClip(in RectInt rect) { }

    /// <summary>Opens drawing back up to the whole surface. The counterpart to <see cref="ApplyClip"/>.</summary>
    protected virtual void ClearClip() { }

    /// <summary>
    /// Draws an ellipse outline bounded by the given rectangle with the specified stroke width.
    /// Default implementation uses the midpoint ellipse algorithm (integer-only, no trig/sqrt)
    /// with 4-way symmetry, outputting horizontal FillRectangle spans per row.
    /// For thick outlines, traces outer and inner ellipses and fills the ring between them.
    /// GPU renderers should override with a ring-shader implementation.
    /// </summary>
    public virtual void DrawEllipse(in RectInt rect, RGBAColor32 strokeColor, float strokeWidth = 1f)
    {
        var icx = (rect.UpperLeft.X + rect.LowerRight.X) / 2;
        var icy = (rect.UpperLeft.Y + rect.LowerRight.Y) / 2;
        var irx = Math.Abs(rect.LowerRight.X - rect.UpperLeft.X) / 2;
        var iry = Math.Abs(rect.LowerRight.Y - rect.UpperLeft.Y) / 2;
        if (irx < 1 || iry < 1) return;

        var sw = Math.Max(1, (int)strokeWidth);
        if (sw <= 1)
        {
            // Thin outline: midpoint ellipse algorithm with 4-way symmetric span output.
            // Each y-row gets exactly one pixel on each side (left and right arcs).
            MidpointEllipseOutline(icx, icy, irx, iry, strokeColor);
        }
        else
        {
            // Thick outline: trace outer and inner ellipses, fill the ring scanline by scanline.
            // Uses sqrt per row (acceptable for thick outlines which are less frequent).
            var halfSW = sw / 2;
            var outerRx = irx + halfSW;
            var outerRy = iry + halfSW;
            var innerRx = Math.Max(0, irx - halfSW);
            var innerRy = Math.Max(0, iry - halfSW);
            ScanlineEllipseRing(icx, icy, outerRx, outerRy, innerRx, innerRy, strokeColor);
        }
    }

    /// <summary>
    /// Midpoint ellipse algorithm: integer-only, no trig/sqrt. Traces one quadrant,
    /// accumulates horizontal spans per row, and outputs 4-way symmetric FillRectangle
    /// calls. Region 1 (top, stepping right) merges consecutive same-Y points into
    /// spans; Region 2 (side, stepping down) outputs per-row.
    /// </summary>
    private void MidpointEllipseOutline(int cx, int cy, int rx, int ry, RGBAColor32 color)
    {
        // Use long to avoid overflow for large radii (rx² * ry² can exceed int range)
        long rx2 = (long)rx * rx;
        long ry2 = (long)ry * ry;
        long twoRx2 = 2 * rx2;
        long twoRy2 = 2 * ry2;

        var x = 0;
        var y = ry;
        long px = 0;
        long py = twoRx2 * y;

        // Region 1: top of ellipse, stepping right. Accumulate x-span per row.
        var spanX0 = 0; // start of current span
        var spanY = y;   // current row being accumulated
        var d1 = ry2 - rx2 * ry + rx2 / 4.0;
        while (px < py)
        {
            x++;
            px += twoRy2;
            if (d1 < 0)
            {
                d1 += ry2 + px;
            }
            else
            {
                // Y changed — flush accumulated span, then start new row
                EmitEllipseSpan(cx, cy, spanX0, x - 1, spanY, color);
                y--;
                py -= twoRx2;
                d1 += ry2 + px - py;
                spanX0 = x;
                spanY = y;
            }
        }
        // Flush remaining Region 1 span
        EmitEllipseSpan(cx, cy, spanX0, x, spanY, color);

        // Region 2: side of ellipse, stepping down. One x per row.
        var d2 = ry2 * (x + 0.5) * (x + 0.5) + rx2 * (y - 1.0) * (y - 1.0) - rx2 * ry2;
        while (y >= 0)
        {
            y--;
            py -= twoRx2;
            if (d2 > 0)
            {
                d2 += rx2 - py;
            }
            else
            {
                x++;
                px += twoRy2;
                d2 += rx2 - py + px;
            }
            // Region 2: each Y step is a new row, output single-pixel span
            EmitEllipseSpan(cx, cy, x, x, y, color);
        }
    }

    /// <summary>
    /// Emits 4-way symmetric horizontal spans for the ellipse outline.
    /// A span from (cx+x0..cx+x1, cy±y) and (cx-x1..cx-x0, cy±y).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void EmitEllipseSpan(int cx, int cy, int x0, int x1, int y, RGBAColor32 color)
    {
        // Right arc, top and bottom (4 spans via symmetry)
        FillRectangle(new RectInt(new PointInt(cx + x1 + 1, cy + y + 1), new PointInt(cx + x0, cy + y)), color);
        FillRectangle(new RectInt(new PointInt(cx + x1 + 1, cy - y + 1), new PointInt(cx + x0, cy - y)), color);
        // Left arc (mirrored X)
        FillRectangle(new RectInt(new PointInt(cx - x0 + 1, cy + y + 1), new PointInt(cx - x1, cy + y)), color);
        FillRectangle(new RectInt(new PointInt(cx - x0 + 1, cy - y + 1), new PointInt(cx - x1, cy - y)), color);
    }

    /// <summary>
    /// Scanline ring fill for thick ellipse outlines. Uses sqrt per row for outer/inner
    /// ellipse intersections, filling horizontal spans between them.
    /// </summary>
    private void ScanlineEllipseRing(int cx, int cy, int outerRx, int outerRy,
        int innerRx, int innerRy, RGBAColor32 color)
    {
        for (var dy = -outerRy; dy <= outerRy; dy++)
        {
            var y = cy + dy;

            // Outer ellipse X half-width at this row
            var outerTerm = 1.0 - (double)(dy * dy) / (outerRy * outerRy);
            if (outerTerm < 0) continue;
            var outerHalfW = (int)Math.Round(outerRx * Math.Sqrt(outerTerm));

            // Inner ellipse X half-width (the hole)
            var innerTerm = innerRy > 0 ? 1.0 - (double)(dy * dy) / (innerRy * innerRy) : -1.0;
            if (innerTerm <= 0 || innerRx < 1)
            {
                // No inner ellipse at this row — fill full outer span
                FillRectangle(new RectInt(
                    new PointInt(cx + outerHalfW, y + 1),
                    new PointInt(cx - outerHalfW, y)), color);
            }
            else
            {
                var innerHalfW = (int)Math.Round(innerRx * Math.Sqrt(innerTerm));

                // Left arc
                if (outerHalfW > innerHalfW)
                {
                    FillRectangle(new RectInt(
                        new PointInt(cx - innerHalfW, y + 1),
                        new PointInt(cx - outerHalfW, y)), color);
                    // Right arc
                    FillRectangle(new RectInt(
                        new PointInt(cx + outerHalfW, y + 1),
                        new PointInt(cx + innerHalfW, y)), color);
                }
            }
        }
    }

    /// <summary>
    /// Draws a line between two points with the given color and thickness.
    /// Default implementation fast-paths axis-aligned lines as a single FillRectangle,
    /// then falls back to Bresenham for diagonal lines.
    /// GPU renderers should override with a rotated-quad implementation for efficiency.
    /// </summary>
    public virtual void DrawLine(float x0, float y0, float x1, float y1, RGBAColor32 color, int thickness = 1)
    {
        var t = Math.Max(1, thickness);
        var halfT = (t - 1) / 2;
        var ix0 = (int)x0;
        var iy0 = (int)y0;
        var ix1 = (int)x1;
        var iy1 = (int)y1;

        // Fast path: horizontal line — single FillRectangle
        // +1 on xMax because RectInt LowerRight is exclusive
        if (iy0 == iy1)
        {
            var xMin = Math.Min(ix0, ix1);
            var xMax = Math.Max(ix0, ix1);
            FillRectangle(new RectInt(
                new PointInt(xMax + 1, iy0 - halfT + t),
                new PointInt(xMin, iy0 - halfT)), color);
            return;
        }

        // Fast path: vertical line — single FillRectangle
        if (ix0 == ix1)
        {
            var yMin = Math.Min(iy0, iy1);
            var yMax = Math.Max(iy0, iy1);
            FillRectangle(new RectInt(
                new PointInt(ix0 - halfT + t, yMax + 1),
                new PointInt(ix0 - halfT, yMin)), color);
            return;
        }

        // Diagonal: heuristic — Bresenham for short lines (< 200px), scanline quad for longer.
        // Scanline setup (sqrt, 4 corners, edge arrays) has ~300ns overhead that dominates
        // short lines but pays off at ~200px+ where per-pixel Bresenham becomes expensive.
        var fdx = (double)(ix1 - ix0);
        var fdy = (double)(iy1 - iy0);
        var lenSq = fdx * fdx + fdy * fdy;
        if (lenSq < 0.25) return;

        if (lenSq < 200 * 200)
        {
            // Short diagonal: Bresenham per-pixel
            var dx = Math.Abs(ix1 - ix0);
            var dy = Math.Abs(iy1 - iy0);
            var sx = ix0 < ix1 ? 1 : -1;
            var sy = iy0 < iy1 ? 1 : -1;
            var err = dx - dy;

            while (true)
            {
                FillRectangle(new RectInt(
                    new PointInt(ix0 - halfT + t, iy0 - halfT + t),
                    new PointInt(ix0 - halfT, iy0 - halfT)), color);

                if (ix0 == ix1 && iy0 == iy1) break;

                var e2 = 2 * err;
                if (e2 > -dy) { err -= dy; ix0 += sx; }
                if (e2 < dx) { err += dx; iy0 += sy; }
            }
            return;
        }

        // Long diagonal: scanline-filled rotated quad (ImageMagick approach).
        // Compute a thin rectangle from the line endpoints, then fill it row by row
        // with horizontal FillRectangle spans. O(height) calls vs O(length) per-pixel.
        var len = Math.Sqrt(lenSq);

        // Perpendicular half-width
        var hw = t * 0.5;
        var nx = -fdy / len * hw;
        var ny = fdx / len * hw;

        // 4 corners of the rotated quad
        double c0x = ix0 + nx, c0y = iy0 + ny;
        double c1x = ix0 - nx, c1y = iy0 - ny;
        double c2x = ix1 - nx, c2y = iy1 - ny;
        double c3x = ix1 + nx, c3y = iy1 + ny;

        // Scanline Y range
        var scanYMin = (int)Math.Floor(Math.Min(Math.Min(c0y, c1y), Math.Min(c2y, c3y)));
        var scanYMax = (int)Math.Ceiling(Math.Max(Math.Max(c0y, c1y), Math.Max(c2y, c3y)));

        // Edges: c0→c3, c3→c2, c2→c1, c1→c0
        ReadOnlySpan<double> edgeX = [c0x, c3x, c2x, c1x, c0x];
        ReadOnlySpan<double> edgeY = [c0y, c3y, c2y, c1y, c0y];

        for (var y = scanYMin; y <= scanYMax; y++)
        {
            var scanY = y + 0.5; // sample at pixel center
            var xLeft = double.MaxValue;
            var xRight = double.MinValue;

            for (var e = 0; e < 4; e++)
            {
                var ey0 = edgeY[e];
                var ey1 = edgeY[e + 1];
                // Does scanline cross this edge?
                if ((ey0 <= scanY && ey1 > scanY) || (ey1 <= scanY && ey0 > scanY))
                {
                    var et = (scanY - ey0) / (ey1 - ey0);
                    var ex = edgeX[e] + et * (edgeX[e + 1] - edgeX[e]);
                    if (ex < xLeft) xLeft = ex;
                    if (ex > xRight) xRight = ex;
                }
            }

            if (xLeft <= xRight)
            {
                // Round (not floor/ceil) to keep spans tight — prevents 3px-wide
                // spans for thickness=1 diagonals where the quad is only 1px wide
                var spanLeft = (int)Math.Round(xLeft);
                var spanRight = (int)Math.Round(xRight);
                if (spanRight <= spanLeft) spanRight = spanLeft + 1;
                FillRectangle(new RectInt(
                    new PointInt(spanRight, y + 1),
                    new PointInt(spanLeft, y)), color);
            }
        }
    }
    /// <summary>
    /// Draws a dashed line between two points. <paramref name="dashLength"/>
    /// is the on-stroke run, <paramref name="gapLength"/> the off-stroke gap,
    /// both measured along the segment in pixels. Equivalent to SVG
    /// <c>stroke-dasharray="dashLength gapLength"</c> with the pattern reset
    /// at the start of each call (no phase continuity across segments).
    /// Either length being &lt;= 0 degrades to a solid <see cref="DrawLine"/>.
    /// GPU renderers should override with a fragment-shader implementation;
    /// this default emits one <see cref="DrawLine"/> per visible dash.
    /// </summary>
    public virtual void DrawLineDashed(float x0, float y0, float x1, float y1,
        RGBAColor32 color, float dashLength, float gapLength, int thickness = 1)
    {
        if (dashLength <= 0f || gapLength <= 0f)
        {
            DrawLine(x0, y0, x1, y1, color, thickness);
            return;
        }

        var dx = x1 - x0;
        var dy = y1 - y0;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len <= 0f) return;

        var ux = dx / len;
        var uy = dy / len;
        var period = dashLength + gapLength;
        var t = 0f;
        while (t < len)
        {
            var dashEnd = MathF.Min(t + dashLength, len);
            DrawLine(x0 + ux * t, y0 + uy * t, x0 + ux * dashEnd, y0 + uy * dashEnd, color, thickness);
            t += period;
        }
    }

    /// <summary>
    /// Draws a connected sequence of line segments through <paramref name="points"/>.
    /// Equivalent to calling <see cref="DrawLine"/> for each consecutive pair.
    /// No special join handling — for thick strokes corners can show small gaps;
    /// callers needing rounded joins can stamp a small <see cref="FillEllipse"/>
    /// at each interior vertex. GPU renderers should override with a batched
    /// rotated-quad implementation.
    /// </summary>
    public virtual void DrawPolyline(ReadOnlySpan<(float X, float Y)> points,
        RGBAColor32 color, int thickness = 1)
    {
        if (points.Length < 2) return;
        for (var i = 1; i < points.Length; i++)
        {
            DrawLine(points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y, color, thickness);
        }
    }

    /// <summary>
    /// Dashed variant of <see cref="DrawPolyline"/>. Each segment is dashed
    /// independently (no phase continuity across vertices) — sufficient for
    /// charts/axes/grid lines that match ImageMagick's <c>StrokeDashArray</c>
    /// behaviour. See <see cref="DrawLineDashed"/> for the dash/gap semantics.
    /// </summary>
    public virtual void DrawPolylineDashed(ReadOnlySpan<(float X, float Y)> points,
        RGBAColor32 color, float dashLength, float gapLength, int thickness = 1)
    {
        if (points.Length < 2) return;
        for (var i = 1; i < points.Length; i++)
        {
            DrawLineDashed(points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y,
                color, dashLength, gapLength, thickness);
        }
    }

    private ITextShaper _textShaper = AdvanceShaper.Default;

    /// <summary>
    /// The shaper <see cref="DrawText"/>/<see cref="MeasureText"/> run text through before placing
    /// glyphs. Defaults to <see cref="AdvanceShaper.Default"/> (no shaping, no kerning — output is
    /// byte-identical to the pre-seam per-rune path). Assign an <see cref="AdvanceShaper"/> with
    /// kerning on for static display text, or a HarfBuzz-backed <see cref="ITextShaper"/> (A3) for
    /// ligatures/complex scripts. Never null — setting null restores the default.
    /// </summary>
    public ITextShaper TextShaper
    {
        get => _textShaper;
        set => _textShaper = value ?? AdvanceShaper.Default;
    }

    public abstract void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize, RGBAColor32 fontColor, in RectInt layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near);

    /// <summary>
    /// Measures the size of the given text in pixels at the specified font size.
    /// Returns (width, height) where height is the line height.
    /// </summary>
    public abstract (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize);

    /// <summary>
    /// Set by a HOST that renders selectable text natively -- e.g. a web host overlaying real DOM
    /// <c>&lt;span&gt;</c>s over the canvas. When true,
    /// <see cref="PixelWidgetBase{TSurface}.DrawSelectableText"/> registers the selectable region but
    /// skips the glyph raster, so the host's native text is the only copy on screen (no double-draw).
    /// Deliberately a host choice rather than a backend override: the same web renderer serves consumers
    /// with and without a DOM text layer, and an un-migrated host must keep getting rastered text.
    /// Default false: selectable text rasters exactly like <c>DrawText</c> (Vulkan, console, and any
    /// consumer that has not mounted a native text layer).
    /// </summary>
    public bool HostRendersSelectableText { get; set; }

    /// <summary>
    /// The <see cref="ContentTransform"/> (rotation ∈ {0°, 90°, 180°, 270°}, uniform scale, translation)
    /// that a backend folds into its projection so the whole frame — text included — rotates and scales as
    /// one. Defaults to <see cref="DIR.Lib.ContentTransform.Identity"/> (rendering is byte-identical to
    /// before it existed). The base implementation only STORES the value; a backend applies it by overriding
    /// the setter to rebuild its projection. Today only the Vulkan backend does so — the pure-software and
    /// WebGL backends inherit the base auto-property and therefore ignore it (stored, not applied) until
    /// they are wired in a later phase.
    /// <para>
    /// <b>This is the POST-layout application:</b> layout has already resolved design units to surface
    /// units, and the finished result is mapped. Nothing reflows and nothing is re-measured, which is what a
    /// safe-area/letterbox change or a hot-seat flip wants — and it is why the transform is uniform-scale,
    /// since a post-map rotation is only coherent when the surface unit is square. A transform that should
    /// reflow (DPI, zoom) does NOT belong here; it belongs in the measure context, applied to design units
    /// before they are mapped. See <see cref="DIR.Lib.ContentTransform"/> for the ordering rule.
    /// </para>
    /// </summary>
    public virtual ContentTransform ContentTransform { get; set; } = DIR.Lib.ContentTransform.Identity;

    /// <summary>
    /// Fills <paramref name="rect"/> with its corners rounded to <paramref name="cornerRadius"/> pixels.
    /// <para>
    /// The default implementation emits one horizontal <see cref="FillRectangle"/> span per row, inset by
    /// the corner arc on the rows that fall inside a corner band. The spans <b>never overlap</b>, which is
    /// what makes a translucent <paramref name="fillColor"/> come out evenly: the obvious decomposition --
    /// a cross of rectangles plus four corner ellipses -- double-blends where they meet and darkens all
    /// four corners, which is exactly what a panel background would show. So this is correct on every
    /// backend as it stands; a GPU renderer should override it with a single rounded-box SDF quad, which
    /// is both cheaper (one draw, not one per row) and antialiased.
    /// </para>
    /// <para>
    /// The radius is clamped to half the shorter side, so an over-large value degrades to a stadium or a
    /// circle instead of inverting the arc. A radius of zero -- or a degenerate rect -- is a plain
    /// <see cref="FillRectangle"/>, so callers can pass a radius through unconditionally.
    /// </para>
    /// </summary>
    public virtual void FillRoundedRectangle(in RectInt rect, RGBAColor32 fillColor, float cornerRadius)
    {
        var left = Math.Min(rect.UpperLeft.X, rect.LowerRight.X);
        var right = Math.Max(rect.UpperLeft.X, rect.LowerRight.X);
        var top = Math.Min(rect.UpperLeft.Y, rect.LowerRight.Y);
        var bottom = Math.Max(rect.UpperLeft.Y, rect.LowerRight.Y);

        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0) return;

        var radius = MathF.Min(cornerRadius, MathF.Min(width, height) * 0.5f);
        if (radius <= 0f)
        {
            FillRectangle(rect, fillColor);
            return;
        }

        var centreY = (top + bottom) * 0.5f;
        // Rows within this distance of the centre are straight-edged; beyond it the arc takes over.
        var flatHalfHeight = height * 0.5f - radius;

        for (var y = top; y < bottom; y++)
        {
            // Sampled at the pixel centre, matching the scanline convention FillEllipse uses.
            var dy = MathF.Abs(y + 0.5f - centreY) - flatHalfHeight;
            if (dy <= 0f)
            {
                FillRectangle(new RectInt((right, y + 1), (left, y)), fillColor);
                continue;
            }

            var chord = radius * radius - dy * dy;
            if (chord <= 0f)
            {
                continue;
            }

            var inset = (int)MathF.Round(radius - MathF.Sqrt(chord));
            var spanLeft = left + inset;
            var spanRight = right - inset;
            if (spanRight > spanLeft)
            {
                FillRectangle(new RectInt((spanRight, y + 1), (spanLeft, y)), fillColor);
            }
        }
    }

    /// <summary>
    /// Fills multiple rectangles in a single batched draw call.
    /// Default implementation falls back to individual FillRectangle calls.
    /// </summary>
    public virtual void FillRectangles(ReadOnlySpan<(RectInt Rect, RGBAColor32 Color)> rectangles)
    {
        foreach (var (rect, color) in rectangles)
        {
            FillRectangle(rect, color);
        }
    }

    /// <summary>
    /// Fills <paramref name="bounds"/> with a translucent scrim color to dim the content beneath a modal overlay.
    /// Delegates to <see cref="FillRectangle"/>; override when the backend provides a more efficient alpha-blend path.
    /// </summary>
    public virtual void DrawScrim(in RectInt bounds, RGBAColor32 scrimColor)
        => FillRectangle(bounds, scrimColor);

    public abstract void Dispose();
}
