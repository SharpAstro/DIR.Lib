using System;
using System.IO;
using System.Linq;
using System.Text;
using DIR.Lib.Markdown;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Pins the parsed AST of every <see cref="MarkdownCorpus"/> document against a committed golden in
/// <c>Fixtures/MarkdownCorpus/golden</c>, written by <see cref="MdAstDump"/>. The golden was taken
/// before the parse was made faster (flight CL2, md-parse-perf), so a change to the parser's speed that
/// changes what it produces shows here. Set <c>DIR_LIB_UPDATE_MD_GOLDEN=1</c> to rewrite the goldens in
/// the source tree, and only for a change that is meant to alter the AST.
/// </summary>
public sealed class MarkdownCorpusGoldenTests
{
    private static readonly bool Update = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_MD_GOLDEN") == "1";

    public static TheoryData<string> DocumentNames()
    {
        var data = new TheoryData<string>();
        foreach (var (name, _) in MarkdownCorpus.AllDocuments()) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void ParsedAst_MatchesGolden(string name)
    {
        var text = MarkdownCorpus.AllDocuments().Single(d => d.Name == name).Text;
        var dump = MdAstDump.Dump(new MarkdownBlockVisitor().Parse(text));
        var file = name.Replace('/', '_') + ".ast.txt";

        if (Update)
        {
            var sourceDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "MarkdownCorpus", "golden");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, file), dump, new UTF8Encoding(false));
            return;
        }

        var golden = File.ReadAllText(Path.Combine(MarkdownCorpus.Directory, "golden", file), Encoding.UTF8);
        dump.ShouldBe(golden, $"parsed AST of '{name}' differs from its golden");
    }

    [Fact]
    public void Corpus_HasEveryDocument()
    {
        MarkdownCorpus.RealDocuments.Length.ShouldBe(3);
        MarkdownCorpus.Formulas().Count.ShouldBe(17);
        MarkdownCorpus.AllDocuments().Count.ShouldBe(3 + 2 * 17);
    }
}
