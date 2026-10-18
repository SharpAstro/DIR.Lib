using System;
using System.IO;
using DIR.Lib.Markdown;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;

namespace DIR.Lib.Tests;

/// <summary>
/// The display-math corpus the raster caches are checked and timed on: <c>MathRasterCacheTests</c> renders
/// every formula with the caches on and with them bypassed and compares the bytes, and the
/// <c>MathRaster</c> benchmark times the same formulas. The benchmark project compiles this file in by
/// link, so the two read one corpus. Nothing here depends on xunit.
/// </summary>
public static class MathRasterCorpus
{
    /// <summary>
    /// Console.Lib's <c>MathCorpus</c> formulas (<c>Console.Lib.Tests/MathCorpus.cs</c>), copied as data,
    /// then radicals tall enough to take STIX Two Math's size variants and its assembly, a radical with an
    /// index, and sets and a half-open interval whose delimiters stretch. The last of Console.Lib's cannot
    /// be lexed by the math grammar; it is kept as written, and <see cref="Build"/> gives null for it, where
    /// Console.Lib takes the Unicode path.
    /// </summary>
    public static readonly (string Name, string Source)[] Formulas =
    [
        ("quadratic", @"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}"),
        ("hypotenuse", @"\sqrt{x^2 + y^2}"),
        ("fraction-sum", @"\frac{a}{b} + \frac{c}{d}"),
        ("gauss-sum", @"\sum_{i=1}^{n} i = \frac{n(n+1)}{2}"),
        ("gaussian-integral", @"\int_0^\infty e^{-x^2} dx = \frac{\sqrt{\pi}}{2}"),
        ("left-right", @"\left( \frac{a}{b} \right)^2"),
        ("euler", @"e^{i\pi} + 1 = 0"),
        ("mass-energy", @"E = mc^2"),
        ("pythagoras", @"a^2 + b^2 = c^2"),
        ("continued-fraction", @"\frac{1}{1 + \frac{1}{1 + \frac{1}{x}}}"),
        ("definite-integral", @"\int_a^b f(x) dx = F(b) - F(a)"),
        ("fine-structure", @"\alpha = \frac{e^2}{4\pi\varepsilon_0 \hbar c}"),
        ("fourteen-terms", @"a + b + c + d + e + f + g + h + i + j + k + l + m + n"),
        ("limit", @"\lim_{x \to 0} \frac{\sin x}{x} = 1"),
        ("binomial", @"\binom{n}{k}"),
        ("vectors", @"\vec{F} = m \vec{a}"),
        ("factorial-unlexable", @"\prod_{k=1}^{n} k = n!"),
        ("radical-nested-fraction", @"\sqrt{\frac{\frac{a}{b}}{\frac{c}{d}}}"),
        ("radical-paren-fraction", @"\sqrt{\left(\frac{a}{b}\right)}"),
        ("radical-continued-fraction", @"\sqrt{1 + \frac{1}{1 + \frac{1}{1 + \frac{1}{x}}}}"),
        ("radical-index", @"\sqrt[3]{\frac{a^2 + b^2}{c}}"),
        ("set-fraction", @"\left\{ \frac{1}{2} \right\}"),
        ("set-nested-fraction", @"\left\{ \frac{\frac{a}{b}}{\frac{c}{d}} \right\}"),
        ("set-literal", @"A = \{1, 2, 3\}"),
        ("set-of-fractions", @"\left\{ \frac{1}{2}, \frac{1}{3}, \frac{1}{4} \right\}"),
        ("half-open", @"\left[ \frac{a}{b}, 1 \right)"),
    ];

    /// <summary>The font sizes the byte-identity check renders STIX Two Math at.</summary>
    public static readonly float[] StixSizes = [32f, 24f, 12f, 10f];

    /// <summary>The fixture font <paramref name="file"/> (<c>STIX2Math.otf</c>, <c>DejaVuSans.ttf</c>): next to
    /// the running assembly when the build copied it there, otherwise found by walking up to the source tree
    /// (a BenchmarkDotNet child process runs from a generated folder of its own).</summary>
    public static string FontPath(string file)
    {
        var local = Path.Combine(AppContext.BaseDirectory, "Fonts", file);
        if (File.Exists(local)) return local;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "DIR.Lib.Tests", "Fonts", file);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Fonts/" + file + " not found from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// <paramref name="latex"/> prepared with <see cref="MarkdownMacros.TryPrepareMathBoxSource"/>, as
    /// Console.Lib prepares display math, and parsed into the builder that lays its box tree out at a given
    /// style (one parse serves every size, as Console.Lib's width-fit loop uses it). Null where the helper
    /// turns it away or it does not lex or parse (LALR.CC throws an <see cref="InvalidOperationException"/>
    /// for both).
    /// </summary>
    public static Func<BoxStyle, Box>? Parse(string latex, BoxStyle style)
    {
        if (!MarkdownMacros.TryPrepareMathBoxSource(latex, out var source)) return null;
        var parser = Latex.BuildParser(new BoxBuildingVisitor(style));
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

    /// <summary><paramref name="latex"/> parsed (<see cref="Parse"/>) and built at <paramref name="style"/>,
    /// or null where it does not parse.</summary>
    public static Box? Build(string latex, BoxStyle style) => Parse(latex, style)?.Invoke(style);

    /// <summary>The formula built at <paramref name="style"/> and rasterized, or null where it does not build.</summary>
    public static RgbaImage? Render(string latex, BoxStyle style) =>
        Build(latex, style) is { } box ? BoxRasterizer.RenderToRgba(box, style) : null;
}
