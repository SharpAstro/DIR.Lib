using System.Linq;
using DIR.Lib.Markdown;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Phase C: validate the LALR.CC markdown-block grammar + the visitor's
/// per-block classification. Each test checks both the block-level
/// structure (count + types) and a representative inline-content
/// assertion to confirm the inline sub-parser is wired in.
/// </summary>
public sealed class MarkdownBlockSpikeTests
{
    private readonly MarkdownBlockVisitor _visitor = new();

    // ── Paragraphs ────────────────────────────────────────────────────

    [Fact]
    public void SingleParagraph_OneBlock()
    {
        var blocks = _visitor.Parse("Hello world");
        blocks.Count.ShouldBe(1);
        var para = blocks[0].ShouldBeOfType<MdParagraph>();
        para.Content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("Hello world");
    }

    [Fact]
    public void TwoParagraphs_SplitByBlankLine()
    {
        var blocks = _visitor.Parse("Para 1\n\nPara 2");
        blocks.Count.ShouldBe(2);
        blocks[0].ShouldBeOfType<MdParagraph>();
        blocks[1].ShouldBeOfType<MdParagraph>();
    }

    [Fact]
    public void MultiLineParagraph_OneBlock()
    {
        // Lines with no blank between them belong to the same paragraph.
        // The inline grammar's `soft_break` token captures the newline.
        var blocks = _visitor.Parse("Line 1\nLine 2");
        blocks.Count.ShouldBe(1);
        var para = blocks[0].ShouldBeOfType<MdParagraph>();
        para.Content.OfType<MdLineBreak>().Single().Hard.ShouldBeFalse();
    }

    // ── Headings ──────────────────────────────────────────────────────

    [Fact]
    public void Heading_LevelFromHashCount()
    {
        var blocks = _visitor.Parse("# Top\n\n## Mid\n\n### Deep");
        blocks.Count.ShouldBe(3);
        blocks[0].ShouldBeOfType<MdHeading>().Level.ShouldBe(1);
        blocks[1].ShouldBeOfType<MdHeading>().Level.ShouldBe(2);
        blocks[2].ShouldBeOfType<MdHeading>().Level.ShouldBe(3);
    }

    [Fact]
    public void Heading_ContentParsedAsInline()
    {
        // The heading's text is run through the inline visitor, so
        // emphasis / code / math all work inside a heading.
        var blocks = _visitor.Parse("# **Bold** title");
        var h = blocks[0].ShouldBeOfType<MdHeading>();
        h.Content.OfType<MdEmphasis>().Single().Level.ShouldBe(2);
    }

    // ── Thematic breaks ──────────────────────────────────────────────

    [Fact]
    public void ThematicBreak_DashOnlyLine()
    {
        var blocks = _visitor.Parse("Above\n\n---\n\nBelow");
        blocks.Count.ShouldBe(3);
        blocks[1].ShouldBeOfType<MdThematicBreak>();
    }

    [Fact]
    public void ThematicBreak_AsteriskOnlyLine()
    {
        var blocks = _visitor.Parse("***");
        blocks[0].ShouldBeOfType<MdThematicBreak>();
    }

    // ── Display math blocks ──────────────────────────────────────────

    [Fact]
    public void MathBlock_DollarFence()
    {
        var blocks = _visitor.Parse("$$\nE = mc^2\n$$");
        var math = blocks[0].ShouldBeOfType<MdMathBlock>();
        math.Source.ShouldBe("E = mc^2");
        math.Unicode.ShouldContain("mc²");
    }

    [Fact]
    public void MathBlock_LatexBracket()
    {
        var blocks = _visitor.Parse("\\[\nE = mc^2\n\\]");
        blocks[0].ShouldBeOfType<MdMathBlock>().Source.ShouldBe("E = mc^2");
    }

    // ── Fenced code blocks ───────────────────────────────────────────

