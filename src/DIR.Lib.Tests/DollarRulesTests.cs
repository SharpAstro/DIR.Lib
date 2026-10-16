using System.Collections.Generic;
using System.Linq;
using DIR.Lib.Markdown;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// A <c>$</c> opens or closes math only by Pandoc's <c>tex_math_dollars</c> rule, and a math
/// delimiter that does not pair renders as literal text. Driven through the block parser, so a
/// paragraph that vanishes (the failure these pin) shows up as a missing block rather than as an
/// empty span list a caller could mistake for empty input.
/// <para>The rule: anything between two <c>$</c> is inline math when the opener has a non-space
/// character to its right, and the closer has a non-space character to its left and no digit to its
/// right. <c>$$..$$</c> is display math. <c>\$</c> is a literal dollar.</para>
/// </summary>
public sealed class DollarRulesTests
{
    private readonly MarkdownBlockVisitor _visitor = new();

    private IReadOnlyList<MdInline> Paragraph(string source) =>
        _visitor.Parse(source).ShouldHaveSingleItem().ShouldBeOfType<MdParagraph>().Content;

    private void ShouldRenderLiterally(string source, string expected)
    {
        var content = Paragraph(source);
        content.OfType<MdMathInline>().ShouldBeEmpty();
        content.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe(expected);
    }

    // ── Prose dollars are not math ───────────────────────────────────

    [Theory]
    [InlineData("It costs $5 today.")]
    [InlineData("Price: 5$ only.")]
    [InlineData("It costs $5 and $10 today.")]
    [InlineData("$20,000 and $30,000")]
    public void ProseDollars_KeepTheParagraphAndTheirDollarSigns(string source) =>
        ShouldRenderLiterally(source, source);

    // ── Math that still pairs ────────────────────────────────────────

    [Fact]
    public void LoneSpan_IsMath()
    {
        var math = Paragraph("$x$").ShouldHaveSingleItem().ShouldBeOfType<MdMathInline>();
        math.Source.ShouldBe("x");
        math.IsDisplay.ShouldBeFalse();
    }

    [Fact]
    public void SpanInProse_IsMath()
    {
        var content = Paragraph("a $x$ b");
        content.Count.ShouldBe(3);
        content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("a ");
        content[1].ShouldBeOfType<MdMathInline>().Source.ShouldBe("x");
        content[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" b");
    }

    [Fact]
    public void SpanBeforePunctuation_IsMath()
    {
        var content = Paragraph("$x^2$.");
        content.Count.ShouldBe(2);
        var math = content[0].ShouldBeOfType<MdMathInline>();
        math.Source.ShouldBe("x^2");
        math.Unicode.ShouldContain("²");
        content[1].ShouldBeOfType<MdLiteral>().Text.ShouldBe(".");
    }

    [Fact]
    public void CloserBeforeComma_IsMath()
    {
        var content = Paragraph("$x$,");
        content.Count.ShouldBe(2);
        content[0].ShouldBeOfType<MdMathInline>().Source.ShouldBe("x");
        content[1].ShouldBeOfType<MdLiteral>().Text.ShouldBe(",");
    }

    [Fact]
    public void DoubleDollar_IsDisplayMath()
    {
        var math = Paragraph("$$x$$").ShouldHaveSingleItem().ShouldBeOfType<MdMathInline>();
        math.Source.ShouldBe("x");
        math.IsDisplay.ShouldBeTrue();
    }

    [Fact]
    public void LoneDollarInDisplayMath_IsBody()
    {
        // `$$` closes at the next `$$`; a single `$` before it is part of the body.
        var math = Paragraph("$$a$b$$").ShouldHaveSingleItem().ShouldBeOfType<MdMathInline>();
        math.Source.ShouldBe("a$b");
        math.IsDisplay.ShouldBeTrue();
    }

    [Fact]
    public void DisplayMath_ClosesAtTheNextDoubleDollar()
    {
        var content = Paragraph("$$a$$$");
        content.Count.ShouldBe(2);
        content[0].ShouldBeOfType<MdMathInline>().Source.ShouldBe("a");
        content[1].ShouldBeOfType<MdLiteral>().Text.ShouldBe("$");
    }

    [Fact]
    public void BracketDisplay_InProse_StaysDisplay()
    {
        var math = Paragraph(@"so \[x\] then").OfType<MdMathInline>().ShouldHaveSingleItem();
        math.Source.ShouldBe("x");
        math.IsDisplay.ShouldBeTrue();
    }

