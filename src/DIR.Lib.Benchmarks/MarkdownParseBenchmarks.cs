using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using DIR.Lib.Markdown;
using DIR.Lib.Tests;

namespace DIR.Lib.Benchmarks;

/// <summary>
/// The markdown parse (<see cref="MarkdownBlockVisitor.Parse"/>, with the inline and math passes it
/// runs) over the documents of <see cref="MarkdownCorpus"/>, the corpus whose AST
/// <c>MarkdownCorpusGoldenTests</c> pins. One case per document, plus <c>formula/*</c>, which parses
/// every single-formula document once per operation, so its row is their sum. The visitor is shared
/// across calls, as Console.Lib's <c>MarkdownRendererLalr</c> shares its one.
/// <para>Run with <c>dotnet run -c Release --project src/DIR.Lib.Benchmarks -- --filter "*MarkdownParse*" --memory</c>.</para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MarkdownParseBenchmarks
{
    private const string AllFormulas = "formula/*";

    private readonly MarkdownBlockVisitor _visitor = new();
    private string[] _texts = [];

    public static IEnumerable<string> Documents() =>
        MarkdownCorpus.AllDocuments().Select(d => d.Name).Append(AllFormulas);

    [ParamsSource(nameof(Documents))]
    public string Document { get; set; } = "";

    [GlobalSetup]
    public void Setup() =>
        _texts = Document == AllFormulas
            ? MarkdownCorpus.FormulaDocuments().Select(d => d.Text).ToArray()
            : [MarkdownCorpus.AllDocuments().Single(d => d.Name == Document).Text];

    [Benchmark]
    public int Parse()
    {
        var blocks = 0;
        foreach (var text in _texts) blocks += _visitor.Parse(text).Count;
        return blocks;
    }
}