    [Fact]
    public void CodeFence_CapturesBodyAndLang()
    {
        var blocks = _visitor.Parse("```csharp\nvar x = 1;\n```");
        var fence = blocks[0].ShouldBeOfType<MdCodeFence>();
        fence.Lang.ShouldBe("csharp");
        fence.Lines.Count.ShouldBe(1);
        fence.Lines[0].ShouldBe("var x = 1;");
    }

    [Fact]
    public void CodeFence_NoLang_LangIsNull()
    {
        var blocks = _visitor.Parse("```\nplain text\n```");
        blocks[0].ShouldBeOfType<MdCodeFence>().Lang.ShouldBeNull();
    }

    [Fact]
    public void CodeFence_WithBlankLines_StaysOneBlock()
    {
        // Blank lines inside a fence are code body, not block separators
        // (CommonMark). Regression: the grouper used to split here, which
        // both shredded the block and orphaned the closing fence into a
        // paragraph the inline lexer rejected.
        var blocks = _visitor.Parse("```csharp\nvar a = 1;\n\nvar b = 2;\n```");
        blocks.Count.ShouldBe(1);
        var fence = blocks[0].ShouldBeOfType<MdCodeFence>();
        fence.Lang.ShouldBe("csharp");
        fence.Lines.ShouldBe(new[] { "var a = 1;", "", "var b = 2;" });
    }

    [Fact]
    public void CodeFence_Unterminated_RunsToEnd()
    {
        // An unterminated fence captures the rest of the input as body
        // rather than spilling later lines into mis-parsed blocks.
        var blocks = _visitor.Parse("```\ncode\n\nmore code");
        blocks.Count.ShouldBe(1);
        blocks[0].ShouldBeOfType<MdCodeFence>().Lines.ShouldBe(new[] { "code", "", "more code" });
    }

