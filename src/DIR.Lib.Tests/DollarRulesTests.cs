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
