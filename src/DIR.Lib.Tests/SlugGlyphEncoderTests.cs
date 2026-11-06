using System.Text;
using SharpAstro.Fonts.Outlines;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="SlugGlyphEncoder"/>: the blob's layout, and above all its bands. A curve must be in every band a
/// ray through the glyph AS DRAWN can cross it, and what is drawn is the quantised outline, so band membership is
/// held to the quantised points, including where a test on the original points would miss.
/// </summary>
public class SlugGlyphEncoderTests
{
    private const string Cmr10 = "mem:cmr10-slug";
    private static readonly string DejaVu = Path.Combine("Fonts", "DejaVuSans.ttf");

    // The blob read back the way a shader reads it.
    private readonly struct Decoded(int[] w)
    {
        public int HBands => w[3];
        public int VBands => w[4];
        public int BandOffset => w[5];
        public int CurveOffset => w[7];
        public int MinX => w[8];
        public int MinY => w[9];
        public int MaxX => w[10];
        public int MaxY => w[11];

        public (int Count, int Desc, int Asc, int Split) Band(bool horizontal, int b)
        {
            int t = BandOffset + (horizontal ? b : HBands + b) * 4;
            return (w[t], w[t + 1], w[t + 2], w[t + 3]);
        }

        public int[] List(bool horizontal, int b, bool descending)
        {
            var (count, desc, asc, _) = Band(horizontal, b);
            return w.AsSpan(descending ? desc : asc, count).ToArray();
        }

        // The curve at a word offset: x1 y1 x2 y2 x3 y3 in blob units.
        public int[] Curve(int offset) => w.AsSpan(offset, 6).ToArray();

        public IEnumerable<int> CurveOffsets()
        {
            for (int i = 0; i < w[2]; i++) yield return CurveOffset + i * 6;
        }
    }

    private static int[] Encode(Action<IGlyphSink> draw, int upem = 1000, float toleranceEm = SlugGlyphEncoder.DefaultToleranceEm)
    {
        var enc = new SlugGlyphEncoder(toleranceEm);
        enc.Begin(upem);
        draw(enc);
        enc.TryFinish(out var g).ShouldBeTrue();
        return g.Words;
    }

    private static void Polygon(IGlyphSink s, params (float X, float Y)[] pts)
    {
        s.MoveTo(pts[0].X, pts[0].Y);
        for (int i = 1; i < pts.Length; i++) s.LineTo(pts[i].X, pts[i].Y);
        s.Close();
    }

    [Fact]
    public void ALineBecomesADegenerateQuadraticWithItsMidpointAsControl()
    {
        var w = Encode(s => Polygon(s, (0, 0), (10, 0), (10, 5.25f)));
        var d = new Decoded(w);

        w[2].ShouldBe(3); // two sides and the closing line
        var curves = d.CurveOffsets().Select(d.Curve).ToArray();
        // Blob units are eighths of a font unit: (10, 0) is (80, 0).
        curves[0].ShouldBe([0, 0, 40, 0, 80, 0]);
        curves[1].ShouldBe([80, 0, 80, 21, 80, 42]); // an odd control: 5.25 fu is 42 blob units, its midpoint is 21
        foreach (var c in curves)
        {
            c[2].ShouldBe((c[0] + c[4]) / 2);
            c[3].ShouldBe((c[1] + c[5]) / 2);
        }
        // The contour closes: each curve starts where the last ended.
        for (int i = 0; i < curves.Length; i++)
        {
            var next = curves[(i + 1) % curves.Length];
            curves[i][4].ShouldBe(next[0]);
            curves[i][5].ShouldBe(next[1]);
        }
    }

