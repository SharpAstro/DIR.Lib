using SharpAstro.Fonts.Outlines;

namespace DIR.Lib;

/// <summary>
/// Outline to <see cref="SlugGlyphBlob"/>: the CPU half of Slug text drawing (Eric Lengyel's algorithm, whose
/// patent is dedicated to the public domain from 2026-03-17). Feed it an outline as an <see cref="IGlyphSink"/>
/// (synthetic shapes in a test, or a font's glyph through <see cref="TryEncode"/>), then call
/// <see cref="TryFinish"/>. Lines become degenerate quadratics; cubics (CFF, Type 1) are split in halves until
/// one quadratic stands in for each within a tolerance in em units; points are snapped to a quarter font unit;
/// and BAND MEMBERSHIP IS COMPUTED FROM THE SNAPPED POINTS, since those are what the shader draws (from the
/// originals a curve that rounding moves across a band edge is missed by the band the shader looks it up in:
/// HarfBuzz #6132). The blob layout is documented on <see cref="SlugGlyphBlob"/>.
/// </summary>
/// <remarks>
/// The scheme follows the structure of HarfBuzz's <c>hb-gpu</c> encoder (<c>hb-gpu-draw.cc</c>, Old MIT,
/// Copyright (C) 2026 Behdad Esfahbod, portions Copyright (C) 2017 Eric Lengyel): quantised points, bands
/// from the quantised bounds, curve lists per band sorted both ways. This is an independent implementation
/// in this repo's own integer layout; no HarfBuzz source is copied.
/// An encoder keeps scratch arrays between glyphs, so encoding a glyph allocates its blob and little else.
/// Not thread-safe: use one per thread.
/// </remarks>
public sealed class SlugGlyphEncoder : IGlyphSink
{
    /// <summary>Blob units per font unit: a quarter-unit grid for points, with room for the exact midpoint of a line.</summary>
    public const int UnitsPerFontUnit = 8;

    /// <summary>The grid glyph points are snapped to, in blob units (a quarter of a font unit).</summary>
    public const int PointStep = 2;

    /// <summary>A quadratic stands in for a cubic within this much of an em: 1/2048, under a pixel at 2000 px/em.</summary>
    public const float DefaultToleranceEm = 1f / 2048f;

    /// <summary>The largest coordinate a blob holds, in blob units; integers stay exact in a float below 2^24.</summary>
    public const int MaxCoordinate = 1 << 22;

    private const int MaxSplitDepth = 12;
    // The most a cubic can stray from the quadratic with control (3(c1 + c2) - (p0 + p3)) / 4 is sqrt(3)/36 of the
    // cubic's third difference |p3 - 3 c2 + 3 c1 - p0|.
    private const float CubicErrorFactor = 0.04811252f;

    private readonly float _toleranceEm;
    private readonly Recorder _recorder = new();

    private int _unitsPerEm = 1000;
    private float _toleranceUnits;
    private bool _failed;

    // The curves so far, six ints each (start, control, end), in blob units, contour after contour.
    private int[] _curves = new int[6 * 64];
    private int _curveCount;
    // Where each contour's curves start, as a curve index; one more entry than contours while a contour is open.
    private int[] _contourStart = new int[8];
    private int _contourCount;
    private bool _open;
    private float _startX, _startY, _penX, _penY;
    private int _penQx, _penQy, _startQx, _startQy;

    // Scratch for TryFinish.
    private int[] _bounds = new int[0]; // minX maxX minY maxY per curve, four ints each
    private int[] _bandLo = new int[0], _bandHi = new int[0]; // the first and last band of a curve, per axis, two arrays per axis
    private long[] _sortKeys = new long[0], _maxKeys = new long[0];
    private int[] _list = new int[0];

    private readonly float[] _cubicStack = new float[8 * (MaxSplitDepth + 2)];
    private readonly int[] _cubicDepth = new int[MaxSplitDepth + 2];

    /// <param name="toleranceEm">How far, in em units, a quadratic may stray from the cubic it replaces.</param>
    public SlugGlyphEncoder(float toleranceEm = DefaultToleranceEm)
    {
        if (!(toleranceEm > 0f)) throw new ArgumentOutOfRangeException(nameof(toleranceEm));
        _toleranceEm = toleranceEm;
        Begin(1000);
    }

    /// <summary>Starts a new glyph in a face of <paramref name="unitsPerEm"/> units; drops whatever was fed so far.</summary>
    public void Begin(int unitsPerEm)
    {
        if (unitsPerEm <= 0) throw new ArgumentOutOfRangeException(nameof(unitsPerEm));
        _unitsPerEm = unitsPerEm;
        _toleranceUnits = _toleranceEm * unitsPerEm;
        _curveCount = 0;
        _contourCount = 0;
        _open = false;
        _failed = false;
    }

