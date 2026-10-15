using System.Reflection;
using DIR.Lib.Markdown;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;
using SharpAstro.Fonts;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The square root's vinculum must continue the radical glyph's flag with no gap, no vertical offset
/// and a shared crisp top edge. Reported from mdcat (Sixel, STIX Two Math at 32 px) and the 96 px
/// baselines: a near-empty column between flag and bar (the flag's antialiased right end), and a bar
/// whose solid top row sat on the flag's antialiased top row, so the bar read as half a pixel high.
///
/// <para>Each case renders a scene to RGBA and finds the join from the layout's own numbers: the
/// <see cref="SqrtBox.LastJoin"/> its draw recorded (the flag's top row and band height, its rightmost
/// full-coverage column, the bar's end), never by searching the image. The assertions then read the
/// pixels there.</para>
///
/// <para>Both fonts run every assertion. DejaVu Sans has no MATH table, so it takes the scaled-glyph
/// path (2), and path 1 with its unstretched base glyph for the 24 px index scene; its hinter already
/// puts the flag's top on a pixel row, so the snap is a no-op there and only the join changed. STIX Two
/// Math takes path 2 for the five scenes the report names (its MATH size variants overshoot those
/// radicands by more than 10%), so two more scenes reach path 1: a size variant
/// (<c>sqrt-paren-frac</c> at 32 and 24 px) and the assembly (<c>sqrt-nested-frac</c>).</para>
/// </summary>
public sealed class RadicalJoinTests
{
    /// <summary>How far, in alpha, the bar may differ from the flag's column on any row.</summary>
    private const int AlphaTolerance = 8;

    private static readonly Dictionary<string, string> FontPaths = new()
    {
        ["STIX2Math"] = Path.Combine(AppContext.BaseDirectory, "Fonts", "STIX2Math.otf"),
        ["DejaVuSans"] = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf"),
    };

    /// <summary>96 px is the baselines' size; 32 px is Console.Lib's Sixel math, and 24 px about where it
    /// shrinks a wide formula to fit the terminal.</summary>
    private static readonly int[] Sizes = [96, 32, 24];

    private static readonly string[] Scenes =
    [
        "sqrt-x2-plus-y2", "sqrt-2", "sqrt-frac-a-b", "quadratic-formula", "index-box-api",
        "sqrt-paren-frac", "sqrt-nested-frac",
    ];

