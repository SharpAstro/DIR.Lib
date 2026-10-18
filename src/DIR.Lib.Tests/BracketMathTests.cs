using DIR.Lib.Markdown;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;
using SharpAstro.Fonts;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Square brackets in the LaTeX math grammar: the optional root index
/// <c>\sqrt[n]{x}</c>, bracket groups <c>[a, b]</c> and <c>\left[ … \right]</c>,
/// and the half-open mixes <c>[0, 1)</c> / <c>(0, 1]</c>, on the box (raster)
/// path and the Unicode path.
///
/// <para>The box path here is the whole of it up to the parse:
/// <see cref="MarkdownMacros.TryPrepareMathBoxSource"/>, then the grammar's
/// lexer and a parser bound to a <see cref="BoxBuildingVisitor"/> (see
/// <see cref="BuildBox"/>). Before the grammar lexed <c>[</c>, every one of
/// these formulas threw in the lexer and the terminal fell back to Unicode
/// text.</para>
///
/// <para><see cref="SqrtBox"/> and <see cref="BracketBox"/> keep their index and
/// kind private, so the tests prove them by rendering: the parsed box must paint
/// exactly what a hand-built box with that index (or kind) paints.</para>
/// </summary>
public sealed class BracketMathTests
{
    private const float FontSize = 32f;

    private static readonly string StixPath =
        Path.Combine(AppContext.BaseDirectory, "Fonts", "STIX2Math.otf");

    private static readonly BoxStyle Style = new(StixPath, FontSize, new RGBAColor32(0, 0, 0, 255));