    /// <summary>
    /// Encodes one glyph of <paramref name="fontPath"/> from <paramref name="rasterizer"/>'s outline (an SFNT by
    /// glyph id, a Type 1 by glyph name: <see cref="ManagedFontRasterizer.TryDrawGlyphOutline"/>). False when the
    /// face or identity is not drawable there, or a coordinate is too large for the blob; a glyph without an
    /// outline (a space) is true with an <see cref="SlugGlyphBlob.IsEmpty"/> result.
    /// </summary>
    public bool TryEncode(ManagedFontRasterizer rasterizer, string fontPath, GlyphIdentity identity, out SlugGlyphBlob glyph)
    {
        ArgumentNullException.ThrowIfNull(rasterizer);
        glyph = default;
        // The em is only known once the outline walk reports it, and the cubic tolerance needs it, so the walk is
        // recorded and replayed.
        _recorder.Clear();
        if (!rasterizer.TryDrawGlyphOutline(fontPath, identity, _recorder, out int unitsPerEm) || unitsPerEm <= 0) return false;
        Begin(unitsPerEm);
        _recorder.Replay(this);
        return TryFinish(out glyph);
    }

    // ---- IGlyphSink ----------------------------------------------------------

    public void MoveTo(float x, float y)
    {
        CloseContour();
        BeginContour(x, y);
    }

    public void LineTo(float x, float y)
    {
        EnsureOpen();
        int qx = Q(x), qy = Q(y);
        if (_failed) return;
        // A line is a quadratic whose control point is the midpoint of its ends; blob units make that exact.
        AddCurve((_penQx + qx) / 2, (_penQy + qy) / 2, qx, qy);
        _penX = x;
        _penY = y;
    }

    public void QuadTo(float cx, float cy, float x, float y)
    {
        EnsureOpen();
        int qcx = Q(cx), qcy = Q(cy), qx = Q(x), qy = Q(y);
        if (_failed) return;
        AddCurve(qcx, qcy, qx, qy);
        _penX = x;
        _penY = y;
    }

    public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
    {
        EnsureOpen();
        var st = _cubicStack;
        int sp = 0;
        Push(ref sp, _penX, _penY, c1x, c1y, c2x, c2y, x, y, 0);
        // Depth first, left half first, so the quadratics come out in drawing order.
        while (sp > 0 && !_failed)
        {
            sp--;
            int o = sp * 8;
            float p0x = st[o], p0y = st[o + 1], a1x = st[o + 2], a1y = st[o + 3], a2x = st[o + 4], a2y = st[o + 5], p3x = st[o + 6], p3y = st[o + 7];
            int depth = _cubicDepth[sp];

            float dx = p3x - 3f * a2x + 3f * a1x - p0x, dy = p3y - 3f * a2y + 3f * a1y - p0y;
            float err = MathF.Sqrt(dx * dx + dy * dy) * CubicErrorFactor;
            if (err <= _toleranceUnits || depth >= MaxSplitDepth)
            {
                float qcx = (3f * (a1x + a2x) - (p0x + p3x)) * 0.25f;
                float qcy = (3f * (a1y + a2y) - (p0y + p3y)) * 0.25f;
                int cqx = Q(qcx), cqy = Q(qcy), eqx = Q(p3x), eqy = Q(p3y);
                if (_failed) return;
                AddCurve(cqx, cqy, eqx, eqy);
                continue;
            }

            // de Casteljau at t = 1/2; the right half goes on first so the left comes off first.
            float m01x = (p0x + a1x) * 0.5f, m01y = (p0y + a1y) * 0.5f;
            float m12x = (a1x + a2x) * 0.5f, m12y = (a1y + a2y) * 0.5f;
            float m23x = (a2x + p3x) * 0.5f, m23y = (a2y + p3y) * 0.5f;
            float l2x = (m01x + m12x) * 0.5f, l2y = (m01y + m12y) * 0.5f;
            float r1x = (m12x + m23x) * 0.5f, r1y = (m12y + m23y) * 0.5f;
            float midx = (l2x + r1x) * 0.5f, midy = (l2y + r1y) * 0.5f;
            Push(ref sp, midx, midy, r1x, r1y, m23x, m23y, p3x, p3y, depth + 1);
            Push(ref sp, p0x, p0y, m01x, m01y, l2x, l2y, midx, midy, depth + 1);
        }
        _penX = x;
        _penY = y;
    }

    public void Close() => CloseContour();

