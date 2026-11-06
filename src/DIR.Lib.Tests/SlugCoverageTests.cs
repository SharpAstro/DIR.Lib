using System.Diagnostics;
using System.Text;
using SharpAstro.Fonts.Outlines;
using Shouldly;
using Xunit;
using T1 = SharpAstro.Fonts.Type1;

namespace DIR.Lib.Tests;

/// <summary>
/// The Slug coverage function (<see cref="SlugCoverageOracle"/>, reading only the blob <see cref="SlugGlyphEncoder"/>
/// wrote) against references that share no code with the encoder or the shader port: the analytic area of a disc, and a
/// supersampled nonzero-winding rasterisation of the ORIGINAL outline (not the quantised, quadratic-split one in the blob)
/// for the glyphs. Each shape is held to three things:
/// <list type="bullet">
/// <item>every pixel is within <see cref="MaxError"/> of the reference and the mean error within <see cref="MeanError"/>
/// (Slug's coverage is an approximation of the pixel's area, not the area, and the blob is the outline snapped to a
/// quarter font unit with cubics replaced by quadratics, so an edge pixel differs by a few percent);</item>
/// <item>a pixel whose whole 3 x 3 neighbourhood is inside or outside in the reference is exactly 1 or 0;</item>
/// <item>the band acceleration changes nothing: the split's list, either list forced, and no bands at all agree.</item>
/// </list>
/// </summary>
public sealed class SlugCoverageTests(ITestOutputHelper output)
{
    // Stated tolerances, in coverage (0..1). Measured with a margin: see the numbers each test writes.
    private const float MaxError = 0.10f;
    private const float MeanError = 0.012f;
    // The band lists, and the ascending list's mirrored ray, only reorder float sums.
    private const float ModeAgreement = 1e-4f;

    private const string Cmr10 = "mem:cmr10-slug-oracle";
    private static readonly string DejaVu = Path.Combine("Fonts", "DejaVuSans.ttf");

    // ---- the four shapes -------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADiscMatchesItsAnalyticArea(bool counterClockwise)
    {
        // A circle of radius 400 in a 1000 unit em as 32 quadratic arcs (the control at the tangents' crossing), so
        // curves, not lines, are what the blob holds. Winding direction must not matter to nonzero winding.
        const int upem = 1000;
        const float cx = 500, cy = 500, r = 400;
        var blob = Encode(s => Disc(s, cx, cy, r, 32, counterClockwise), upem);
        blob.CurveCount.ShouldBe(32);

        const float ppe = 100f; // a pixel is 10 font units
        var (ox, oy, w, h) = Frame(blob, ppe, 0.37f, 0.61f);
        var image = SlugCoverageOracle.Render(blob, ppe, ox, oy, w, h);

        var reference = new float[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                // The pixel's box in font units.
                double x0 = (ox + i / ppe) * upem, x1 = (ox + (i + 1) / ppe) * upem;
                double y1 = (oy - j / ppe) * upem, y0 = (oy - (j + 1) / ppe) * upem;
                reference[j * w + i] = (float)(DiscBoxArea(cx, cy, r, x0, x1, y0, y1) / ((x1 - x0) * (y1 - y0)));
            }

        Check("disc", blob, image, reference, w, h, ppe, ox, oy);

        // A sample in the middle is 1 whichever way the circle runs.
        SlugCoverageOracle.Coverage(blob, 0.5f, 0.5f, ppe).ShouldBe(1f);
    }

    [Fact]
    public void ACounterIsZeroAndTheBowlOfAnOIsOne_TrueType()
    {
        using var r = new ManagedFontRasterizer();
        var id = r.ResolveGlyphIdentity(DejaVu, new Rune('o'), 'o', GlyphMapHint.Auto);
        var enc = new SlugGlyphEncoder();
        enc.TryEncode(r, DejaVu, id, out var blob).ShouldBeTrue();
        blob.UnitsPerEm.ShouldBe(2048);

        var edges = new Flattener();
        r.TryDrawGlyphOutline(DejaVu, id, edges, out int upem).ShouldBeTrue();
        CheckOWithACounter("DejaVuSans o", blob, edges, upem);
    }

