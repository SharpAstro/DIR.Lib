using DIR.Lib.MathLayout;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The math rasterizer keeps its glyphs and its stretched glyphs (radicals, big delimiters) across
/// <see cref="BoxRasterizer.RenderToRgba"/> calls (DIR.Lib #118). A cache that hands back a glyph drawn for
/// another request, or one shifted differently, changes the picture, so every formula of
/// <see cref="MathRasterCorpus"/> is rendered with the caches on and compared, byte for byte, with the
/// same formula rendered with them bypassed (<see cref="ManagedFontRasterizer.BypassRasterCaches"/>).
/// </summary>
public sealed class MathRasterCacheTests
{
    private static readonly RGBAColor32 Ink = new(0, 0, 0, 255);

    /// <summary>STIX Two Math at the four sizes Console.Lib draws display math at (Sixel at 32 and 24 px,
    /// sextant and half-block near 12 and 10), and DejaVu Sans, which has no MATH table, at 32 px.</summary>
    public static TheoryData<string, float> FontsAndSizes()
    {
        var data = new TheoryData<string, float>();
        foreach (var size in MathRasterCorpus.StixSizes) data.Add("STIX2Math.otf", size);
        data.Add("DejaVuSans.ttf", 32f);
        return data;
    }

    /// <summary>
    /// Each formula rendered twice with the caches on, in one process: the first render fills them (or
    /// finds what another test left there) and the second is served from them. Both must be the render
    /// with the caches bypassed, to the byte.
    /// </summary>
    [Theory]
    [MemberData(nameof(FontsAndSizes))]
    public void EachFormulaRenderedTwiceWithTheCachesIsTheRenderWithoutThem(string font, float size)
    {
        var style = new BoxStyle(MathRasterCorpus.FontPath(font), size, Ink);
        var bypassed = RenderCorpus(style, bypass: true);
        var first = RenderCorpus(style, bypass: false);
        var second = RenderCorpus(style, bypass: false);

        MathRasterCorpus.Formulas.Where((_, i) => bypassed[i] is null).Select(f => f.Name)
            .ShouldBe(["factorial-unlexable"], "every formula but the unlexable one builds");
        var differences = Differences(bypassed, first, "first").Concat(Differences(bypassed, second, "second")).ToList();
        differences.ShouldBeEmpty($"{font} at {size} px: renders with the caches differ from renders without them");
    }

    /// <summary>
    /// The corpus rendered on eight threads at once, each in its own order and starting together on empty
    /// caches, so the threads miss, fill and read the same entries concurrently. Every image must be the
    /// one rendered on a single thread with the caches bypassed.
    /// </summary>
    [Fact]
    public void RendersOnSeveralThreadsAtOnceMatchTheSingleThreadedRenders()
    {
        const int threads = 8;
        var styles = new[]
        {
            new BoxStyle(MathRasterCorpus.FontPath("STIX2Math.otf"), 32f, Ink),
            new BoxStyle(MathRasterCorpus.FontPath("STIX2Math.otf"), 12f, Ink),
            new BoxStyle(MathRasterCorpus.FontPath("DejaVuSans.ttf"), 32f, Ink),
        };
        var jobs = styles.SelectMany(style => MathRasterCorpus.Formulas.Select(f => (Style: style, f.Name, f.Source))).ToArray();
        var expected = WithCaches(bypass: true, () => jobs.Select(job => MathRasterCorpus.Render(job.Source, job.Style)).ToArray());

        BoxStyle.SharedRasterizer.ClearRasterCaches();
        var results = new RgbaImage?[threads][];
        using var start = new Barrier(threads);
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            // Each thread its own order, so they meet on different entries at different times.
            var order = Enumerable.Range(0, jobs.Length).OrderBy(i => (i * 7919 + t * 104729) % jobs.Length).ToArray();
            var mine = new RgbaImage?[jobs.Length];
            start.SignalAndWait();
            foreach (var i in order) mine[i] = MathRasterCorpus.Render(jobs[i].Source, jobs[i].Style);
            results[t] = mine;
        })).ToList();
        workers.ForEach(w => w.Start());
        workers.ForEach(w => w.Join());

        var differences = new List<string>();
        for (var t = 0; t < threads; t++)
        {
            var mine = results[t].ShouldNotBeNull($"thread {t} finished");
            for (var i = 0; i < jobs.Length; i++)
                if (Difference(expected[i], mine[i]) is { } d)
                    differences.Add($"thread {t}, {jobs[i].Name} ({Path.GetFileName(jobs[i].Style.FontPath)} {jobs[i].Style.FontSize} px): {d}");
        }
        differences.ShouldBeEmpty("renders on several threads at once differ from the single-threaded renders");
    }

    private static RgbaImage?[] RenderCorpus(BoxStyle style, bool bypass) =>
        WithCaches(bypass, () => MathRasterCorpus.Formulas.Select(f => MathRasterCorpus.Render(f.Source, style)).ToArray());

    /// <summary><paramref name="render"/>'s result with the raster caches bypassed or not on this flow.</summary>
    private static T WithCaches<T>(bool bypass, Func<T> render)
    {
        var previous = ManagedFontRasterizer.BypassRasterCaches.Value;
        ManagedFontRasterizer.BypassRasterCaches.Value = bypass;
        try
        {
            return render();
        }
        finally
        {
            ManagedFontRasterizer.BypassRasterCaches.Value = previous;
        }
    }

    private static IEnumerable<string> Differences(RgbaImage?[] expected, RgbaImage?[] actual, string which)
    {
        for (var i = 0; i < expected.Length; i++)
            if (Difference(expected[i], actual[i]) is { } d)
                yield return $"{MathRasterCorpus.Formulas[i].Name}, {which} render: {d}";
    }

    /// <summary>Where <paramref name="actual"/> first differs from <paramref name="expected"/>, or null where
    /// the two are the same size and the same bytes (or both absent).</summary>
    private static string? Difference(RgbaImage? expected, RgbaImage? actual)
    {
        if (expected is null || actual is null)
            return expected is null && actual is null ? null : $"built {(expected is null ? "only with" : "only without")} the caches";
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return $"{actual.Width}x{actual.Height}, expected {expected.Width}x{expected.Height}";
        var a = expected.Pixels;
        var b = actual.Pixels;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                return $"byte {i} (x {i / 4 % expected.Width}, y {i / 4 / expected.Width}, channel {i % 4}) is {b[i]}, expected {a[i]}";
        return null;
    }
}