    private void Push(ref int sp, float a, float b, float c, float d, float e, float f, float g, float h, int depth)
    {
        int o = sp * 8;
        var st = _cubicStack;
        st[o] = a; st[o + 1] = b; st[o + 2] = c; st[o + 3] = d; st[o + 4] = e; st[o + 5] = f; st[o + 6] = g; st[o + 7] = h;
        _cubicDepth[sp] = depth;
        sp++;
    }

    // ---- contours ------------------------------------------------------------

    private void BeginContour(float x, float y)
    {
        int qx = Q(x), qy = Q(y);
        if (_failed) return;
        if (_contourCount == _contourStart.Length) Array.Resize(ref _contourStart, _contourCount * 2);
        _contourStart[_contourCount++] = _curveCount;
        _open = true;
        _startX = _penX = x;
        _startY = _penY = y;
        _penQx = _startQx = qx;
        _penQy = _startQy = qy;
    }

    // A segment after Close continues from the contour's start, as in a font's path.
    private void EnsureOpen()
    {
        if (!_open) BeginContour(_startX, _startY);
    }

    private void CloseContour()
    {
        if (!_open) return;
        if (_penQx != _startQx || _penQy != _startQy)
            AddCurve((_penQx + _startQx) / 2, (_penQy + _startQy) / 2, _startQx, _startQy);
        _open = false;
        // A contour that drew nothing is a lone point: drop it.
        if (_contourStart[_contourCount - 1] == _curveCount) _contourCount--;
    }

    private void AddCurve(int cx, int cy, int x, int y)
    {
        // Nothing to draw when all three points coincide.
        if (cx == _penQx && cy == _penQy && x == _penQx && y == _penQy) return;
        int o = _curveCount * SlugGlyphBlob.CurveWords;
        if (o + SlugGlyphBlob.CurveWords > _curves.Length) Array.Resize(ref _curves, _curves.Length * 2);
        _curves[o] = _penQx; _curves[o + 1] = _penQy;
        _curves[o + 2] = cx; _curves[o + 3] = cy;
        _curves[o + 4] = x; _curves[o + 5] = y;
        _curveCount++;
        _penQx = x;
        _penQy = y;
    }

    // Snaps a font-unit coordinate to the quarter-unit grid, in blob units (always even). Past the blob's range, or
    // NaN, it marks the glyph failed.
    private int Q(float v)
    {
        float s = MathF.Floor(v * 4f + 0.5f);
        if (!(MathF.Abs(s) * PointStep < MaxCoordinate))
        {
            _failed = true;
            return 0;
        }
        return (int)s * PointStep;
    }

    // ---- the blob ------------------------------------------------------------

    /// <summary>The blob of what was fed in; <see cref="InvalidOperationException"/> for an outline too large for it.</summary>
    public SlugGlyphBlob Finish() =>
        TryFinish(out var g) ? g : throw new InvalidOperationException("The glyph's coordinates exceed what a Slug blob can hold.");