    [Fact]
    public void ACounterIsZeroAndTheBowlOfAnOIsOne_Type1WithCubics()
    {
        // cmr10.pfb is Type 1: every segment is a cubic, so the blob holds the encoder's quadratic splits of them.
        using var r = new ManagedFontRasterizer();
        r.RegisterFontFromMemory(Cmr10, File.ReadAllBytes(Path.Combine("Fonts", "cmr10.pfb"))).ShouldBeTrue();
        var id = new GlyphIdentity(0, "o");
        var enc = new SlugGlyphEncoder();
        enc.TryEncode(r, Cmr10, id, out var blob).ShouldBeTrue();
        blob.UnitsPerEm.ShouldBe(1000);

        var edges = new Flattener();
        r.TryDrawGlyphOutline(Cmr10, id, edges, out int upem).ShouldBeTrue();
        edges.CubicCount.ShouldBeGreaterThan(0, "the glyph must draw cubics for this to test the split");
        edges.QuadCount.ShouldBe(0);
        blob.CurveCount.ShouldBeGreaterThan(edges.CubicCount, "each cubic became at least one quadratic");
        CheckOWithACounter("cmr10 o", blob, edges, upem);
    }

    [Fact]
    public void ASelfOverlappingContourHasWindingTwoAndCoverageOne()
    {
        // No fixture font has a contour that overlaps itself (a variable or hand-drawn TrueType glyph would), so this
        // one is fed through the sink: a pentagram {5/2} as ONE contour. Its middle pentagon is wound twice (the points
        // once); nonzero winding fills it, so 2 must come out as 1, not 0 (even-odd) and not 2.
        const int upem = 1000;
        const float cx = 500, cy = 500, rad = 450;
        var pts = Enumerable.Range(0, 5).Select(k =>
        {
            double a = Math.PI / 2 + k * 2 * 2 * Math.PI / 5; // every second vertex of a pentagon
            return ((float)(cx + rad * Math.Cos(a)), (float)(cy + rad * Math.Sin(a)));
        }).ToArray();
        var blob = Encode(s => Polygon(s, pts), upem);
        blob.CurveCount.ShouldBe(5);

        const float ppe = 100f;
        var (ox, oy, w, h) = Frame(blob, ppe, 0.21f, 0.43f);
        var image = SlugCoverageOracle.Render(blob, ppe, ox, oy, w, h);
        var edges = new Flattener();
        Polygon(edges, pts);
        var reference = Supersample(edges, upem, ppe, ox, oy, w, h);

        Check("pentagram", blob, image, reference, w, h, ppe, ox, oy);

        // The middle of the star is wound twice.
        SlugCoverageOracle.Coverage(blob, cx / upem, cy / upem, ppe).ShouldBe(1f);
        // The winding there really is 2: the reference's winding at the centre, from the independent flattener.
        edges.Winding(cx, cy).ShouldBe(2);
        // A point of the star, wound once, is 1 too; the pocket between two points is outside: 0.
        var tip = pts[0];
        SlugCoverageOracle.Coverage(blob, tip.Item1 / upem, (tip.Item2 - 40) / upem, ppe).ShouldBe(1f);
        SlugCoverageOracle.Coverage(blob, (cx - 300) / upem, (cy + 290) / upem, ppe).ShouldBe(0f);
        edges.Winding(cx - 300, cy + 290).ShouldBe(0);
    }

    [Fact]
    public void OverlappingSquaresOfOneDirectionAreOneWhereTheyOverlap()
    {
        // Two contours, both counter-clockwise, overlapping in a square: winding 2 there, and 0 where a counter-
        // clockwise square meets a clockwise one (winding 0: a counter) is covered by the 'o' tests.
        const int upem = 1000;
        var blob = Encode(s =>
        {
            Polygon(s, (100, 100), (600, 100), (600, 600), (100, 600));
            Polygon(s, (400, 400), (900, 400), (900, 900), (400, 900));
        }, upem);
        const float ppe = 80f;
        var (ox, oy, w, h) = Frame(blob, ppe, 0.17f, 0.29f);
        var image = SlugCoverageOracle.Render(blob, ppe, ox, oy, w, h);
        var edges = new Flattener();
        Polygon(edges, (100, 100), (600, 100), (600, 600), (100, 600));
        Polygon(edges, (400, 400), (900, 400), (900, 900), (400, 900));
        Check("two squares", blob, image, Supersample(edges, upem, ppe, ox, oy, w, h), w, h, ppe, ox, oy);
        SlugCoverageOracle.Coverage(blob, 0.5f, 0.5f, ppe).ShouldBe(1f);
        edges.Winding(500, 500).ShouldBe(2);
    }