    /// <summary>Where the renders land for a look by eye; never compared.</summary>
    private static string OutputDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "bracket-math-output");

    // ── Box path ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\sqrt[3]{x}")]
    [InlineData(@"\sqrt[n]{x^2+1}")]
    [InlineData(@"\sqrt [3] x")]
    [InlineData(@"\sqrt{2} + \sqrt[n]{y}")]
    [InlineData(@"[a, b]")]
    [InlineData(@"\left[ \frac{1}{2} \right]")]
    [InlineData(@"[0, 1)")]
    [InlineData(@"(0, 1]")]
    [InlineData(@"x \in [0, 1]")]
    [InlineData(@"f(x, y) = 1,")]
    public void BoxPath_ParsesAndRasterizesNonEmpty(string latex)
    {
        var box = BuildBox(latex).ShouldNotBeNull($"'{latex}' should parse on the box path");
        var image = BoxRasterizer.RenderToRgba(box, Style);

        image.Width.ShouldBeGreaterThan(0);
        image.Height.ShouldBeGreaterThan(0);
        InkPixels(image).ShouldBeGreaterThan(0, $"'{latex}' rasterized to a blank image");
    }

    /// <summary>
    /// <c>\sqrt[3]{x}</c> is a <see cref="SqrtBox"/> whose index is the "3" at
    /// script-script style: it paints pixel for pixel what a hand-built
    /// <c>new SqrtBox(x, 3 at Smaller().Smaller(), style)</c> paints, and not
    /// what the index-less <c>\sqrt{x}</c> paints. The parsed x is a variable,
    /// so math italic.
    /// </summary>
    [Fact]
    public void BoxPath_CubeRoot_IsSqrtBoxWithScriptScriptIndex()
    {
        var box = BuildBox(@"\sqrt[3]{x}").ShouldBeOfType<SqrtBox>();

        var handBuilt = new SqrtBox(
            new MathGlyphBox("x", Style, MathStyle.Italic),
            new GlyphBox("3", Style.Smaller().Smaller()),
            Style);
        Render(box).ShouldBe(Render(handBuilt), "\\sqrt[3]{x} should carry a script-script \"3\" index");

        var plain = BuildBox(@"\sqrt{x}").ShouldBeOfType<SqrtBox>();
        Render(box).ShouldNotBe(Render(plain), "the index should paint ink the plain root does not");
    }

    /// <summary>
    /// <c>\sqrt[n]{x^2+1}</c>: a composite radicand under a letter index. The
    /// radicand is whatever the parser makes of <c>x^2+1</c> on its own; the
    /// root must paint what a <see cref="SqrtBox"/> of that radicand with a
    /// script-script "n" paints (a math-italic n, being a variable).
    /// </summary>
    [Fact]
    public void BoxPath_NthRootOfComposite_IsSqrtBoxWithIndex()
    {
        var box = BuildBox(@"\sqrt[n]{x^2+1}").ShouldBeOfType<SqrtBox>();

        var radicand = BuildBox(@"x^2+1").ShouldNotBeNull();
        var handBuilt = new SqrtBox(radicand,
            new MathGlyphBox("n", Style.Smaller().Smaller(), MathStyle.Italic), Style);
        Render(box).ShouldBe(Render(handBuilt));

        var plain = BuildBox(@"\sqrt{x^2+1}").ShouldBeOfType<SqrtBox>();
        Render(box).ShouldNotBe(Render(plain));
    }

    /// <summary>
    /// A bracket group is a square <see cref="BracketBox"/> (the stretchy one
    /// parens use), whether written bare or as <c>\left[ … \right]</c>.
    /// </summary>
    [Theory]
    [InlineData(@"[a, b]", @"a, b")]
    [InlineData(@"\left[ \frac{1}{2} \right]", @"\frac{1}{2}")]
    public void BoxPath_BracketGroup_IsSquareBracketBox(string latex, string inner)
    {
        var box = BuildBox(latex).ShouldBeOfType<BracketBox>();

        var innerBox = BuildBox(inner).ShouldNotBeNull();
        Render(box).ShouldBe(Render(new BracketBox(innerBox, BracketKind.Square, Style)));
        Render(box).ShouldNotBe(Render(new BracketBox(innerBox, BracketKind.Paren, Style)));
    }

    /// <summary>
    /// The bracket stretches with its content: around a fraction it is taller
    /// than around a single letter, as a <c>\left( … \right)</c> paren is.
    /// </summary>
    [Fact]
    public void BoxPath_BracketAroundFraction_Stretches()
    {
        var tall = BuildBox(@"\left[ \frac{1}{2} \right]").ShouldBeOfType<BracketBox>();
        var single = BuildBox(@"[x]").ShouldBeOfType<BracketBox>();
        tall.TotalHeight.ShouldBeGreaterThan(single.TotalHeight * 1.3f);
    }

    /// <summary>
    /// Mixed delimiters parse, and the side that differs is drawn as written:
    /// <c>[0, 1)</c> paints differently from <c>[0, 1]</c>.
    /// </summary>
    [Fact]
    public void BoxPath_HalfOpenInterval_DrawsBothDelimitersAsWritten()
    {
        var halfOpen = BuildBox(@"[0, 1)").ShouldNotBeNull();
        var closed = BuildBox(@"[0, 1]").ShouldBeOfType<BracketBox>();
        halfOpen.ShouldNotBeOfType<BracketBox>();
        Render(halfOpen).ShouldNotBe(Render(closed));

        BuildBox(@"(0, 1]").ShouldNotBeNull();
        BuildBox(@"\left( 0, 1 \right]").ShouldNotBeNull();
    }

    /// <summary>
    /// LaTeX lets whitespace separate <c>\left</c> and <c>\right</c> from
    /// their delimiter. Spaced or not, the pair is the same stretchy bracket
    /// box. The aliases used to match the command and the delimiter only when
    /// they touched, so <c>\left ( x \right )</c> drew the command names.
    /// </summary>
    [Theory]
    [InlineData(@"\left \{ x \right \}", BracketKind.Curly)]
    [InlineData(@"\left ( x \right )", BracketKind.Paren)]
    [InlineData(@"\left [ x \right ]", BracketKind.Square)]
    [InlineData(@"\left \lbrace x \right \rbrace", BracketKind.Curly)]
    [InlineData("\\left\t(\n x \\right  )", BracketKind.Paren)]
    public void BoxPath_WhitespaceAfterLeftAndRight_IsTheBracketPair(string latex, BracketKind kind)
    {
        var box = BuildBox(latex).ShouldBeOfType<BracketBox>($"'{latex}' should build a bracket box");
        var inner = BuildBox("x").ShouldNotBeNull();
        Render(box).ShouldBe(Render(new BracketBox(inner, kind, Style)));
    }

    /// <summary>
    /// An unbalanced bracket does not parse on the box path, so the terminal
    /// falls back to the Unicode path, which keeps rendering it as before.
    /// </summary>
    [Fact]
    public void BoxPath_UnbalancedBracket_DoesNotParse()
    {
        BuildBox(@"[0, 1[").ShouldBeNull();
        BuildBox(@"\left[ x \right.").ShouldBeNull();
    }

    /// <summary>
    /// Renders written to <c>obj/</c> for a look by eye: the indexed root, the
    /// stretchy bracket, and a stretched half-open pair. Asserts only that they
    /// have ink; the PNGs are not baselines.
    /// </summary>
    [Theory]
    [InlineData(@"\sqrt[3]{x}", "sqrt3-x")]
    [InlineData(@"\left[ \frac{1}{2} \right]", "left-bracket-half")]
    [InlineData(@"\left[ \frac{1}{2}, 1 \right)", "half-open-interval")]
    public void BoxPath_WritesPngForInspection(string latex, string name)
    {
        var box = BuildBox(latex).ShouldNotBeNull();
        var image = BoxRasterizer.RenderToRgba(box, Style);
        InkPixels(image).ShouldBeGreaterThan(0);

        Directory.CreateDirectory(OutputDir);
        File.WriteAllBytes(Path.Combine(OutputDir, name + ".png"),
            PngWriter.Encode(image.Pixels, image.Width, image.Height));
    }

    // ── Unicode path ───────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\sqrt[3]{x}", "∛x")]
    [InlineData(@"\sqrt[4]{x}", "∜x")]
    [InlineData(@"\sqrt[n]{y}", "ⁿ√y")]
    [InlineData(@"\sqrt[10]{x}", "¹⁰√x")]
    [InlineData(@"\sqrt{2} + \sqrt[n]{y}", "√2 + ⁿ√y")]
    [InlineData(@"\sqrt[3]{x+1}", "∛(x + 1)")]
    [InlineData(@"\sqrt [3] x", "∛x")]
    // No superscript form for the index: the bracketed shape it had before.
    [InlineData(@"\sqrt[a+b]{x}", "√[a + b]x")]
    public void UnicodePath_NthRoot(string latex, string expected)
        => MarkdownMacros.RenderMathUnicode(latex).ShouldBe(expected);

    [Theory]
    [InlineData(@"[0, 1)", "[0, 1)")]
    [InlineData(@"(0, 1]", "(0, 1]")]
    [InlineData(@"\left( x \right]", "(x]")]
    // A `-` straight after `[` is unary now, as it is after `(`. While `[`
    // was an opaque atom this read as a binary minus: "[ − 1,1]".
    [InlineData(@"[-1, 1]", "[−1, 1]")]
    // Whitespace between \left or \right and its delimiter, as LaTeX allows.
    [InlineData(@"\left \{ x \right \}", "{x}")]
    [InlineData(@"\left ( x \right )", "(x)")]
    [InlineData(@"\left [ x \right ]", "[x]")]
    [InlineData(@"\left \lbrace x \right \rbrace", "{x}")]
    [InlineData(@"\left | x \right |", "|x|")]
    [InlineData(@"\left . x \right |", "x|")]
    [InlineData("\\left\t(\n x \\right  )", "(x)")]
    // A longer command that starts with \left or \right is left alone.
    [InlineData(@"a \leftarrow b \rightarrow c", "a ← b → c")]
    public void UnicodePath_NewlyParsedBrackets(string latex, string expected)
        => MarkdownMacros.RenderMathUnicode(latex).ShouldBe(expected);

    /// <summary>
    /// Formulas with brackets or commas that rendered before the grammar lexed
    /// them, each pinned to the exact string the old grammar produced (captured
    /// from the build before this change). Unbalanced brackets take the
    /// placeholder retry in <see cref="MarkdownMacros.RenderMathUnicode"/>.
    /// Since then a comma takes a space after it outside a script, as KaTeX and
    /// the box path set one (<c>f(x, y)</c>), and <c>\times</c> is a spaced ×.
    /// </summary>
    [Theory]
    [InlineData(@"[a, b]", "[a, b]")]
    [InlineData(@"[a,b]", "[a, b]")]
    [InlineData(@"x \in [0, 1]", "x ∈ [0, 1]")]
    [InlineData(@"f(x, y)", "f(x, y)")]
    [InlineData(@"f(x,y) = x^2 + y^2", "f(x, y) = x² + y²")]
    [InlineData(@"x = 1,", "x = 1,")]
    [InlineData(@", x", ", x")]
    [InlineData(@"a, b, c", "a, b, c")]
    [InlineData(@"x_{i,j}", "x_i,j")]
    [InlineData(@"a_{n,m}^{2}", "a_n,m²")]
    [InlineData(@"a,b^2", "a, b²")]
    [InlineData(@"x,\alpha", "x, α")]
    [InlineData(@"x^[2]", "x^[2]")]
    [InlineData(@"[a+b]^2", "[a + b]²")]
    [InlineData(@"[a+b]^{10}", "[a + b]¹⁰")]
    [InlineData(@"[x]^{-1}", "[x]^(−1)")]
    [InlineData(@"\frac{1}{[a, b]}", "1⁄[a, b]")]
    [InlineData(@"\frac{a,b}{c}", "(a, b)⁄c")]
    [InlineData(@"-[a]", "−[a]")]
    [InlineData(@"(-1, -2)", "(−1, − 2)")]
    [InlineData(@"\left[ x \right]", "[x]")]
    [InlineData(@"\left[ \frac{1}{2} \right]", "[1⁄2]")]
    [InlineData(@"[[a]]", "[[a]]")]
    [InlineData(@"[a]_n", "[a]ₙ")]
    [InlineData(@"x[n] = x[n-1] + 1", "x[n] = x[n − 1] + 1")]
    [InlineData(@"\sum_{i=1}^{n} [x_i]", "∑_(i = 1)ⁿ[xᵢ]")]
    [InlineData(@"\left[ \sum_{k=0}^{n} a_k \right]^2", "[∑_(k = 0)ⁿaₖ]²")]
    [InlineData(@"\sin[\alpha + \beta]", "sin[α + β]")]
    [InlineData(@"[a, b] \times [c, d]", "[a, b] × [c, d]")]
    [InlineData(@"\int_{[0,1]} f", "∫_[0,1]f")]
    [InlineData(@"\sqrt{[a, b]}", "√[a, b]")]
    [InlineData(@"\boxed{[a,b]}", "[[a, b]]")]
    [InlineData(@"\text{if } x \in [0,1]", "if x ∈ [0, 1]")]
    [InlineData(@"\ce{[Cu(NH3)4]^{2+}}", "[Cu(NH₃)₄]²⁺")]
    [InlineData(@"[]", "[]")]
    // Unbalanced: the bracket-as-placeholder retry.
    [InlineData(@"[0, 1[", "[0, 1[")]
    [InlineData(@"]0, 1[", "]0, 1[")]
    [InlineData(@"x \in ]0, 1]", "x ∈ ]0, 1]")]
    [InlineData(@"\left[ x \right.", "[x")]
    [InlineData(@"\sqrt[a, b]", "√[a, b]")]
    [InlineData(@"[", "[")]
    [InlineData(@"]", "]")]
    public void UnicodePath_RendersAsBefore(string latex, string expected)
        => MarkdownMacros.RenderMathUnicode(latex).ShouldBe(expected);

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Prepares <paramref name="latex"/> with
    /// <see cref="MarkdownMacros.TryPrepareMathBoxSource"/>, then parses and
    /// builds it at a fixed 32 px style. Null when the helper turns it away or
    /// it does not lex or parse, which is where the terminal falls back to the
    /// Unicode path. LALR.CC throws for both (<c>LexerException</c>,
    /// <c>ParseErrorException</c>), and both are
    /// <see cref="InvalidOperationException"/>s. The font lookup and the
    /// shrink-to-width loop are the terminal's and are not replicated.
    /// </summary>
    private static Box? BuildBox(string latex)
    {
        if (!MarkdownMacros.TryPrepareMathBoxSource(latex, out var source)) return null;
        var parser = Latex.BuildParser(new BoxBuildingVisitor(Style));
        try
        {
            using var lexer = BytesLexer.FromString(source, MarkdownMacros.MathLexerTable);
            using var tokens = new SyncLATokenIterator(lexer);
            var item = parser.ParseInput(tokens, debugger: null);
            return item.IsError || item.Content is not Func<BoxStyle, Box> builder ? null : builder(Style);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] Render(Box box) => BoxRasterizer.RenderToRgba(box, Style).Pixels;

    private static int InkPixels(RgbaImage image)
    {
        var ink = 0;
        for (var i = 3; i < image.Pixels.Length; i += 4)
            if (image.Pixels[i] > 0) ink++;
        return ink;
    }
}