    /// <summary>
    /// Builds the blob from what was fed in and leaves the encoder ready for <see cref="Begin"/>. An empty outline
    /// is an <see cref="SlugGlyphBlob.IsEmpty"/> glyph; false means a coordinate was outside what the blob holds.
    /// </summary>
    public bool TryFinish(out SlugGlyphBlob glyph)
    {
        CloseContour();
        glyph = default;
        if (_failed) return false;

        int n = _curveCount;
        const int H = SlugGlyphBlob.HeaderWords, BH = SlugGlyphBlob.BandHeaderWords, CW = SlugGlyphBlob.CurveWords;
        if (n == 0)
        {
            var empty = new int[H];
            empty[0] = SlugGlyphBlob.FormatVersion;
            empty[1] = _unitsPerEm;
            empty[7] = H;
            glyph = new SlugGlyphBlob(empty);
            return true;
        }

        EnsureScratch(n);
        int gMinX = int.MaxValue, gMinY = int.MaxValue, gMaxX = int.MinValue, gMaxY = int.MinValue;
        int hCurves = 0, vCurves = 0;
        for (int i = 0; i < n; i++)
        {
            int o = i * CW;
            int x1 = _curves[o], y1 = _curves[o + 1], x2 = _curves[o + 2], y2 = _curves[o + 3], x3 = _curves[o + 4], y3 = _curves[o + 5];
            int minX = Math.Min(x1, Math.Min(x2, x3)), maxX = Math.Max(x1, Math.Max(x2, x3));
            int minY = Math.Min(y1, Math.Min(y2, y3)), maxY = Math.Max(y1, Math.Max(y2, y3));
            _bounds[i * 4] = minX; _bounds[i * 4 + 1] = maxX; _bounds[i * 4 + 2] = minY; _bounds[i * 4 + 3] = maxY;
            gMinX = Math.Min(gMinX, minX); gMaxX = Math.Max(gMaxX, maxX);
            gMinY = Math.Min(gMinY, minY); gMaxY = Math.Max(gMaxY, maxY);
            if (maxY > minY) hCurves++;
            if (maxX > minX) vCurves++;
        }

        // At most 16 bands an axis, and no more than the curves that cross that axis; one when there is no extent.
        int nh = gMaxY > gMinY ? Math.Clamp(hCurves, 1, SlugGlyphBlob.MaxBandsPerAxis) : 1;
        int nv = gMaxX > gMinX ? Math.Clamp(vCurves, 1, SlugGlyphBlob.MaxBandsPerAxis) : 1;

        // Band membership from the snapped bounds, exactly, in integers.
        int hTotal = 0, vTotal = 0;
        for (int i = 0; i < n; i++)
        {
            int b = i * 4;
            hTotal += BandRange(_bounds[b + 2], _bounds[b + 3], gMinY, gMaxY, nh, out _bandLo[i], out _bandHi[i]);
            vTotal += BandRange(_bounds[b], _bounds[b + 1], gMinX, gMaxX, nv, out _bandLo[n + i], out _bandHi[n + i]);
        }

        long words = (long)H + (long)(nh + nv) * BH + 2L * (hTotal + vTotal) + (long)n * CW;
        if (words > int.MaxValue / 4) { _failed = true; return false; }
        int bandOffset = H, indexOffset = bandOffset + (nh + nv) * BH, curveOffset = indexOffset + 2 * (hTotal + vTotal);

        var blob = new int[(int)words];
        blob[0] = SlugGlyphBlob.FormatVersion;
        blob[1] = _unitsPerEm;
        blob[2] = n;
        blob[3] = nh;
        blob[4] = nv;
        blob[5] = bandOffset;
        blob[6] = indexOffset;
        blob[7] = curveOffset;
        blob[8] = gMinX; blob[9] = gMinY; blob[10] = gMaxX; blob[11] = gMaxY;
        Array.Copy(_curves, 0, blob, curveOffset, n * CW);

        int next = indexOffset;
        for (int b = 0; b < nh; b++)
            WriteBand(blob, bandOffset + b * BH, ref next, b, true, curveOffset, gMinX, gMaxX, n);
        for (int b = 0; b < nv; b++)
            WriteBand(blob, bandOffset + (nh + b) * BH, ref next, b, false, curveOffset, gMinY, gMaxY, n);

        glyph = new SlugGlyphBlob(blob);
        return true;
    }

    // The first and last band whose closed strip [min + b*span/bands, min + (b+1)*span/bands] the interval [lo, hi]
    // touches; the count of bands (0 for a curve with no extent across the axis, which no ray along it crosses).
    private static int BandRange(int lo, int hi, int min, int max, int bands, out int first, out int last)
    {
        first = 0;
        last = -1;
        if (hi <= lo || max <= min) return 0;
        long span = max - min;
        // Touches band b when hi*bands >= min*bands + span*b and lo*bands <= min*bands + span*(b+1).
        long loScaled = (long)(lo - min) * bands, hiScaled = (long)(hi - min) * bands;
        first = (int)Math.Max(0, (loScaled + span - 1) / span - 1);
        last = (int)Math.Min(bands - 1, hiScaled / span);
        return last - first + 1;
    }

