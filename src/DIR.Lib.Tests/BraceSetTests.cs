using System.Reflection;
using DIR.Lib.Markdown;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;
using SharpAstro.Fonts;
using SharpAstro.Png;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Sets keep their braces on both math paths. <c>\{1, 2, 3\}</c>,
/// <c>\left\{ … \right\}</c> and <c>\lbrace … \rbrace</c> all mean the same
/// pair of curly braces as glyphs, unlike a bare <c>{ … }</c>, which is an
/// invisible group.
///
/// <para>The box path here is the whole of it up to the parse:
/// <see cref="MarkdownMacros.TryPrepareMathBoxSource"/>, then the grammar's
/// lexer and a parser bound to a <see cref="BoxBuildingVisitor"/>.
/// <see cref="BracketBox"/> keeps its kind private, so a test proves the kind
/// by rendering: the parsed box must paint exactly what a hand-built curly
/// <see cref="BracketBox"/> around the expected content paints.</para>
/// </summary>
public sealed class BraceSetTests
{
    private const float FontSize = 32f;

    private static readonly RGBAColor32 Ink = new(0, 0, 0, 255);

    private static readonly BoxStyle Stix = StyleFor("STIX2Math");

    /// <summary>Where the renders land for a look by eye; never compared.</summary>
    private static string OutputDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "brace-set-output");

    // ── Unicode path ───────────────────────────────────────────────────

    /// <summary>
    /// Every spelling of a set keeps both braces. <c>\left\{</c> used to
    /// alias to a bare <c>{</c>, which the grammar reads as a group, so the
    /// braces vanished; nothing aliased <c>\lbrace</c>, so it printed as its
    /// own name. <c>\{1, 2, 3\}</c> already kept them.
    /// </summary>
    [Theory]
    [InlineData(@"\left\{ 1, 2 \right\}", "{1,2}")]
    [InlineData(@"\lbrace x \rbrace", "{x}")]
    [InlineData(@"\{1, 2, 3\}", "{1,2,3}")]
    [InlineData(@"\left\lbrace x \right\rbrace", "{x}")]
    [InlineData(@"\left\{ \frac{1}{2} \right\}", "{1⁄2}")]
    public void UnicodePath_SetKeepsBothBraces(string latex, string expected)
        => MarkdownMacros.RenderMathUnicode(latex).ShouldBe(expected);

    /// <summary>
    /// <c>\lbrace</c> and <c>\rbrace</c> alias only as whole command names:
    /// <c>\lbracex</c> is a different command, and stays as written.
    /// </summary>
    [Theory]
    [InlineData(@"\lbracex")]
    [InlineData(@"\rbracex")]
    [InlineData(@"\lbraceX \rbraceY")]
    public void UnicodePath_LongerCommandIsNotAliased(string latex)
    {
        MarkdownMacros.NormalizeLatexAliases(latex).ShouldBe(latex);
        MarkdownMacros.RenderMathUnicode(latex).ShouldBe(latex.Replace(" ", ""));
    }

    /// <summary>
    /// An unbalanced brace, and the empty set, render as they did before
    /// the box path learned braces (each pinned to the string the build
    /// before this change produced).
    /// </summary>
    [Theory]
    [InlineData(@"\{x", "{x")]
    [InlineData(@"x\}", "x}")]
    [InlineData(@"\{a, b", "{a,b")]
    [InlineData(@"a, b\}", "a,b}")]
    [InlineData(@"\{\}", "{}")]
    public void UnicodePath_UnbalancedRendersAsBefore(string latex, string expected)
        => MarkdownMacros.RenderMathUnicode(latex).ShouldBe(expected);

    /// <summary>
    /// An escaped brace inside <c>\text{...}</c> is a glyph, not one of the
    /// argument's braces, so it neither opens nor closes the argument. The
    /// argument's end used to be found by counting every brace, so
    /// <c>\text{\{}</c> never closed and printed its own source. A <c>\\</c> is
    /// one unit, so the brace after it still closes the argument.
    /// </summary>
    [Theory]
    [InlineData(@"\text{\{}", "{")]
    [InlineData(@"\text{\lbrace}", "{")]
    [InlineData(@"\text{\}}", "}")]
    [InlineData(@"\text{a\}b}", "a}b")]
    [InlineData(@"\text{\{a, b\}}", "{a, b}")]
    [InlineData(@"\text{a\\}", "a ")]
    // Balanced arguments, as they rendered before.
    [InlineData(@"\text{a {b} c}", "a {b} c")]
    [InlineData(@"\text{if } x", "if x")]
    public void UnicodePath_EscapedBraceInsideText(string latex, string expected)
        => MarkdownMacros.RenderMathUnicode(latex).ShouldBe(expected);

    /// <summary>
    /// A piecewise definition, <c>\left\{</c> with no right brace
    /// (<c>\right.</c>) around an environment, now parses. Its brace used to
    /// alias to a bare <c>{</c> that left a group unclosed, so the formula
    /// fell back to its source text, blanks and all:
    /// <c>"f(x) = { x    x > 0\n 0    otherwise "</c>.
    /// </summary>
    [Fact]
    public void UnicodePath_PiecewiseKeepsItsBrace()
        => MarkdownMacros.RenderMathUnicode(
                @"f(x) = \left\{ \begin{array}{ll} x & x > 0 \\ 0 & \text{otherwise} \end{array} \right.")
            .ShouldBe("f(x) = {x    x > 0\n 0    otherwise");

    // ── Box path ───────────────────────────────────────────────────────

    /// <summary>
    /// A set is a curly <see cref="BracketBox"/> around its content, in STIX
    /// Two Math and in DejaVu Sans. Both draw the braces from their MATH
    /// table's stretchy brace (DejaVu Sans as an assembly of parts); the
    /// parametric fallback is <see cref="BoxPath_SetWithoutMathCoverage_TakesTheParametricPath"/>'s.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", @"\{1, 2, 3\}", "1, 2, 3")]
    [InlineData("STIX2Math", @"\{a\}", "a")]
    [InlineData("STIX2Math", @"\lbrace x \rbrace", "x")]
    [InlineData("STIX2Math", @"\left\{ \frac{1}{2} \right\}", @"\frac{1}{2}")]
    [InlineData("DejaVuSans", @"\{1, 2, 3\}", "1, 2, 3")]
    [InlineData("DejaVuSans", @"\{a\}", "a")]
    [InlineData("DejaVuSans", @"\lbrace x \rbrace", "x")]
    [InlineData("DejaVuSans", @"\left\{ \frac{1}{2} \right\}", @"\frac{1}{2}")]
    public void BoxPath_Set_IsCurlyBracketBox(string font, string latex, string inner)
    {
        var style = StyleFor(font);
        var box = BuildBox(latex, style).ShouldBeOfType<BracketBox>($"'{latex}' should build a bracket box");

        var innerBox = BuildBox(inner, style).ShouldNotBeNull();
        Render(box, style).ShouldBe(Render(new BracketBox(innerBox, BracketKind.Curly, style), style),
            $"'{latex}' should paint a curly bracket box around '{inner}'");
        Render(box, style).ShouldNotBe(Render(new BracketBox(innerBox, BracketKind.Square, style), style));
    }

    /// <summary>
    /// The braces stretch with their content: around a fraction they are
    /// taller than around a single letter, as brackets and parens are.
    /// </summary>
    [Fact]
    public void BoxPath_SetAroundFraction_Stretches()
    {
        var tall = BuildBox(@"\left\{ \frac{1}{2} \right\}", Stix).ShouldBeOfType<BracketBox>();
        var single = BuildBox(@"\{x\}", Stix).ShouldBeOfType<BracketBox>();
        tall.TotalHeight.ShouldBeGreaterThan(single.TotalHeight * 1.3f);
    }

    /// <summary>
    /// A font with no MATH construction for the brace draws a set's braces
    /// with <see cref="BracketBox"/>'s parametric strokes once the content is
    /// taller than its brace glyph. Among the test fonts that is BabelStone
    /// Xiangqi Colour: a colour-glyph fixture, but it maps <c>{</c>, <c>}</c>,
    /// the digits and Latin letters, and has no MATH table. Around a fraction
    /// its brace glyph is too short, so the braces are strokes.
    /// </summary>
    [Fact]
    public void BoxPath_SetWithoutMathCoverage_TakesTheParametricPath()
    {
        var style = StyleFor("BabelStone");
        var inner = BuildBox(@"\frac{1}{2}", style).ShouldNotBeNull();
        new StretchyVerticalBox('{', inner.TotalHeight, style).IsAvailable.ShouldBeFalse("no stretchy { in the font");
        new StretchyVerticalBox('}', inner.TotalHeight, style).IsAvailable.ShouldBeFalse("no stretchy } in the font");

        var box = BuildBox(@"\left\{ \frac{1}{2} \right\}", style).ShouldBeOfType<BracketBox>();
        Render(box, style).ShouldBe(Render(new BracketBox(inner, BracketKind.Curly, style), style));
        Render(box, style).ShouldNotBe(Render(new BracketBox(inner, BracketKind.Square, style), style));
    }

    /// <summary>
    /// What a set paints: a brace on each side, each at least as tall as
    /// the content, with the content's ink between them, and each brace's
    /// cusp (its mid-height point) turned away from the content, so the left
    /// one reads as <c>{</c> and the right one as <c>}</c>. Read off the
    /// raster, so it holds for a font's own braces and for the parametric
    /// strokes alike.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", @"\{1, 2, 3\}")]
    [InlineData("STIX2Math", @"\left\{ \frac{1}{2} \right\}")]
    [InlineData("DejaVuSans", @"\{1, 2, 3\}")]
    [InlineData("DejaVuSans", @"\left\{ \frac{1}{2} \right\}")]
    [InlineData("BabelStone", @"\left\{ \frac{1}{2} \right\}")]
    public void BoxPath_Set_PaintsBracesAroundTheContent(string font, string latex)
    {
        var style = StyleFor(font);
        var image = BoxRasterizer.RenderToRgba(BuildBox(latex, style).ShouldBeOfType<BracketBox>(), style);

        var runs = InkColumnRuns(image);
        runs.Count.ShouldBeGreaterThanOrEqualTo(3, "a brace either side and the content between");
        var (left, right) = (runs[0], runs[^1]);
        var content = (Start: runs[1].Start, End: runs[^2].End);

        // To within a row: a font's own brace at text size is centred on the
        // math axis, and DejaVu Sans's then tops out a row below its digits.
        var (contentTop, contentBottom) = InkRows(image, content.Start, content.End);
        foreach (var (name, brace) in new[] { ("left", left), ("right", right) })
        {
            var (top, bottom) = InkRows(image, brace.Start, brace.End);
            top.ShouldBeLessThanOrEqualTo(contentTop + 1, $"the {name} brace should reach the content's top");
            bottom.ShouldBeGreaterThanOrEqualTo(contentBottom - 1, $"the {name} brace should reach the content's bottom");
        }

        var (leftTop, leftBottom) = InkRows(image, left.Start, left.End);
        var leftMid = (leftTop + leftBottom) / 2;
        var leftCusp = ExtremeInkX(image, left, leftMid - 1, leftMid + 1, leftmost: true);
        leftCusp.ShouldBeLessThan(ExtremeInkX(image, left, leftTop, leftTop, leftmost: true),
            "the left brace's cusp should be left of its top end");
        leftCusp.ShouldBeLessThan(ExtremeInkX(image, left, leftBottom, leftBottom, leftmost: true),
            "the left brace's cusp should be left of its bottom end");

        var (rightTop, rightBottom) = InkRows(image, right.Start, right.End);
        var rightMid = (rightTop + rightBottom) / 2;
        var rightCusp = ExtremeInkX(image, right, rightMid - 1, rightMid + 1, leftmost: false);
        rightCusp.ShouldBeGreaterThan(ExtremeInkX(image, right, rightTop, rightTop, leftmost: false),
            "the right brace's cusp should be right of its top end");
        rightCusp.ShouldBeGreaterThan(ExtremeInkX(image, right, rightBottom, rightBottom, leftmost: false),
            "the right brace's cusp should be right of its bottom end");
    }

    /// <summary>
    /// A brace that is not escaped is still an invisible group: it builds
    /// what its content builds, with no bracket around it.
    /// </summary>
    [Fact]
    public void BoxPath_BareBraces_StayAGroup()
    {
        var group = BuildBox(@"{a + b}", Stix).ShouldNotBeNull();
        group.ShouldNotBeOfType<BracketBox>();
        Render(group, Stix).ShouldBe(Render(BuildBox(@"a + b", Stix).ShouldNotBeNull(), Stix));
    }

    /// <summary>
    /// An unbalanced brace, or an empty set, does not parse on the box
    /// path, so the terminal falls back to the Unicode path, which renders
    /// it as before.
    /// </summary>
    [Theory]
    [InlineData(@"\{x")]
    [InlineData(@"x\}")]
    [InlineData(@"\left\{ x \right.")]
    [InlineData(@"\{\}")]
    public void BoxPath_UnbalancedOrEmpty_DoesNotParse(string latex)
        => BuildBox(latex, Stix).ShouldBeNull();

    /// <summary>
    /// Renders written to <c>obj/</c> for a look by eye, in both fonts.
    /// Asserts that they have ink and that none of it reaches the raster's
    /// outermost rows or columns, which are margin: ink there was cut off at
    /// the edge. The PNGs are not baselines.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", @"\{1, 2, 3\}", "set-1-2-3")]
    [InlineData("STIX2Math", @"\left\{ \frac{1}{2} \right\}", "set-half")]
    [InlineData("DejaVuSans", @"\{1, 2, 3\}", "set-1-2-3")]
    [InlineData("DejaVuSans", @"\left\{ \frac{1}{2} \right\}", "set-half")]
    [InlineData("BabelStone", @"\left\{ \frac{1}{2} \right\}", "set-half")]
    public void BoxPath_WritesPngForInspection(string font, string latex, string name)
    {
        var style = StyleFor(font);
        var box = BuildBox(latex, style).ShouldBeOfType<BracketBox>();
        var image = BoxRasterizer.RenderToRgba(box, style);
        InkPixels(image).ShouldBeGreaterThan(0);
        EdgeInkPixels(image).ShouldBe(0, $"'{latex}' in {font} has ink on the raster's edge");

        Directory.CreateDirectory(OutputDir);
        File.WriteAllBytes(Path.Combine(OutputDir, $"{font}-{name}.png"),
            PngWriter.Encode(image.Pixels, image.Width, image.Height));
    }

    // ── The preparation helper ─────────────────────────────────────────

    /// <summary>
    /// <c>\text</c> and environments have no box yet, so the helper turns
    /// them away, before or after the <c>\boxed</c> and <c>\ce</c>
    /// expansions.
    /// </summary>
    [Theory]
    [InlineData(@"\text{if } x > 0")]
    [InlineData(@"x + \text{y}")]
    [InlineData(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}")]
    [InlineData(@"\boxed{\text{done}}")]
    [InlineData(@"f(x) = \left\{ \begin{array}{ll} x & x > 0 \\ 0 & \text{otherwise} \end{array} \right.")]
    public void Helper_DeclinesTextAndEnvironments(string latex)
        => MarkdownMacros.TryPrepareMathBoxSource(latex, out _).ShouldBeFalse();

    /// <summary>
    /// For any formula without an escaped brace, the helper prepares exactly
    /// what Console.Lib's own sequence prepared (<see cref="ConsoleLibPrepare"/>),
    /// or turns away exactly what it turned away, so moving that sequence into
    /// DIR.Lib changes nothing a brace-free formula renders.
    ///
    /// <para>The corpus is every string argument of every <c>[InlineData]</c>
    /// in <see cref="BracketMathTests"/>, <see cref="MathItalicTests"/> and
    /// <see cref="RadicalJoinTests"/>, the formulas behind the radical
    /// scenes, and <see cref="BoxedAndChemCorpus"/>. A formula the old
    /// sequence turned away for its braces is left out: that is the change.</para>
    /// </summary>
    [Fact]
    public void Helper_MatchesConsoleLibSequence_WithoutEscapedBraces()
    {
        var corpus = new[] { typeof(BracketMathTests), typeof(MathItalicTests), typeof(RadicalJoinTests) }
            .SelectMany(InlineDataStrings)
            .Concat(RadicalScenes)
            .Concat(BoxedAndChemCorpus)
            .Where(latex => !ConsoleLibTurnsAwayBraces(latex))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        corpus.Count.ShouldBeGreaterThan(150);
        corpus.ShouldContain(latex => latex.Contains(@"\boxed"));
        corpus.ShouldContain(latex => latex.Contains(@"\ce"));
        corpus.ShouldContain(latex => ConsoleLibPrepare(latex) == null);

        var mismatches = new List<string>();
        foreach (var latex in corpus)
        {
            var expected = ConsoleLibPrepare(latex);
            var actual = MarkdownMacros.TryPrepareMathBoxSource(latex, out var prepared) ? prepared : null;
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                mismatches.Add($"'{latex}': helper '{actual ?? "<declined>"}', Console.Lib '{expected ?? "<declined>"}'");
        }
        mismatches.ShouldBeEmpty();
    }

    /// <summary><c>\boxed</c> and <c>\ce</c> formulas, and a few that reach the
    /// other steps (the aliases, a <c>\\</c> before a group).</summary>
    private static readonly string[] BoxedAndChemCorpus =
    [
        @"\boxed{x^2 + 1}",
        @"\boxed{\frac{a}{b}}",
        @"x = \boxed{42}",
        @"\boxed{\boxed{x}}",
        @"\boxed {\sqrt{2}}",
        @"\boxed{\ce{H2O}}",
        @"\boxed{\begin{matrix} a \end{matrix}}",
        @"\ce{H2O}",
        @"\ce{NaCl}",
        @"\ce{Fe^{3+}}",
        @"\ce{Fe^3+}",
        @"\ce{SO4^{2-}}",
        @"\ce{Ca(OH)2}",
        @"\ce{2H2 + O2 -> 2H2O}",
        @"\ce{^{238}U -> ^{234}Th + ^{4}_{2}He}",
        @"\ce{HCl(aq) + NaOH(aq) -> NaCl(aq) + H2O(l)}",
        @"\ce{Fe^{3+} + e^- -> Fe^{2+}}",
        @"\ce{\frac{H2O}{2}}",
        @"\ce{H2O(\text {aq})}",
        @"\ce{H+}",
        @"\ce{CO2 + C -> 2CO}",
        @"\ce{A <=> B}",
        @"\dfrac{1}{2} + \tfrac{3}{4}",
        @"\bigl( x \bigr) + \Biggl[ y \Biggr]",
        @"\left( \frac{a}{b} \right)",
        @"a \\{b}",
        @"x\,y\;z\!w",
        @"\textbf{x}",
        @"\beginning",
    ];

    /// <summary>The formulas <see cref="RadicalJoinTests"/>'s scenes parse.</summary>
    private static readonly string[] RadicalScenes =
    [
        @"\sqrt{x^2+y^2}",
        @"\sqrt{2}",
        @"\sqrt{x}",
        @"\sqrt{\frac{a}{b}}",
        @"\frac{-b \pm \sqrt{b^2-4ac}}{2a}",
        @"\sqrt{\left(\frac{a}{b}\right)}",
        @"\sqrt{\frac{\frac{a}{b}}{\frac{c}{d}}}",
    ];

    private static IEnumerable<string> InlineDataStrings(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .SelectMany(method => method.GetCustomAttributes<InlineDataAttribute>())
            .SelectMany(data => data.Data)
            .OfType<string>();

    // ── Console.Lib's sequence, as it stood ────────────────────────────

    /// <summary>
    /// Console.Lib's box-path preparation as it stood before this helper,
    /// copied from <c>src/Console.Lib/MarkdownRenderer.cs</c> on Console.Lib
    /// main at 29489ca (<c>TryRenderMathBox</c>, from its first
    /// <c>CanBuildBox</c> to its <c>ResolveBackslashEscapes</c>). Null where it
    /// turned the formula away to the Unicode path.
    /// </summary>
    private static string? ConsoleLibPrepare(string source)
    {
        if (!CanBuildBox(source)) return null;

        source = MarkdownMacros.NormalizeLatexAliases(source);
        source = MarkdownMacros.ExpandBalancedMacro(source, "boxed", inner => inner);
        source = MarkdownMacros.ExpandBalancedMacro(source, "ce", inner => Mhchem.ToLatex(inner));
        if (!CanBuildBox(source)) return null;
        return MarkdownMacros.ResolveBackslashEscapes(source);
    }

    private static bool CanBuildBox(string source)
        => !MarkdownMacros.ContainsMacro(source, "text")
            && !source.Contains(@"\begin", StringComparison.Ordinal)
            && !ConsoleLibTurnsAwayBraces(source);

    /// <summary>The part of Console.Lib's <c>CanBuildBox</c> that this change
    /// retires: a brace meant as a glyph.</summary>
    private static bool ConsoleLibTurnsAwayBraces(string source)
        => MarkdownMacros.ContainsMacro(source, "lbrace")
            || MarkdownMacros.ContainsMacro(source, "rbrace")
            || HasEscapedBrace(source);

    private static bool HasEscapedBrace(string source)
    {
        for (var i = 0; i < source.Length - 1; i++)
        {
            if (source[i] != '\\') continue;
            var next = source[i + 1];
            if (next is '{' or '}') return true;
            i++; // the escaped character, which for \\ is the second backslash
        }
        return false;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static BoxStyle StyleFor(string font) => new(font switch
    {
        "STIX2Math" => Path.Combine(AppContext.BaseDirectory, "Fonts", "STIX2Math.otf"),
        "DejaVuSans" => Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf"),
        "BabelStone" => Path.Combine(AppContext.BaseDirectory, "Fonts", "BabelStoneXiangqiColour.ttf"),
        _ => throw new ArgumentException($"unknown font '{font}'"),
    }, FontSize, Ink);

    /// <summary>
    /// Prepares <paramref name="latex"/> with
    /// <see cref="MarkdownMacros.TryPrepareMathBoxSource"/>, then parses and
    /// builds it at <paramref name="style"/>. Null when the helper turns it
    /// away or it does not lex or parse, which is where the terminal falls
    /// back to the Unicode path. LALR.CC throws for both (<c>LexerException</c>,
    /// <c>ParseErrorException</c>), and both are
    /// <see cref="InvalidOperationException"/>s.
    /// </summary>
    private static Box? BuildBox(string latex, BoxStyle style)
    {
        if (!MarkdownMacros.TryPrepareMathBoxSource(latex, out var source)) return null;
        var parser = Latex.BuildParser(new BoxBuildingVisitor(style));
        try
        {
            using var lexer = BytesLexer.FromString(source, MarkdownMacros.MathLexerTable);
            using var tokens = new SyncLATokenIterator(lexer);
            var item = parser.ParseInput(tokens, debugger: null);
            return item.IsError || item.Content is not Func<BoxStyle, Box> builder ? null : builder(style);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] Render(Box box, BoxStyle style) => BoxRasterizer.RenderToRgba(box, style).Pixels;

    private static int InkPixels(RgbaImage image)
    {
        var ink = 0;
        for (var i = 3; i < image.Pixels.Length; i += 4)
            if (image.Pixels[i] > 0) ink++;
        return ink;
    }

    private static bool Inked(RgbaImage image, int x, int y) => image.Pixels[(y * image.Width + x) * 4 + 3] > 0;

    /// <summary>The image's inked columns as runs of adjacent columns, left to
    /// right; a column with no ink at all ends a run.</summary>
    private static List<(int Start, int End)> InkColumnRuns(RgbaImage image)
    {
        var runs = new List<(int Start, int End)>();
        var start = -1;
        for (var x = 0; x <= image.Width; x++)
        {
            var inked = false;
            for (var y = 0; x < image.Width && y < image.Height && !inked; y++)
                inked = Inked(image, x, y);
            if (inked && start < 0) start = x;
            else if (!inked && start >= 0)
            {
                runs.Add((start, x - 1));
                start = -1;
            }
        }
        return runs;
    }

    /// <summary>The first and last rows with ink in columns
    /// <paramref name="x0"/> to <paramref name="x1"/>.</summary>
    private static (int Top, int Bottom) InkRows(RgbaImage image, int x0, int x1)
    {
        var (top, bottom) = (-1, -1);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if (!Inked(image, x, y)) continue;
                if (top < 0) top = y;
                bottom = y;
                break;
            }
        }
        top.ShouldBeGreaterThanOrEqualTo(0, $"columns {x0}..{x1} have no ink");
        return (top, bottom);
    }

    /// <summary>The leftmost (or rightmost) column of <paramref name="run"/> with
    /// ink in rows <paramref name="y0"/> to <paramref name="y1"/>.</summary>
    private static int ExtremeInkX(RgbaImage image, (int Start, int End) run, int y0, int y1, bool leftmost)
    {
        var found = -1;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = run.Start; x <= run.End; x++)
            {
                if (!Inked(image, x, y)) continue;
                if (found < 0 || (leftmost ? x < found : x > found)) found = x;
            }
        }
        found.ShouldBeGreaterThanOrEqualTo(0, $"rows {y0}..{y1} of columns {run.Start}..{run.End} have no ink");
        return found;
    }

    /// <summary>Inked pixels in the image's outermost rows and columns.</summary>
    private static int EdgeInkPixels(RgbaImage image)
    {
        var ink = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (y != 0 && y != image.Height - 1 && x != 0 && x != image.Width - 1) continue;
                if (image.Pixels[(y * image.Width + x) * 4 + 3] > 0) ink++;
            }
        }
        return ink;
    }
}