    [Fact]
    public void MalformedBlock_DegradesToRaw_AndResyncs()
    {
        // A block whose classification throws must not abort the document: it
        // degrades to raw text, and the blank-line sync point lets the next
        // block parse normally. No ordinary input makes the inline parser throw
        // any more (this used a stray ```, then a trailing backslash), so the
        // test seam's inline parser throws on the first block. The raw text
        // keeps its line as a soft break, so it reflows.
        var inline = new MarkdownInlineVisitor();
        var visitor = new MarkdownBlockVisitor(text =>
            text.StartsWith("some text") ? throw new System.InvalidOperationException("forced") : inline.Parse(text));
        var blocks = visitor.Parse("some text;\nmore\n\nafter");
        blocks.Count.ShouldBe(2);
        var content = blocks[0].ShouldBeOfType<MdParagraph>().Content;
        content.Count.ShouldBe(3);
        content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("some text;");
        content[1].ShouldBeOfType<MdLineBreak>().Hard.ShouldBeFalse();
        content[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe("more");
        blocks[1].ShouldBeOfType<MdParagraph>()
            .Content.OfType<MdLiteral>().Single().Text.ShouldBe("after");
    }

    // ── Lists ────────────────────────────────────────────────────────

    [Fact]
    public void UnorderedList_Dashes()
    {
        var blocks = _visitor.Parse("- one\n- two\n- three");
        var list = blocks[0].ShouldBeOfType<MdList>();
        list.Ordered.ShouldBeFalse();
        list.Items.Count.ShouldBe(3);
        list.Items[0].Body.OfType<MdParagraph>().Single()
            .Content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("one");
    }

    [Fact]
    public void OrderedList_NumberedMarkers()
    {
        var blocks = _visitor.Parse("1. first\n2. second\n3. third");
        var list = blocks[0].ShouldBeOfType<MdList>();
        list.Ordered.ShouldBeTrue();
        list.OrderedStart.ShouldBe(1);
        list.Items.Count.ShouldBe(3);
    }

    // ── Tables ───────────────────────────────────────────────────────

    [Fact]
    public void Table_BasicWithAlignment()
    {
        var md = "| H1 | H2 | H3 |\n|:---|:---:|---:|\n| a | b | c |";
        var blocks = _visitor.Parse(md);
        var table = blocks[0].ShouldBeOfType<MdTable>();
        table.Headers.Count.ShouldBe(3);
        table.Alignments[0].ShouldBe(MdTableAlignment.Left);
        table.Alignments[1].ShouldBe(MdTableAlignment.Center);
        table.Alignments[2].ShouldBe(MdTableAlignment.Right);
        table.Rows.Count.ShouldBe(1);
    }

    // ── Mixed document ───────────────────────────────────────────────

    [Fact]
    public void MixedDocument_PreservesOrderAndTypes()
    {
        var md = "# Title\n\nSome prose with **bold**.\n\n---\n\n- item 1\n- item 2\n\n$$\nx^2\n$$";
        var blocks = _visitor.Parse(md);
        blocks.Select(b => b.GetType().Name).ShouldBe(new[]
        {
            nameof(MdHeading),
            nameof(MdParagraph),
            nameof(MdThematicBreak),
            nameof(MdList),
            nameof(MdMathBlock),
        });
    }

    // ── Failure modes ────────────────────────────────────────────────

    [Fact]
    public void EmptyInput_ProducesEmptyList()
    {
        _visitor.Parse(string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void BlankLinesOnly_ProducesEmptyList()
    {
        _visitor.Parse("\n\n\n").ShouldBeEmpty();
    }

    // ── Line endings ─────────────────────────────────────────────────
    //
    // `\r\n`, a lone `\r` and `\n` all end a line, as in CommonMark. A lone
    // `\r` used to match no block lexer rule, so Parse threw LexerException.

    [Theory]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void LineEndings_ParseLikeTheirLfTwin(string newline)
    {
        const string lf = "# Head\n\npara one\nline two\n\n- a\n- b\n\nlast";
        Describe(_visitor.Parse(lf)).ShouldBe("H1[L(Head)] | P[L(para one) SB L(line two)] | List[P[L(a)] ; P[L(b)]] | P[L(last)]");
        Describe(_visitor.Parse(lf.Replace("\n", newline))).ShouldBe(Describe(_visitor.Parse(lf)));
    }

    [Fact]
    public void Parse_NeverThrows_OnRandomDocuments()
    {
        string[] alphabet =
        [
            "[", "]", "(", ")", "{", "}", "!", "$", "\\", "`", "*", " ", "a", "5", "_",
            @"\boxed{", @"\text{", @"\(", @"\)", @"\[", @"\]", "$$", "``", "```", "](", "]{", "![", @"\$",
            "#", "# ", "-", "- ", "1. ", ">", "> ", "|", "| ", "|---|", "\n", "\r", "\r\n", "\n\n", "\r\r",
        ];
        var rng = new System.Random(20261017);
        for (int n = 0; n < 2000; n++)
        {
            var source = string.Concat(Enumerable.Range(0, rng.Next(1, 25)).Select(_ => alphabet[rng.Next(alphabet.Length)]));
            Should.NotThrow(() => _visitor.Parse(source), $"source: [{source.Replace("\r", "\\r").Replace("\n", "\\n")}]");
        }
    }

    private static string Describe(System.Collections.Generic.IEnumerable<MdBlock> blocks) =>
        string.Join(" | ", blocks.Select(Describe));

    private static string Describe(MdBlock block) => block switch
    {
        MdParagraph p => $"P[{Describe(p.Content)}]",
        MdHeading h => $"H{h.Level}[{Describe(h.Content)}]",
        MdList l => $"List[{string.Join(" ; ", l.Items.Select(i => string.Join(" ", i.Body.Select(Describe))))}]",
        _ => block.GetType().Name,
    };

    private static string Describe(System.Collections.Generic.IEnumerable<MdInline> spans) =>
        string.Join(" ", spans.Select(s => s switch
        {
            MdLiteral l => $"L({l.Text})",
            MdLineBreak b => b.Hard ? "HB" : "SB",
            _ => s.GetType().Name,
        }));
}
