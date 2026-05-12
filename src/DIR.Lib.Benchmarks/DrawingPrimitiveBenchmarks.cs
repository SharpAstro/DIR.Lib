using System;
using BenchmarkDotNet.Attributes;

namespace DIR.Lib.Benchmarks;

/// <summary>
/// Benchmarks for the CPU rendering primitives on <see cref="RgbaImageRenderer"/>.
/// Measures the actual pixel-buffer path used by the planner altitude chart and
/// other CPU-rendered surfaces.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class DrawingPrimitiveBenchmarks
{
    private RgbaImageRenderer _renderer = null!;
    private RgbaImage _image = null!;

    // Spline-like points for DrawSolidCurve simulation (200 segments across 1000px)
    private (float X, float Y)[] _splinePoints = null!;

    private static readonly RGBAColor32 White = new(0xFF, 0xFF, 0xFF, 0xFF);

    [GlobalSetup]
    public void Setup()
    {
        _renderer = new RgbaImageRenderer(1920, 1080);
        _image = _renderer.Surface;

        // Generate a realistic altitude-curve-like spline (smooth arc across the chart)
        var rng = new Random(42);
        _splinePoints = new (float, float)[201];
        for (var i = 0; i < _splinePoints.Length; i++)
        {
            var x = 100f + i * 4f; // 100..900 across 800px
            // Sine-ish curve with noise, simulating an altitude chart
            var y = 540f - 200f * MathF.Sin(MathF.PI * i / 200f) + (float)(rng.NextDouble() - 0.5) * 3f;
            _splinePoints[i] = (x, y);
        }
    }

    // --- DrawLine: horizontal ---

    [Benchmark(Description = "DrawLine horizontal 100px")]
    public void DrawLine_Horizontal_100()
        => _renderer.DrawLine(100, 500, 200, 500, White);

    [Benchmark(Description = "DrawLine horizontal 1000px")]
    public void DrawLine_Horizontal_1000()
        => _renderer.DrawLine(100, 500, 1100, 500, White);

    // --- DrawLine: vertical ---

    [Benchmark(Description = "DrawLine vertical 100px")]
    public void DrawLine_Vertical_100()
        => _renderer.DrawLine(500, 100, 500, 200, White);

    [Benchmark(Description = "DrawLine vertical 1000px")]
    public void DrawLine_Vertical_1000()
        => _renderer.DrawLine(500, 100, 500, 1100, White);

    // --- DrawLine: diagonal ---

    [Benchmark(Description = "DrawLine diagonal 100px")]
    public void DrawLine_Diagonal_100()
        => _renderer.DrawLine(100, 100, 171, 171, White);

    [Benchmark(Description = "DrawLine diagonal 1000px")]
    public void DrawLine_Diagonal_1000()
        => _renderer.DrawLine(100, 100, 807, 807, White);

    // --- DrawLine: thick ---

    [Benchmark(Description = "DrawLine horizontal 1000px thick=3")]
    public void DrawLine_Horizontal_1000_Thick()
        => _renderer.DrawLine(100, 500, 1100, 500, White, thickness: 3);

    [Benchmark(Description = "DrawLine diagonal 1000px thick=3")]
    public void DrawLine_Diagonal_1000_Thick()
        => _renderer.DrawLine(100, 100, 807, 807, White, thickness: 3);

    // --- DrawLine: old FillRect baseline for comparison ---

    [Benchmark(Description = "FillRect horizontal 1000px (old approach)", Baseline = true)]
    public void FillRect_Horizontal_1000()
        => _renderer.FillRectangle(new RectInt(new PointInt(1100, 501), new PointInt(100, 500)), White);

    // --- DrawEllipse (outline) ---

    [Benchmark(Description = "DrawEllipse r=50")]
    public void DrawEllipse_Small()
        => _renderer.DrawEllipse(
            new RectInt(new PointInt(550, 550), new PointInt(450, 450)), White);

    [Benchmark(Description = "DrawEllipse r=200")]
    public void DrawEllipse_Large()
        => _renderer.DrawEllipse(
            new RectInt(new PointInt(700, 700), new PointInt(300, 300)), White);

    // --- FillEllipse (filled, for comparison) ---

    [Benchmark(Description = "FillEllipse r=50")]
    public void FillEllipse_Small()
        => _renderer.FillEllipse(
            new RectInt(new PointInt(550, 550), new PointInt(450, 450)), White);

    // --- Spline curve (simulates altitude chart) ---

    [Benchmark(Description = "DrawSolidCurve 200 segments via DrawLine")]
    public void DrawSolidCurve_200Segments()
    {
        for (var i = 0; i < _splinePoints.Length - 1; i++)
        {
            _renderer.DrawLine(
                _splinePoints[i].X, _splinePoints[i].Y,
                _splinePoints[i + 1].X, _splinePoints[i + 1].Y,
                White);
        }
    }

    // --- DrawPolyline: same 200-segment curve via wrapper ---
    // Should be ~equal to DrawSolidCurve_200Segments (default impl loops DrawLine).
    // Difference measures the polyline wrapper overhead (a pair-iteration loop).

    [Benchmark(Description = "DrawPolyline 200 segments")]
    public void DrawPolyline_200Segments()
        => _renderer.DrawPolyline(_splinePoints, White);

    [Benchmark(Description = "DrawPolyline 200 segments thick=2")]
    public void DrawPolyline_200Segments_Thick()
        => _renderer.DrawPolyline(_splinePoints, White, thickness: 2);

    // --- DrawLineDashed: dash overhead ---

    [Benchmark(Description = "DrawLineDashed horizontal 1000px dash=10,gap=10")]
    public void DrawLineDashed_Horizontal_1000()
        => _renderer.DrawLineDashed(100, 500, 1100, 500, White, dashLength: 10f, gapLength: 10f);

    [Benchmark(Description = "DrawLineDashed horizontal 1000px dash=3,gap=3")]
    public void DrawLineDashed_Horizontal_1000_Tight()
        => _renderer.DrawLineDashed(100, 500, 1100, 500, White, dashLength: 3f, gapLength: 3f);

    [Benchmark(Description = "DrawLineDashed diagonal 1000px dash=10,gap=10")]
    public void DrawLineDashed_Diagonal_1000()
        => _renderer.DrawLineDashed(100, 100, 807, 807, White, dashLength: 10f, gapLength: 10f);

    // --- DrawPolylineDashed: the MetricSampleMap / planner chart case ---

    [Benchmark(Description = "DrawPolylineDashed 200 segments dash=6,gap=3")]
    public void DrawPolylineDashed_200Segments()
        => _renderer.DrawPolylineDashed(_splinePoints, White, dashLength: 6f, gapLength: 3f);

    // --- Alpha blend ---

    private static readonly RGBAColor32 SemiWhite = new(0xFF, 0xFF, 0xFF, 0x80);

    [Benchmark(Description = "FillRect alpha 100x100")]
    public void FillRect_Alpha_100x100()
        => _renderer.FillRectangle(
            new RectInt(new PointInt(200, 200), new PointInt(100, 100)), SemiWhite);

    [Benchmark(Description = "FillRect alpha 1000x10")]
    public void FillRect_Alpha_1000x10()
        => _renderer.FillRectangle(
            new RectInt(new PointInt(1100, 510), new PointInt(100, 500)), SemiWhite);

    // --- DrawLine: various angles (short, uses Bresenham heuristic) ---

    [Benchmark(Description = "DrawLine 15-deg 100px")]
    public void DrawLine_15deg_100()
        => _renderer.DrawLine(100, 500, 197, 526, White);

    [Benchmark(Description = "DrawLine 30-deg 100px")]
    public void DrawLine_30deg_100()
        => _renderer.DrawLine(100, 500, 187, 550, White);

    [Benchmark(Description = "DrawLine 60-deg 100px")]
    public void DrawLine_60deg_100()
        => _renderer.DrawLine(100, 500, 150, 587, White);

    [Benchmark(Description = "DrawLine 75-deg 100px")]
    public void DrawLine_75deg_100()
        => _renderer.DrawLine(100, 500, 126, 597, White);

    // --- DrawEllipse scanline (replaces old per-pixel trig) ---

    [Benchmark(Description = "DrawEllipse circle r=20")]
    public void DrawEllipse_Circle_20()
        => _renderer.DrawEllipse(
            new RectInt(new PointInt(520, 520), new PointInt(480, 480)), White);

    [Benchmark(Description = "DrawEllipse circle r=100")]
    public void DrawEllipse_Circle_100()
        => _renderer.DrawEllipse(
            new RectInt(new PointInt(600, 600), new PointInt(400, 400)), White);

    [Benchmark(Description = "DrawEllipse wide 160x80")]
    public void DrawEllipse_Wide()
        => _renderer.DrawEllipse(
            new RectInt(new PointInt(580, 540), new PointInt(420, 460)), White);

    [Benchmark(Description = "DrawEllipse circle r=100 thick=3")]
    public void DrawEllipse_Circle_100_Thick()
        => _renderer.DrawEllipse(
            new RectInt(new PointInt(600, 600), new PointInt(400, 400)), White, 3f);
}
