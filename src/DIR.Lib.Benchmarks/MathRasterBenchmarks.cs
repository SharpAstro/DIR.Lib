using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using DIR.Lib.MathLayout;
using DIR.Lib.Tests;

namespace DIR.Lib.Benchmarks;

/// <summary>
/// Display math to RGBA over <see cref="MathRasterCorpus"/>, the formulas <c>MathRasterCacheTests</c> renders
/// with the raster caches on and off: Console.Lib's <c>MathCorpus</c>, tall radicals and stretched sets. Each
/// operation does the whole corpus (the unlexable formula is left out, since it never reaches a raster), so
/// a row is the sum over its formulas.
/// <list type="bullet">
/// <item><c>BuildAndRender</c>: a parsed formula's box tree built at the style, then
/// <see cref="BoxRasterizer.RenderToRgba"/>. This is the layout and raster work the raster caches serve:
/// the stretched glyphs are rasterized while the tree is built, the rest while it is drawn. The parse is
/// done once in setup, as Console.Lib's width-fit loop parses once for every size it tries.</item>
/// <item><c>Build</c> and <c>Render</c>: the two halves alone, <c>Render</c> drawing trees built once in
/// setup.</item>
/// <item><c>Parse</c>: the parse alone, and <c>ParseBuildAndRender</c>: everything Console.Lib does for a
/// display formula, source to RGBA.</item>
/// </list>
/// <para>Run with <c>dotnet run -c Release --project src/DIR.Lib.Benchmarks -- --filter "*MathRaster*"</c>.</para>
/// </summary>
[MemoryDiagnoser]
[MedianColumn]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MathRasterBenchmarks
{
    private static readonly RGBAColor32 Ink = new(0, 0, 0, 255);

    /// <summary>The face and size, as <c>file@px</c>: STIX Two Math at Sixel size and near half-block
    /// size, and DejaVu Sans, which has no MATH table.</summary>
    public static IEnumerable<string> Faces() => ["STIX2Math.otf@32", "STIX2Math.otf@12", "DejaVuSans.ttf@32"];

    [ParamsSource(nameof(Faces))]
    public string Face { get; set; } = "";

    private BoxStyle _style = null!;
    private string[] _sources = [];
    private Func<BoxStyle, Box>[] _builders = [];
    private Box[] _boxes = [];

    [GlobalSetup]
    public void Setup()
    {
        var at = Face.IndexOf('@');
        _style = new BoxStyle(MathRasterCorpus.FontPath(Face[..at]), float.Parse(Face[(at + 1)..]), Ink);
        var parsed = MathRasterCorpus.Formulas
            .Select(f => (f.Source, Builder: MathRasterCorpus.Parse(f.Source, _style)))
            .Where(f => f.Builder is not null)
            .ToArray();
        _sources = parsed.Select(f => f.Source).ToArray();
        _builders = parsed.Select(f => f.Builder!).ToArray();
        _boxes = _builders.Select(build => build(_style)).ToArray();
    }

    [Benchmark]
    public long BuildAndRender()
    {
        var pixels = 0L;
        foreach (var build in _builders)
        {
            var image = BoxRasterizer.RenderToRgba(build(_style), _style);
            pixels += image.Width * image.Height;
        }
        return pixels;
    }

    [Benchmark]
    public float Build()
    {
        var height = 0f;
        foreach (var build in _builders) height += build(_style).TotalHeight;
        return height;
    }

    [Benchmark]
    public long Render()
    {
        var pixels = 0L;
        foreach (var box in _boxes)
        {
            var image = BoxRasterizer.RenderToRgba(box, _style);
            pixels += image.Width * image.Height;
        }
        return pixels;
    }

    [Benchmark]
    public int Parse()
    {
        var parsed = 0;
        foreach (var source in _sources)
            if (MathRasterCorpus.Parse(source, _style) is not null) parsed++;
        return parsed;
    }

    [Benchmark]
    public long ParseBuildAndRender()
    {
        var pixels = 0L;
        foreach (var source in _sources)
        {
            var image = MathRasterCorpus.Render(source, _style);
            if (image is not null) pixels += image.Width * image.Height;
        }
        return pixels;
    }
}
