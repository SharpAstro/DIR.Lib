using System.Reflection;
using DIR.Lib.Markdown;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;
using SharpAstro.Fonts;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The display formulas of a real document, StarNet2_Architecture.md (a model's
/// architecture notes, written for VS Code's preview), on both math paths. VS Code
/// draws every one with KaTeX. On the box path all but one used to be turned away
/// (eight for a <c>\text</c>, one for a prime), and the one that built drew
/// <c>\hat</c> as its name; on the Unicode path several printed a raw <c>\name</c>.
/// </summary>
public sealed class VsCodeParityTests
{
    private const float FontSize = 32f;
    private static readonly RGBAColor32 Ink = new(0, 0, 0, 255);
    private static readonly BoxStyle Stix = new(Path.Combine(AppContext.BaseDirectory, "Fonts", "STIX2Math.otf"), FontSize, Ink);

    /// <summary>Where the renders land for a look by eye; never compared.</summary>
    private static string OutputDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "vscode-parity-output");

    public static readonly TheoryData<string, string> StarNet2Formulas = new()
    {
        { "conv", @"Y_{c_{\text{out}}, i, j} = B_{c_{\text{out}}} + \sum_{c_{\text{in}}=0}^{C_{\text{in}}-1} \sum_{u=-k}^{k} \sum_{v=-k}^{k} X_{c_{\text{in}}, \, i \cdot s + u, \, j \cdot s + v} \cdot W_{c_{\text{out}}, c_{\text{in}}, u, v}" },
        { "floor", @"H_{\text{out}} = \left\lfloor \frac{H_{\text{in}} + 2p - K}{2} \right\rfloor + 1 = \frac{H_{\text{in}}}{2}" },
        { "concat", @"\text{Concat}([X_{\text{upsampled}}, X_{\text{encoder}}]) = Z \quad \text{where } Z \in \mathbb{R}^{B \times (C_1 + C_2) \times H \times W}" },
        { "batchnorm", @"\hat{x} = \frac{x - \mu_B}{\sqrt{\sigma_B^2 + \epsilon}} \cdot \gamma + \beta" },
        { "shadows", @"\text{Shadows} = \max\left(0.0, \, \text{median} + (-2.8) \cdot \frac{\text{MAD}}{0.67448975}\right)" },
        { "mtf", @"f(x', m) = \frac{(m - 1)x'}{(2m - 1)x' - m}" },
        { "mask", @"\text{Mask} = \text{Input} - \text{Starless}" },
        { "unscreen", @"\text{Result} = \text{Starless} + S \cdot (1.0 - \text{Starless})" },
        { "stars", @"S = \frac{\text{Input} - \text{Starless}}{1.0 - \text{Starless}}" },
    };

    [Theory]
    [MemberData(nameof(StarNet2Formulas))]
    public void StarNet2Formula_BuildsOnTheBoxPath_WithNoCommandDrawnAsItsName(string name, string latex)
    {
        var box = BuildBox(latex, Stix).ShouldNotBeNull($"'{name}' should build on the box path");
        BoxesIn<GlyphBox>(box).Where(g => g.Text.Contains('\\')).Select(g => g.Text)
            .ShouldBeEmpty($"'{name}' draws a command as its name");
    }

    [Theory]
    [MemberData(nameof(StarNet2Formulas))]
    public void StarNet2Formula_ReadsOnTheUnicodePath_WithNoCommandLeft(string name, string latex)
        => MarkdownMacros.RenderMathUnicode(latex).ShouldNotContain("\\", customMessage: $"'{name}'");

    /// <summary>
    /// The inline formulas of the same document, and the constructs behind
    /// them, as KaTeX reads them, where they used to print a raw command or the
    /// wrong operator.
    /// </summary>
    [Theory]
    [InlineData(@"7 \times 7", "7 × 7")]
    [InlineData(@"8\times", "8×")]
    [InlineData(@"\to", "→")]
    [InlineData(@"= \mathbf{1024}", "= 𝟏𝟎𝟐𝟒")]
    [InlineData(@"\hat{x}", "x̂")]
    [InlineData(@"\bar{\psi} + \vec{v}", "ψ̄ + v⃗")]
    [InlineData(@"\overline{AB}", "A̅B̅")]
    [InlineData(@"f'(x) + x''", "f′(x) + x″")]
    [InlineData(@"\left\lfloor \frac{a}{2} \right\rfloor", "⌊a⁄2⌋")]
    [InlineData(@"\lceil x \rceil + \langle v \rangle", "⌈x⌉ + ⟨v⟩")]
    [InlineData(@"\max(0, x)", "max(0, x)")]
    [InlineData(@"Z \in \mathbb{R}", "Z ∈ ℝ")]
    [InlineData(@"\pm 0.2", "±0.2")]
    [InlineData(@"x \pm 1", "x ± 1")]
    [InlineData(@"\ce{H+}", "H+")]
    [InlineData(@"\ce{e-}", "e-")]
    [InlineData(@"x_{i,j}", "x_i,j")]
    public void UnicodePath_ReadsAsKaTeXSetsIt(string latex, string expected)
        => MarkdownMacros.RenderMathUnicode(latex).ShouldBe(expected);

    /// <summary>
    /// A formula that still does not parse falls back to its source as written:
    /// an operand <see cref="MarkdownMacros.SupplyMissingOperands"/> added is
    /// not printed (round 1 B1: <c>$x^*$</c> read <c>x^* \null</c>). A lone
    /// <c>*</c> is an asterisk (TeX's <c>\ast</c>), not the middle dot of a
    /// product: an optimum, a conjugate, an adjoint (B3: <c>x^{*}</c> read
    /// <c>x^·</c>). A <c>'</c> after no atom does not throw the formula back
    /// to its source (it did once primes lexed); it reads as a prime, as KaTeX
    /// sets every <c>'</c> in math.
    /// </summary>
    [Theory]
    [InlineData(@"x^*", @"x^*")]
    [InlineData(@"x^+", @"x^+")]
    [InlineData(@"\boxed{x^*}", @"[x^*]")]
    [InlineData(@"x^{*}", "x^∗")]
    [InlineData(@"\theta^{*} = \arg\min_x f(x)", "θ^∗ = arg minₓ f(x)")]
    [InlineData(@"\alpha = '\beta'", "α = ′β′")]
    public void UnicodePath_FallsBackAsWritten_AndALoneStarIsAnAsterisk(string latex, string expected)
        => MarkdownMacros.RenderMathUnicode(latex).ShouldBe(expected);

    /// <summary>
    /// An accent's mark never lands inside a placeholder still to be spliced
    /// back (a <c>\text</c>, a math space, a character the lexer has no rule
    /// for), which would break its name: <c>\overline{\text{abc}}</c> printed
    /// the placeholder's name (round 1 B2). A letter written straight after a
    /// placeholder lexes into the placeholder's own command, so it takes no
    /// mark either: <c>\overline{a\,b}</c> overlines the a only, but keeps
    /// every character.
    /// </summary>
    [Fact]
    public void UnicodePath_AnAccentLeavesPlaceholdersWhole()
    {
        var overline = ((char)0x0305).ToString();
        var hat = ((char)0x0302).ToString();
        MarkdownMacros.RenderMathUnicode(@"\overline{\text{ab}}").ShouldBe("ab");
        MarkdownMacros.RenderMathUnicode(@"\overline{a\,b}").ShouldBe($"a{overline} b");
        MarkdownMacros.RenderMathUnicode(@"\overline{a < b}").ShouldNotContain("\\");
        MarkdownMacros.RenderMathUnicode(@"\hat{x\,}").ShouldBe($"x{hat} ");
    }

    [Fact]
    public void BoxPath_ALoneStarIsAnAsterisk()
    {
        var glyphs = BoxesIn<GlyphBox>(BuildBox(@"x^{*}", Stix).ShouldNotBeNull()).Select(g => g.Text).ToList();
        glyphs.ShouldContain("∗");
        glyphs.ShouldNotContain("·");
    }

    /// <summary>
    /// <c>\vec</c>'s arrow is centred over its base in a font with no MATH
    /// accent attachment (DejaVu Sans), as in one with it. Its glyph is the
    /// combining U+20D7, which has no advance and draws its ink left of its
    /// origin, so centring its origin put the arrow left of the letter (round
    /// 1 B4).
    /// </summary>
    [Theory]
    [InlineData("STIX2Math.otf")]
    [InlineData("DejaVuSans.ttf")]
    public void BoxPath_TheVecArrowIsCentredOverItsBase(string font)
    {
        var style = new BoxStyle(Path.Combine(AppContext.BaseDirectory, "Fonts", font), 48f, Ink);
        var image = BoxRasterizer.RenderToRgba(BuildBox(@"\vec{v}", style).ShouldNotBeNull(), style);
        var runs = InkRowRuns(image);
        runs.Count.ShouldBeGreaterThanOrEqualTo(2, "the arrow and the v");
        var arrow = InkColumnCentre(image, runs[0]);
        var letter = InkColumnCentre(image, runs[^1]);
        Math.Abs(arrow - letter).ShouldBeLessThanOrEqualTo(48f * 0.15f, $"arrow centre {arrow:F1}, v centre {letter:F1}");
    }

    /// <summary>
    /// An operator with no operand on a side gets <c>\nulloperand</c> there, an
    /// empty atom of the supplier's own (so a failed parse can drop it again); a
    /// minus at either end, and text, are left alone.
    /// </summary>
    [Theory]
    [InlineData(@"8\times", @"8\times \nulloperand")]
    [InlineData(@"\to", @"\nulloperand \to \nulloperand")]
    [InlineData(@"= x", @"\nulloperand = x")]
    [InlineData(@"(+1, 2)", @"(\nulloperand +1, 2)")]
    [InlineData(@"\{x \mid\}", @"\{x \mid \nulloperand\}")]
    [InlineData(@"-x", @"-x")]
    [InlineData(@"e-", @"e-")]
    [InlineData(@"a + b", @"a + b")]
    [InlineData(@"\text{= x +} y", @"\text{= x +} y")]
    [InlineData(@"\textrm{a =}", @"\textrm{a =}")]
    public void SupplyMissingOperands_FillsOnlyAnEmptySide(string latex, string expected)
        => MarkdownMacros.SupplyMissingOperands(latex).ShouldBe(expected);

    [Fact]
    public void BoxPath_TimesIsACross()
        => BoxesIn<GlyphBox>(BuildBox(@"7 \times 7", Stix).ShouldNotBeNull()).Select(g => g.Text).ShouldContain("×");

    /// <summary>
    /// An accent's ink stands clear of its base's, a blank row between, over a
    /// short base and a tall one. <see cref="AccentBox"/> used to put the
    /// accent's top just above the base, which drew a hat's band of ink across
    /// the top of an x.
    /// </summary>
    [Theory]
    [InlineData(@"\hat{x}")]
    [InlineData(@"\hat{A}")]
    [InlineData(@"\bar{\psi}")]
    [InlineData(@"\tilde{n}")]
    [InlineData(@"\dot{x}")]
    public void BoxPath_AnAccentsInkStandsClearOfItsBase(string latex)
    {
        var box = BuildBox(latex, Stix).ShouldNotBeNull();
        BoxesIn<AccentBox>(box).ShouldHaveSingleItem();
        InkRowRuns(BoxRasterizer.RenderToRgba(box, Stix)).Count.ShouldBeGreaterThanOrEqualTo(2, $"'{latex}': the accent's ink and the base's are one run");
    }

    /// <summary>The floor of a fraction stretches to it, as KaTeX's <c>\left\lfloor</c> does.</summary>
    [Fact]
    public void BoxPath_ANamedPairStretchesToItsContent()
    {
        var pair = BuildBox(@"\left\lfloor \frac{a}{2} \right\rfloor", Stix).ShouldNotBeNull();
        var glyph = new GlyphBox("⌊", Stix);
        (pair.Height + pair.Depth).ShouldBeGreaterThan(1.3f * (glyph.Height + glyph.Depth));
    }

    [Theory(Explicit = true)]
    [MemberData(nameof(StarNet2Formulas))]
    public void StarNet2Formula_WritePngForInspection(string name, string latex)
    {
        var box = BuildBox(latex, Stix).ShouldNotBeNull($"'{latex}' should build on the box path");
        var image = BoxRasterizer.RenderToRgba(box, Stix);
        Directory.CreateDirectory(OutputDir);
        File.WriteAllBytes(Path.Combine(OutputDir, $"{name}.png"), PngWriter.Encode(OnWhite(image), image.Width, image.Height));
    }

    /// <summary>The raster composited onto white, so a viewer that ignores alpha shows the ink.</summary>
    private static byte[] OnWhite(RgbaImage image)
    {
        var pixels = (byte[])image.Pixels.Clone();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3] / 255f;
            for (int c = 0; c < 3; c++) pixels[i + c] = (byte)(pixels[i + c] * a + 255 * (1 - a));
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    /// <summary>The mean column of the ink in the rows of <paramref name="rows"/>.</summary>
    private static float InkColumnCentre(RgbaImage image, (int Start, int End) rows)
    {
        long sum = 0, count = 0;
        for (int y = rows.Start; y <= rows.End; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.Pixels[(y * image.Width + x) * 4 + 3] > 64) { sum += x; count++; }
        return count == 0 ? -1 : (float)sum / count;
    }

    /// <summary>The runs of rows that hold ink, top to bottom, each as its first and last row.</summary>
    private static List<(int Start, int End)> InkRowRuns(RgbaImage image)
    {
        var runs = new List<(int, int)>();
        int start = -1;
        for (int y = 0; y <= image.Height; y++)
        {
            var inked = false;
            for (int x = 0; y < image.Height && x < image.Width && !inked; x++)
                inked = image.Pixels[(y * image.Width + x) * 4 + 3] > 64;
            if (inked && start < 0) start = y;
            if (!inked && start >= 0) { runs.Add((start, y - 1)); start = -1; }
        }
        return runs;
    }

    /// <summary>Every <typeparamref name="T"/> in <paramref name="root"/>'s tree.
    /// Boxes keep their children in private fields, so this walks those, as
    /// <see cref="BraceSetTests"/>' probe does.</summary>
    private static List<T> BoxesIn<T>(Box root) where T : Box
    {
        var found = new List<T>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node)) continue;
            if (node is T match) found.Add(match);
            for (var type = node.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    switch (field.GetValue(node))
                    {
                        case Box child:
                            pending.Push(child);
                            break;
                        case System.Collections.IEnumerable items and not string:
                            foreach (var item in items)
                                if (item is Box b) pending.Push(b);
                            break;
                    }
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Prepares <paramref name="latex"/> with <see cref="MarkdownMacros.TryPrepareMathBoxSource"/>,
    /// then parses and builds it at <paramref name="style"/>; null where the terminal would take
    /// the Unicode path instead.
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
}
