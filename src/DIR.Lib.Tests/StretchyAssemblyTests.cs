using System.Text;
using DIR.Lib.Markdown;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;
using SharpAstro.Png;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A stretchy vertical glyph is the size asked for. An OpenType MATH assembly grows to the request: STIX Two
/// Math's radical extender (FullAdvance 651, connectors 650) added one font unit a copy when every join took
/// the most overlap its connectors allow, so a radicand taller than three nested fractions outgrew the
/// radical. A delimiter does not overshoot the request: DejaVu Sans ships no size variants, only assemblies
/// of parts 1.2 em tall, and one copy of every extender put a parenthesis around <c>\frac{1}{2}</c> at about
/// twice the fraction's height and a brace at about three times. And a composed glyph shows no seam where
/// its parts join.
/// </summary>
public sealed class StretchyAssemblyTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, string> FontPaths = new()
    {
        ["STIX2Math"] = Path.Combine(AppContext.BaseDirectory, "Fonts", "STIX2Math.otf"),
        ["DejaVuSans"] = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf"),
    };

    private static readonly RGBAColor32 Ink = new(0, 0, 0, 255);

    private static ManagedFontRasterizer Rasterizer => BoxStyle.SharedRasterizer;

    /// <summary>Where the crops land for a look by eye; never compared.</summary>
    private static string OutputDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "stretchy-assembly-output");

    /// <summary><paramref name="count"/> fractions, each the numerator of the next:
    /// <c>\frac{\frac{\frac{a}{b}}{c}}{d}</c> for four.</summary>
    internal static string NestedFractions(int count)
    {
        var latex = @"\frac{a}{b}";
        for (var i = 1; i < count; i++) latex = @"\frac{" + latex + "}{" + (char)('b' + i) + "}";
        return latex;
    }

    // ── radicals ────────────────────────────────────────────────────────

    /// <summary>
    /// STIX Two Math radicals over four, five and six nested fractions take the assembly, and it grows to
    /// what the radical asks of it: the drawn glyph is at least as tall as the request
    /// (<see cref="SqrtBox"/>'s: the radicand's total height, the 0.12 em gap above and below it, and the
    /// rule), and the radical's depth is not negative. Before, the assembly stopped at its 64-copy cap a
    /// little over two em tall, and the depth went negative.
    /// </summary>
    [Theory]
    [InlineData(4, 32f)]
    [InlineData(5, 32f)]
    [InlineData(6, 32f)]
    [InlineData(4, 12f)]
    [InlineData(5, 12f)]
    [InlineData(6, 12f)]
    public void StixRadicalOverNestedFractionsGrowsToTheRequest(int fractions, float size)
    {
        var style = new BoxStyle(FontPaths["STIX2Math"], size, Ink);
        var radicand = Parse(NestedFractions(fractions), style);
        var sqrt = new SqrtBox(radicand, style);
        var requested = radicand.TotalHeight + 2 * 0.12f * size + style.RadicalRuleThickness;

        sqrt.Path.ShouldBe(1, "the radical should take the MATH assembly");
        var join = sqrt.JoinAt(0, sqrt.Height, style).ShouldNotBeNull();
        output.WriteLine($"{fractions} fractions at {size} px: requested {requested:F2}, glyph {join.GlyphRows} rows, " +
                         $"radical height {sqrt.Height:F2} depth {sqrt.Depth:F2}, radicand depth {radicand.Depth:F2}");
        join.GlyphRows.ShouldBeGreaterThanOrEqualTo((int)MathF.Floor(requested),
            $"the radical's glyph against the {requested:F2} px requested of it");
        sqrt.Depth.ShouldBeGreaterThanOrEqualTo(0f);
    }

    // ── delimiters ──────────────────────────────────────────────────────

    /// <summary>
    /// DejaVu Sans at 32 px: <c>\left( \frac{1}{2} \right)</c> and <c>\left\{ \frac{1}{2} \right\}</c> draw
    /// each delimiter between 0.901 times the request (TeX's <c>\delimiterfactor</c> 901) and the larger of
    /// 1.25 times it and the request plus half an em. The request is what <see cref="BracketBox"/> asks of
    /// the font, the content's own total height. The delimiter's drawn height is its ink rows in the render,
    /// in the columns its glyph covers.
    /// </summary>
    [Theory]
    [InlineData(@"\left( \frac{1}{2} \right)", '(', ')')]
    [InlineData(@"\left\{ \frac{1}{2} \right\}", '{', '}')]
    public void DejaVuDelimitersAroundAFractionFitIt(string latex, char open, char close)
    {
        const float size = 32f;
        var style = new BoxStyle(FontPaths["DejaVuSans"], size, Ink);
        var request = Parse(@"\frac{1}{2}", style).TotalHeight;
        var box = Parse(latex, style).ShouldBeOfType<BracketBox>();
        var image = BoxRasterizer.RenderToRgba(box, style);
        var margin = (image.Width - (int)MathF.Ceiling(box.Width)) / 2;

        var left = new StretchyVerticalBox(open, request, style);
        var right = new StretchyVerticalBox(close, request, style);
        var leftRows = InkRows(image, margin, margin + (int)left.Width);
        var rightX = margin + (int)MathF.Floor(box.Width - right.Width);
        var rightRows = InkRows(image, rightX, rightX + (int)MathF.Ceiling(right.Width));

        var lo = 0.901f * request;
        var hi = MathF.Max(1.25f * request, request + 0.5f * size);
        output.WriteLine($"{latex}: request {request:F2} px, allowed {lo:F2}..{hi:F2}, drawn {leftRows} and {rightRows} rows");
        leftRows.ShouldBeInRange((int)MathF.Ceiling(lo), (int)MathF.Floor(hi), $"the opening {open}");
        rightRows.ShouldBeInRange((int)MathF.Ceiling(lo), (int)MathF.Floor(hi), $"the closing {close}");
    }

    // ── the choice ──────────────────────────────────────────────────────

    public static IEnumerable<TheoryDataRow<string, int>> ChoiceCases() =>
    [
        new("STIX2Math", '('), new("STIX2Math", '{'), new("STIX2Math", 0x221A),
        new("DejaVuSans", '('), new("DejaVuSans", '['), new("DejaVuSans", '{'),
    ];

    /// <summary>
    /// What a request gets is the smallest candidate that covers it, in the order OpenType MATH sets them:
    /// the unstretched glyph, then each size variant, then the assembly, which counts as covering at
    /// the larger of the request and its own minimum (no extender, every join at the most overlap the
    /// connectors allow). Swept at 32 px from 8 px to 12 em.
    /// </summary>
    [Theory]
    [MemberData(nameof(ChoiceCases))]
    public void TheChoiceIsTheSmallestCandidateThatCovers(string font, int codepoint)
    {
        const float size = 32f;
        var path = FontPaths[font];
        var rune = new Rune(codepoint);
        var construction = Rasterizer.GetVerticalMathConstruction(path, rune).ShouldNotBeNull();
        var (unitsPerEm, minOverlap) = Rasterizer.GetMathStretchInfo(path).ShouldNotBeNull();
        var scale = size / unitsPerEm;
        var baseHeight = Rasterizer.RasterizeGlyph(path, size, rune).Height;
        Rasterizer.RasterizeStretchyVertical(path, size, rune, 1f, out var baseGid);
        var asmMinimum = AssemblyMinimum(construction.Assembly.ShouldNotBeNull(), minOverlap) * scale;
        var failures = new List<string>();

        for (var request = 8f; request <= 12 * size; request += 1.37f)
        {
            var bitmap = Rasterizer.RasterizeStretchyVerticalWithParts(path, size, rune, request, out var parts);
            bitmap.Height.ShouldBeGreaterThan(0);
            Rasterizer.RasterizeStretchyVertical(path, size, rune, request, out var gid);

            // A glyph id names the base or a variant (STIX Two Math lists its base as variant 0); 0 the assembly.
            uint expected;
            var requiredUnits = (int)Math.Ceiling(request * unitsPerEm / size);
            if (baseHeight >= request) expected = baseGid;
            else if (construction.Variants.FirstOrDefault(v => v.AdvanceMeasurement >= requiredUnits) is { GlyphId: > 0 } v)
                expected = v.GlyphId;
            else expected = 0;
            var got = parts is not null ? 0 : gid;

            string Name(uint g) => g == 0 ? $"the assembly at {MathF.Max(request, asmMinimum):F1} px" : $"glyph {g}";
            if (got != expected)
                failures.Add($"request {request:F2}: expected {Name(expected)}, got {(got == 0 ? $"the assembly, {bitmap.Height} px" : Name(got))}");
            else if (got == 0 && bitmap.Height > MathF.Ceiling(MathF.Max(request, asmMinimum)) + 1)
                failures.Add($"request {request:F2}: expected {Name(0)}, got the assembly at {bitmap.Height} px");
        }
        failures.ShouldBeEmpty($"{font} U+{codepoint:X4}, assembly minimum {asmMinimum:F1} px:" + Environment.NewLine
                               + string.Join(Environment.NewLine, failures));
    }

    /// <summary>The assembly's shortest height in font units: each non-extender part once, no extender, every
    /// join at the most overlap its connectors allow (never less than the table's minimum).</summary>
    private static int AssemblyMinimum(SharpAstro.Fonts.Tables.OpenTypeMath.MathGlyphAssembly asm, ushort minOverlap)
    {
        var ends = asm.Parts.Where(p => !p.IsExtender).ToList();
        var height = ends.Sum(p => p.FullAdvance);
        for (var i = 0; i < ends.Count - 1; i++)
            height -= Math.Max(minOverlap, Math.Min(ends[i].EndConnectorLength, ends[i + 1].StartConnectorLength));
        return height;
    }

    // ── the composition ─────────────────────────────────────────────────

    public static IEnumerable<TheoryDataRow<string, int, float>> CompositionCases() =>
    [
        new("STIX2Math", 0x221A, 100f), new("STIX2Math", 0x221A, 151.3f), new("STIX2Math", 0x221A, 237.6f),
        new("STIX2Math", 0x221A, 400f), new("STIX2Math", '(', 150.5f), new("STIX2Math", '{', 222.2f),
        new("DejaVuSans", '(', 120f), new("DejaVuSans", '(', 201.7f), new("DejaVuSans", '[', 333.3f),
        new("DejaVuSans", '{', 250.4f),
    ];

    /// <summary>
    /// For a request between the assembly's minimum and its cap, the composed glyph lands within one pixel of
    /// the request: the extenders are counted with the table's least overlap, and what is left over is
    /// spread across the joins. At 32 px.
    /// </summary>
    [Theory]
    [MemberData(nameof(CompositionCases))]
    public void TheComposedHeightLandsOnTheRequest(string font, int codepoint, float request)
    {
        var bitmap = Rasterizer.RasterizeStretchyVerticalWithParts(FontPaths[font], 32f, new Rune(codepoint), request, out var parts);
        parts.ShouldNotBeNull("the request should need the assembly");
        output.WriteLine($"{font} U+{codepoint:X4}: request {request} px, composed {bitmap.Height} px from {parts.Length} parts, " +
                         $"{parts.Count(p => p.IsExtender)} extenders");
        Math.Abs(bitmap.Height - request).ShouldBeLessThanOrEqualTo(1f);
    }

    // ── seams ───────────────────────────────────────────────────────────

    public static IEnumerable<TheoryDataRow<string, int, float, float>> SeamCases()
    {
        foreach (var font in new[] { "STIX2Math", "DejaVuSans" })
            foreach (var cp in new[] { '(', ')', '[', ']', '{', '}' })
                foreach (var (size, request) in new[] { (32f, 260f), (12f, 97.3f) })
                    yield return new(font, cp, size, request);
        yield return new("STIX2Math", 0x221A, 32f, 150f);
        yield return new("STIX2Math", 0x221A, 32f, 333.3f);
        yield return new("STIX2Math", 0x221A, 12f, 61.7f);
    }

    /// <summary>
    /// No seams. The stem columns are the columns inked in the extender's own bitmap; on every row of the
    /// composed glyph that lies wholly inside an extender copy, the stem's alpha (summed across those
    /// columns) is at least 0.9 times the unstretched extender's peak. A join the old composer placed by
    /// whole pixels showed as a lighter row there.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeamCases))]
    public void AComposedGlyphHasNoSeams(string font, int codepoint, float size, float request)
    {
        var path = FontPaths[font];
        var bitmap = Rasterizer.RasterizeStretchyVerticalWithParts(path, size, new Rune(codepoint), request, out var parts);
        parts.ShouldNotBeNull("the request should need the assembly");
        var extenderGid = parts.First(p => p.IsExtender).GlyphId;
        var extender = Rasterizer.RasterizeGlyphByGid(path, size, extenderGid);

        var stem = Enumerable.Range(0, extender.Width)
            .Where(x => Enumerable.Range(0, extender.Height).Any(y => Alpha(extender, x, y) > 0))
            .Select(x => x + extender.BearingX - bitmap.BearingX)
            .ToArray();
        int StemSum(GlyphBitmap bm, int y, int[] cols) => cols.Where(x => x >= 0 && x < bm.Width).Sum(x => Alpha(bm, x, y));
        var extenderCols = stem.Select(x => x - extender.BearingX + bitmap.BearingX).ToArray();
        var peak = Enumerable.Range(0, extender.Height).Max(y => StemSum(extender, y, extenderCols));

        var failures = new List<string>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            if (!parts.Any(p => p.IsExtender && p.Top <= y && y + 1 <= p.Bottom)) continue;
            var sum = StemSum(bitmap, y, stem);
            if (sum < 0.9f * peak) failures.Add($"row {y}: stem alpha {sum}, the extender's peak {peak}");
        }
        failures.ShouldBeEmpty($"{font} U+{codepoint:X4} at {size} px, request {request} ({bitmap.Height} rows, stem columns " +
                               $"{string.Join(",", stem)}):" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    // ── crops ───────────────────────────────────────────────────────────

    /// <summary>
    /// Writes 8x crops of the STIX radicals over four and six nested fractions and the two DejaVu Sans
    /// delimiter cases at 32 px under <c>obj/stretchy-assembly-output</c>, named by
    /// <c>DIR_LIB_CROP_TAG</c> (default "after"), for a look by eye. Asserts nothing about the pixels.
    /// </summary>
    [Fact]
    public void WriteCrops()
    {
        var tag = Environment.GetEnvironmentVariable("DIR_LIB_CROP_TAG") is { Length: > 0 } t ? t : "after";
        Directory.CreateDirectory(OutputDir);
        var stix = new BoxStyle(FontPaths["STIX2Math"], 32f, Ink);
        var dejavu = new BoxStyle(FontPaths["DejaVuSans"], 32f, Ink);
        (string Name, string Latex, BoxStyle Style)[] scenes =
        [
            ("stix-sqrt-4-fractions", @"\sqrt{" + NestedFractions(4) + "}", stix),
            ("stix-sqrt-6-fractions", @"\sqrt{" + NestedFractions(6) + "}", stix),
            ("dejavu-paren-frac", @"\left( \frac{1}{2} \right)", dejavu),
            ("dejavu-brace-frac", @"\left\{ \frac{1}{2} \right\}", dejavu),
        ];
        foreach (var (name, latex, style) in scenes)
        {
            var image = BoxRasterizer.RenderToRgba(Parse(latex, style), style);
            var file = Path.Combine(OutputDir, $"{name}-{tag}.png");
            var big = Upscale(image, 8);
            File.WriteAllBytes(file, PngWriter.Encode(big.Pixels, big.Width, big.Height));
            output.WriteLine(Path.GetFullPath(file));
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static int Alpha(GlyphBitmap bm, int x, int y) => bm.Rgba[(y * bm.Width + x) * 4 + 3];

    /// <summary>Rows with any ink in columns [<paramref name="x0"/>, <paramref name="x1"/>): from the first to
    /// the last, inclusive.</summary>
    private static int InkRows(RgbaImage image, int x0, int x1)
    {
        int top = -1, bottom = -1;
        for (var y = 0; y < image.Height; y++)
            for (var x = Math.Max(0, x0); x < Math.Min(image.Width, x1); x++)
                if (image.Pixels[(y * image.Width + x) * 4 + 3] > 0)
                {
                    if (top < 0) top = y;
                    bottom = y;
                    break;
                }
        return top < 0 ? 0 : bottom - top + 1;
    }

    /// <summary><paramref name="image"/> on white, each pixel blown up to a <paramref name="k"/> square.</summary>
    private static RgbaImage Upscale(RgbaImage image, int k)
    {
        var big = new RgbaImage(image.Width * k, image.Height * k);
        for (var y = 0; y < big.Height; y++)
            for (var x = 0; x < big.Width; x++)
            {
                var i = ((y / k) * image.Width + x / k) * 4;
                var a = image.Pixels[i + 3];
                var o = (y * big.Width + x) * 4;
                var v = (byte)(255 - a);
                big.Pixels[o] = v;
                big.Pixels[o + 1] = v;
                big.Pixels[o + 2] = v;
                big.Pixels[o + 3] = 255;
            }
        return big;
    }

    private static Box Parse(string latex, BoxStyle style)
    {
        if (!MarkdownMacros.TryPrepareMathBoxSource(latex, out var src))
            Assert.Fail($"'{latex}' was turned away from the box path");
        var parser = Latex.BuildParser(new BoxBuildingVisitor(style));
        using var lexer = BytesLexer.FromString(src, MarkdownMacros.MathLexerTable);
        using var tokens = new SyncLATokenIterator(lexer);
        var item = parser.ParseInput(tokens, debugger: null);
        item.IsError.ShouldBeFalse($"'{latex}' did not parse");
        return item.Content.ShouldBeOfType<Func<BoxStyle, Box>>()(style);
    }
}