    [Fact]
    public void PointsAreSnappedToAQuarterFontUnit()
    {
        var w = Encode(s => Polygon(s, (0.1f, 0.13f), (10.13f, 0.38f), (10.4f, 5f)));
        var d = new Decoded(w);
        foreach (var o in d.CurveOffsets())
        {
            var c = d.Curve(o);
            (c[0] % 2).ShouldBe(0);
            (c[1] % 2).ShouldBe(0);
            (c[4] % 2).ShouldBe(0);
            (c[5] % 2).ShouldBe(0);
        }
        // 0.1 -> 0 and 0.13 -> 0.25 (in blob units 0 and 2); 10.13 -> 10.25 (82); 0.38 -> 0.5 (4); 10.4 -> 10.5 (84).
        d.Curve(d.CurveOffset).ShouldBe([0, 2, 41, 3, 82, 4]);
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.002f)]
    [InlineData(0.0005f)]
    public void ACubicSplitStaysWithinTheToleranceOfTheCubic(float toleranceEm)
    {
        const int upem = 1000;
        // An S bend and a loop-ish arch, big enough that one quadratic is far off.
        (float X, float Y)[][] cubics =
        [
            [(0, 0), (0, 600), (1000, -400), (1000, 200)],
            [(0, 0), (-300, 900), (1300, 900), (1000, 0)],
        ];
        foreach (var cb in cubics)
        {
            var w = Encode(s =>
            {
                s.MoveTo(cb[0].X, cb[0].Y);
                s.CubicTo(cb[1].X, cb[1].Y, cb[2].X, cb[2].Y, cb[3].X, cb[3].Y);
                s.Close();
            }, upem, toleranceEm);
            var d = new Decoded(w);
            var quads = d.CurveOffsets().Select(d.Curve).ToArray();
            // The cubic is the first curves; the last is the closing line.
            var chain = quads.Take(quads.Length - 1).ToArray();
            chain.Length.ShouldBeGreaterThan(1);

            // Every point of the cubic is within the tolerance of the quadratic chain (plus the snap to a quarter unit).
            double tol = toleranceEm * upem + 0.25;
            var samples = new List<(double X, double Y)>();
            foreach (var q in chain)
                for (int i = 0; i <= 200; i++)
                {
                    double t = i / 200.0, u = 1 - t;
                    samples.Add((((u * u * q[0] + 2 * u * t * q[2] + t * t * q[4])) / 8, ((u * u * q[1] + 2 * u * t * q[3] + t * t * q[5])) / 8));
                }
            double worst = 0;
            for (int i = 0; i <= 400; i++)
            {
                double t = i / 400.0, u = 1 - t;
                double x = u * u * u * cb[0].X + 3 * u * u * t * cb[1].X + 3 * u * t * t * cb[2].X + t * t * t * cb[3].X;
                double y = u * u * u * cb[0].Y + 3 * u * u * t * cb[1].Y + 3 * u * t * t * cb[2].Y + t * t * t * cb[3].Y;
                double best = double.MaxValue;
                foreach (var p in samples) best = Math.Min(best, Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)));
                worst = Math.Max(worst, best);
            }
            worst.ShouldBeLessThanOrEqualTo(tol, $"tolerance {toleranceEm} em");
        }
    }

    [Fact]
    public void ATighterToleranceSplitsACubicIntoMoreQuadratics()
    {
        int Count(float tol) => Encode(s =>
        {
            s.MoveTo(0, 0);
            s.CubicTo(0, 600, 1000, -400, 1000, 200);
            s.Close();
        }, 1000, tol)[2];

        Count(0.0005f).ShouldBeGreaterThan(Count(0.01f));
    }

    [Fact]
    public void ACurveThatCrossesABandEdgeOnlyAfterQuantisationIsListedInBothBands()
    {
        // More than 16 curves, so 16 horizontal bands over a height of 19.25 fu (19.2 snaps up): a band is 1.203 fu high, and
        // the first edge is at 1.203 fu. The diagonal tops out at 1.13 fu, under that edge, but snaps to 1.25 fu, over it.
        var w = Encode(s =>
        {
            s.MoveTo(0, 0);
            s.LineTo(10, 0);
            s.LineTo(10, 0.2f);
            s.LineTo(12, 1.13f);
            s.LineTo(12, 19.2f);
            s.LineTo(0, 19.2f);
            for (int y = 18; y >= 2; y--) s.LineTo(0, y);
            s.Close();
        });
        var d = new Decoded(w);
        d.HBands.ShouldBe(16);
        double edge = d.MinY + (double)(d.MaxY - d.MinY) / 16; // the top of band 0, in blob units
        edge.ShouldBe(9.625, 1e-9);

        var diagonal = d.CurveOffsets().Single(o => d.Curve(o)[0] == 80 && d.Curve(o)[1] == 2 && d.Curve(o)[4] == 96);
        var c = d.Curve(diagonal);
        (1.13 * 8).ShouldBeLessThan(edge); // on the original points the curve is in band 0 only
        ((double)c[5]).ShouldBeGreaterThan(edge);    // on the snapped ones it reaches into band 1

        d.List(true, 0, true).ShouldContain(diagonal);
        d.List(true, 0, false).ShouldContain(diagonal);
        d.List(true, 1, true).ShouldContain(diagonal);
        d.List(true, 1, false).ShouldContain(diagonal);
        d.List(true, 2, true).ShouldNotContain(diagonal);
    }

    [Fact]
    public void ACurveIsListedInEveryBandItsBoundsTouchAndNoOther()
    {
        // A tall zig-zag: every segment spans several bands, with a sheared diagonal that ends exactly on band edges.
        var w = Encode(s => Polygon(s, (0, 0), (30, 0), (30, 24), (20, 48), (10, 6), (0, 40), (0, 0.5f)));
        AssertBandsHoldExactlyTheCurvesTheirBoundsTouch(w);
    }

    [Fact]
    public void ABandEdgeIsInclusiveSoACurveEndingOnItIsInBothBands()
    {
        // Four bands over y in [0, 32] fu: edges at 8, 16, 24. The lines from y = 0 to 16 touch bands 0, 1 and 2 (16 is the
        // bottom edge of band 2); one from 8 to 12 touches 0 (top edge) and 1.
        var w = Encode(s => Polygon(s, (0, 0), (4, 0), (4, 16), (8, 32), (0, 32), (0, 20)));
        var d = new Decoded(w);
        AssertBandsHoldExactlyTheCurvesTheirBoundsTouch(w);
        var up = d.CurveOffsets().Single(o => d.Curve(o)[1] == 0 && d.Curve(o)[5] == 128 && d.Curve(o)[0] == 32 && d.Curve(o)[4] == 32);
        d.HBands.ShouldBe(4);
        for (int b = 0; b < 3; b++) d.List(true, b, true).ShouldContain(up, $"band {b}");
        d.List(true, 3, true).ShouldNotContain(up);
    }

    [Fact]
    public void BandCountsNeverExceedSixteenAndAreOneWhenThereIsNoExtent()
    {
        // A 200-sided polygon wants far more than 16 bands.
        var pts = Enumerable.Range(0, 200).Select(i => (X: 500 + 400 * MathF.Cos(i * MathF.Tau / 200), Y: 500 + 400 * MathF.Sin(i * MathF.Tau / 200))).ToArray();
        var w = Encode(s => Polygon(s, pts));
        w[2].ShouldBe(200);
        w[3].ShouldBe(16);
        w[4].ShouldBe(16);
        AssertBandsHoldExactlyTheCurvesTheirBoundsTouch(w);

        // Two curves and no height: a flat sliver has one horizontal band, and its vertical bands hold its two lines.
        var flat = Encode(s => Polygon(s, (0, 5), (50, 5)));
        flat[3].ShouldBe(1);
        flat[4].ShouldBeInRange(1, 16);
        new Decoded(flat).Band(true, 0).Count.ShouldBe(0); // a ray along a horizontal line crosses nothing
    }

    [Fact]
    public void TheBlobCarriesItsFormatVersionAndAnOldOneIsRefused()
    {
        var w = Encode(s => Polygon(s, (0, 0), (10, 0), (10, 10)));
        w[0].ShouldBe(SlugGlyphBlob.FormatVersion);
        SlugGlyphBlob.TryFromWords(w, out var blob).ShouldBeTrue();
        blob.Version.ShouldBe(SlugGlyphBlob.FormatVersion);
        blob.CurveCount.ShouldBe(3);

        var old = (int[])w.Clone();
        old[0] = SlugGlyphBlob.FormatVersion - 1;
        SlugGlyphBlob.IsReadable(old).ShouldBeFalse();
        SlugGlyphBlob.TryFromWords(old, out var refused).ShouldBeFalse();
        refused.Words.ShouldBeNull();
        SlugGlyphBlob.IsReadable(new int[3]).ShouldBeFalse();
    }

    [Fact]
    public void ATruncatedOrInconsistentBlobIsRefused()
    {
        var pts = Enumerable.Range(0, 40).Select(i => (X: 500 + 400 * MathF.Cos(i * MathF.Tau / 40), Y: 500 + 400 * MathF.Sin(i * MathF.Tau / 40))).ToArray();
        var w = Encode(s => Polygon(s, pts));
        SlugGlyphBlob.IsReadable(w).ShouldBeTrue();

        // Every shorter prefix is refused: the header, a band header, a curve list or a curve is cut off.
        for (int len = 0; len < w.Length; len++)
            SlugGlyphBlob.IsReadable(w.AsSpan(0, len)).ShouldBeFalse($"a blob cut to {len} of {w.Length} words");
        SlugGlyphBlob.TryFromWords(w[..^1], out var cut).ShouldBeFalse();
        cut.Words.ShouldBeNull();

        // Words past the end are not a blob either.
        SlugGlyphBlob.IsReadable(w.Concat(new[] { 0 }).ToArray()).ShouldBeFalse();

        // A band list that points outside the lists, or a count that cannot be, is refused.
        var badList = (int[])w.Clone();
        badList[w[5] + 1] = w.Length;
        SlugGlyphBlob.IsReadable(badList).ShouldBeFalse();
        var badBands = (int[])w.Clone();
        badBands[3] = 17;
        SlugGlyphBlob.IsReadable(badBands).ShouldBeFalse();

        // An empty glyph is the header alone, and it is readable.
        var empty = Encode(s => { });
        empty.Length.ShouldBe(SlugGlyphBlob.HeaderWords);
        SlugGlyphBlob.IsReadable(empty).ShouldBeTrue();
        SlugGlyphBlob.IsReadable(empty.AsSpan(0, empty.Length - 1)).ShouldBeFalse();
    }

    [Fact]
    public void TheBlobHasItsEmSpaceBounds()
    {
        var w = Encode(s => Polygon(s, (100, 200), (600, 200), (600, 800), (100, 800)), upem: 1000);
        SlugGlyphBlob.TryFromWords(w, out var g).ShouldBeTrue();
        g.UnitsPerEm.ShouldBe(1000);
        g.MinXEm.ShouldBe(0.1f, 1e-6f);
        g.MinYEm.ShouldBe(0.2f, 1e-6f);
        g.MaxXEm.ShouldBe(0.6f, 1e-6f);
        g.MaxYEm.ShouldBe(0.8f, 1e-6f);
    }

    [Fact]
    public void AnEmptyOutlineIsAHeaderOnlyBlobAndAnOutOfRangeOneFails()
    {
        var enc = new SlugGlyphEncoder();
        enc.Begin(1000);
        enc.TryFinish(out var empty).ShouldBeTrue();
        empty.IsEmpty.ShouldBeTrue();
        empty.Words.Length.ShouldBe(SlugGlyphBlob.HeaderWords);
        empty.Version.ShouldBe(SlugGlyphBlob.FormatVersion);

        enc.Begin(1000);
        enc.MoveTo(0, 0);
        enc.LineTo(1e9f, 0);
        enc.LineTo(0, 10);
        enc.TryFinish(out _).ShouldBeFalse();

        enc.Begin(1000); // the encoder is reusable after a failure
        enc.MoveTo(0, 0);
        enc.LineTo(1, 0);
        enc.LineTo(0, 1);
        enc.TryFinish(out var ok).ShouldBeTrue();
        ok.CurveCount.ShouldBe(3);
    }

    [Fact]
    public void AnSfntGlyphEncodes()
    {
        using var r = new ManagedFontRasterizer();
        var enc = new SlugGlyphEncoder();
        foreach (char ch in "OgB&@8WQ%")
        {
            var id = r.ResolveGlyphIdentity(DejaVu, new Rune(ch), ch, GlyphMapHint.Auto);
            enc.TryEncode(r, DejaVu, id, out var g).ShouldBeTrue($"'{ch}'");
            g.IsEmpty.ShouldBeFalse();
            g.UnitsPerEm.ShouldBe(2048);
            g.HBandCount.ShouldBeInRange(1, 16);
            g.VBandCount.ShouldBeInRange(1, 16);
            g.MaxXEm.ShouldBeGreaterThan(g.MinXEm);
            g.MaxYEm.ShouldBeGreaterThan(g.MinYEm);
            AssertBandsHoldExactlyTheCurvesTheirBoundsTouch(g.Words);
        }
    }

    [Fact]
    public void ASpaceEncodesAsAnEmptyGlyph()
    {
        using var r = new ManagedFontRasterizer();
        var id = r.ResolveGlyphIdentity(DejaVu, new Rune(' '), ' ', GlyphMapHint.Auto);
        new SlugGlyphEncoder().TryEncode(r, DejaVu, id, out var g).ShouldBeTrue();
        g.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void AType1GlyphEncodesFromItsCubicsInItsOwnEm()
    {
        using var r = new ManagedFontRasterizer();
        r.RegisterFontFromMemory(Cmr10, File.ReadAllBytes(Path.Combine("Fonts", "cmr10.pfb"))).ShouldBeTrue();
        var enc = new SlugGlyphEncoder();

        enc.TryEncode(r, Cmr10, new GlyphIdentity(0, "o"), out var g).ShouldBeTrue();

        g.UnitsPerEm.ShouldBe(1000);
        g.IsEmpty.ShouldBeFalse();
        g.CurveCount.ShouldBeGreaterThan(8); // a bowl and its counter, cubics split into quadratics
        g.MaxYEm.ShouldBeGreaterThan(g.MinYEm);
        AssertBandsHoldExactlyTheCurvesTheirBoundsTouch(g.Words);

        enc.TryEncode(r, Cmr10, new GlyphIdentity(12, null), out _).ShouldBeFalse();
        enc.TryEncode(r, Cmr10, new GlyphIdentity(0, "no_such_glyph_xyz"), out _).ShouldBeFalse();
        enc.TryEncode(r, "mem:never-registered-slug", new GlyphIdentity(5, null), out _).ShouldBeFalse();
    }

    [Fact]
    public void EncodingTheSameGlyphTwiceGivesTheSameBlob()
    {
        using var r = new ManagedFontRasterizer();
        var enc = new SlugGlyphEncoder();
        var id = r.ResolveGlyphIdentity(DejaVu, new Rune('g'), 'g', GlyphMapHint.Auto);
        enc.TryEncode(r, DejaVu, id, out var a).ShouldBeTrue();
        enc.TryEncode(r, DejaVu, id, out var b).ShouldBeTrue();
        a.Words.ShouldBe(b.Words);
    }

    // For every band of both axes, every curve whose quantised bounds touch the band's closed extent, exactly, is in both
    // lists, nothing else is, a curve flat across the axis is in none, and the lists are sorted as the early-out assumes.
    private static void AssertBandsHoldExactlyTheCurvesTheirBoundsTouch(int[] w)
    {
        var d = new Decoded(w);
        w[3].ShouldBeInRange(1, 16);
        w[4].ShouldBeInRange(1, 16);
        foreach (bool horizontal in new[] { true, false })
        {
            int bands = horizontal ? d.HBands : d.VBands;
            long lo = horizontal ? d.MinY : d.MinX, hi = horizontal ? d.MaxY : d.MaxX;
            for (int b = 0; b < bands; b++)
            {
                var desc = d.List(horizontal, b, true);
                var asc = d.List(horizontal, b, false);
                asc.OrderBy(x => x).ShouldBe(desc.OrderBy(x => x));
                desc.Distinct().Count().ShouldBe(desc.Length);

                foreach (var o in d.CurveOffsets())
                {
                    var c = d.Curve(o);
                    long cLo = horizontal ? Math.Min(c[1], Math.Min(c[3], c[5])) : Math.Min(c[0], Math.Min(c[2], c[4]));
                    long cHi = horizontal ? Math.Max(c[1], Math.Max(c[3], c[5])) : Math.Max(c[0], Math.Max(c[2], c[4]));
                    // Band b is [lo + (hi-lo) b / bands, lo + (hi-lo) (b+1) / bands]; compare scaled by bands.
                    long bandLo = lo * bands + (hi - lo) * b, bandHi = lo * bands + (hi - lo) * (b + 1);
                    bool touches = cHi > cLo && cHi * bands >= bandLo && cLo * bands <= bandHi;
                    (desc.Contains(o)).ShouldBe(touches, $"{(horizontal ? "H" : "V")} band {b}: curve at word {o} [{cLo}, {cHi}]");
                }

                // Descending by max along the ray, ascending by min.
                var maxes = desc.Select(o => AlongMax(d.Curve(o), horizontal)).ToArray();
                maxes.ShouldBe(maxes.OrderByDescending(x => x).ToArray());
                var mins = asc.Select(o => AlongMin(d.Curve(o), horizontal)).ToArray();
                mins.ShouldBe(mins.OrderBy(x => x).ToArray());
            }
        }
    }

    private static int AlongMax(int[] c, bool horizontal) =>
        horizontal ? Math.Max(c[0], Math.Max(c[2], c[4])) : Math.Max(c[1], Math.Max(c[3], c[5]));

    private static int AlongMin(int[] c, bool horizontal) =>
        horizontal ? Math.Min(c[0], Math.Min(c[2], c[4])) : Math.Min(c[1], Math.Min(c[3], c[5]));
}
