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

    private static string Dump(IEnumerable<MdInline> spans) => string.Join(" ", spans.Select(Dump));

    private static string Dump(MdInline span) => span switch
    {
        MdLiteral l => $"L({l.Text})",
        MdMathInline m => $"M({m.Source})",
        MdCodeInline c => $"C({c.Content})",
        MdEmphasis e => $"E{e.Level}[{Dump(e.Content)}]",
        MdLink k => $"Link[{Dump(k.Text)}]({k.Url})",
        MdLineBreak b => b.Hard ? "HB" : "SB",
        _ => span.GetType().Name,
    };

    // ── A block whose inline parse fails keeps its text ──────────────
    //
    // The inline grammar has no rule for a link whose URL never closes (`[a](b` to the end), so
    // the root lexer fails on it and the block falls back to its source as literal text instead
    // of rendering empty. These tests used a lone `[`, `]` or backtick as the trigger until those
    // became text; each asserts its trigger still fails, so the next such change has to move them.

    private static void ShouldFailTheInlineParse(string text) =>
        new MarkdownInlineVisitor().Parse(text).ShouldBeEmpty();

    [Theory]
    [InlineData("$5 for a [discount](x and $10")]
    [InlineData("Prices: $5 for [0, 1](x and $10 otherwise.")]
    [InlineData("$a [](x $")]
    [InlineData("costs $5 (the [key](x and $10")]
    public void UnclosedLinkUrl_BetweenProseDollars_KeepsTheParagraph(string source)
    {
        ShouldFailTheInlineParse(source);
        ShouldRenderLiterally(source, source);
    }

    [Fact]
    public void Heading_WithAnUnclosedLinkUrl_KeepsItsText()
    {
        ShouldFailTheInlineParse("see [a](b here");
        var heading = _visitor.Parse("# see [a](b here").ShouldHaveSingleItem().ShouldBeOfType<MdHeading>();
        heading.Level.ShouldBe(1);
        heading.Content.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("see [a](b here");
    }

    [Fact]
    public void ListItem_WithAnUnclosedLinkUrl_KeepsItsText()
    {
        ShouldFailTheInlineParse("see [a](b here");
        var list = _visitor.Parse("- see [a](b here\n- next").ShouldHaveSingleItem().ShouldBeOfType<MdList>();
        list.Items.Count.ShouldBe(2);
        list.Items[0].Body.ShouldHaveSingleItem().ShouldBeOfType<MdParagraph>()
            .Content.ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("see [a](b here");
    }

    [Fact]
    public void TableCells_WithAnUnclosedLinkUrl_KeepTheirText()
    {
        ShouldFailTheInlineParse("[a](b");
        ShouldFailTheInlineParse("[x](y");
        var table = _visitor.Parse("| [a](b | b |\n|---|---|\n| [x](y | y |").ShouldHaveSingleItem().ShouldBeOfType<MdTable>();
        table.Headers[0].ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("[a](b");
        table.Rows.ShouldHaveSingleItem()[0].ShouldHaveSingleItem().ShouldBeOfType<MdLiteral>().Text.ShouldBe("[x](y");
    }

    // ── A fallback literal keeps its soft breaks ─────────────────────
    //
    // A newline inside one literal does not reflow (a consumer appends a literal verbatim and
    // wraps only at spaces), and it reads as the hard break a renderer already draws that way. So
    // the fallback splits at each newline into a soft break, as the inline grammar would.

    [Fact]
    public void FallbackParagraph_KeepsItsSoftBreaks()
    {
        ShouldFailTheInlineParse("see [a](b\nhere");
        var content = Paragraph("see [a](b\nhere");
        content.Count.ShouldBe(3);
        content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("see [a](b");
        content[1].ShouldBeOfType<MdLineBreak>().Hard.ShouldBeFalse();
        content[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe("here");
    }

    [Fact]
    public void FallbackListItem_KeepsItsSoftBreaks()
    {
        // A list item's first line is one line; its continuation lines are where it can hold a newline.
        ShouldFailTheInlineParse("see [a](b\nhere");
        var list = _visitor.Parse("- item\n  see [a](b\n  here").ShouldHaveSingleItem().ShouldBeOfType<MdList>();
        var body = list.Items.ShouldHaveSingleItem().Body;
        body.Count.ShouldBe(2);
        var content = body[1].ShouldBeOfType<MdParagraph>().Content;
        content.Count.ShouldBe(3);
        content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("see [a](b");
        content[1].ShouldBeOfType<MdLineBreak>().Hard.ShouldBeFalse();
        content[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe("here");
    }

    [Fact]
    public void ClassifyFallback_KeepsItsSoftBreaks()
    {
        // ClassifyBlockSafe's catch builds the same literals as the inline fallback. An inline
        // parser that throws forces that path whatever the input.
        var visitor = new MarkdownBlockVisitor(_ => throw new System.InvalidOperationException("forced"));
        var content = visitor.Parse("see more\nhere").ShouldHaveSingleItem().ShouldBeOfType<MdParagraph>().Content;
        Dump(content).ShouldBe("L(see more) SB L(here)");
    }

    [Theory]
    [InlineData("see [a](b\nhere\nand more\nthere", "see [a](b\nhere\nand more\nthere")]
    [InlineData("# see [a](b here\n\nmore [c](d\nhere", "more [c](d\nhere")]   // a heading is one line by construction
    [InlineData("- see [a](b\n- item\n  more [c](d\n  here", "more [c](d\nhere")]
    [InlineData("| [a](b | b |\n|---|---|\n| [x](y | y |", "[a](b")]
    public void FallbackLiterals_HoldNoNewline(string source, string failingInline)
    {
        ShouldFailTheInlineParse(failingInline);
        var literals = Inlines(_visitor.Parse(source)).OfType<MdLiteral>().ToList();
        literals.ShouldNotBeEmpty();
        literals.ShouldAllBe(l => !l.Text.Contains('\n'));
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