    /// <summary>
    /// The radical's <see cref="Box.Height"/> and <see cref="Box.Depth"/> before the join was reworked,
    /// recorded from commit 619f0e4 by a probe that built these same scenes. Printed to three decimals,
    /// so the comparison allows 0.002 px for that rounding on top of its one pixel.
    /// </summary>
    private static readonly Dictionary<(string Font, int Size, string Scene), (float Height, float Depth)> Before = new()
    {
        [("STIX2Math", 96, "sqrt-x2-plus-y2")] = (98.080f, 32.920f),
        [("STIX2Math", 96, "sqrt-2")] = (79.520f, 12.480f),
        [("STIX2Math", 96, "sqrt-frac-a-b")] = (115.064f, 71.936f),
        [("STIX2Math", 96, "quadratic-formula")] = (108.440f, 11.560f),
        [("STIX2Math", 96, "index-box-api")] = (62.520f, 13.480f),
        [("STIX2Math", 96, "sqrt-paren-frac")] = (128.020f, 77.980f),
        [("STIX2Math", 96, "sqrt-nested-frac")] = (220.680f, 79.320f),
        [("STIX2Math", 32, "sqrt-x2-plus-y2")] = (33.360f, 10.640f),
        [("STIX2Math", 32, "sqrt-2")] = (26.840f, 4.160f),
        [("STIX2Math", 32, "sqrt-frac-a-b")] = (39.688f, 24.312f),
        [("STIX2Math", 32, "quadratic-formula")] = (36.480f, 5.520f),
        [("STIX2Math", 32, "index-box-api")] = (21.840f, 5.160f),
        [("STIX2Math", 32, "sqrt-paren-frac")] = (45.016f, 31.984f),
        [("STIX2Math", 32, "sqrt-nested-frac")] = (75.560f, 4.440f),
        [("STIX2Math", 24, "sqrt-x2-plus-y2")] = (25.520f, 7.480f),
        [("STIX2Math", 24, "sqrt-2")] = (20.880f, 2.120f),
        [("STIX2Math", 24, "sqrt-frac-a-b")] = (30.016f, 17.136f),
        [("STIX2Math", 24, "quadratic-formula")] = (27.360f, 2.640f),
        [("STIX2Math", 24, "index-box-api")] = (15.880f, 5.120f),
        [("STIX2Math", 24, "sqrt-paren-frac")] = (34.012f, 23.988f),
        [("STIX2Math", 24, "sqrt-nested-frac")] = (56.920f, 4.080f),
        [("DejaVuSans", 96, "sqrt-x2-plus-y2")] = (122.520f, 27.480f),
        [("DejaVuSans", 96, "sqrt-2")] = (89.520f, 9.480f),
        [("DejaVuSans", 96, "sqrt-frac-a-b")] = (121.909f, 73.091f),
        [("DejaVuSans", 96, "quadratic-formula")] = (143.520f, 7.480f),
        [("DejaVuSans", 96, "index-box-api")] = (69.520f, 10.480f),
        [("DejaVuSans", 96, "sqrt-paren-frac")] = (228.020f, 147.500f),
        [("DejaVuSans", 96, "sqrt-nested-frac")] = (246.688f, 164.168f),
        [("DejaVuSans", 32, "sqrt-x2-plus-y2")] = (40.840f, 8.160f),
        [("DejaVuSans", 32, "sqrt-2")] = (28.840f, 3.160f),
        [("DejaVuSans", 32, "sqrt-frac-a-b")] = (40.303f, 22.697f),
        [("DejaVuSans", 32, "quadratic-formula")] = (46.840f, 1.160f),
        [("DejaVuSans", 32, "index-box-api")] = (23.840f, 3.160f),
        [("DejaVuSans", 32, "sqrt-paren-frac")] = (77.340f, 50.500f),
        [("DejaVuSans", 32, "sqrt-nested-frac")] = (80.229f, 53.389f),
        [("DejaVuSans", 24, "sqrt-x2-plus-y2")] = (29.880f, 7.120f),
        [("DejaVuSans", 24, "sqrt-2")] = (21.880f, 3.120f),
        [("DejaVuSans", 24, "sqrt-frac-a-b")] = (29.727f, 17.273f),
        [("DejaVuSans", 24, "quadratic-formula")] = (34.880f, 2.120f),
        [("DejaVuSans", 24, "index-box-api")] = (16.935f, 3.065f),
        [("DejaVuSans", 24, "sqrt-paren-frac")] = (58.380f, 38.500f),
        [("DejaVuSans", 24, "sqrt-nested-frac")] = (60.422f, 39.542f),
    };

    public static IEnumerable<TheoryDataRow<string, int, string>> Cases()
    {
        foreach (var font in FontPaths.Keys)
            foreach (var size in Sizes)
                foreach (var scene in Scenes)
                    yield return new TheoryDataRow<string, int, string>(font, size, scene);
    }

    /// <summary>
    /// No column from the flag's last full column to the bar's end carries less ink than that column:
    /// the flag's antialiased right end is not left as a lighter column between flag and bar.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void NoColumnAtTheJoinIsLighterThanTheBar(string font, int size, string scene)
    {
        var r = Render(font, size, scene);
        var reference = r.ColumnSum(r.Join.FlagLastFullX);
        for (var x = r.Join.FlagLastFullX; x < r.Join.BarEndX; x++)
        {
            var sum = r.ColumnSum(x);
            sum.ShouldBeGreaterThanOrEqualTo(reference - AlphaTolerance * r.Join.FlagRows,
                $"column {x} (flag's last full column {r.Join.FlagLastFullX}, bar ends {r.Join.BarEndX}) " +
                $"has {sum} alpha down the flag's band against the flag's {reference}");
        }
    }

    /// <summary>
    /// Every column of the bar, and of the flag's filled-in end, has the flag's last full column's alpha on
    /// every row of the band and on the row above it: the same top edge and weight, so the bar is neither
    /// offset from the flag nor thinner than it. The row below the band is compared too, which is what
    /// would catch a bar thicker than the flag, but only up to the bar's first column: over the radicand
    /// that row can hold the radicand's own antialiased top, which is not the bar.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void BarRepeatsTheFlagsLastFullColumn(string font, int size, string scene)
    {
        var r = Render(font, size, scene);
        var j = r.Join;
        for (var x = j.FlagLastFullX + 1; x < j.BarEndX; x++)
        {
            var lastRow = x <= j.BarStartX ? j.FlagTopY + j.FlagRows : j.FlagTopY + j.FlagRows - 1;
            for (var y = j.FlagTopY - 1; y <= lastRow; y++)
            {
                var flag = r.Alpha(j.FlagLastFullX, y);
                var bar = r.Alpha(x, y);
                Math.Abs(bar - flag).ShouldBeLessThanOrEqualTo(AlphaTolerance,
                    $"column {x}, row {y} (flag band {j.FlagTopY}..{j.FlagTopY + j.FlagRows - 1}): " +
                    $"bar alpha {bar}, flag's column {flag}");
            }
        }
    }

