using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using DIR.Lib.MathLayout;
using DIR.Lib.Tests;

namespace DIR.Lib.Benchmarks;

/// <summary>
/// Display math from source to RGBA over <see cref="MathRasterCorpus"/>, the formulas
/// <c>MathRasterCacheTests</c> renders with the raster caches on and off: Console.Lib's <c>MathCorpus</c>,
/// tall radicals and stretched sets. Each operation does the whole corpus (the unlexable formula is left
/// out, since it never reaches a raster), so a row is the sum over its formulas.
/// <list type="bullet">
/// <item><c>BuildAndRender</c>: what Console.Lib does for each display formula, parse and build the box
/// tree, then <see cref="BoxRasterizer.RenderToRgba"/>. The stretched glyphs are rasterized while the tree
/// is built, the rest while it is drawn.</item>
/// <item><c>Build</c>: the parse and build alone.</item>
/// <item><c>Render</c>: <see cref="BoxRasterizer.RenderToRgba"/> alone, of trees built once in setup.</item>
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
    private Box[] _boxes = [];

    [GlobalSetup]
    public void Setup()
    {
        var at = Face.IndexOf('@');
        _style = new BoxStyle(MathRasterCorpus.FontPath(Face[..at]), float.Parse(Face[(at + 1)..]), Ink);
        _sources = MathRasterCorpus.Formulas
            .Where(f => MathRasterCorpus.Build(f.Source, _style) is not null)
            .Select(f => f.Source)
            .ToArray();
        _boxes = _sources.Select(s => MathRasterCorpus.Build(s, _style)!).ToArray();
    }

    [Benchmark]
    public long BuildAndRender()
    {
        var pixels = 0L;
        foreach (var source in _sources)
        {
            var image = MathRasterCorpus.Render(source, _style);
            if (image is not null) pixels += image.Width * image.Height;
        }
        return pixels;
    }

    [Benchmark]
    public int Build()
    {
        var built = 0;
        foreach (var source in _sources)
            if (MathRasterCorpus.Build(source, _style) is not null) built++;
        return built;
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
}
