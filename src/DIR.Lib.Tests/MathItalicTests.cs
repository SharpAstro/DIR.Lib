using DIR.Lib.Markdown;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;
using SharpAstro.Fonts;
using SharpAstro.Png;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Variables in parsed math are set in math italic, as TeX sets them, while
/// numbers, operator names, upright Greek, <c>\mathrm{...}</c> and chemistry
/// stay upright. <see cref="BoxBuildingVisitor"/> builds a variable as a
/// <see cref="MathGlyphBox"/>, which remaps the letter to its U+1D400-block
/// italic when the font's cmap has it (STIX Two Math does) and leaves it
/// alone when the font lacks the block (DejaVu Sans).
///
/// <para>The tests read what a box tree paints: a <see cref="PaintRecorder"/>
/// keeps every string a glyph box hands the renderer, so an assertion names
/// the codepoints that reach the rasterizer. Where the claim is "the same as
/// before", it is proved by rendering instead, byte for byte.</para>
/// </summary>
public sealed class MathItalicTests
{
    private const float FontSize = 32f;

    private static readonly string StixPath =
        Path.Combine(AppContext.BaseDirectory, "Fonts", "STIX2Math.otf");

    private static readonly string DejaVuPath =
        Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");

    private static readonly RGBAColor32 Ink = new(0, 0, 0, 255);

    private static readonly BoxStyle Stix = new(StixPath, FontSize, Ink);

