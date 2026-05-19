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
}
