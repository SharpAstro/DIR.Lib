using System.Linq;
using DIR.Lib.Markdown;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Phase B: validate the LALR.CC inline grammar across all five math
/// forms. Each form pushes a dedicated lexer state and invokes the
/// LaTeX sub-parser via a rewriter; the assertions check both
/// structural shape (correct number of spans, correct body capture)
/// and rendered semantics (sub-parser actually produced the expected
/// Unicode glyphs).
/// </summary>
public sealed class MarkdownInlineSpikeTests
{
    private readonly MarkdownInlineVisitor _visitor = new();

    // ── Plain text + dollar math (Phase A coverage) ───────────────────

    [Fact]
    public void PlainText_ProducesSingleLiteral()
    {
        var spans = _visitor.Parse("hello world");
        spans.Count.ShouldBe(1);
        spans[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("hello world");
    }

    [Fact]
    public void DollarMath_ProducesMathInline()
    {
        var spans = _visitor.Parse("$x^2$");
        spans.Count.ShouldBe(1);
        var math = spans[0].ShouldBeOfType<MdMathInline>();
        math.Source.ShouldBe("x^2");
        math.Unicode.ShouldContain("x");
        math.Unicode.ShouldContain("²");
    }

    [Fact]
    public void TextThenMathThenText_ProducesThreeSpans()
    {
        var spans = _visitor.Parse("before $x^2$ after");
        spans.Count.ShouldBe(3);
        spans[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("before ");
        spans[1].ShouldBeOfType<MdMathInline>().Source.ShouldBe("x^2");
        spans[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" after");
    }

    [Fact]
    public void MultipleMathSpans_EachInvokeSubParser()
    {
        var spans = _visitor.Parse("$a$ and $b$");
        spans.OfType<MdMathInline>().Select(m => m.Source).ShouldBe(new[] { "a", "b" });
    }

    // ── $$ display math ──────────────────────────────────────────────

    [Fact]
    public void DoubleDollarMath_ProducesMathInline()
    {
        // The lexer's longest-match resolves `$$` before bare `$`, so the
        // dollar2 rule wins and the math_dollar2 state captures the body
        // as a whole even though `$` alone has its own opener rule.
        var spans = _visitor.Parse("text $$x^2$$ more");
        spans.OfType<MdMathInline>().Single().Source.ShouldBe("x^2");
    }

    // ── \(..\) inline math ───────────────────────────────────────────

    [Fact]
    public void LatexParenMath_ProducesMathInline()
    {
        // \( opens the math_paren state; the body is a list of latex_frag
        // tokens (so backslash-prefixed LaTeX commands like \pi survive
        // the lex pass), concatenated by the visitor.
        var spans = _visitor.Parse(@"see \( e^{i\pi} + 1 = 0 \) end");
        var math = spans.OfType<MdMathInline>().Single();
        math.Source.ShouldBe(" e^{i\\pi} + 1 = 0 ");
    }

    [Fact]
    public void LatexParenMath_PreservesCommandsInBody()
    {
        // The body's `\ll` must survive intact so the LaTeX sub-parser
        // can tokenise it as a `rel`. Direct-grammar-driven path —
        // unlike the prose-substitution fallback, no upstream regex
        // touches the body.
        var spans = _visitor.Parse(@"\( v \ll c \)");
        spans.OfType<MdMathInline>().Single().Source.ShouldBe(" v \\ll c ");
    }

    // ── \[..\] inline (block-level handling lands in Phase C) ────────

    [Fact]
    public void LatexBracketMath_ProducesMathInline()
    {
        var spans = _visitor.Parse(@"intro \[ E = mc^2 \] outro");
        spans.OfType<MdMathInline>().Single().Source.ShouldBe(" E = mc^2 ");
    }

    // ── \boxed{..} with balanced braces ──────────────────────────────

    [Fact]
    public void BoxedMath_CapturesBody()
    {
        var spans = _visitor.Parse(@"answer: \boxed{x}");
        spans.OfType<MdMathInline>().Single().Source.ShouldBe("x");
    }

    [Fact]
    public void BoxedMath_BalancesNestedBraces()
    {
        // \boxed{\frac{1}{2}} has nested {} that must NOT terminate the
        // outer box. The BoxedBody recursion handles this naturally —
        // the LR grammar makes balanced braces a one-line production
        // (no hand-rolled scanner needed).
        var spans = _visitor.Parse(@"\boxed{\frac{1}{2}mv^2}");
        spans.OfType<MdMathInline>().Single().Source.ShouldBe(@"\frac{1}{2}mv^2");
    }

    [Fact]
    public void BoxedMath_RoundTripsThroughSubParser()
    {
        // Visit(MathBoxedSpan) wraps the body back in \boxed{...} so the
        // existing LaTeX sub-parser's \boxed handler (which frames the
        // body in [..] for the Unicode renderer) takes over.
        var spans = _visitor.Parse(@"\boxed{E = mc^2}");
        var math = spans.OfType<MdMathInline>().Single();
        math.Unicode.ShouldContain("E");
        math.Unicode.ShouldContain("mc²");
    }

    // ── Inline code ──────────────────────────────────────────────────

    [Fact]
    public void Backticks_ProduceCodeInline()
    {
        var spans = _visitor.Parse("use the `git status` command");
        spans.Count.ShouldBe(3);
        spans[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("use the ");
        spans[1].ShouldBeOfType<MdCodeInline>().Content.ShouldBe("git status");
        spans[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" command");
    }

    [Fact]
    public void Backticks_BodyIsOpaque_NoMathSubstitution()
    {
        // Math markers inside a code span must NOT be parsed as math —
        // the lexer state isolates the body and the visitor emits it
        // verbatim. This is one of the regressions the LALR.CC path
        // fixes vs the current regex preprocessing.
        var spans = _visitor.Parse("`\\boxed{x}` is the syntax");
        spans[0].ShouldBeOfType<MdCodeInline>().Content.ShouldBe("\\boxed{x}");
        // No MdMathInline produced for the code body.
        spans.OfType<MdMathInline>().ShouldBeEmpty();
    }

    [Fact]
    public void DoubleBacktick_PreservesSingleBacktickInBody()
    {
        // ``foo `bar` baz`` — a 2-backtick fence lets a single ` survive
        // inside the body. The lexer pops only on `` `` ``, not on a
        // bare `, so the body is captured as the literal "foo `bar` baz".
        var spans = _visitor.Parse("see ``foo `bar` baz`` end");
        var code = spans.OfType<MdCodeInline>().Single();
        code.Content.ShouldBe("foo `bar` baz");
    }

    [Fact]
    public void Backticks_EmptyBody_ParseFailsCleanly()
    {
        // `` (two adjacent backticks) is degenerate — the grammar requires
        // a non-empty codebody. Fail-closed matches the rest of the
        // error-recovery posture; CommonMark's escape-for-empty-code
        // (`<code></code>`) isn't supported here.
        _visitor.Parse("`` foo").ShouldBeEmpty();
    }

    // ── Backslash escapes ────────────────────────────────────────────

    [Fact]
    public void BackslashEscape_StripsLeadingBackslash()
    {
        // `\*` in markdown source means "literal *" — the rendered text
        // should be just `*`. The grammar emits `\*` as an `escape`
        // terminal, the visitor strips the backslash, and the literal-
        // merging post-pass collapses the resulting "a " + "*" + " b"
        // run into a single MdLiteral.
        var spans = _visitor.Parse("a \\* b");
        spans.Count.ShouldBe(1);
        spans[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("a * b");
    }

    [Fact]
    public void BackslashEscape_MultipleEscapes()
    {
        // `\\` → `\`, `\_` → `_`, `\{` → `{` — every non-letter follower
        // is a literal escape per CommonMark.
        var literals = _visitor.Parse("\\\\ \\_ \\{")
            .OfType<MdLiteral>()
            .Select(l => l.Text)
            .ToArray();
        // Joined: `\ _ {` — the literal of each escape interleaved with
        // the literal space runs.
        string.Concat(literals).ShouldBe("\\ _ {");
    }

    [Fact]
    public void BackslashCommand_SubstitutedToUnicode()
    {
        // `\div` (backslash + letters) is a known LaTeX command — the
        // visitor's Visit(LiteralSpan) looks it up in the loose-latex
        // table and emits the Unicode glyph directly. Mirrors what the
        // Markdig path's SubstituteLooseLatex pass produces, with the
        // difference that the LALR path applies the substitution at
        // visit time rather than via a pre-pass over the source string
        // (so things inside code spans and math states aren't touched).
        var spans = _visitor.Parse("see \\div here");
        string.Concat(spans.OfType<MdLiteral>().Select(l => l.Text)).ShouldContain("÷");
    }

    // ── Emphasis (delimiter-stack pairing) ───────────────────────────

    [Fact]
    public void SingleStar_ProducesItalic()
    {
        var spans = _visitor.Parse("an *italic* word");
        spans.Count.ShouldBe(3);
        spans[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("an ");
        var em = spans[1].ShouldBeOfType<MdEmphasis>();
        em.Level.ShouldBe(1);
        em.Content.Count.ShouldBe(1);
        em.Content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("italic");
        spans[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" word");
    }

    [Fact]
    public void DoubleStar_ProducesBold()
    {
        var spans = _visitor.Parse("a **bold** word");
        var em = spans.OfType<MdEmphasis>().Single();
        em.Level.ShouldBe(2);
        em.Content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("bold");
    }

    [Fact]
    public void NestedEmphasis_BoldContainsItalic()
    {
        // `**a *b* c**` → bold(a, italic(b), c). The pairing scan finds
        // the inner `*` pair first, then the outer `**` pair wraps the
        // result.
        var spans = _visitor.Parse("**a *b* c**");
        spans.Count.ShouldBe(1);
        var bold = spans[0].ShouldBeOfType<MdEmphasis>();
        bold.Level.ShouldBe(2);
        bold.Content.Count.ShouldBe(3);
        bold.Content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("a ");
        var italic = bold.Content[1].ShouldBeOfType<MdEmphasis>();
        italic.Level.ShouldBe(1);
        italic.Content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("b");
        bold.Content[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" c");
    }

    [Fact]
    public void UnmatchedStar_StaysLiteral()
    {
        // `2 * 3 = 6` has a lone `*` that doesn't pair. The post-pass
        // rewrites it back to literal text so the rendered output
        // shows the multiplication sign as intended. This is the
        // pragmatic alternative to a strict grammar that would
        // fail-and-fallback the entire paragraph.
        var spans = _visitor.Parse("2 * 3 = 6");
        spans.OfType<MdEmphasis>().ShouldBeEmpty();
        // The literal sequence `2 ` + `*` + ` 3 = 6` makes it through.
        string.Concat(spans.OfType<MdLiteral>().Select(l => l.Text)).ShouldBe("2 * 3 = 6");
    }

    [Fact]
    public void StarThenDoubleStar_DontCrossPair()
    {
        // `*a **b* c**` — the inner `*` pair matches with the outer
        // `*`, leaving the `**` markers unpaired. Loose pairing here
        // is approximate; CommonMark's strict flanking rules would
        // do better. Test documents what we DO produce: an italic
        // span and dangling `**` text.
        var spans = _visitor.Parse("*a **b* c**");
        spans.OfType<MdEmphasis>().Single().Level.ShouldBe(1);
    }

    // ── Links ────────────────────────────────────────────────────────

    [Fact]
    public void Link_BasicTextAndUrl()
    {
        var spans = _visitor.Parse("see [example](https://example.com) here");
        spans.Count.ShouldBe(3);
        spans[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("see ");
        var link = spans[1].ShouldBeOfType<MdLink>();
        link.Url.ShouldBe("https://example.com");
        link.Text.Count.ShouldBe(1);
        link.Text[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("example");
        spans[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" here");
    }

    [Fact]
    public void Link_TextCanContainEmphasis()
    {
        // `[**bold link**](url)` — the bracketed text is parsed by the
        // full inline grammar, so emphasis (and math, code, etc.) all
        // work inside link text.
        var spans = _visitor.Parse("[**bold**](url)");
        var link = spans[0].ShouldBeOfType<MdLink>();
        link.Url.ShouldBe("url");
        var em = link.Text[0].ShouldBeOfType<MdEmphasis>();
        em.Level.ShouldBe(2);
        em.Content[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("bold");
    }

    [Fact]
    public void PlainBrackets_NotALink_StayLiteral()
    {
        // `[TODO]` with no `(` following — the grammar uses the
        // rbracket production (not link_tail_open), and the visitor
        // emits a transient MdGroup that Flatten resolves into
        // literal-text spans `[ + content + ]`.
        var spans = _visitor.Parse("a [TODO] item");
        // After Flatten + merge: should be a single literal containing
        // "a [TODO] item".
        spans.Count.ShouldBe(1);
        spans[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("a [TODO] item");
    }

    // ── Images ───────────────────────────────────────────────────────

    [Fact]
    public void Image_BasicAltAndUrl()
    {
        var spans = _visitor.Parse("![a cat](cat.png)");
        var img = spans[0].ShouldBeOfType<MdImage>();
        img.Url.ShouldBe("cat.png");
        img.Alt.Count.ShouldBe(1);
        img.Alt[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("a cat");
    }

    [Fact]
    public void Image_EmptyAlt()
    {
        // `![](url)` — empty alt; Spans is nullable (shared with links),
        // so the image production accepts zero inner spans.
        var spans = _visitor.Parse("![](logo.png)");
        var img = spans[0].ShouldBeOfType<MdImage>();
        img.Url.ShouldBe("logo.png");
        img.Alt.Count.ShouldBe(0);
    }

    [Fact]
    public void Image_MidText_SpaceBefore()
    {
        var spans = _visitor.Parse("see ![logo](a.png) now");
        spans.Count.ShouldBe(3);
        spans[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("see ");
        spans[1].ShouldBeOfType<MdImage>().Url.ShouldBe("a.png");
        spans[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" now");
    }

    [Fact]
    public void Image_AfterWord_NoSpace()
    {
        // `word![x](y)` — `!` is excluded from the main text run so the
        // preceding word doesn't swallow it, letting `![` win longest-match.
        var spans = _visitor.Parse("word![x](y)");
        spans.Count.ShouldBe(2);
        spans[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("word");
        spans[1].ShouldBeOfType<MdImage>().Url.ShouldBe("y");
    }

    [Fact]
    public void Image_AltCanContainEmphasis()
    {
        var spans = _visitor.Parse("![**bold**](u)");
        var img = spans[0].ShouldBeOfType<MdImage>();
        img.Url.ShouldBe("u");
        img.Alt[0].ShouldBeOfType<MdEmphasis>().Level.ShouldBe(2);
    }

    [Fact]
    public void Bang_NotImage_StaysLiteral()
    {
        // A `!` not followed by `[` is plain text and re-merges with its
        // neighbours, so `![`-less bangs round-trip unchanged.
        var spans = _visitor.Parse("a != b! ok");
        spans.Count.ShouldBe(1);
        spans[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("a != b! ok");
    }

    // ── Color inlines ────────────────────────────────────────────────

    [Fact]
    public void Color_BasicTextAndColor()
    {
        var spans = _visitor.Parse("[red text]{red}");
        var color = spans[0].ShouldBeOfType<MdColor>();
        color.Color.ShouldBe("red");
        color.Text[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("red text");
    }

    [Fact]
    public void Color_HexValueAccepted()
    {
        var spans = _visitor.Parse("[hot]{#ff0080}");
        spans.OfType<MdColor>().Single().Color.ShouldBe("#ff0080");
    }

    // ── Line breaks ──────────────────────────────────────────────────

    [Fact]
    public void HardBreak_FromTrailingSpaces()
    {
        // Two-plus trailing spaces + newline → hard line break.
        var spans = _visitor.Parse("line 1  \nline 2");
        spans.OfType<MdLineBreak>().Single().Hard.ShouldBeTrue();
    }

    [Fact]
    public void HardBreak_FromTrailingBackslash()
    {
        // `\<newline>` → hard line break (CommonMark).
        var spans = _visitor.Parse("line 1\\\nline 2");
        spans.OfType<MdLineBreak>().Single().Hard.ShouldBeTrue();
    }

    [Fact]
    public void SoftBreak_FromLoneNewline()
    {
        // Lone newline within a paragraph → soft break.
        var spans = _visitor.Parse("line 1\nline 2");
        spans.OfType<MdLineBreak>().Single().Hard.ShouldBeFalse();
    }

    // ── Failure modes ────────────────────────────────────────────────

    [Fact]
    public void EmptyInput_ProducesEmptyList()
    {
        _visitor.Parse(string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void UnterminatedMath_ParseFailsCleanly()
    {
        // Spike scope: malformed input returns empty rather than throwing.
        // Phase B-extension may tighten error recovery (preserve the
        // literal up to the unmatched delimiter); for now "fail closed"
        // is enough.
        _visitor.Parse("text $unterminated").ShouldBeEmpty();
    }
}