    /// <summary>Where the renders land for a look by eye; never compared.</summary>
    private static string OutputDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "math-italic-output");

    /// <summary>
    /// The math-italic form of an ASCII letter, worked out here rather than
    /// asked of the code under test: lowercase from U+1D44E, uppercase from
    /// U+1D434, and h at U+210E (PLANCK CONSTANT), because Unicode left its
    /// slot in the block empty.
    /// </summary>
    private static string Italic(char letter) => letter switch
    {
        'h' => "ℎ",
        >= 'a' and <= 'z' => char.ConvertFromUtf32(0x1D44E + (letter - 'a')),
        >= 'A' and <= 'Z' => char.ConvertFromUtf32(0x1D434 + (letter - 'A')),
        _ => throw new ArgumentOutOfRangeException(nameof(letter)),
    };

    /// <summary>True when <paramref name="text"/> holds a math-alphanumeric
    /// codepoint (U+1D400..U+1D7FF) or U+210E, the italic h.</summary>
    private static bool HasMathItalic(string text) =>
        text.EnumerateRunes().Any(r => r.Value == 0x210E || r.Value is >= 0x1D400 and <= 0x1D7FF);

    [Fact]
    public void ItalicOracle_MatchesTheUnicodeChart()
    {
        Italic('a').ShouldBe("𝑎");
        Italic('x').ShouldBe("𝑥");
        Italic('E').ShouldBe("𝐸");
        Italic('h').ShouldBe("ℎ");
    }

    // ── Variables are math italic ──────────────────────────────────────

    [Fact]
    public void Variable_IsMathGlyphBoxInItalic()
    {
        var box = Build("x", Stix).ShouldBeOfType<MathGlyphBox>();
        box.Text.ShouldBe(Italic('x'));
        Painted("x", Stix).ShouldBe([Italic('x')]);
    }

    [Fact]
    public void Variable_h_IsPlanckConstant()
        => Painted("h", Stix).ShouldBe(["ℎ"]);

    [Fact]
    public void EmcSquared_LettersItalic_DigitAndEqualsUpright()
        => Painted("E = mc^2", Stix).ShouldBe(
            [Italic('E'), "=", Italic('m'), Italic('c'), "2"], ignoreOrder: true);

    [Fact]
    public void Fraction_NumeratorAndDenominatorItalic()
        => Painted(@"\frac{a}{b}", Stix).ShouldBe([Italic('a'), Italic('b')], ignoreOrder: true);

    // ── What stays upright ─────────────────────────────────────────────

    [Fact]
    public void OperatorName_StaysUpright_ArgumentItalic()
        => Painted(@"\sin x", Stix).ShouldBe(["sin", Italic('x')], ignoreOrder: true);

    [Fact]
    public void Number_StaysUpright()
        => Painted("2x", Stix).ShouldBe(["2", Italic('x')], ignoreOrder: true);

    /// <summary>
    /// Greek comes from the command table at its base codepoint, never
    /// remapped: uppercase Gamma upright, TeX's default, and alpha as the
    /// font draws U+03B1 (STIX Two Math slants it by design).
    /// </summary>
    [Fact]
    public void Greek_KeepsItsBaseCodepoint()
    {
        Painted(@"\Gamma", Stix).ShouldBe(["Γ"]);
        Painted(@"\alpha", Stix).ShouldBe(["α"]);
    }

    [Fact]
    public void BigOperator_StaysUpright()
        => Painted(@"\sum_{i} x_i", Stix).ShouldBe(
            ["∑", Italic('i'), Italic('x'), Italic('i')], ignoreOrder: true);

    // ── \mathrm ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>\mathrm{d}x</c>: the d upright, and the x after it italic again,
    /// so the upright scope ends with the argument.
    /// </summary>
    [Fact]
    public void Mathrm_KeepsItsLettersUpright_AndEndsWithItsArgument()
        => Painted(@"\mathrm{d}x", Stix).ShouldBe(["d", Italic('x')], ignoreOrder: true);

    /// <summary>
    /// TeX's <c>\mathrm</c> sets every letter in its argument upright, at any
    /// depth, scripts included.
    /// </summary>
    [Fact]
    public void Mathrm_ReachesNestedScripts()
        => Painted(@"\mathrm{e^{x}} y", Stix).ShouldBe(["e", "x", Italic('y')], ignoreOrder: true);

    /// <summary>
    /// A multi-letter argument is the upright run it was before variables
    /// went italic: <c>\mathrm{x + y}</c> paints what main painted for
    /// <c>x + y</c>, a hand-built tree of plain <see cref="GlyphBox"/>es.
    /// </summary>
    [Fact]
    public void Mathrm_PaintsThePlainGlyphTree()
        => Render(Build(@"\mathrm{x + y}", Stix), Stix).ShouldBe(Render(PlainXPlusY(Stix), Stix));

    // ── \mathrm on the Unicode path ────────────────────────────────────

    /// <summary>
    /// On the Unicode path <c>\mathrm{X}</c> is <c>X</c>, and so it stays
    /// when the formula around it fails to parse for another reason (here a
    /// trailing <c>+</c>) and falls back to its source text.
    /// </summary>
    [Theory]
    [InlineData(@"\mathrm{d}x", "dx")]
    [InlineData(@"\mathrm{d}x +", "dx +")]
    public void UnicodePath_MathrmIsItsArgument(string latex, string expected)
        => MarkdownMacros.RenderMathUnicode(latex).ShouldBe(expected);

    // ── Chemistry stays upright ────────────────────────────────────────

    [Theory]
    [InlineData("H2O", new[] { "H", "2", "O" })]
    [InlineData("^{14}_{6}C", new[] { "14", "6", "C" })]
    [InlineData("NaCl", new[] { "N", "a", "C", "l" })]
    [InlineData("Fe^{3+}", new[] { "F", "e", "3", "+" })]
    public void Chemistry_ElementSymbolsUpright(string body, string[] expected)
        => Painted(@"\ce{" + body + "}", Stix).ShouldBe(expected, ignoreOrder: true);

    /// <summary>
    /// Nothing in a reaction is italic: not the symbols, not a state
    /// marker's letters, not an electron's e.
    /// </summary>
    [Theory]
    [InlineData("HCl(aq) + NaOH(aq) -> NaCl(aq) + H2O(l)")]
    [InlineData("Fe^{3+} + e^- -> Fe^{2+}")]
    [InlineData(@"\frac{H2O}{2}")]
    public void Chemistry_NoLetterIsItalic(string body)
    {
        var painted = Painted(@"\ce{" + body + "}", Stix);
        painted.ShouldNotBeEmpty();
        painted.ShouldAllBe(t => !HasMathItalic(t));
    }

    /// <summary>
    /// <c>\ce{...}</c> rasterizes byte for byte as it did before variables
    /// went italic. The reference is the LaTeX main's <see cref="Mhchem.ToLatex"/>
    /// emitted for the body, built inside one <c>\mathrm</c>, which is the
    /// same box tree main built (its letters were plain upright glyphs).
    /// </summary>
    [Theory]
    [InlineData("H2O", "H_{2}O")]
    [InlineData("^{14}_{6}C", @"\null^{14}_{6}C")]
    [InlineData("NaCl", "{Na}{Cl}")]
    [InlineData("Fe^3+", @"{Fe}^{3\plus}")]
    [InlineData("SO4^{2-}", @"SO_{4}^{2\minus}")]
    [InlineData("Ca(OH)2", "{Ca}(OH)_{2}")]
    [InlineData("2H2 + O2 -> 2H2O", @"2H_{2} + O_{2} \to  2H_{2}O")]
    [InlineData("^{238}U -> ^{234}Th + ^{4}_{2}He", @"\null^{238}U \to  \null^{234}{Th} + \null^{4}_{2}{He}")]
    public void Chemistry_RasterizesAsBefore(string body, string mainLatex)
    {
        var reference = @"\mathrm{" + mainLatex + "}";
        Painted(reference, Stix).ShouldAllBe(t => !HasMathItalic(t));
        Render(Build(@"\ce{" + body + "}", Stix), Stix).ShouldBe(Render(Build(reference, Stix), Stix));
    }

    // ── A font without the math-alphanumeric block ─────────────────────

    /// <summary>
    /// DejaVu Sans has no italic in the U+1D400 block, so a variable falls
    /// back to its upright letter and <c>x + y</c> rasterizes byte for byte
    /// as main did: the same pixels as the hand-built tree main's visitor
    /// made, plain <see cref="GlyphBox"/>es with its 0.25 em operator kerns.
    /// </summary>
    [Fact]
    public void DejaVuSans_XPlusY_RasterizesAsMain()
    {
        var dejaVu = new BoxStyle(DejaVuPath, FontSize, Ink);
        Painted("x + y", dejaVu).ShouldBe(["x", "+", "y"], ignoreOrder: true);
        Render(Build("x + y", dejaVu), dejaVu).ShouldBe(Render(PlainXPlusY(dejaVu), dejaVu));
    }

    // ── Parse once, build at any size ──────────────────────────────────

    /// <summary>
    /// The visitor stays pure at parse time: a builder parsed by a visitor at
    /// 32 px and invoked at 24 px rasterizes exactly what a parse at 24 px
    /// does. Console.Lib parses a formula once and calls its builder at
    /// several sizes while fitting it to the width; anything the parse took
    /// from the visitor's own style would render a shrunk formula at the
    /// wrong size. The corpus runs <c>\mathrm</c>'s upright scope and
    /// <c>\ce</c> through it.
    /// </summary>
    [Theory]
    [InlineData("E = mc^2")]
    [InlineData(@"\sin x + \mathrm{d}x")]
    [InlineData(@"\frac{a}{b}")]
    [InlineData(@"\mathrm{e^{x}} y")]
    [InlineData(@"\int_0^\infty e^{-x} \mathrm{d}x")]
    [InlineData(@"\sqrt[n]{x^2+1}")]
    [InlineData(@"\ce{H2O}")]
    [InlineData(@"\ce{^{14}_{6}C}")]
    [InlineData(@"\ce{2H2 + O2 -> 2H2O}")]
    public void Builder_ParsedAt32_InvokedAt24_MatchesParseAt24(string latex)
    {
        var at32 = new BoxStyle(StixPath, 32f, Ink);
        var at24 = new BoxStyle(StixPath, 24f, Ink);

        var parsedAt32 = Parse(latex, at32).ShouldNotBeNull();
        var reused = Render(parsedAt32(at24), at24);

        var visitorAt24 = new BoxBuildingVisitor(at24);
        var parsedAt24 = Parse(latex, visitorAt24).ShouldNotBeNull();
        var fresh = Render(visitorAt24.Build(parsedAt24), at24);

        reused.ShouldBe(fresh);
        Render(parsedAt32(at32), at32).Length.ShouldNotBe(fresh.Length, "the two sizes should differ");
    }

    // ── Renders for the eye ────────────────────────────────────────────

    /// <summary>
    /// Renders written to <c>obj/</c> for a look by eye. Asserts only that
    /// they have ink; the PNGs are not baselines.
    /// </summary>
    [Theory]
    [InlineData(@"E = mc^2", "e-mc2")]
    [InlineData(@"\sin x + \mathrm{d}x", "sin-x-dx")]
    [InlineData(@"\ce{H2O}", "ce-h2o")]
    public void WritesPngForInspection(string latex, string name)
    {
        var image = BoxRasterizer.RenderToRgba(Build(latex, Stix), Stix);
        image.Pixels.Where((_, i) => i % 4 == 3).Count(a => a > 0).ShouldBeGreaterThan(0);

        Directory.CreateDirectory(OutputDir);
        File.WriteAllBytes(Path.Combine(OutputDir, name + ".png"),
            PngWriter.Encode(image.Pixels, image.Width, image.Height));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>What main's visitor built for <c>x + y</c>.</summary>
    private static Box PlainXPlusY(BoxStyle style)
    {
        var kern = new KernBox(style.FontSize * 0.25f);
        return new HBox(new GlyphBox("x", style), kern, new GlyphBox("+", style), kern, new GlyphBox("y", style));
    }

    /// <summary>
    /// Prepares <paramref name="latex"/> for the box path with
    /// <see cref="MarkdownMacros.TryPrepareMathBoxSource"/>, as the terminal
    /// does before it parses, then parses it with <paramref name="visitor"/>.
    /// Returns the deferred builder, or null when the helper turns the formula
    /// away or it does not lex or parse.
    /// </summary>
    private static Func<BoxStyle, Box>? Parse(string latex, BoxBuildingVisitor visitor)
    {
        if (!MarkdownMacros.TryPrepareMathBoxSource(latex, out var source)) return null;
        var parser = Latex.BuildParser(visitor);
        try
        {
            using var lexer = BytesLexer.FromString(source, MarkdownMacros.MathLexerTable);
            using var tokens = new SyncLATokenIterator(lexer);
            var item = parser.ParseInput(tokens, debugger: null);
            return item.IsError ? null : item.Content as Func<BoxStyle, Box>;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Func<BoxStyle, Box>? Parse(string latex, BoxStyle style)
        => Parse(latex, new BoxBuildingVisitor(style));

    private static Box Build(string latex, BoxStyle style)
        => Parse(latex, style).ShouldNotBeNull($"'{latex}' should parse on the box path")(style);

    private static byte[] Render(Box box, BoxStyle style) => BoxRasterizer.RenderToRgba(box, style).Pixels;

    /// <summary>
    /// The strings <paramref name="latex"/>'s box tree hands the renderer,
    /// in paint order, painted the way <see cref="BoxRasterizer.RenderToRgba"/>
    /// paints. An empty string (<c>\null</c>'s glyph) is left out.
    /// </summary>
    private static List<string> Painted(string latex, BoxStyle style)
    {
        var box = Build(latex, style);
        var margin = (int)MathF.Ceiling(style.FontSize * 0.15f);
        using var recorder = new PaintRecorder(
            (uint)((int)MathF.Ceiling(box.Width) + margin * 2),
            (uint)((int)MathF.Ceiling(box.TotalHeight) + margin * 2));
        box.Draw(recorder, margin, margin + box.Height, style);
        return recorder.Texts;
    }

    private sealed class PaintRecorder(uint width, uint height) : RgbaImageRenderer(width, height)
    {
        public List<string> Texts { get; } = [];

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout,
            TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
        {
            if (!text.IsEmpty) Texts.Add(text.ToString());
            base.DrawText(text, fontFamily, fontSize, fontColor, layout, horizAlignment, vertAlignment);
        }
    }
}