    // ---- the report ------------------------------------------------------------

    [Fact]
    public void ReportEncodeTimeAndBlobSizeOnTheTestFonts()
    {
        // Nothing here is timing-dependent: it asserts only that glyphs encode and their blobs are readable.
        // The numbers go to the test output (xunit v3 swallows Console).
        var fonts = new[] { "DejaVuSans.ttf", "Merida.ttf", "ISOCPEUR_subset.ttf", "Tahoma_subset.ttf", "XXTIIT_Arial_subset.ttf",
            "STIX2Math.otf", "Noto-COLRv1.ttf", "cmr10.pfb" };
        output.WriteLine("font, glyphs encoded, empty, curves (total), curves/glyph (mean, max), blob words/glyph (mean, max), bytes/glyph (mean), encode us/glyph (mean), us/curve");
        using var r = new ManagedFontRasterizer();
        var enc = new SlugGlyphEncoder();
        foreach (var name in fonts)
        {
            string path = Path.Combine("Fonts", name);
            if (!File.Exists(path)) { output.WriteLine($"{name}: not found"); continue; }
            var ids = new List<GlyphIdentity>();
            string fontPath = path;
            if (name.EndsWith(".pfb", StringComparison.Ordinal))
            {
                fontPath = "mem:report-" + name;
                r.RegisterFontFromMemory(fontPath, File.ReadAllBytes(path)).ShouldBeTrue();
                var t1 = T1.Type1Font.LoadType1(File.ReadAllBytes(path));
                foreach (var g in t1.GlyphNames) ids.Add(new GlyphIdentity(0, g));
            }
            else
            {
                r.TryGetOpenTypeFont(path, out var font).ShouldBeTrue();
                int n = Math.Min(font!.NumGlyphs, 4096);
                for (int g = 0; g < n; g++) ids.Add(new GlyphIdentity(g, null));
            }

            // One pass to warm up the JIT and the font's tables, then the timed one.
            foreach (var id in ids.Take(64)) enc.TryEncode(r, fontPath, id, out _);

            int encoded = 0, empty = 0, curves = 0, maxCurves = 0;
            long words = 0, maxWords = 0;
            var sw = Stopwatch.StartNew();
            foreach (var id in ids)
            {
                if (!enc.TryEncode(r, fontPath, id, out var blob)) continue;
                SlugGlyphBlob.IsReadable(blob.Words).ShouldBeTrue();
                encoded++;
                if (blob.IsEmpty) { empty++; continue; }
                curves += blob.CurveCount;
                maxCurves = Math.Max(maxCurves, blob.CurveCount);
                words += blob.Words.Length;
                maxWords = Math.Max(maxWords, blob.Words.Length);
            }
            sw.Stop();
            int drawn = Math.Max(encoded - empty, 1);
            double us = sw.Elapsed.TotalMilliseconds * 1000.0;
            output.WriteLine($"{name}, {encoded}, {empty}, {curves}, {curves / (double)drawn:F1} / {maxCurves}, " +
                $"{words / (double)drawn:F0} / {maxWords}, {words * 4.0 / drawn:F0}, {us / Math.Max(encoded, 1):F1}, {us / Math.Max(curves, 1):F2}");
            encoded.ShouldBeGreaterThan(0, name);
        }
    }

    // ---- shared checks ---------------------------------------------------------

