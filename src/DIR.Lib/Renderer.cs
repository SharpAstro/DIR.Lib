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
    public abstract void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize, RGBAColor32 fontColor, in RectInt layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near);

    /// <summary>
    /// Measures the size of the given text in pixels at the specified font size.
    /// Returns (width, height) where height is the line height.
    /// </summary>
    public abstract (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize);

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

    public abstract void Dispose();
}