    [Fact]
    public void MathBlock_BodyWithEdgeSpaces_StillRenders()
    {
        // The block visitor used to render a $$ block's body by wrapping it in `$..$` and
        // reparsing. Under the dollar rule a body with leading or trailing spaces would no longer
        // pair, and the block would fall back to its raw source.
        var block = _visitor.Parse("$$\n  x^2  \n$$").ShouldHaveSingleItem().ShouldBeOfType<MdMathBlock>();
        block.Unicode.ShouldContain("²");
    }

    // ── Spans the rule rejects ───────────────────────────────────────

    [Theory]
    [InlineData("$ x$")]   // opener followed by a space
    [InlineData("$x $")]   // closer preceded by a space
    [InlineData("$x$5")]   // closer followed by a digit
    public void RejectedSpans_RenderLiterally(string source) =>
        ShouldRenderLiterally(source, source);

    [Fact]
    public void EscapedDollar_IsALiteralDollar() =>
        ShouldRenderLiterally(@"\$5", "$5");

    // ── An escaped dollar inside math is body, not a closer ──────────

    [Fact]
    public void EscapedDollarInInlineMath_DoesNotClose()
    {
        // The `\$` must not close the span. Taken as the closer, a digit follows it, so the
        // opener would not pair at all and the paragraph would lose its math.
        var content = Paragraph(@"$x = \$5$ each");
        content.Count.ShouldBe(2);
        content[0].ShouldBeOfType<MdMathInline>().Source.ShouldBe(@"x = \$5");
        content[1].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" each");
    }

    [Fact]
    public void EscapedDollarOpeningInlineMath_IsBody()
    {
        var content = Paragraph(@"$\$42$.");
        content.Count.ShouldBe(2);
        content[0].ShouldBeOfType<MdMathInline>().Source.ShouldBe(@"\$42");
        content[1].ShouldBeOfType<MdLiteral>().Text.ShouldBe(".");
    }

    [Fact]
    public void EscapedDollarMidInlineMath_IsBody()
    {
        var math = Paragraph(@"$a\$b$").ShouldHaveSingleItem().ShouldBeOfType<MdMathInline>();
        math.Source.ShouldBe(@"a\$b");
    }

    [Fact]
    public void EscapedSpaceBeforeTheCloser_StillCloses()
    {
        // Pandoc reads `\ ` as one escaped item, so the closer has no bare space to its left.
        Paragraph(@"$x\ $").ShouldHaveSingleItem().ShouldBeOfType<MdMathInline>().Source.ShouldBe(@"x\ ");
    }

    [Fact]
    public void EscapedDollarInDisplayMath_DoesNotClose()
    {
        var math = Paragraph(@"$$a = \$5$$").ShouldHaveSingleItem().ShouldBeOfType<MdMathInline>();
        math.Source.ShouldBe(@"a = \$5");
        math.IsDisplay.ShouldBeTrue();
    }

    // ── Unmatched delimiters render as text ──────────────────────────

    [Theory]
    [InlineData("text $unterminated")]
    [InlineData("text $$unterminated")]
    [InlineData(@"text \(unterminated")]
    [InlineData(@"text \[unterminated")]
    [InlineData(@"text \boxed{unterminated")]
    public void UnmatchedOpeners_RenderAsText(string source) =>
        ShouldRenderLiterally(source, source);

    [Fact]
    public void UnmatchedDoubleDollar_KeepsAnEarlierSpan()
    {
        var content = Paragraph("$a$ and $$x");
        content.Count.ShouldBe(2);
        var math = content[0].ShouldBeOfType<MdMathInline>();
        math.Source.ShouldBe("a");
        math.IsDisplay.ShouldBeFalse();
        content[1].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" and $$x");
    }

    [Fact]
    public void UnmatchedParen_KeepsALaterSpan()
    {
        var content = Paragraph(@"\(a and $b$");
        content.Count.ShouldBe(2);
        content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe(@"\(a and ");
        content[1].ShouldBeOfType<MdMathInline>().Source.ShouldBe("b");
    }

    [Fact]
    public void UnmatchedDollar_InAListItem_KeepsTheItem()
    {
        var list = _visitor.Parse("- costs $5\n- free").ShouldHaveSingleItem().ShouldBeOfType<MdList>();
        list.Items.Count.ShouldBe(2);
        list.Items[0].Body.ShouldHaveSingleItem().ShouldBeOfType<MdParagraph>()
            .Content.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("costs $5");
    }

    // ── Unmatched brackets and backticks are text ────────────────────
    //
    // A `[` or `]` with no partner, and a backtick run with no closing run, are literal (as in
    // CommonMark and Pandoc), the way an unpaired math opener is. They used to fail the inline
    // parse and send the whole block to the literal fallback, which lost its emphasis, code and math.

    [Theory]
    [InlineData("a] with *em*, `code` and $x$", "L(a] with ) E1[L(em)] L(, ) C(code) L( and ) M(x)")]
    [InlineData("[a with *em*, `code` and $x$", "L([a with ) E1[L(em)] L(, ) C(code) L( and ) M(x)")]
    [InlineData("*em*, `code` and $x$ cost a ` tick", "E1[L(em)] L(, ) C(code) L( and ) M(x) L( cost a ` tick)")]
    [InlineData("*em*, `code` and $x$ cost ``two", "E1[L(em)] L(, ) C(code) L( and ) M(x) L( cost ``two)")]
    [InlineData("[a [b] with *em*, `code` and $x$", "L([a [b] with ) E1[L(em)] L(, ) C(code) L( and ) M(x)")]
    public void UnmatchedBracketOrBacktick_IsText_AndTheRestStillParses(string source, string expected) =>
        Dump(Paragraph(source)).ShouldBe(expected);

    [Fact]
    public void BracketAfterMathTookItsPartner_IsText() =>
        Dump(Paragraph("$a [b$ c]")).ShouldBe("M(a [b) L( c])");

    [Fact]
    public void BracketInALabelAfterMathTookItsPartner_IsText() =>
        Dump(Paragraph("[$a [b$ c] d](u)")).ShouldBe("Link[M(a [b) L( c] d)](u)");

    [Theory]
    [InlineData("$5 for a [discount and $10")]
    [InlineData("Prices: $5 for [0, 1) and $10 otherwise.")]
    [InlineData("$a ] $")]
    [InlineData("costs $5 (the ` key) and $10")]
    public void LoneBracketOrBacktick_BetweenProseDollars_IsText(string source) =>
        new MarkdownInlineVisitor().Parse(source).ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe(source);

    // ── A label walk stays inside its bound ──────────────────────────
    //
    // Inside a label, math can take a `[` that LabelEnd counted. The `]` of that bracket's tail
    // is then text, and so is the URL after it, which the label walk goes on to read. A code span
    // or a tail there that ends past the label's own `]` would carry the walk past it, out of step
    // with the lexer; here the backtick at 6 would close at the one inside the label's URL. So
    // inside a label such a code span is an unclosed fence, and such a tail never closes.

    [Theory]
    [InlineData("[$[$](`x) y](u`v``w) `z`", "Link[M([) L(](`x) y)](u`v``w) L( ) C(z)")]
    [InlineData("[$[$](`x) y](u`v\r) z", "Link[M([) L(](`x) y)](u`v\r) L( z)")]
    [InlineData("*em* [$[$](`x) y](u) z`w``", "E1[L(em)] L( ) Link[M([) L(](`x) y)](u) L( z) C(w) L(`)")]
    public void LabelWalk_StaysInsideItsBound(string source, string expected) =>
        Dump(new MarkdownInlineVisitor().Parse(source)).ShouldBe(expected);

    [Fact]
    public void LabelWalk_StaysInsideItsBound_InATableCell()
    {
        var blocks = ParseCountingFallbacks("| [$[$](`x) y](u`v``w) `z` | b |\n|---|---|\n| x | y |", out int fallbacks);
        fallbacks.ShouldBe(0);
        var table = blocks.ShouldHaveSingleItem().ShouldBeOfType<MdTable>();
        Dump(table.Headers[0]).ShouldBe("Link[M([) L(](`x) y)](u`v``w) L( ) C(z)");
    }

    // ── A link or colour tail that never closes is text ──────────────

    [Theory]
    [InlineData("[a](b", "L([a](b)")]
    [InlineData("**x** [a](b *y*", "E2[L(x)] L( [a](b ) E1[L(y)]")]
    [InlineData("[a](b) [c](d", "Link[L(a)](b) L( [c](d)")]
    [InlineData("[a]{red *y*", "L([a]{red ) E1[L(y)]")]
    public void UnclosedTail_IsText_AndTheRestStillParses(string source, string expected)
    {
        var blocks = ParseCountingFallbacks(source, out int fallbacks);
        fallbacks.ShouldBe(0);
        Dump(blocks.ShouldHaveSingleItem().ShouldBeOfType<MdParagraph>().Content).ShouldBe(expected);
    }

    // ── An empty tail parses ─────────────────────────────────────────
    //
    // `[a]()` is a link with an empty URL, as in CommonMark, and `![a]()` an image with an empty
    // source (a renderer that cannot resolve it shows the alt). `[a]{}` names no colour, so it is
    // the label itself, which is how an unknown colour renders too; `[]{}` has no label either,
    // so it stays its own text rather than vanishing.

    [Theory]
    [InlineData("[a]()", "Link[L(a)]()")]
    [InlineData("see [*a*]() now", "L(see ) Link[E1[L(a)]]() L( now)")]
    [InlineData("![a]()", "Img[L(a)]()")]
    [InlineData("[a]{}", "L(a)")]
    [InlineData("see [*a*]{} now", "L(see ) E1[L(a)] L( now)")]
    [InlineData("*x [a*]{}", "L(*x a*)")]   // the label's star does not pair with one outside it
    [InlineData("[a](b) [c]() [d]{red}", "Link[L(a)](b) L( ) Link[L(c)]() L( ) Col[L(d)]{red}")]
    [InlineData("[]()", "Link[]()")]   // CommonMark's empty link
    [InlineData("[]{}", "L([]{})")]   // no label and no colour: nothing to show but its text
    [InlineData("see []{} now", "L(see []{} now)")]
    public void EmptyTail_Parses(string source, string expected)
    {
        var blocks = ParseCountingFallbacks(source, out int fallbacks);
        fallbacks.ShouldBe(0);
        Dump(blocks.ShouldHaveSingleItem().ShouldBeOfType<MdParagraph>().Content).ShouldBe(expected);
    }

    // ── An image opener without a link tail is text ──────────────────
    //
    // The grammar has an image only as `![alt](url)`. Any other `![` is a `!` before a bracket.

    [Theory]
    [InlineData("![a]", "L(![a])")]
    [InlineData("![a] *em*", "L(![a] ) E1[L(em)]")]
    [InlineData("![a]{red} x", "L(!) Col[L(a)]{red} L( x)")]
    [InlineData("![a](src)", "Img[L(a)](src)")]
    [InlineData("![a](src", "L(![a](src)")]
    [InlineData("![$a]$](src)", "Img[M(a])](src)")]   // the alt's `]` is inside math
    public void ImageOpener_WithoutALinkTail_IsText(string source, string expected)
    {
        var blocks = ParseCountingFallbacks(source, out int fallbacks);
        fallbacks.ShouldBe(0);
        Dump(blocks.ShouldHaveSingleItem().ShouldBeOfType<MdParagraph>().Content).ShouldBe(expected);
    }

    // ── Parse never throws ───────────────────────────────────────────
    //
    // MarkdownInlineVisitor.Parse returns spans, or an empty list on a parse error; it does not
    // throw. Two inputs used to make the root lexer throw LexerException: a backslash at the very
    // end, and a carriage return with no line feed after it.

    [Theory]
    [InlineData(@"text\", @"L(text\)")]
    [InlineData(@"\", @"L(\)")]
    [InlineData(@"**b**\", @"E2[L(b)] L(\)")]
    [InlineData("a\rb", "L(a) SB L(b)")]   // a lone CR is a line ending, as in CommonMark
    public void InputTheLexerHasNoRuleFor_ParsesWithoutThrowing(string source, string expected) =>
        Dump(new MarkdownInlineVisitor().Parse(source)).ShouldBe(expected);

    [Theory]
    [InlineData("a\\\nb")]
    [InlineData("a\\\r\nb")]
    public void BackslashBeforeANewline_IsStillAHardBreak(string source) =>
        Dump(new MarkdownInlineVisitor().Parse(source)).ShouldBe("L(a) HB L(b)");

    [Fact]
    public void Parse_NeverThrows_OnDelimiterSoup()
    {
        string[] alphabet =
        [
            "[", "]", "(", ")", "{", "}", "!", "$", "\\", "`", "*", " ", "\n", "\r", "a", "5", "_", "|",
            @"\boxed{", @"\text{", @"\(", @"\)", @"\[", @"\]", "$$", "``", "](", "]{", "![", @"\$",
        ];
        var rng = new System.Random(20261017);
        var parser = new MarkdownInlineVisitor();
        for (int n = 0; n < 5000; n++)
        {
            var source = string.Concat(Enumerable.Range(0, rng.Next(1, 11)).Select(_ => alphabet[rng.Next(alphabet.Length)]));
            Should.NotThrow(() => parser.Parse(source), $"source: [{source.Replace("\r", "\\r").Replace("\n", "\\n")}]");
        }
    }

    [Fact]
    public void Parse_NeitherThrowsNorFails_OnNestedLabels()
    {
        // Built, not drawn letter by letter: labels inside labels, math holding brackets and
        // backticks, tails with no label, URLs holding backticks and CRs, and then one character
        // deleted at random. That is the shape that once carried a label walk past its bound and
        // out of step with the lexer, which a uniform draw almost never builds. Every input parses
        // to spans: none throws and none needs the block fallback.
        var rng = new System.Random(20261018);
        string Pick(params string[] items) => items[rng.Next(items.Length)];
        string Url() => string.Concat(Enumerable.Range(0, rng.Next(0, 5)).Select(_ => Pick("u", "`", "``", "\r", " ", "$", "[", "]", "\\")));
        string Gen(int depth) => string.Concat(Enumerable.Range(0, rng.Next(1, 5)).Select(_ => (rng.Next(0, 8), depth) switch
        {
            (2, < 3) => Pick("", "", "!") + "[" + Gen(depth + 1) + Pick("](", "](", "]{", "]") + Url() + Pick(")", ")", "}", ""),
            (1, _) => "$" + string.Concat(Enumerable.Range(0, rng.Next(1, 4)).Select(_ => Pick("[", "]", "x", "`", "(", ")"))) + "$",
            (3, _) => "`" + Pick("x", "$", "[", "]", ")") + "`",
            (4, _) => "](" + Url() + ")",
            (5, _) => Pick("[", "]", "`", "``", "$", "$$", "\r", "\\", ")", "(", "!["),
            (6, _) => Pick(@"\(", @"\)", @"\[", @"\]", @"\boxed{", "}", @"\text{"),
            (7, _) => "$$" + Pick("x", "[", "`") + "$$",
            _ => Pick("x", " ", "*", "**", "y z"),
        }));
        var parser = new MarkdownInlineVisitor();
        for (int n = 0; n < 4000; n++)
        {
            var source = Gen(0);
            if (rng.Next(3) == 0 && source.Length > 1) source = source.Remove(rng.Next(source.Length), 1);
            var shown = $"source: [{source.Replace("\r", "\\r").Replace("\n", "\\n")}]";
            Should.NotThrow(() => parser.Parse(source), shown).ShouldNotBeEmpty(shown);
        }
    }

    // ── No stray delimiter takes the fallback ────────────────────────
    //
    // Every ordinary stray delimiter is text, so no block holding one reaches either fallback (an
    // inline parse with no spans, or one that throws). Each goes into a paragraph next to emphasis,
    // and into a heading, a list item and a table cell, at the end of the text, where a trailing
    // backslash is.

    [Theory]
    [InlineData("[")]
    [InlineData("]")]
    [InlineData("`")]
    [InlineData("``")]
    [InlineData("[a](b")]
    [InlineData("[a]{red")]
    [InlineData("![a]")]
    [InlineData(@"\")]
    [InlineData("$")]
    [InlineData("$$")]
    [InlineData(@"\(")]
    [InlineData(@"\[")]
    [InlineData(@"\boxed{")]
    [InlineData("[a]()", "L(@ ) Link[L(a)]()")]
    [InlineData("![a]()", "L(@ ) Img[L(a)]()")]
    [InlineData("[a]{}", "L(@ a)")]
    [InlineData("[]{}")]
    public void StrayDelimiter_NeverTakesTheFallback(string stray, string? shown = null)
    {
        // `shown` is how the block renders after its leading word, which stands in for `@`; by
        // default the stray is plain text after it.
        shown ??= $"L(@ {stray})";
        var source = $"*em* then {stray}\n\n# head {stray}\n\n- item {stray}\n\n| cell {stray} | b |\n|---|---|\n| x | y |";
        var blocks = ParseCountingFallbacks(source, out int fallbacks);
        fallbacks.ShouldBe(0);
        blocks.Count.ShouldBe(4);
        Dump(blocks[0].ShouldBeOfType<MdParagraph>().Content).ShouldBe("E1[L(em)] " + shown.Replace("@", " then"));
        Dump(blocks[1].ShouldBeOfType<MdHeading>().Content).ShouldBe(shown.Replace("@", "head"));
        Dump(blocks[2].ShouldBeOfType<MdList>().Items.ShouldHaveSingleItem().Body.ShouldHaveSingleItem()
            .ShouldBeOfType<MdParagraph>().Content).ShouldBe(shown.Replace("@", "item"));
        Dump(blocks[3].ShouldBeOfType<MdTable>().Headers[0]).ShouldBe(shown.Replace("@", "cell"));
    }

    /// <summary>The block visitor over the real inline parser, counting each block that takes a
    /// fallback: an inline parse that returns no spans for text that is not blank, or one that
    /// throws.</summary>
    private static IReadOnlyList<MdBlock> ParseCountingFallbacks(string source, out int fallbacks)
    {
        var inline = new MarkdownInlineVisitor();
        int count = 0;
        var visitor = new MarkdownBlockVisitor(text =>
        {
            IReadOnlyList<MdInline> spans;
            try { spans = inline.Parse(text); }
            catch { count++; throw; }
            if (spans.Count == 0 && !string.IsNullOrWhiteSpace(text)) count++;
            return spans;
        });
        var blocks = visitor.Parse(source);
        fallbacks = count;
        return blocks;
    }

    private static string Dump(IEnumerable<MdInline> spans) => string.Join(" ", spans.Select(Dump));

    private static string Dump(MdInline span) => span switch
    {
        MdLiteral l => $"L({l.Text})",
        MdMathInline m => $"M({m.Source})",
        MdCodeInline c => $"C({c.Content})",
        MdEmphasis e => $"E{e.Level}[{Dump(e.Content)}]",
        MdLink k => $"Link[{Dump(k.Text)}]({k.Url})",
        MdImage i => $"Img[{Dump(i.Alt)}]({i.Url})",
        MdColor c => $"Col[{Dump(c.Text)}]{{{c.Color}}}",
        MdLineBreak b => b.Hard ? "HB" : "SB",
        _ => span.GetType().Name,
    };

    // ── A block whose inline parse fails keeps its text ──────────────
    //
    // No ordinary input fails the inline parse any more: every unpaired delimiter is text. The
    // fallback stays for whatever the pre-pass and the grammar might still disagree on, so these
    // tests force it through the block visitor's seam: an inline parser that returns no spans (the
    // fallback in ParseInline) or one that throws (the catch in ClassifyBlockSafe).

    private static readonly MarkdownBlockVisitor s_failingInline = new(_ => []);
    private static readonly MarkdownBlockVisitor s_throwingInline = new(_ => throw new System.InvalidOperationException("forced"));

    [Theory]
    [InlineData("$5 for a [discount and $10")]
    [InlineData("Prices: $5 for [0, 1) and $10 otherwise.")]
    [InlineData("$a ] $")]
    [InlineData("costs $5 (the ` key) and $10")]
    public void FailedInlineParse_BetweenProseDollars_KeepsTheParagraph(string source) =>
        s_failingInline.Parse(source).ShouldHaveSingleItem().ShouldBeOfType<MdParagraph>()
            .Content.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe(source);

    [Fact]
    public void Heading_WhoseInlineParseFails_KeepsItsText()
    {
        var heading = s_failingInline.Parse("# see a[0 here").ShouldHaveSingleItem().ShouldBeOfType<MdHeading>();
        heading.Level.ShouldBe(1);
        heading.Content.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("see a[0 here");
    }

    [Fact]
    public void ListItem_WhoseInlineParseFails_KeepsItsText()
    {
        var list = s_failingInline.Parse("- see a[0 here\n- next").ShouldHaveSingleItem().ShouldBeOfType<MdList>();
        list.Items.Count.ShouldBe(2);
        list.Items[0].Body.ShouldHaveSingleItem().ShouldBeOfType<MdParagraph>()
            .Content.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("see a[0 here");
    }

    [Fact]
    public void TableCells_WhoseInlineParseFails_KeepTheirText()
    {
        var table = s_failingInline.Parse("| a[0 | b |\n|---|---|\n| x[1 | y |").ShouldHaveSingleItem().ShouldBeOfType<MdTable>();
        table.Headers[0].ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("a[0");
        table.Rows.ShouldHaveSingleItem()[0].ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("x[1");
    }

    // ── A fallback literal keeps its soft breaks ─────────────────────
    //
    // A newline inside one literal does not reflow (a consumer appends a literal verbatim and
    // wraps only at spaces), and it reads as the hard break a renderer already draws that way. So
    // the fallback splits at each newline into a soft break, as the inline grammar would.

    [Fact]
    public void FallbackParagraph_KeepsItsSoftBreaks()
    {
        var content = s_failingInline.Parse("see a[0\nhere").ShouldHaveSingleItem().ShouldBeOfType<MdParagraph>().Content;
        Dump(content).ShouldBe("L(see a[0) SB L(here)");
    }

    [Fact]
    public void FallbackListItem_KeepsItsSoftBreaks()
    {
        // A list item's first line is one line; its continuation lines are where it can hold a newline.
        var list = s_failingInline.Parse("- item\n  see a[0\n  here").ShouldHaveSingleItem().ShouldBeOfType<MdList>();
        var body = list.Items.ShouldHaveSingleItem().Body;
        body.Count.ShouldBe(2);
        Dump(body[1].ShouldBeOfType<MdParagraph>().Content).ShouldBe("L(see a[0) SB L(here)");
    }

    [Fact]
    public void ClassifyFallback_KeepsItsSoftBreaks()
    {
        // ClassifyBlockSafe's catch builds the same literals as the inline fallback.
        var content = s_throwingInline.Parse("see more\nhere").ShouldHaveSingleItem().ShouldBeOfType<MdParagraph>().Content;
        Dump(content).ShouldBe("L(see more) SB L(here)");
    }

    [Theory]
    [InlineData("see a[0\nhere\nand a `tick\nthere")]
    [InlineData("# see a[0 here\n\nmore a[1\nhere")]   // a heading is one line by construction
    [InlineData("- see a[0\n- item\n  more a[1\n  here")]
    [InlineData("| a[0 | b |\n|---|---|\n| x[1 | y |")]
    public void FallbackLiterals_HoldNoNewline(string source)
    {
        foreach (var visitor in new[] { s_failingInline, s_throwingInline })
        {
            var literals = Inlines(visitor.Parse(source)).OfType<MdLiteral>().ToList();
            literals.ShouldNotBeEmpty();
            literals.ShouldAllBe(l => !l.Text.Contains('\n'));
        }
    }

    private static IEnumerable<MdInline> Inlines(IEnumerable<MdBlock> blocks) => blocks.SelectMany(InlinesOf);

    private static IEnumerable<MdInline> InlinesOf(MdBlock block) => block switch
    {
        MdParagraph p => p.Content,
        MdHeading h => h.Content,
        MdList l => Inlines(l.Items.SelectMany(i => i.Body)),
        MdTable t => t.Headers.SelectMany(c => c).Concat(t.Rows.SelectMany(r => r.SelectMany(c => c))),
        _ => [],
    };

    // ── A link label is parsed on its own ────────────────────────────
    //
    // The label opens before any `$` in it, so that `$` pairs only inside the label. A math span
    // that opens before the `[` still runs to its own closer and takes the bracket with it.

    [Fact]
    public void DollarInALinkLabel_DoesNotPairPastTheLabel()
    {
        var content = Paragraph("See [the $5 plan](https://x) or pay 5$ later.");
        content.OfType<MdMathInline>().ShouldBeEmpty();
        content.Count.ShouldBe(3);
        content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("See ");
        var link = content[1].ShouldBeOfType<MdLink>();
        link.Url.ShouldBe("https://x");
        link.Text.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("the $5 plan");
        content[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" or pay 5$ later.");
    }

    [Fact]
    public void DollarInALinkLabel_LeavesLaterMathAlone()
    {
        var content = Paragraph("See [the $5 plan](https://x) for ($x$) here.");
        content.Count.ShouldBe(5);
        content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("See ");
        var link = content[1].ShouldBeOfType<MdLink>();
        link.Url.ShouldBe("https://x");
        link.Text.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("the $5 plan");
        content[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" for (");
        content[3].ShouldBeOfType<MdMathInline>().Source.ShouldBe("x");
        content[4].ShouldBeOfType<MdLiteral>().Text.ShouldBe(") here.");
    }

    [Fact]
    public void MathInsideALinkLabel_IsMath()
    {
        var link = Paragraph("[$a$](https://x)").ShouldHaveSingleItem().ShouldBeOfType<MdLink>();
        link.Url.ShouldBe("https://x");
        link.Text.ShouldHaveSingleItem().ShouldBeOfType<MdMathInline>().Source.ShouldBe("a");
    }

    [Fact]
    public void MathOpenedBeforeALabel_TakesItsBracket()
    {
        var content = Paragraph("$a [b$ c](https://x)");
        content.Count.ShouldBe(2);
        content[0].ShouldBeOfType<MdMathInline>().Source.ShouldBe("a [b");
        content[1].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" c](https://x)");
    }

    [Theory]
    [InlineData("See [the $$5 plan](https://x) or pay 5$$ later.", "the $$5 plan")]
    [InlineData(@"See [the \(5 plan](https://x) or pay 5\) later.", @"the \(5 plan")]
    [InlineData(@"See [the \boxed{5 plan](https://x) or pay 5} later.", @"the \boxed{5 plan")]
    public void OtherOpenersInALinkLabel_PairOnlyInsideIt(string source, string label)
    {
        var content = Paragraph(source);
        content.OfType<MdMathInline>().ShouldBeEmpty();
        content[1].ShouldBeOfType<MdLink>().Text.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe(label);
    }

    [Fact]
    public void DollarInAnImageAlt_DoesNotPairPastIt()
    {
        var content = Paragraph("See ![the $5 plan](https://x) or pay 5$ later.");
        content.OfType<MdMathInline>().ShouldBeEmpty();
        content[1].ShouldBeOfType<MdImage>().Alt.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("the $5 plan");
    }

    [Fact]
    public void DollarInAColourLabel_DoesNotPairPastIt()
    {
        var content = Paragraph("See [the $5 plan]{red} or pay 5$ later.");
        content.OfType<MdMathInline>().ShouldBeEmpty();
        var color = content[1].ShouldBeOfType<MdColor>();
        color.Color.ShouldBe("red");
        color.Text.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("the $5 plan");
    }

    [Theory]
    [InlineData("[on $[0,1]$](https://x)", "[0,1]")]
    [InlineData("[on $[0,1)$](https://x)", "[0,1)")]   // the math's own `[` never closes
    public void BracketsInsideMathInALabel_StayMath(string source, string body)
    {
        var link = Paragraph(source).ShouldHaveSingleItem().ShouldBeOfType<MdLink>();
        link.Text.Count.ShouldBe(2);
        link.Text[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("on ");
        link.Text[1].ShouldBeOfType<MdMathInline>().Source.ShouldBe(body);
    }

    // ── A \text{..} group in math may hold a dollar ──────────────────
    //
    // Pandoc reads `\text` and the balanced brace group after it as one piece of a math body, so
    // a `$` inside the braces does not close the span.

    [Theory]
    [InlineData(@"$\text{costs $5}$", false)]
    [InlineData(@"$$\text{costs $5}$$", true)]
    public void DollarInsideText_IsBody(string source, bool display)
    {
        var math = Paragraph(source).ShouldHaveSingleItem().ShouldBeOfType<MdMathInline>();
        math.Source.ShouldBe(@"\text{costs $5}");
        math.IsDisplay.ShouldBe(display);
    }

    [Fact]
    public void DoubleDollarInsideText_DoesNotCloseDisplayMath()
    {
        var math = Paragraph(@"$$\text{a $$ b}$$").ShouldHaveSingleItem().ShouldBeOfType<MdMathInline>();
        math.Source.ShouldBe(@"\text{a $$ b}");
        math.IsDisplay.ShouldBeTrue();
    }

    [Fact]
    public void TextGroup_EndsAtItsBrace()
    {
        var content = Paragraph(@"$\text{costs $5}$ and $x$");
        content.Count.ShouldBe(3);
        content[0].ShouldBeOfType<MdMathInline>().Source.ShouldBe(@"\text{costs $5}");
        content[1].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" and ");
        content[2].ShouldBeOfType<MdMathInline>().Source.ShouldBe("x");
    }

    [Fact]
    public void UnclosedTextGroup_IsNotOnePiece()
    {
        // With no closing brace, `\text{` is ordinary body, so the `$` after `costs ` is a closer
        // with a space before it: the first opener does not pair, and `$5$` does.
        var content = Paragraph(@"$\text{costs $5$");
        content.Count.ShouldBe(2);
        content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe(@"$\text{costs ");
        content[1].ShouldBeOfType<MdMathInline>().Source.ShouldBe("5");
    }

    // ── Code spans are opaque ────────────────────────────────────────

    [Fact]
    public void CodeSpans_KeepTheirDollars()
    {
        var content = Paragraph("run `echo $HOME` or `$x$` now");
        content.OfType<MdMathInline>().ShouldBeEmpty();
        content.OfType<MdCodeInline>().Select(c => c.Content).ShouldBe(["echo $HOME", "$x$"]);
    }

    [Fact]
    public void DollarInCode_DoesNotPairWithOneOutside()
    {
        // The `$` inside the code span must not be taken as the closer of the `$` before it.
        var content = Paragraph("costs $5 `$` each");
        content.Count.ShouldBe(3);
        content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("costs $5 ");
        content[1].ShouldBeOfType<MdCodeInline>().Content.ShouldBe("$");
        content[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" each");
    }
}