    private void CheckOWithACounter(string name, SlugGlyphBlob blob, Flattener edges, int upem)
    {
        const float ppe = 64f;
        var (ox, oy, w, h) = Frame(blob, ppe, 0.31f, 0.47f);
        var image = SlugCoverageOracle.Render(blob, ppe, ox, oy, w, h);
        var reference = Supersample(edges, upem, ppe, ox, oy, w, h);
        Check(name, blob, image, reference, w, h, ppe, ox, oy);

        // The middle of an o is inside its counter: the bowl's two contours run opposite ways, winding 0.
        float mx = (blob.MinXEm + blob.MaxXEm) / 2, my = (blob.MinYEm + blob.MaxYEm) / 2;
        edges.Winding(mx * upem, my * upem).ShouldBe(0, $"{name}: the centre must be in the counter");
        SlugCoverageOracle.Coverage(blob, mx, my, ppe).ShouldBe(0f, $"{name}: a counter pixel");
        // The left side of the bowl, mid-height, is ink: the first run of full pixels from the left edge along that row.
        int row = (int)MathF.Round((oy - my) * ppe - 0.5f);
        int firstInk = -1;
        for (int i = 0; i < w && firstInk < 0; i++) if (reference[row * w + i] == 1f) firstInk = i;
        firstInk.ShouldBeGreaterThanOrEqualTo(0, $"{name}: a stroke at {ppe} px/em is wide enough to have a full pixel");
        image[row * w + firstInk].ShouldBe(1f, $"{name}: a stroke pixel");
    }

    // The three holds of the class summary. The frame is the glyph's bounds with a margin of two pixels all round.
    private void Check(string name, SlugGlyphBlob blob, float[] image, float[] reference, int w, int h, float ppe, float ox, float oy)
    {
        double sum = 0, worst = 0;
        int worstAt = 0;
        for (int k = 0; k < image.Length; k++)
        {
            double e = Math.Abs(image[k] - reference[k]);
            sum += e;
            if (e > worst) { worst = e; worstAt = k; }
            image[k].ShouldBeInRange(0f, 1f);
        }
        double mean = sum / image.Length;
        output.WriteLine($"{name}: {w}x{h} px at {ppe} px/em, {blob.CurveCount} curves, {blob.Words.Length} words, {blob.HBandCount}x{blob.VBandCount} bands; " +
            $"error vs reference: max {worst:F4} (pixel {worstAt % w},{worstAt / w}), mean {mean:F5}");
        worst.ShouldBeLessThanOrEqualTo(MaxError, $"{name}: worst pixel ({worstAt % w}, {worstAt / w}) oracle {image[worstAt]} reference {reference[worstAt]}");
        mean.ShouldBeLessThanOrEqualTo(MeanError, $"{name}: mean error");

        // Whole 3 x 3 neighbourhood inside or outside in the reference: no edge is within half a pixel of the pixel's centre
        // in either direction, so the shader's crossings are all saturated and the answer is exact.
        int inside = 0, outside = 0, partial = 0;
        for (int j = 1; j < h - 1; j++)
            for (int i = 1; i < w - 1; i++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        float v = reference[(j + dj) * w + i + di];
                        lo = Math.Min(lo, v);
                        hi = Math.Max(hi, v);
                    }
                // A disc's analytic reference is never quite 1 or 0 within rounding: allow a sliver.
                if (lo >= 0.99999f) { inside++; image[j * w + i].ShouldBe(1f, $"{name}: interior pixel ({i}, {j})"); }
                else if (hi <= 0.00001f) { outside++; image[j * w + i].ShouldBe(0f, $"{name}: exterior pixel ({i}, {j})"); }
                else if (image[j * w + i] is > 0f and < 1f) partial++;
            }
        output.WriteLine($"{name}: {inside} interior and {outside} exterior pixels exactly 1 and 0, {partial} partial");
        inside.ShouldBeGreaterThan(0, $"{name}: nothing was checked as interior");
        outside.ShouldBeGreaterThan(0, $"{name}: nothing was checked as exterior");

