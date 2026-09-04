namespace DIR.Lib;

/// <summary>
/// Gathers anti-aliased coverage for contours into a <see cref="CoverageMask"/> by scanline sweep.
///
/// <para>Slices the plane into horizontal bands and pairs edge crossings by fill rule, nonzero
/// winding or even-odd, so a hole is a hole whether it comes from a separate contour or from one
/// contour looping back through itself. Unlike a triangulator it emits the spans directly, because
/// a software target wants runs of pixels rather than triangles.</para>
///
/// <para><b>Anti-aliasing is not decoration when the output is small.</b> Testing pixel centres
/// turns a downscaled drawing into noise: every hairline either lands on a centre and draws at full
/// strength or misses and vanishes, so a regular hatch becomes a moire. Coverage is accumulated over
/// <see cref="SubSamples"/> horizontal sub-scanlines per pixel row, each contributing exact
/// fractional x coverage at its span ends, which is where the quality actually comes from: the
/// vertical direction is sampled, the horizontal direction is analytic.</para>
///
/// <para>Cost is O(edges log edges) to sort plus O(active edges) per sub-scanline. The active edge
/// list is carried across rows rather than rebuilt, so a page of many small shapes does not pay a
/// full scan per row.</para>
/// </summary>
public sealed class PolygonRasterizer
{
    /// <summary>
    /// Vertical sub-scanlines per pixel row. Four is the knee: it resolves the near-horizontal
    /// hairlines a drawing is full of, and going to eight costs another full sweep for a difference
    /// that does not survive being looked at in a small image.
    /// </summary>
    private const int SubSamples = 4;

    private readonly record struct Edge(float YMin, float YMax, float XAtYMin, float DxDy, int Dir);

    private readonly List<Edge> _edges = new(256);
    private readonly List<int> _active = new(64);
    private readonly List<(float X, int Dir)> _crossings = new(64);
    private float[] _coverage = [];

    /// <summary>
    /// Gathers coverage for <paramref name="points"/> (flat x,y pairs, already in device pixels)
    /// into <paramref name="target"/>. Each contour is implicitly closed. The caller composites,
    /// which is what lets a stroke union many overlapping quads before painting once.
    /// <paramref name="contourStarts"/> holds each contour's first vertex in POINT-PAIR units;
    /// empty means a single contour spanning every point.
    /// </summary>
    public void FillInto(CoverageMask target, ReadOnlySpan<float> points,
        ReadOnlySpan<int> contourStarts, bool evenOdd)
    {
        ArgumentNullException.ThrowIfNull(target);

        var n = points.Length / 2;
        if (n < 3) return;

        BuildEdges(points, contourStarts, n);
        if (_edges.Count == 0) return;

        // Sorting by YMin is what makes the active list a moving window rather than a rescan.
        _edges.Sort(static (p, q) => p.YMin.CompareTo(q.YMin));

        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var e in _edges)
        {
            if (e.YMin < minY) minY = e.YMin;
            if (e.YMax > maxY) maxY = e.YMax;
        }

        var yStart = Math.Max(0, (int)MathF.Floor(minY));
        var yEnd = Math.Min(target.Height - 1, (int)MathF.Ceiling(maxY));
        if (yStart > yEnd) return;

        if (_coverage.Length < target.Width) _coverage = new float[target.Width];
        var coverage = _coverage.AsSpan(0, target.Width);

        _active.Clear();
        var next = 0;
        const float weight = 1f / SubSamples;

        for (var py = yStart; py <= yEnd; py++)
        {
            coverage.Clear();

            var rowBottom = py + 1f;
            while (next < _edges.Count && _edges[next].YMin < rowBottom) _active.Add(next++);

            // Compacted in place rather than with RemoveAll, whose predicate would capture `py` and
            // allocate a closure on every row of every fill.
            var keep = 0;
            for (var i = 0; i < _active.Count; i++)
            {
                if (_edges[_active[i]].YMax > py) _active[keep++] = _active[i];
            }
            _active.RemoveRange(keep, _active.Count - keep);

            if (_active.Count == 0)
            {
                // Nothing overlaps this row, but later rows may: keep sweeping rather than stopping.
                continue;
            }

            for (var s = 0; s < SubSamples; s++)
            {
                var sy = py + (s + 0.5f) / SubSamples;
                _crossings.Clear();

                foreach (var i in _active)
                {
                    var e = _edges[i];
                    if (sy < e.YMin || sy >= e.YMax) continue;
                    _crossings.Add((e.XAtYMin + (sy - e.YMin) * e.DxDy, e.Dir));
                }

                if (_crossings.Count < 2) continue;
                _crossings.Sort(static (p, q) => p.X.CompareTo(q.X));

                var winding = 0;
                for (var c = 0; c < _crossings.Count - 1; c++)
                {
                    winding += _crossings[c].Dir;
                    var inside = evenOdd
                        ? ((c + 1) & 1) == 1          // parity of crossings passed
                        : winding != 0;
                    if (!inside) continue;

                    AddSpan(coverage, _crossings[c].X, _crossings[c + 1].X, weight);
                }
            }

            target.MaxRow(py, coverage);
        }
    }

    private void BuildEdges(ReadOnlySpan<float> points, ReadOnlySpan<int> contourStarts, int n)
    {
        _edges.Clear();

        if (contourStarts.Length > 1)
        {
            for (var c = 0; c < contourStarts.Length; c++)
            {
                var from = contourStarts[c];
                var to = c + 1 < contourStarts.Length ? contourStarts[c + 1] : n;
                AddContour(points, from, to);
            }
        }
        else
        {
            AddContour(points, 0, n);
        }
    }

    private void AddContour(ReadOnlySpan<float> points, int from, int to)
    {
        var count = to - from;
        if (count < 3) return;

        for (var i = 0; i < count; i++)
        {
            var a = from + i;
            var b = from + (i + 1) % count;   // implicitly closed

            var x0 = points[a * 2];
            var y0 = points[a * 2 + 1];
            var x1 = points[b * 2];
            var y1 = points[b * 2 + 1];

            // A horizontal edge crosses no scanline, and including it would double-count the vertex
            // it shares with its neighbours.
            if (y0 == y1) continue;

            if (y0 < y1) _edges.Add(new Edge(y0, y1, x0, (x1 - x0) / (y1 - y0), +1));
            else _edges.Add(new Edge(y1, y0, x1, (x0 - x1) / (y0 - y1), -1));
        }
    }

    /// <summary>
    /// Adds <paramref name="weight"/> coverage across [xa, xb), exact at both ends. The fractional
    /// endpoints are where horizontal anti-aliasing comes from, so this is deliberately not a
    /// rounded span.
    /// </summary>
    private static void AddSpan(Span<float> coverage, float xa, float xb, float weight)
    {
        if (xb <= xa) return;

        var width = coverage.Length;
        if (xa < 0f) xa = 0f;
        if (xb > width) xb = width;
        if (xb <= xa) return;

        var ia = (int)MathF.Floor(xa);
        var ib = (int)MathF.Floor(xb);

        if (ia == ib)
        {
            if (ia < width) coverage[ia] += (xb - xa) * weight;
            return;
        }

        coverage[ia] += (ia + 1 - xa) * weight;
        for (var i = ia + 1; i < ib && i < width; i++) coverage[i] += weight;
        if (ib < width) coverage[ib] += (xb - ib) * weight;
    }
}
