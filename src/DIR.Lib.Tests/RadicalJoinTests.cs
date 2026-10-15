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
///
/// <para>Two layout invariants hold against the radical before this rework (commit 619f0e4, data in
/// <c>RadicalJoinTests.Before.cs</c>): the radicand never has fewer clear rows under the bar, and the
/// radical's height and depth move at most 2 px. They are swept over every size from 8 to 40 px and a few
/// larger ones, since review round 1 found its bugs between the join assertions' three sizes.</para>
/// </summary>
public sealed partial class RadicalJoinTests
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
    /// The sizes the two layout invariants are swept over: every integer size from 8 to 40 px, which covers
    /// where Console.Lib draws math (Sixel at 32 px shrinking to about 21, sextant at 12 down to 8,
    /// half-block at 10 down to about 7, the last below the sweep), plus 48, 64, 72 and 96. The exact-pixel
    /// join assertions above stay at 24, 32 and 96 px; the review found its bugs at sizes in between.
    /// </summary>
    private static readonly float[] SweepSizes = [.. Enumerable.Range(8, 33).Select(i => (float)i), 48f, 64f, 72f, 96f];

    private static readonly string[] SweepScenes = ["sqrt-2", "sqrt-x", "sqrt-paren-frac"];

    /// <summary>Every point of <see cref="BeforePoints"/> rendered once, for the sweep facts: the sweep, the
    /// review's named points, and the 42 join cases.</summary>
    private static readonly Lazy<Swept[]> SweptPoints = new(() =>
    [
        .. BeforePoints.Select(p =>
        {
            var r = Render(p.Font, p.Size, p.Scene);
            return new Swept(p, r.Sqrt.Height, r.Sqrt.Depth, r.ClearRowsUnderBar());
        }),
    ]);

    private sealed record Swept(BeforePoint Before, float Height, float Depth, int ClearRows)
    {
        public override string ToString() => $"{Before.Font} {Before.Scene} {Before.Size} px";
    }

    /// <summary>The before data holds every point of the sweep, so a point cannot drop out of it unnoticed.</summary>
    [Fact]
    public void BeforeDataCoversTheSweep()
    {
        var missing = (from font in FontPaths.Keys
                       from scene in SweepScenes
                       from size in SweepSizes
                       where !BeforePoints.Any(p => p.Font == font && p.Scene == scene && p.Size == size)
                       select $"{font} {scene} {size} px").ToList();
        missing.ShouldBeEmpty("sweep points with no before data:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// Across the sweep the radicand never comes closer to the bar than it was before: the layout reserves
    /// every row the bar is drawn on, on both glyph paths, so no bar row lands in the gap. Every failing point
    /// is listed in the one message.
    /// </summary>
    [Fact]
    public void SweepNeverHasFewerClearRowsThanBefore()
    {
        var failures = SweptPoints.Value
            .Where(s => s.ClearRows < s.Before.ClearRows)
            .Select(s => $"{s}: {s.ClearRows} clear rows under the bar, before {s.Before.ClearRows}")
            .ToList();
        failures.ShouldBeEmpty(Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Across the sweep the radical's height and depth, which a parent fraction or row lays out against,
    /// move at most 2 px from before. The snap moves the glyph by under half a pixel and the layout now
    /// counts the rows the bar is drawn on; a path flip (review round 1, B2) or a reserved antialiasing tail
    /// (Q1) shows here first. Every failing point is listed in the one message.
    /// </summary>
    [Fact]
    public void SweepHeightAndDepthDriftAtMostTwoPixels()
    {
        var failures = SweptPoints.Value
            .Where(s => MathF.Abs(s.Height - s.Before.Height) > 2f || MathF.Abs(s.Depth - s.Before.Depth) > 2f)
            .Select(s => $"{s}: height {s.Before.Height} -> {s.Height}, depth {s.Before.Depth} -> {s.Depth}")
            .ToList();
        failures.ShouldBeEmpty(Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The review's flip, with the base's numbers written out: STIX Two Math
    /// <c>\sqrt{\left(\frac{a}{b}\right)}</c> at 72 px stays on the scaled-glyph path with height 96.14 and
    /// depth 57.86, within 2 px.
    /// </summary>
    [Fact]
    public void Stix72PxParenFracKeepsTheBasePathAndMetrics()
    {
        var r = Render("STIX2Math", 72f, "sqrt-paren-frac");
        r.Sqrt.Path.ShouldBe(2);
        r.Sqrt.Height.ShouldBe(96.14f, 2f);
        r.Sqrt.Depth.ShouldBe(57.86f, 2f);
    }

    /// <summary>
    /// The case that decided how the layout counts the bar: STIX Two Math's <c>\sqrt{2}</c> at 24 px, where
    /// the terminal lands when it shrinks math to fit. The radicand keeps the clear row it had.
    /// </summary>
    [Fact]
    public void Sqrt2At24PxKeepsAClearRowUnderTheBar()
        => Render("STIX2Math", 24, "sqrt-2").ClearRowsUnderBar().ShouldBeGreaterThanOrEqualTo(1);

    /// <summary>
    /// Review round 1, B1. On path 1 (a MATH size variant or assembly, or a base glyph tall enough to need
    /// neither) the layout placed the flag by the font's rule thickness and never reserved the rows the bar
    /// is drawn on, so the bar sat in the gap. DejaVu Sans <c>\sqrt{x}</c> at 20 px draws a three-row bar
    /// where the base drew one, and the x's top merged into it: no clear row, where the base had one.
    /// </summary>
    [Theory]
    [InlineData("DejaVuSans", "sqrt-x", 20f)]
    [InlineData("DejaVuSans", "sqrt-x", 22.7f)]
    [InlineData("DejaVuSans", "sqrt-x", 26f)]
    [InlineData("DejaVuSans", "sqrt-x", 28f)]
    [InlineData("STIX2Math", "sqrt-frac-a-b", 10f)]
    [InlineData("STIX2Math", "sqrt-frac-a-b", 11f)]
    public void ReviewedCasesKeepTheirClearRows(string font, string scene, float size)
    {
        var before = BeforeAt(font, scene, size);
        var r = Render(font, size, scene);
        r.ClearRowsUnderBar().ShouldBeGreaterThanOrEqualTo(before.ClearRows,
            $"clear rows between the bar (rows {r.Join.FlagTopY}..{r.Join.FlagTopY + r.Join.FlagRows - 1}) and the radicand");
    }

    /// <summary>
    /// Review round 1, B2. The 1.1x overshoot cap that sends a radical to path 1 (a MATH size variant) or
    /// path 2 (the scaled base glyph) was tested on the SNAPPED bitmap, and the snap can drop a row, so it
    /// flipped radicals across that cap. STIX Two Math <c>\sqrt{\left(\frac{a}{b}\right)}</c> at 72 px:
    /// the variant is 172 rows against a cap of 171.03, so the base took path 2 (height 96.14, depth
    /// 57.86); snapped it is 171 rows, path 1 won, and the depth grew by 21 px.
    /// <para>Review round 1, Q1. At small sizes the flag's column ends in antialiasing tail rows (STIX
    /// <c>\sqrt{2}</c> at 10 px: 158, 112, 46, 1), and the layout reserved a whole row for each: height
    /// 3 px over the base there and for DejaVu Sans at 14 to 17 px, depth 2.2 to 3 px under it for STIX at
    /// 13 to 19 px.</para>
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", "sqrt-paren-frac", 72f)]
    [InlineData("STIX2Math", "sqrt-paren-frac", 10f)]
    [InlineData("STIX2Math", "sqrt-frac-a-b", 13f)]
    [InlineData("STIX2Math", "sqrt-2", 10f)]
    [InlineData("STIX2Math", "sqrt-2", 15f)]
    [InlineData("STIX2Math", "sqrt-2", 17f)]
    [InlineData("STIX2Math", "sqrt-x", 13f)]
    [InlineData("STIX2Math", "sqrt-x", 19f)]
    [InlineData("DejaVuSans", "sqrt-2", 14f)]
    [InlineData("DejaVuSans", "sqrt-x", 16f)]
    [InlineData("DejaVuSans", "sqrt-x", 17f)]
    public void ReviewedCasesKeepTheirPathAndMetrics(string font, string scene, float size)
    {
        var before = BeforeAt(font, scene, size);
        var r = Render(font, size, scene);
        r.Sqrt.Path.ShouldBe(before.Path, "the rendering path the base took");
        r.Sqrt.Height.ShouldBe(before.Height, 2f, "radical height");
        r.Sqrt.Depth.ShouldBe(before.Depth, 2f, "radical depth");
    }

    private static BeforePoint BeforeAt(string font, string scene, float size) =>
        BeforePoints.Single(p => p.Font == font && p.Scene == scene && p.Size == size);

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

        /// <summary>Rows with no ink at all, under the bar's columns (from the glyph's right edge to the bar's
        /// end), between the bar's last row and the first row below it that has ink: the radicand's top.</summary>
        public int ClearRowsUnderBar()
        {
            var below = Join.FlagTopY + Join.FlagRows;
            var y = below;
            while (y < Image.Height && !RowHasInk(y)) y++;
            return y - below;
        }

        private bool RowHasInk(int y)
        {
            for (var x = Join.BarStartX; x < Join.BarEndX; x++)
                if (Alpha(x, y) > 0) return true;
            return false;
        }
    }

    private static Rendered Render(string font, float size, string scene)
    {
        var style = new BoxStyle(FontPaths[font], size, new RGBAColor32(0, 0, 0, 255));
        var box = scene switch
        {
            "sqrt-x2-plus-y2" => Parse(@"\sqrt{x^2+y^2}", style),
            "sqrt-2" => Parse(@"\sqrt{2}", style),
            "sqrt-x" => Parse(@"\sqrt{x}", style),
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