        // The acceleration changes nothing.
        float spread = 0;
        foreach (var choice in new[] { SlugCoverageOracle.ListChoice.Descending, SlugCoverageOracle.ListChoice.Ascending, SlugCoverageOracle.ListChoice.AllCurves })
        {
            var other = SlugCoverageOracle.Render(blob, ppe, ox, oy, w, h, choice);
            for (int k = 0; k < image.Length; k++) spread = Math.Max(spread, Math.Abs(other[k] - image[k]));
        }
        output.WriteLine($"{name}: largest difference between the lists and the unbanded walk {spread:E2}");
        spread.ShouldBeLessThanOrEqualTo(ModeAgreement, $"{name}: split, descending, ascending and unbanded must agree");
    }

    // ---- building ---------------------------------------------------------------

    private static SlugGlyphBlob Encode(Action<IGlyphSink> draw, int upem)
    {
        var enc = new SlugGlyphEncoder();
        enc.Begin(upem);
        draw(enc);
        enc.TryFinish(out var g).ShouldBeTrue();
        return g;
    }

    private static void Polygon(IGlyphSink s, params (float X, float Y)[] pts)
    {
        s.MoveTo(pts[0].X, pts[0].Y);
        for (int i = 1; i < pts.Length; i++) s.LineTo(pts[i].X, pts[i].Y);
        s.Close();
    }

    // A circle of n quadratic arcs: each arc's end points are on the circle and its control at the crossing of the tangents.
    private static void Disc(IGlyphSink s, float cx, float cy, float r, int n, bool counterClockwise)
    {
        double step = 2 * Math.PI / n * (counterClockwise ? 1 : -1);
        double ctrl = r / Math.Cos(step / 2);
        s.MoveTo(cx + r, cy);
        for (int k = 0; k < n; k++)
        {
            double a0 = k * step, mid = a0 + step / 2, a1 = (k + 1) * step;
            s.QuadTo((float)(cx + ctrl * Math.Cos(mid)), (float)(cy + ctrl * Math.Sin(mid)),
                (float)(cx + r * Math.Cos(a1)), (float)(cy + r * Math.Sin(a1)));
        }
        s.Close();
    }

    // The pixel frame around the glyph: two pixels of margin, and an origin at a fractional pixel so no edge sits on a
    // pixel boundary by luck. Returns the em coordinate of the frame's left and top edges and its size in pixels.
    private static (float OriginX, float OriginY, int W, int H) Frame(SlugGlyphBlob blob, float ppe, float fracX, float fracY)
    {
        float left = MathF.Floor(blob.MinXEm * ppe - 2) + fracX;
        float top = MathF.Ceiling(blob.MaxYEm * ppe + 2) + fracY;
        int w = (int)MathF.Ceiling(blob.MaxXEm * ppe + 2 - left) + 1;
        int h = (int)MathF.Ceiling(top - (blob.MinYEm * ppe - 2)) + 1;
        return (left / ppe, top / ppe, w, h);
    }

    // ---- the references ----------------------------------------------------------

    // The exact area of the disc (cx, cy, r) inside the box [x0, x1] x [y0, y1]: integrate the clamped chord over x, split
    // where the chord's ends cross the box's top and bottom, so each piece is a constant or a circular segment with a closed
    // form antiderivative.
    private static double DiscBoxArea(double cx, double cy, double r, double x0, double x1, double y0, double y1)
    {
        var cuts = new List<double> { x0, x1 };
        foreach (double y in new[] { y0, y1 })
        {
            double dy = y - cy;
            if (Math.Abs(dy) < r)
            {
                double dx = Math.Sqrt(r * r - dy * dy);
                cuts.Add(cx - dx);
                cuts.Add(cx + dx);
            }
        }
        cuts.Add(cx - r);
        cuts.Add(cx + r);
        cuts.Sort();

        // Integral of sqrt(r^2 - u^2) du from 0 to u.
        double F(double u) => 0.5 * (u * Math.Sqrt(Math.Max(r * r - u * u, 0)) + r * r * Math.Asin(Math.Clamp(u / r, -1, 1)));
        double Chord(double a, double b) => F(b - cx) - F(a - cx);

        double area = 0;
        for (int k = 0; k + 1 < cuts.Count; k++)
        {
            double a = Math.Max(cuts[k], x0), b = Math.Min(cuts[k + 1], x1);
            if (b <= a) continue;
            double m = (a + b) / 2, hm = Math.Sqrt(Math.Max(r * r - (m - cx) * (m - cx), 0));
            if (Math.Abs(m - cx) >= r) continue;
            // Upper end of the chord, clamped to the box: above it, below it, or the circle itself.
            double upper;
            if (cy + hm >= y1) upper = y1 * (b - a);
            else if (cy + hm <= y0) upper = y0 * (b - a);
            else upper = cy * (b - a) + Chord(a, b);
            double lower;
            if (cy - hm >= y1) lower = y1 * (b - a);
            else if (cy - hm <= y0) lower = y0 * (b - a);
            else lower = cy * (b - a) - Chord(a, b);
            area += upper - lower;
        }
        return area;
    }

    // A fine polyline of an outline in font units, with winding numbers from it. It is the reference's own flattener:
    // 64 steps per quadratic and 128 per cubic, far finer than a pixel, so it stands for the original curve.
    private sealed class Flattener : IGlyphSink
    {
        public readonly List<(double X0, double Y0, double X1, double Y1)> Edges = [];
        public int CubicCount, QuadCount;
        private double _x, _y, _sx, _sy;
        private bool _open;

        public void MoveTo(float x, float y) { CloseContour(); _x = _sx = x; _y = _sy = y; _open = true; }
        public void LineTo(float x, float y) { Ensure(); Line(x, y); }
        public void QuadTo(float cx, float cy, float x, float y)
        {
            Ensure();
            QuadCount++;
            double x0 = _x, y0 = _y;
            const int n = 64;
            for (int i = 1; i <= n; i++)
            {
                double t = i / (double)n, u = 1 - t;
                Line(u * u * x0 + 2 * u * t * cx + t * t * x, u * u * y0 + 2 * u * t * cy + t * t * y);
            }
        }
        public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
        {
            Ensure();
            CubicCount++;
            double x0 = _x, y0 = _y;
            const int n = 128;
            for (int i = 1; i <= n; i++)
            {
                double t = i / (double)n, u = 1 - t;
                Line(u * u * u * x0 + 3 * u * u * t * c1x + 3 * u * t * t * c2x + t * t * t * x,
                     u * u * u * y0 + 3 * u * u * t * c1y + 3 * u * t * t * c2y + t * t * t * y);
            }
        }
        public void Close() => CloseContour();

        private void Ensure() { if (!_open) { _x = _sx; _y = _sy; _open = true; } }
        private void Line(double x, double y) { Edges.Add((_x, _y, x, y)); _x = x; _y = y; }
        private void CloseContour()
        {
            if (_open && (_x != _sx || _y != _sy)) Line(_sx, _sy);
            _open = false;
        }

        // Winding number at a point: signed crossings of a ray towards +x.
        public int Winding(double px, double py)
        {
            CloseContour();
            int wn = 0;
            foreach (var (x0, y0, x1, y1) in Edges)
            {
                if ((y0 <= py) == (y1 <= py)) continue;
                double x = x0 + (py - y0) / (y1 - y0) * (x1 - x0);
                if (x > px) wn += y1 > y0 ? 1 : -1;
            }
            return wn;
        }
    }

    // Nonzero-winding coverage by 16 x 16 samples a pixel: for each sample row the crossings of the edges are sorted along
    // x, and a sample's winding is the sum of the crossings left of it.
    private static float[] Supersample(Flattener f, int upem, float ppe, float ox, float oy, int w, int h)
    {
        const int ss = 16;
        var hits = new int[w * h];
        var xs = new List<(double X, int Dir)>();
        // The flattener closes its last contour on the first query; do it now through Winding.
        f.Winding(0, 0);
        for (int j = 0; j < h; j++)
            for (int sj = 0; sj < ss; sj++)
            {
                double y = (oy - (j + (sj + 0.5) / ss) / ppe) * upem;
                xs.Clear();
                foreach (var (x0, y0, x1, y1) in f.Edges)
                {
                    if ((y0 <= y) == (y1 <= y)) continue;
                    xs.Add((x0 + (y - y0) / (y1 - y0) * (x1 - x0), y1 > y0 ? 1 : -1));
                }
                if (xs.Count == 0) continue;
                xs.Sort((a, b) => a.X.CompareTo(b.X));
                int k = 0, wn = 0;
                for (int i = 0; i < w; i++)
                    for (int si = 0; si < ss; si++)
                    {
                        double x = (ox + (i + (si + 0.5) / ss) / ppe) * upem;
                        while (k < xs.Count && xs[k].X <= x) wn += xs[k++].Dir;
                        if (wn != 0) hits[j * w + i]++;
                    }
            }
        var img = new float[w * h];
        for (int k = 0; k < img.Length; k++) img[k] = hits[k] / (float)(ss * ss);
        return img;
    }
}