    /// <summary>
    /// The flag's top row is fully covered and nothing sits in the row above it: the top edge is snapped
    /// to a pixel row rather than antialiased across two.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void FlagTopRowIsFullyCovered(string font, int size, string scene)
    {
        var r = Render(font, size, scene);
        var j = r.Join;
        r.Alpha(j.FlagLastFullX, j.FlagTopY).ShouldBeGreaterThanOrEqualTo(240,
            $"flag's top row {j.FlagTopY} at its last full column {j.FlagLastFullX}");
        r.Alpha(j.FlagLastFullX, j.FlagTopY - 1).ShouldBe(0,
            $"row {j.FlagTopY - 1}, above the flag's top, at column {j.FlagLastFullX}");
    }

    /// <summary>
    /// The snap moves the glyph by under half a pixel and the bar now has the flag's thickness; neither
    /// may move the radical's height or depth, which a parent fraction or row lays out against, by more
    /// than a pixel.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void HeightAndDepthStayWithinAPixelOfBefore(string font, int size, string scene)
    {
        var r = Render(font, size, scene);
        var (height, depth) = Before[(font, size, scene)];
        r.Sqrt.Height.ShouldBe(height, 1.002f, "radical height");
        r.Sqrt.Depth.ShouldBe(depth, 1.002f, "radical depth");
    }

    private sealed record Rendered(RgbaImage Image, SqrtBox Sqrt, SqrtBox.RadicalJoin Join)
    {
        public int Alpha(int x, int y) =>
            x < 0 || y < 0 || x >= Image.Width || y >= Image.Height ? 0 : Image.Pixels[(y * Image.Width + x) * 4 + 3];

        public int ColumnSum(int x)
        {
            var sum = 0;
            for (var y = Join.FlagTopY; y < Join.FlagTopY + Join.FlagRows; y++) sum += Alpha(x, y);
            return sum;
        }
    }

    private static Rendered Render(string font, int size, string scene)
    {
        var style = new BoxStyle(FontPaths[font], size, new RGBAColor32(0, 0, 0, 255));
        var box = scene switch
        {
            "sqrt-x2-plus-y2" => Parse(@"\sqrt{x^2+y^2}", style),
            "sqrt-2" => Parse(@"\sqrt{2}", style),
            "sqrt-frac-a-b" => Parse(@"\sqrt{\frac{a}{b}}", style),
            "quadratic-formula" => Parse(@"\frac{-b \pm \sqrt{b^2-4ac}}{2a}", style),
            // The index goes through the Box API: the grammar's \sqrt takes no optional argument.
            "index-box-api" => new SqrtBox(
                new MathGlyphBox("x", style, MathStyle.Italic),
                new GlyphBox("3", style.Smaller().Smaller()),
                style),
            "sqrt-paren-frac" => Parse(@"\sqrt{\left(\frac{a}{b}\right)}", style),
            "sqrt-nested-frac" => Parse(@"\sqrt{\frac{\frac{a}{b}}{\frac{c}{d}}}", style),
            _ => throw new ArgumentException($"unknown scene '{scene}'"),
        };

        var image = BoxRasterizer.RenderToRgba(box, style);
        var sqrt = FindSqrt(box);
        var join = sqrt.LastJoin.ShouldNotBeNull("the radical drew no flag and bar (parametric path?)");
        join.FlagRows.ShouldBeGreaterThan(0);
        join.BarEndX.ShouldBeGreaterThan(join.FlagLastFullX);
        return new Rendered(image, sqrt, join);
    }

    private static Box Parse(string latex, BoxStyle style)
    {
        var src = MarkdownMacros.ResolveBackslashEscapes(MarkdownMacros.NormalizeLatexAliases(latex));
        var parser = Latex.BuildParser(new BoxBuildingVisitor(style));
        using var lexer = BytesLexer.FromString(src, MarkdownMacros.MathLexerTable);
        using var tokens = new SyncLATokenIterator(lexer);
        var item = parser.ParseInput(tokens, debugger: null);
        item.IsError.ShouldBeFalse($"'{latex}' did not parse");
        return item.Content.ShouldBeOfType<Func<BoxStyle, Box>>()(style);
    }

    /// <summary>The scene's one radical. Boxes keep their children in private fields, so this walks
    /// them; it only locates the box, and the join's position comes from the box itself.</summary>
    private static SqrtBox FindSqrt(Box root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node)) continue;
            if (node is SqrtBox sqrt) return sqrt;
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
        throw new InvalidOperationException("scene has no SqrtBox");
    }
}
