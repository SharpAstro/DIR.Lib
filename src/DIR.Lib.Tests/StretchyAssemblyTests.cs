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
        join.GlyphRows.ShouldBeGreaterThanOrEqualTo((int)MathF.Ceiling(requested),
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

    /// <summary>
    /// The squash that fits a too-tall assembly to its request is never reached by STIX Two Math, whose size
    /// variants and finely stretched assemblies always land within the bound: these composed glyphs, at
    /// 32 px and 12 px, are byte for byte what they were before it (hashes taken at 157ad06).
    /// </summary>
    [Theory]
    [InlineData('{', 32f, 260f, "B7BD6F3AD652DB0E")]
    [InlineData('{', 32f, 138.15f, "4CE278826D9E4471")]
    [InlineData('(', 12f, 97.3f, "1E963ECD9E3EA525")]
    [InlineData(0x221A, 32f, 333.3f, "DACDCAECC56DB2A3")]
    public void StixAssembliesAreUnchangedByTheSquash(int codepoint, float size, float request, string hash)
    {
        var bitmap = Rasterizer.RasterizeStretchyVerticalWithParts(FontPaths["STIX2Math"], size, new Rune(codepoint), request, out var parts);
        parts.ShouldNotBeNull("the request should need the assembly");
        var bytes = new byte[bitmap.Rgba.Length + 8];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), bitmap.Width);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), bitmap.Height);
        bitmap.Rgba.CopyTo(bytes, 8);
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
        output.WriteLine($"U+{codepoint:X4} {size} px request {request}: {bitmap.Width}x{bitmap.Height} {actual}");
        actual.ShouldBe(hash);
    }

    /// <summary>
    /// Renders DejaVu Sans's parenthesis and brace pairs around a fraction, and a brace around a nested one,
    /// at 32 px and 12 px under <c>obj/stretchy-assembly-output</c>, 8x, for a look by eye at the squash
    /// that fits a too-tall assembly to its request. Asserts nothing about the pixels.
    /// </summary>
    [Fact]
    public void WriteSquashRenders()
    {
        Directory.CreateDirectory(OutputDir);
        (string Name, string Latex)[] scenes =
        [
            ("paren-frac", @"\left( \frac{1}{2} \right)"),
            ("brace-frac", @"\left\{ \frac{1}{2} \right\}"),
            ("brace-nested-frac", @"\left\{ \frac{\frac{a}{b}}{c} \right\}"),
        ];
        foreach (var size in new[] { 32f, 12f })
            foreach (var (name, latex) in scenes)
            {
                var style = new BoxStyle(FontPaths["DejaVuSans"], size, Ink);
                var image = BoxRasterizer.RenderToRgba(Parse(latex, style), style);
                var big = Upscale(image, 8);
                var file = Path.Combine(OutputDir, $"squash-dejavu-{name}-{size:0}px.png");
                File.WriteAllBytes(file, PngWriter.Encode(big.Pixels, big.Width, big.Height));
                output.WriteLine(Path.GetFullPath(file));
            }
    }

    // ── asymmetric content ──────────────────────────────────────────────

    /// <summary>
    /// A <c>\left..\right</c> pair is centred on the math axis, so it covers content that reaches further
    /// above the axis than below it (a fraction whose numerator is itself a fraction). The pair asks the font
    /// for twice the content's larger reach from the axis, as TeX does, not its total height, and takes the
    /// first glyph at least <see cref="ManagedFontRasterizer.DelimiterCovers"/> of that: TeX's guarantee,
    /// which this asserts, is that the delimiters' ink reaches the content's layout box to within half that
    /// shortfall at each end. The box, not the ink: DejaVu Sans's hinted text can start inking up to 1.4 px
    /// above its own layout top (the numerator's a here), which no layout of the delimiter sees. At 32 px.
    /// Each render is saved under <c>obj/stretchy-assembly-output</c> as <c>asymmetric-…-32px.png</c>, 8x.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", @"\left\{ \frac{\frac{a}{b}}{c} \right\}", "brace")]
    [InlineData("STIX2Math", @"\left( \frac{\frac{a}{b}}{c} \right)", "paren")]
    [InlineData("DejaVuSans", @"\left\{ \frac{\frac{a}{b}}{c} \right\}", "brace")]
    [InlineData("DejaVuSans", @"\left( \frac{\frac{a}{b}}{c} \right)", "paren")]
    public void DelimitersCoverAsymmetricContent(string font, string latex, string name)
    {
        var style = new BoxStyle(FontPaths[font], 32f, Ink);
        var image = BoxRasterizer.RenderToRgba(Parse(latex, style).ShouldBeOfType<BracketBox>(), style);
        Directory.CreateDirectory(OutputDir);
        var big = Upscale(image, 8);
        var file = Path.Combine(OutputDir, $"asymmetric-{font}-{name}-nested-frac-32px.png");
        File.WriteAllBytes(file, PngWriter.Encode(big.Pixels, big.Width, big.Height));
        output.WriteLine(Path.GetFullPath(file));

        // The delimiters are the first and last runs of inked columns; the content is what lies between.
        bool Inked(int x) => Enumerable.Range(0, image.Height).Any(y => image.Pixels[(y * image.Width + x) * 4 + 3] > 0);
        var leftStart = Enumerable.Range(0, image.Width).First(Inked);
        var leftEnd = leftStart;
        while (Inked(leftEnd)) leftEnd++;
        var rightEnd = Enumerable.Range(0, image.Width).Last(Inked) + 1;
        var rightStart = rightEnd - 1;
        while (Inked(rightStart - 1)) rightStart--;

        var (delimTop, delimBottom) = InkRowSpan(image, leftStart, leftEnd);
        var (rTop, rBottom) = InkRowSpan(image, rightStart, rightEnd);
        var (inkTop, inkBottom) = InkRowSpan(image, leftEnd, rightStart);

        // The content's layout box, where the scene's draw put it: RenderToRgba sets the outermost box's
        // baseline at the margin plus its height.
        var box = Parse(latex, style);
        var content = Parse(latex.Replace(@"\left\{ ", "").Replace(@" \right\}", "").Replace(@"\left( ", "").Replace(@" \right)", ""), style);
        var margin = (image.Width - (int)MathF.Ceiling(box.Width)) / 2;
        var baseline = margin + box.Height;
        var boxTop = baseline - content.Height;
        var boxBottom = baseline + content.Depth;
        var request = 2 * MathF.Max(content.Height - style.AxisHeight, content.Depth + style.AxisHeight);
        var allowance = (request - ManagedFontRasterizer.DelimiterCovers(request, style.FontSize)) / 2;
        output.WriteLine($"{font} {latex}: delimiters rows {delimTop}..{delimBottom} and {rTop}..{rBottom}, content box " +
                         $"{boxTop:F2}..{boxBottom:F2} (ink {inkTop}..{inkBottom}), request {request:F2}, allowance {allowance:F2} at each end");
        Math.Max(delimTop, rTop).ShouldBeLessThanOrEqualTo((int)MathF.Floor(boxTop + allowance),
            "the delimiters' top ink row against the content's layout top");
        (Math.Min(delimBottom, rBottom) + 1).ShouldBeGreaterThanOrEqualTo((int)MathF.Ceiling(boxBottom - allowance),
            "the delimiters' bottom ink row against the content's layout bottom");
    }

    /// <summary>
    /// Content as far above the axis as below it asks for the same size as before the symmetric request:
    /// <c>\left( \frac{1}{2} \right)</c> at 32 px. DejaVu Sans, with no size variants, renders byte for byte
    /// as it did (hash taken at d03fa45). STIX Two Math takes the variant TeX's shortfall allows, 53.3 px
    /// (glyph 1303) for the 58.93 px request where the full request took the 61 px one (1304); its hash is
    /// taken at 65ed40d, which introduced the shortfall.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", "9D7B4F1A22ACBA9E")]
    [InlineData("DejaVuSans", "9E30A697D91269FF")]
    public void SymmetricContentKeepsItsSizeWithinTheShortfall(string font, string hash)
    {
        var style = new BoxStyle(FontPaths[font], 32f, Ink);
        var image = BoxRasterizer.RenderToRgba(Parse(@"\left( \frac{1}{2} \right)", style), style);
        var bytes = new byte[image.Pixels.Length + 8];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), image.Width);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), image.Height);
        image.Pixels.CopyTo(bytes, 8);
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
        output.WriteLine($"{font}: {image.Width}x{image.Height} {actual}");
        actual.ShouldBe(hash);
    }

    /// <summary>The first and last rows with ink in columns [<paramref name="x0"/>, <paramref name="x1"/>).</summary>
    private static (int Top, int Bottom) InkRowSpan(RgbaImage image, int x0, int x1)
    {
        int top = -1, bottom = -1;
        for (var y = 0; y < image.Height; y++)
            for (var x = x0; x < x1; x++)
                if (image.Pixels[(y * image.Width + x) * 4 + 3] > 0)
                {
                    if (top < 0) top = y;
                    bottom = y;
                    break;
                }
        return (top, bottom);
    }

    // ── the delimiter's shortfall ───────────────────────────────────────

    /// <summary>
    /// A <c>\left..\right</c> delimiter takes the first candidate at least
    /// <see cref="ManagedFontRasterizer.DelimiterCovers"/> of its request, max(0.901 x request, request - 0.5 em)
    /// (TeX's <c>\delimiterfactor</c> 901 and <c>\delimitershortfall</c> 5 pt at a 10 pt design size), so it
    /// does not go a size up for content just over a variant. STIX Two Math's parenthesis at 32 px asked for
    /// 48 px takes its 45.7 px variant (glyph 1302), where the full request needs the 53.3 px one (1303),
    /// which a radical, asking without the shortfall, still gets. An assembly is still built to the whole
    /// request: DejaVu Sans's brace asked for 122.78 px is 123 rows either way.
    /// </summary>
    [Fact]
    public void TheDelimiterChoiceTakesACandidateWithinTheShortfall()
    {
        var stix = FontPaths["STIX2Math"];
        Rasterizer.RasterizeStretchyDelimiter(stix, 32f, new Rune('('), 48f, out var delimiterGid, out _);
        Rasterizer.RasterizeStretchyVertical(stix, 32f, new Rune('('), 48f, out var fullGid);
        output.WriteLine($"STIX ( at 48 px: delimiter glyph {delimiterGid}, full request glyph {fullGid}");
        delimiterGid.ShouldBe(1302u, "the variant within the shortfall");
        fullGid.ShouldBe(1303u, "the variant covering the whole request");

        var brace = Rasterizer.RasterizeStretchyDelimiter(FontPaths["DejaVuSans"], 32f, new Rune('{'), 122.78f, out _, out var parts);
        parts.ShouldNotBeNull("no variant: the assembly");
        brace.Height.ShouldBe(123);
    }

    // ── the choice ──────────────────────────────────────────────────────

    public static IEnumerable<TheoryDataRow<string, int>> ChoiceCases() =>
    [
        new("STIX2Math", '('), new("STIX2Math", '{'), new("STIX2Math", 0x221A),
        new("DejaVuSans", '('), new("DejaVuSans", '['), new("DejaVuSans", '{'),
    ];

    /// <summary>
    /// What a request gets is the smallest candidate that covers it, in the order OpenType MATH sets them:
    /// the unstretched glyph, then each size variant, then the assembly. The assembly's candidates are its
    /// extender counts: r copies of every extender reach the heights from <see cref="AssemblyRange"/>'s
    /// least to its most, so it takes the fewest copies whose most covers the request, at the request or,
    /// where the request falls short of that count's least, at the least. Swept at 32 px from 8 px to 12 em.
    /// </summary>
    [Theory]
    [MemberData(nameof(ChoiceCases))]
    public void TheChoiceIsTheSmallestCandidateThatCovers(string font, int codepoint)
    {
        const float size = 32f;
        var path = FontPaths[font];
        var rune = new Rune(codepoint);
        var construction = Rasterizer.GetVerticalMathConstruction(path, rune).ShouldNotBeNull();
        var extenders = construction.Assembly.ShouldNotBeNull().Parts.Count(p => p.IsExtender);
        var (unitsPerEm, _) = Rasterizer.GetMathStretchInfo(path).ShouldNotBeNull();
        var baseHeight = Rasterizer.RasterizeGlyph(path, size, rune).Height;
        Rasterizer.RasterizeStretchyVertical(path, size, rune, 1f, out var baseGid);
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

            if (got != expected)
            {
                failures.Add($"request {request:F2}: expected {(expected == 0 ? "the assembly" : $"glyph {expected}")}, " +
                             $"got {(got == 0 ? $"the assembly, {bitmap.Height} px" : $"glyph {got}")}");
                continue;
            }
            if (got != 0) continue;
            var (copies, height) = ExpectedAssembly(path, size, rune, request);
            var drawnCopies = parts!.Count(p => p.IsExtender) / extenders;
            if (drawnCopies != copies || Math.Abs(bitmap.Height - height) > 1f)
                failures.Add($"request {request:F2}: expected {copies} copies of the extenders, {height:F2} px; " +
                             $"got {drawnCopies}, {bitmap.Height} px");
        }
        failures.ShouldBeEmpty($"{font} U+{codepoint:X4}:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>The heights, in font units, an assembly with <paramref name="copies"/> of every extender can
    /// take: from every join at the most overlap its two connectors allow (never under the table's minimum)
    /// to every join at the table's minimum.</summary>
    private static (int Least, int Most) AssemblyRange(
        SharpAstro.Fonts.Tables.OpenTypeMath.MathGlyphAssembly asm, ushort minOverlap, int copies)
    {
        var parts = asm.Parts.SelectMany(p => Enumerable.Repeat(p, p.IsExtender ? copies : 1)).ToList();
        var advance = parts.Sum(p => p.FullAdvance);
        var most = 0;
        for (var i = 0; i < parts.Count - 1; i++)
            most += Math.Max(minOverlap, Math.Min(parts[i].EndConnectorLength, parts[i + 1].StartConnectorLength));
        return (advance - most, advance - minOverlap * (parts.Count - 1));
    }

    /// <summary>The fewest copies of the extenders whose range reaches <paramref name="request"/>, and the
    /// height in pixels that count gives it: the request, or the count's least where that is taller, unless
    /// that least overshoots the bound a delimiter may (the larger of 1.25 times the request and the request
    /// plus half an em), where the assembly is squashed to the request.</summary>
    private static (int Copies, float HeightPx) ExpectedAssembly(string path, float size, Rune rune, float request)
    {
        var asm = Rasterizer.GetVerticalMathConstruction(path, rune)!.Assembly!;
        var (unitsPerEm, minOverlap) = Rasterizer.GetMathStretchInfo(path)!.Value;
        var units = request * unitsPerEm / size;
        var copies = 0;
        while (AssemblyRange(asm, minOverlap, copies).Most < units) copies++;
        var height = MathF.Max(units, AssemblyRange(asm, minOverlap, copies).Least) * size / unitsPerEm;
        return (copies, height > MathF.Max(1.25f * request, request + 0.5f * size) ? request : height);
    }

    // ── the composition ─────────────────────────────────────────────────

    public static IEnumerable<TheoryDataRow<string, int, float>> CompositionCases() =>
    [
        new("STIX2Math", 0x221A, 100f), new("STIX2Math", 0x221A, 151.3f), new("STIX2Math", 0x221A, 237.6f),
        new("STIX2Math", 0x221A, 400f), new("STIX2Math", '(', 150.5f), new("STIX2Math", '{', 222.2f),
        new("STIX2Math", '{', 138.15f),
        new("DejaVuSans", '(', 120f), new("DejaVuSans", '(', 201.7f), new("DejaVuSans", '[', 333.3f),
        new("DejaVuSans", '{', 250.4f),
    ];

    /// <summary>
    /// For a request between the assembly's minimum and its cap, the composed glyph lands within one pixel of
    /// the height its extender count can reach nearest the request: the extenders are counted with the
    /// table's least overlap, and what is left over is spread across the joins, so wherever the joins have
    /// room the glyph is the request. Where they have none it is the next height the assembly can reach:
    /// DejaVu Sans's connectors are exactly its <c>MinConnectorOverlap</c>, 40 units, so each copy of an
    /// extender adds a fixed 1.17 em; and STIX Two Math's brace at 138.15 px falls between its no-extender
    /// most (137.5 px) and its one-copy least (140.8 px). Where that next height overshoots the bound a
    /// delimiter may (see <see cref="ExpectedAssembly"/>) the assembly is squashed to the request. At 32 px.
    /// </summary>
    [Theory]
    [MemberData(nameof(CompositionCases))]
    public void TheComposedHeightLandsOnTheRequest(string font, int codepoint, float request)
    {
        var rune = new Rune(codepoint);
        var bitmap = Rasterizer.RasterizeStretchyVerticalWithParts(FontPaths[font], 32f, rune, request, out var parts);
        parts.ShouldNotBeNull("the request should need the assembly");
        var (copies, height) = ExpectedAssembly(FontPaths[font], 32f, rune, request);
        output.WriteLine($"{font} U+{codepoint:X4}: request {request} px, composed {bitmap.Height} px from {parts.Length} parts " +
                         $"({copies} copies of the extenders), which reach {height:F2} px");
        if (font == "STIX2Math" && request != 138.15f) height.ShouldBe(request, 1e-3f, "the joins should have room to reach the request");
        Math.Abs(bitmap.Height - height).ShouldBeLessThanOrEqualTo(1f);
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
        // The radicals over four, five and six nested fractions, at what each asks of the assembly.
        foreach (var size in new[] { 32f, 12f })
            foreach (var fractions in new[] { 4, 5, 6 })
            {
                var style = new BoxStyle(FontPaths["STIX2Math"], size, Ink);
                var radicand = Parse(NestedFractions(fractions), style);
                yield return new("STIX2Math", 0x221A, size, radicand.TotalHeight + 2 * 0.12f * size + style.RadicalRuleThickness);
            }
    }

    /// <summary>
    /// No seams. The stem columns are the columns inked in the extender's own bitmap; on every row of the
    /// composed glyph between its end parts, from the row the bottom end part's top crosses to the row the
    /// top end part's bottom crosses (so every join, and the rows straddling it, among them), the stem's
    /// alpha (summed across those columns) is at least 0.9 times the unstretched extender's peak. A join
    /// the old composer placed by whole pixels showed as a lighter row there. A brace's middle part, which
    /// leaves the stem for its cusp, is checked only over its connectors, where it is stem.
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

        // Parts are bottom-up: parts[0] the bottom end, parts[^1] the top end; rows run Y down.
        var asm = Rasterizer.GetVerticalMathConstruction(path, new Rune(codepoint))!.Assembly!;
        var first = (int)MathF.Floor(parts[^1].Bottom);
        var last = (int)MathF.Ceiling(parts[0].Top) - 1;
        bool InMiddlePartAwayFromItsConnectors(int y) => parts.Skip(1).SkipLast(1).Any(p =>
        {
            if (p.IsExtender) return false;
            var record = asm.Parts.First(q => q.GlyphId == p.GlyphId);
            var pxPerUnit = (p.Bottom - p.Top) / record.FullAdvance;
            return y + 1 > p.Top + record.EndConnectorLength * pxPerUnit && y < p.Bottom - record.StartConnectorLength * pxPerUnit;
        });
        var failures = new List<string>();
        for (var y = Math.Max(0, first); y <= Math.Min(bitmap.Height - 1, last); y++)
        {
            if (InMiddlePartAwayFromItsConnectors(y)) continue;
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