    // One band: its header and its two sorted lists. For a horizontal band the ray runs along x, so the lists are sorted
    // by x bounds and band membership was ranged over y; a vertical band swaps them. crossMin and crossMax are the glyph's
    // extent along the ray.
    private void WriteBand(int[] blob, int header, ref int next, int band, bool horizontal, int curveOffset,
        int crossMin, int crossMax, int n)
    {
        int rangeBase = horizontal ? 0 : n;
        // _bounds holds minX maxX minY maxY: the ray's own axis is x for a horizontal band, y for a vertical one.
        int alongMin = horizontal ? 0 : 2, alongMax = alongMin + 1;
        int m = 0;
        for (int i = 0; i < n; i++)
        {
            if (_bandLo[rangeBase + i] <= band && band <= _bandHi[rangeBase + i])
            {
                _list[m++] = i;
            }
        }

        int descStart = next, ascStart = next + m;
        blob[header] = m;
        blob[header + 1] = descStart;
        blob[header + 2] = ascStart;
        if (m == 0)
        {
            blob[header + 3] = (int)(((long)crossMin + crossMax) / 2);
            return;
        }

        // Ascending by the smallest along-ray coordinate, ties by curve order.
        for (int k = 0; k < m; k++)
            _sortKeys[k] = ((long)_bounds[_list[k] * 4 + alongMin] << 32) | (uint)_list[k];
        Array.Sort(_sortKeys, 0, m);
        for (int k = 0; k < m; k++)
            blob[ascStart + k] = curveOffset + (int)(_sortKeys[k] & 0xFFFFFFFF) * SlugGlyphBlob.CurveWords;

        // The split, from the ascending keys now in _sortKeys: a sample before s walks at most the curves with
        // min <= s, one at or past it at most those with max >= s. Take the s that leaves the longer walk shortest.
        for (int k = 0; k < m; k++) _list[k] = (int)(_sortKeys[k] & 0xFFFFFFFF);
        int bestCost = m, bestSplit = (int)(((long)crossMin + crossMax) / 2);
        // The maxes ascending, for counting the curves with max >= s.
        var maxes = _maxKeys;
        for (int k = 0; k < m; k++) maxes[k] = ((long)_bounds[_list[k] * 4 + alongMax] << 32) | (uint)_list[k];
        Array.Sort(maxes, 0, m);
        int below = 0; // maxes < s
        for (int k = 0; k < m; k++)
        {
            int s = (int)(_sortKeys[k] >> 32);
            if (k + 1 < m && (int)(_sortKeys[k + 1] >> 32) == s) continue; // the last of a run of equal mins
            while (below < m && (int)(maxes[below] >> 32) < s) below++;
            int cost = Math.Max(k + 1, m - below);
            if (cost < bestCost) { bestCost = cost; bestSplit = s; }
        }
        blob[header + 3] = bestSplit;

        // Descending by the largest along-ray coordinate, ties by curve order.
        for (int k = 0; k < m; k++)
            _sortKeys[k] = ((long)(-_bounds[_list[k] * 4 + alongMax]) << 32) | (uint)_list[k];
        Array.Sort(_sortKeys, 0, m);
        for (int k = 0; k < m; k++)
            blob[descStart + k] = curveOffset + (int)(_sortKeys[k] & 0xFFFFFFFF) * SlugGlyphBlob.CurveWords;

        next += 2 * m;
    }

    private void EnsureScratch(int n)
    {
        if (_bandLo.Length >= 2 * n) return;
        int size = Math.Max(n, _bandLo.Length);
        _bounds = new int[size * 4];
        _bandLo = new int[size * 2];
        _bandHi = new int[size * 2];
        _sortKeys = new long[size];
        _maxKeys = new long[size];
        _list = new int[size];
    }

    // Replays an outline once the em is known.
    private sealed class Recorder : IGlyphSink
    {
        private enum Op : byte { Move, Line, Quad, Cubic, Close }

        private Op[] _ops = new Op[64];
        private float[] _args = new float[256];
        private int _opCount, _argCount;

        public void Clear() => _opCount = _argCount = 0;

        public void MoveTo(float x, float y) => Add(Op.Move, x, y);
        public void LineTo(float x, float y) => Add(Op.Line, x, y);
        public void QuadTo(float cx, float cy, float x, float y) => Add(Op.Quad, cx, cy, x, y);
        public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y) => Add(Op.Cubic, c1x, c1y, c2x, c2y, x, y);
        public void Close() => Add(Op.Close);

        private void Add(Op op, params ReadOnlySpan<float> args)
        {
            if (_opCount == _ops.Length) Array.Resize(ref _ops, _opCount * 2);
            if (_argCount + args.Length > _args.Length) Array.Resize(ref _args, Math.Max(_args.Length * 2, _argCount + args.Length));
            _ops[_opCount++] = op;
            args.CopyTo(_args.AsSpan(_argCount));
            _argCount += args.Length;
        }

        public void Replay(IGlyphSink sink)
        {
            int a = 0;
            for (int i = 0; i < _opCount; i++)
            {
                switch (_ops[i])
                {
                    case Op.Move: sink.MoveTo(_args[a], _args[a + 1]); a += 2; break;
                    case Op.Line: sink.LineTo(_args[a], _args[a + 1]); a += 2; break;
                    case Op.Quad: sink.QuadTo(_args[a], _args[a + 1], _args[a + 2], _args[a + 3]); a += 4; break;
                    case Op.Cubic: sink.CubicTo(_args[a], _args[a + 1], _args[a + 2], _args[a + 3], _args[a + 4], _args[a + 5]); a += 6; break;
                    default: sink.Close(); break;
                }
            }
        }
    }
}
