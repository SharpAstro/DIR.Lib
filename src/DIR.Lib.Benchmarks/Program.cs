using BenchmarkDotNet.Running;
using DIR.Lib.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(DrawingPrimitiveBenchmarks).Assembly).Run(args);
