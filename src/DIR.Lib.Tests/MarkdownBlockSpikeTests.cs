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

    // ── Interruption (CommonMark 4.2, 4.3, 4.5, 5.2, 5.3) ─────────────
    //
    // A heading, a fence, a display-math opener, a thematic break and a list
    // start on the line after paragraph text, with no blank line between; only
    // an ordered list starting at 1 may interrupt a paragraph, so prose that
    // happens to begin "14." stays prose. GitHub's renderer and VS Code's
    // preview (markdown-it) agree on every case here; the visitor used to split
    // blocks at blank lines only, and so folded each of them into the paragraph.

    [Theory]
    [InlineData("# Head\ntext", "H1[L(Head)] | P[L(text)]")]
    [InlineData("text\n# Head", "P[L(text)] | H1[L(Head)]")]
    [InlineData("# One\n## Two\ntext", "H1[L(One)] | H2[L(Two)] | P[L(text)]")]
    [InlineData("Where:\n* one\n* two", "P[L(Where:)] | List[P[L(one)] ; P[L(two)]]")]
    [InlineData("Where:\n- one", "P[L(Where:)] | List[P[L(one)]]")]
    [InlineData("Lead:\n1. one\n2. two", "P[L(Lead:)] | List[P[L(one)] ; P[L(two)]]")]
    [InlineData("Lead:\n1) one", "P[L(Lead:)] | List[P[L(one)]]")]
    [InlineData("text\n***", "P[L(text)] | MdThematicBreak")]
    [InlineData("text\n```\ncode\n```", "P[L(text)] | MdCodeFence")]
    [InlineData("### Usage\n```bash\npython x\n```", "H3[L(Usage)] | MdCodeFence")]
    [InlineData("Before:\n$$\nx^2\n$$", "P[L(Before:)] | MdMathBlock")]
    [InlineData("- a\n# H", "List[P[L(a)]] | H1[L(H)]")]
    [InlineData("- a\n```\ncode\n```", "List[P[L(a)]] | MdCodeFence")]
    public void ABlockStart_InterruptsTheBlockBefore(string md, string expected) =>
        Describe(_visitor.Parse(md)).ShouldBe(expected);

    [Theory]
    [InlineData("The number of windows in my house is\n14.  The number of doors is 6.")]
    [InlineData("In the year\n2. it rained")]
    [InlineData("Not a heading\n#hashtag")]
    public void AnOrderedListNotStartingAtOne_DoesNotInterruptAParagraph(string md)
    {
        var blocks = _visitor.Parse(md);
        blocks.Count.ShouldBe(1);
        blocks[0].ShouldBeOfType<MdParagraph>();
    }

    [Theory]
    [InlineData("Title\n=====", "H1[L(Title)]")]
    [InlineData("Title\n---", "H2[L(Title)]")]
    [InlineData("Two\nlines\n===", "H1[L(Two) SB L(lines)]")]
    public void ASetextUnderline_MakesTheParagraphAHeading(string md, string expected) =>
        Describe(_visitor.Parse(md)).ShouldBe(expected);

    [Theory]
    [InlineData("# C#", "H1[L(C#)]")]
    [InlineData("# Title ##", "H1[L(Title)]")]
    [InlineData("   ## Indented", "H2[L(Indented)]")]
    public void AHeading_DropsOnlyAClosingSequenceAfterASpace(string md, string expected) =>
        Describe(_visitor.Parse(md)).ShouldBe(expected);

    // ── List items (CommonMark 5.2) ──────────────────────────────────
    //
    // An item's content starts at the column after its marker and the spaces
    // that follow it, so `*   text` is continued by a line indented four. A
    // line that is less indented but is not a block start is a lazy
    // continuation of the item's paragraph. Both used to be wrong: a fixed two
    // columns were stripped, a lazy line was dropped, and an item's second line
    // became a paragraph of its own, which a renderer cannot reflow.

    [Theory]
    [InlineData("- one\n  two", "List[P[L(one) SB L(two)]]")]
    [InlineData("- a\nlazy", "List[P[L(a) SB L(lazy)]]")]
    [InlineData("*   top\n    1.  first\n    2.  second", "List[P[L(top)] List[P[L(first)] ; P[L(second)]]]")]
    [InlineData("* top\n  - a\n  - b", "List[P[L(top)] List[P[L(a)] ; P[L(b)]]]")]
    [InlineData("4. last:\n   - a\n   - b", "List[P[L(last:)] List[P[L(a)] ; P[L(b)]]]")]
    [InlineData("- a\n\n- b", "List[P[L(a)] ; P[L(b)]]")]
    [InlineData("- a\n1. b", "List[P[L(a)]] | List[P[L(b)]]")]
    public void AListItem_TakesItsLinesByTheMarkersContentColumn(string md, string expected) =>
        Describe(_visitor.Parse(md)).ShouldBe(expected);

    [Fact]
    public void ADisplayMathLineInsideAnItem_LeavesTheNextItemAnItem()
    {
        // StarNet2_Architecture.md's shape: a bullet whose nested ordered items
        // each end in a display-math line, item 3's swallowed 4 and 5.
        var md = "*   **Workflow:**\n" +
                 "    1.  **Analyze:** median.\n" +
                 "    2.  **Shadows:** as:\n" +
                 "        $$S = m$$\n" +
                 "    3.  **Stretch:** solve:\n" +
                 "        $$f = x$$\n" +
                 "    4.  **Inverse:** back.\n" +
                 "    5.  **Equalize:** each.";
        var outer = _visitor.Parse(md).Single().ShouldBeOfType<MdList>();
        var inner = outer.Items.Single().Body.OfType<MdList>().Single();
        inner.Ordered.ShouldBeTrue();
        inner.Items.Count.ShouldBe(5);
    }

    // ── Round 1 of PR #129 ───────────────────────────────────────────

    /// <summary>A table header interrupts a paragraph, inside a list item as at
    /// the top, as GitHub and VS Code read it (B1: the rows were the item's prose).</summary>
    [Theory]
    [InlineData("- a\n  | x | y |\n  |---|---|\n  | 1 | 2 |", "List[P[L(a)] MdTable]")]
    [InlineData("1. Configure:\n   | key | value |\n   |-----|-------|\n   | a | b |", "List[P[L(Configure:)] MdTable]")]
    [InlineData("Intro:\n| x | y |\n|---|---|\n| 1 | 2 |", "P[L(Intro:)] | MdTable")]
    public void ATableHeader_InterruptsAParagraph(string md, string expected) =>
        Describe(_visitor.Parse(md)).ShouldBe(expected);

    /// <summary>
    /// A fence too little indented to belong to a list item ends the list and
    /// runs to its own closer, blank lines and all (B2: the list's blank-line
    /// rule cut it at the blank, and code and prose swapped places from there
    /// to the end of the document).
    /// </summary>
    [Fact]
    public void AnUnderIndentedFenceAfterAnItem_StaysOneFence()
    {
        var blocks = _visitor.Parse("10. Run:\n   ```\n   a\n\nb\n   ```\n\nafter\n\n```\ncode\n```");
        Describe(blocks).ShouldBe("List[P[L(Run:)]] | MdCodeFence | P[L(after)] | MdCodeFence");
        blocks[1].ShouldBeOfType<MdCodeFence>().Lines.Select(l => l.Trim()).ShouldBe(new[] { "a", "", "b" });
        blocks[3].ShouldBeOfType<MdCodeFence>().Lines.ShouldBe(new[] { "code" });
    }

    /// <summary>Every blank line inside a fence in a list item stays (it kept one).</summary>
    [Fact]
    public void BlankLinesInsideAFenceInAnItem_AllStay()
    {
        var item = _visitor.Parse("- ```\n  b\n\n\n  ```").Single().ShouldBeOfType<MdList>().Items.Single();
        item.Body.Single().ShouldBeOfType<MdCodeFence>().Lines.ShouldBe(new[] { "b", "", "" });
    }

    /// <summary>
    /// A long document of lists that keep breaking parses in a flat loop (B4:
    /// each break recursed, and 2,500 alternating bullets overflowed a 1 MB
    /// stack, which no catch survives). A 256 KB stack makes the old recursion
    /// overflow at a size the suite can afford to parse.
    /// </summary>
    [Fact]
    public void ManyListBreaks_DoNotRecurse()
    {
        // Alternating bullets: each line a list of its own. Notes indented two,
        // short of "N. "'s content column, after a blank line: each item a list,
        // each note a paragraph.
        var bullets = string.Join("\n", Enumerable.Range(0, 1500).Select(i => i % 2 == 0 ? "- a" : "* b"));
        var notes = string.Join("\n\n", Enumerable.Range(1, 800).Select(i => $"{i}. step\n\n  note"));
        System.Collections.Generic.IReadOnlyList<MdBlock>? fromBullets = null, fromNotes = null;
        var thread = new System.Threading.Thread(() =>
        {
            fromBullets = new MarkdownBlockVisitor().Parse(bullets);
            fromNotes = new MarkdownBlockVisitor().Parse(notes);
        }, maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();
        fromBullets.ShouldNotBeNull().Count.ShouldBe(1500);
        fromNotes.ShouldNotBeNull().Count.ShouldBe(1600);
    }

    /// <summary>A list item can begin with at most one blank line (CommonMark
    /// 5.2): an empty marker and a blank line make an empty item.</summary>
    [Fact]
    public void AnEmptyItemThenABlankLine_IsEmpty() =>
        Describe(_visitor.Parse("-\n\n  foo")).ShouldBe("List[] | P[L(foo)]");

    /// <summary>A tilde fence's info string may hold a backtick (CommonMark example 146).</summary>
    [Fact]
    public void ATildeFencesInfoString_MayHoldABacktick() =>
        _visitor.Parse("~~~ aa ``` ~~~\nfoo\n~~~").Single().ShouldBeOfType<MdCodeFence>().Lines.ShouldBe(new[] { "foo" });

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
        for (int n = 0; n < 200; n++)   // a document costs its blocks' inline parses, a few ms each
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
        MdList l => $"List[{string.Join(" ; ", l.Items.Select(i => string.Join(" ", i.Body.Select(Describe))))}]",        _ => block.GetType().Name,
    };

    private static string Describe(System.Collections.Generic.IEnumerable<MdInline> spans) =>
        string.Join(" ", spans.Select(s => s switch
        {
            MdLiteral l => $"L({l.Text})",
            MdLineBreak b => b.Hard ? "HB" : "SB",
            _ => s.GetType().Name,
        }));
}
