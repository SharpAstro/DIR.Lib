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
/// twice the fraction's height and a brace at about three times. Where no glyph the font has fits, the one
/// that needs the least scaling to fit is scaled. And a composed glyph shows no seam where its parts join.
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
    /// Scaling a glyph to fit, where nothing the font has fits, is never reached by STIX Two Math, whose
    /// size variants and finely stretched assemblies always land within the bound: these composed glyphs,
    /// at 32 px and 12 px, are byte for byte what they were before it (hashes taken at 157ad06, before the
    /// squash that came first).
    /// </summary>
    [Theory]
    [InlineData('{', 32f, 260f, "B7BD6F3AD652DB0E")]
    [InlineData('{', 32f, 138.15f, "4CE278826D9E4471")]
    [InlineData('(', 12f, 97.3f, "1E963ECD9E3EA525")]
    [InlineData(0x221A, 32f, 333.3f, "DACDCAECC56DB2A3")]
    public void StixAssembliesAreUnchangedByTheFit(int codepoint, float size, float request, string hash)
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
    /// Renders DejaVu Sans's parenthesis and brace pairs around a fraction, a brace around a nested one and
    /// a parenthesised b, at 32 px and 12 px under <c>obj/stretchy-assembly-output</c>, 8x, for a look by
    /// eye at the glyph scaled to fit where nothing the font has fits. Asserts nothing about the pixels.
    /// </summary>
    [Fact]
    public void WriteFitRenders()
    {
        Directory.CreateDirectory(OutputDir);
        (string Name, string Latex)[] scenes =
        [
            ("paren-frac", @"\left( \frac{1}{2} \right)"),
            ("brace-frac", @"\left\{ \frac{1}{2} \right\}"),
            ("brace-nested-frac", @"\left\{ \frac{\frac{a}{b}}{c} \right\}"),
            ("paren-b", "(b)"),
        ];
        foreach (var size in new[] { 32f, 12f })
            foreach (var (name, latex) in scenes)
            {
                var style = new BoxStyle(FontPaths["DejaVuSans"], size, Ink);
                var image = BoxRasterizer.RenderToRgba(Parse(latex, style), style);
                var big = Upscale(image, 8);
                var file = Path.Combine(OutputDir, $"fit-dejavu-{name}-{size:0}px.png");
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
    /// <c>\left( \frac{1}{2} \right)</c> at 32 px. DejaVu Sans, with no size variants, rendered byte for byte
    /// as it did through the symmetric request (hash 9E30A697D91269FF at d03fa45); its hash is now taken
    /// with the least-distortion fit, which draws its no-extender assembly squeezed to 74 rows where the
    /// squash drew it at the 59-row request (see <see cref="WhereNothingFitsTheLeastDistortedCandidateIsScaled"/>).
    /// STIX Two Math takes the variant TeX's shortfall allows, 53.3 px (glyph 1303) for the 58.93 px request
    /// where the full request took the 61 px one (1304); its hash is taken at 65ed40d, which introduced the
    /// shortfall.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", "9D7B4F1A22ACBA9E")]
    [InlineData("DejaVuSans", "3799CC19463B9397")]
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
    /// which a radical, asking without the shortfall, still gets. An assembly's copy counts are candidates
    /// too: DejaVu Sans's brace asked for 122.78 px takes no extender, three parts at their own 114 rows,
    /// which is within the shortfall; asked for the whole request, nothing fits (one copy of each extender
    /// is 189 px, past the bound of 153.5), and the same three parts are stretched to 123 rows, which
    /// distorts them least (see <see cref="WhereNothingFitsTheLeastDistortedCandidateIsScaled"/>).
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

        var dejavu = FontPaths["DejaVuSans"];
        var brace = Rasterizer.RasterizeStretchyDelimiter(dejavu, 32f, new Rune('{'), 122.78f, out _, out var parts);
        parts.ShouldNotBeNull("no variant: the assembly").Length.ShouldBe(3, "the assembly with no extender");
        Math.Abs(brace.Height - 114).ShouldBeLessThanOrEqualTo(1, "rows of the assembly with no extender");
        var full = Rasterizer.RasterizeStretchyVerticalWithParts(dejavu, 32f, new Rune('{'), 122.78f, out var fullParts);
        fullParts.ShouldNotBeNull("no variant: the assembly").Length.ShouldBe(3, "the assembly with no extender, stretched");
        full.Height.ShouldBe(123);
    }

    // ── where nothing fits ──────────────────────────────────────────────

    public static IEnumerable<TheoryDataRow<string, float, int, uint, int, int>> FitCases() =>
    [
        // (b) at 96 px asks for 98 px, at least 88.3 within the shortfall; the base parenthesis is 86 rows
        // and the assembly with no extender 227 px, past the bound of 146. The base, stretched to 89 rows.
        new("(b)", 96f, '(', 11u, 0, 89),
        // At 32 px the base parenthesis, 29 rows, covers the 28.8 px (b) needs: drawn as it is.
        new("(b)", 32f, '(', 11u, 0, 29),
        // A brace around a nested fraction asks for 122.78 px, at least 110.62: the assembly with no
        // extender reaches 114 px as it is (one copy of each extender would be 189 px).
        new(@"\left\{ \frac{\frac{a}{b}}{c} \right\}", 32f, '{', 0u, 0, 114),
        // A parenthesis around a half asks for 58.93 px, at most 74.93: the assembly with no extender is
        // 76.8 px, which squeezed to 74 rows distorts far less than the base's 29 rows stretched to 54.
        new(@"\left( \frac{1}{2} \right)", 32f, '(', 0u, 0, 74),
    ];

    /// <summary>
    /// Where nothing the font has covers a <c>\left..\right</c> delimiter's request within TeX's shortfall
    /// without overshooting the bound (the larger of 1.25 times the request and the request plus half an em),
    /// the candidate that needs the least scaling to fit is scaled, to the nearest whole row the shortfall
    /// and the bound allow; one that fits is drawn as it is. The candidates are the base glyph, the size
    /// variants and the assembly at each copy count. Squashing the assembly to the request, what this did
    /// before, thinned ordinary text: <c>(b)</c> at 96 px took an assembly 2.4 em tall squashed to 98 px,
    /// where the base parenthesis needs only 3% more height. DejaVu Sans, which has no size variants and no
    /// room in its joins. <paramref name="glyphId"/> is the base glyph's id, or 0 for the assembly with
    /// <paramref name="copies"/> copies of the extenders; <paramref name="rows"/> the rows drawn, within one
    /// for a glyph drawn as it is.
    /// </summary>
    [Theory]
    [MemberData(nameof(FitCases))]
    public void WhereNothingFitsTheLeastDistortedCandidateIsScaled(string latex, float size, int codepoint, uint glyphId, int copies, int rows)
    {
        var style = new BoxStyle(FontPaths["DejaVuSans"], size, Ink);
        var content = Parse(latex.Replace(@"\left\{ ", "").Replace(@" \right\}", "").Replace(@"\left( ", "").Replace(@" \right)", "")
            .Replace("(b)", "b"), style);
        var request = 2 * MathF.Max(content.Height - style.AxisHeight, content.Depth + style.AxisHeight);
        Parse(latex, style).ShouldBeOfType<BracketBox>();
        var bitmap = Rasterizer.RasterizeStretchyDelimiter(style.FontPath, size, new Rune(codepoint), request, out var gid, out var parts);
        output.WriteLine($"{latex} at {size} px: request {request:F2} (covered from {ManagedFontRasterizer.DelimiterCovers(request, size):F2}, " +
                         $"bound {MathF.Max(1.25f * request, request + 0.5f * size):F2}): {bitmap.Height} rows, " +
                         $"{(parts is null ? $"glyph {gid}" : $"{parts.Length} parts")}");
        if (glyphId != 0)
        {
            parts.ShouldBeNull("the base glyph, not the assembly");
            gid.ShouldBe(glyphId);
            bitmap.Height.ShouldBe(rows);
        }
        else
        {
            parts.ShouldNotBeNull("the assembly");
            var perCopy = Rasterizer.GetVerticalMathConstruction(style.FontPath, new Rune(codepoint))!.Assembly!.Parts.Count(p => p.IsExtender);
            parts.Count(p => p.IsExtender).ShouldBe(copies * perCopy, "copies of the extenders");
            Math.Abs(bitmap.Height - rows).ShouldBeLessThanOrEqualTo(1, $"rows against {rows}");
        }
    }

    /// <summary>
    /// The height does not jump where the assembly starts to fit. DejaVu Sans's parenthesis at 32 px is 76.8
    /// px with no extender; that fits once the bound reaches it, at a request of 60.8 px. Below it the
    /// assembly used to be squashed to the request, so 60.7 px drew 61 rows and 60.9 px drew 77. Squeezed to
    /// the bound instead, the least it must be, it meets its own height there. Swept from 45 to 80 px, past
    /// where the base glyph stretched would distort less (42.4 px) and short of where the assembly no longer
    /// covers within the shortfall (85.2 px): one row at most between requests 0.1 px apart.
    /// </summary>
    [Fact]
    public void TheHeightDoesNotJumpWhereTheAssemblyStartsToFit()
    {
        var path = FontPaths["DejaVuSans"];
        var jumps = new List<string>();
        var previous = -1;
        for (var tenths = 450; tenths <= 800; tenths++)
        {
            var request = tenths / 10f;
            var rows = Rasterizer.RasterizeStretchyDelimiter(path, 32f, new Rune('('), request, out _, out _).Height;
            if (previous >= 0 && Math.Abs(rows - previous) > 1) jumps.Add($"request {request:F1}: {previous} -> {rows} rows");
            previous = rows;
        }
        jumps.ShouldBeEmpty(string.Join(Environment.NewLine, jumps));
    }

    // ── pairs ───────────────────────────────────────────────────────────

    /// <summary>Content of a given height and depth that draws nothing, for a pair to be sized to.</summary>
    private sealed class Strut(float width, float height, float depth) : Box
    {
        public override float Width => width;
        public override float Height => height;
        public override float Depth => depth;
        public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style) { }
    }

    public static IEnumerable<TheoryDataRow<string, BracketKind>> PairCases() =>
        from font in new[] { "STIX2Math", "DejaVuSans" }
        from kind in new[] { BracketKind.Paren, BracketKind.Square, BracketKind.Curly }
        select new TheoryDataRow<string, BracketKind>(font, kind);

    /// <summary>
    /// A pair's delimiters share one height. Each side's glyph is chosen on its own, and the two need not
    /// come out alike: DejaVu Sans's <c>[</c> and <c>]</c> have parts of different advances (the extender
    /// 2445 units against 2430, the ends 2421 and 2417 against 2406 and 2417), so one request can take a
    /// different copy count, or a different candidate where nothing fits, on each side (at 16 px, asked
    /// for 150 px, <c>[</c> drew 151 rows and <c>]</c> 169 under the squash); STIX Two Math's braces end in
    /// connectors of different lengths, so where their joins run out of room they land a few rows apart.
    /// Swept from 1 em to 12 em in 1.5 px steps at 12, 16 and 32 px, a <see cref="BracketBox"/> around
    /// content centred on the axis: its two delimiters' ink spans the same rows.
    /// </summary>
    [Theory]
    [MemberData(nameof(PairCases))]
    public void APairsDelimitersShareOneHeight(string font, BracketKind kind)
    {
        var failures = new List<string>();
        foreach (var size in new[] { 12f, 16f, 32f })
        {
            var style = new BoxStyle(FontPaths[font], size, Ink);
            for (var request = size; request <= 12 * size; request += 1.5f)
            {
                var content = new Strut(0.5f * size, style.AxisHeight + request / 2, MathF.Max(0f, request / 2 - style.AxisHeight));
                var image = BoxRasterizer.RenderToRgba(new BracketBox(content, kind, style), style);
                var (left, right) = DelimiterRowSpans(image);
                if (left != right)
                    failures.Add($"{size} px, request {request:F1}: rows {left.Top}..{left.Bottom} on the left, {right.Top}..{right.Bottom} on the right");
            }
        }
        failures.ShouldBeEmpty($"{font} {kind}, {failures.Count} requests:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    public static IEnumerable<TheoryDataRow<string, string, float>> MixedPairCases() =>
        from font in new[] { "STIX2Math", "DejaVuSans" }
        from latex in new[]
        {
            @"[\frac{a}{b}, c)", @"[\frac{\frac{a}{b}}{c}, d)", @"(\frac{\frac{\frac{a}{b}}{c}}{d}, e]",
            @"\lfloor \frac{\frac{a}{b}}{c} \rfloor",
        }
        from size in new[] { 12f, 16f, 32f }
        select new TheoryDataRow<string, string, float>(font, latex, size);

    /// <summary>
    /// The same for a pair of two kinds, the half-open intervals and the named pairs, which a
    /// <see cref="BracketBox"/> cannot draw: the opening and the closing delimiter's ink spans the same rows.
    /// </summary>
    [Theory]
    [MemberData(nameof(MixedPairCases))]
    public void AMixedPairsDelimitersShareOneHeight(string font, string latex, float size)
    {
        var style = new BoxStyle(FontPaths[font], size, Ink);
        var image = BoxRasterizer.RenderToRgba(Parse(latex, style), style);
        var (left, right) = DelimiterRowSpans(image);
        output.WriteLine($"{font} {latex} at {size} px: rows {left.Top}..{left.Bottom} and {right.Top}..{right.Bottom}");
        right.ShouldBe(left, "the closing delimiter's rows against the opening one's");
    }

    public static IEnumerable<TheoryDataRow<string, string, string, string, float>> HalfOpenCases() =>
        from font in new[] { "STIX2Math", "DejaVuSans" }
        from pair in new[]
        {
            ("[", @"\frac{\frac{a}{b}}{c}, d", ")"), ("(", @"\frac{a}{\frac{b}{c}}, d", "]"), ("[", @"\frac{a}{b}, c", ")"),
        }
        from size in new[] { 12f, 16f, 32f }
        select new TheoryDataRow<string, string, string, string, float>(font, pair.Item1, pair.Item2, pair.Item3, size);

    /// <summary>
    /// A half-open interval's delimiters are sized as a <see cref="BracketBox"/>'s are: the pair asks for
    /// twice the content's larger reach from the math axis, as TeX's <c>\left..\right</c> does, may fall
    /// short of it by TeX's shortfall, and is drawn at one height
    /// (<see cref="StretchyVerticalBox.DelimiterPair"/>). It asked for the content's total height, with no
    /// shortfall, so <c>[a, b)</c> and <c>[a, b]</c> were sized by different rules, and content reaching
    /// further above the axis than below it (a numerator that is itself a fraction) poked out of
    /// <c>[a, b)</c>. Each rendered delimiter's ink spans as many rows as that pair's glyph inks.
    /// </summary>
    [Theory]
    [MemberData(nameof(HalfOpenCases))]
    public void AHalfOpenIntervalIsSizedAsABracketPairIs(string font, string open, string content, string close, float size)
    {
        var style = new BoxStyle(FontPaths[font], size, Ink);
        var inner = Parse(content, style);
        var request = 2 * MathF.Max(inner.Height - style.AxisHeight, inner.Depth + style.AxisHeight);
        var (opening, closing) = StretchyVerticalBox.DelimiterPair(open[0], close[0], request, style);
        var image = BoxRasterizer.RenderToRgba(Parse(open + content + close, style), style);
        var (left, right) = DelimiterRowSpans(image);
        output.WriteLine($"{font} {open}{content}{close} at {size} px: request {request:F2} (total height {inner.TotalHeight:F2}); " +
                         $"drawn rows {left.Top}..{left.Bottom} and {right.Top}..{right.Bottom}; the pair inks " +
                         $"{InkedRows(opening.Bitmap)} and {InkedRows(closing.Bitmap)} rows");
        (left.Bottom - left.Top + 1).ShouldBe(InkedRows(opening.Bitmap), $"rows of the opening {open}");
        (right.Bottom - right.Top + 1).ShouldBe(InkedRows(closing.Bitmap), $"rows of the closing {close}");
    }

    /// <summary>The rows of <paramref name="bitmap"/>, from the first to the last that holds ink.</summary>
    private static int InkedRows(GlyphBitmap bitmap)
    {
        var rows = Enumerable.Range(0, bitmap.Height)
            .Where(y => Enumerable.Range(0, bitmap.Width).Any(x => Alpha(bitmap, x, y) > 0)).ToList();
        return rows.Count == 0 ? 0 : rows[^1] - rows[0] + 1;
    }

    /// <summary>The rows the leftmost and the rightmost run of inked columns span: a pair's delimiters, with
    /// the content between them.</summary>
    private static ((int Top, int Bottom) Left, (int Top, int Bottom) Right) DelimiterRowSpans(RgbaImage image)
    {
        bool Inked(int x) => Enumerable.Range(0, image.Height).Any(y => image.Pixels[(y * image.Width + x) * 4 + 3] > 0);
        var leftStart = Enumerable.Range(0, image.Width).First(Inked);
        var leftEnd = leftStart;
        while (Inked(leftEnd)) leftEnd++;
        var rightEnd = Enumerable.Range(0, image.Width).Last(Inked) + 1;
        var rightStart = rightEnd - 1;
        while (Inked(rightStart - 1)) rightStart--;
        return (InkRowSpan(image, leftStart, leftEnd), InkRowSpan(image, rightStart, rightEnd));
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
    /// where the request falls short of that count's least, at the least. Where that overshoots the bound,
    /// or nothing covers, the candidate that needs the least scaling to fit is scaled
    /// (<see cref="ExpectedChoice"/>). Swept at 32 px from 8 px to 12 em, for a radical's or an operator's
    /// request (covered in full) and a <c>\left..\right</c> delimiter's (covered within TeX's shortfall).
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
        var failures = new List<string>();

        foreach (var delimiter in new[] { false, true })
            for (var request = 8f; request <= 12 * size; request += 1.37f)
            {
                var coversFrom = delimiter ? ManagedFontRasterizer.DelimiterCovers(request, size) : request;
                var gid = 0u;
                ManagedFontRasterizer.AssemblyPart[]? parts;
                var bitmap = delimiter
                    ? Rasterizer.RasterizeStretchyDelimiter(path, size, rune, request, out gid, out parts)
                    : Rasterizer.RasterizeStretchyVerticalWithParts(path, size, rune, request, out parts);
                if (!delimiter) Rasterizer.RasterizeStretchyVertical(path, size, rune, request, out gid);
                bitmap.Height.ShouldBeGreaterThan(0);

                // A glyph id names the base or a variant (STIX Two Math lists its base as variant 0); 0 the assembly.
                var expected = ExpectedChoice(path, size, rune, request, coversFrom);
                var got = parts is not null ? 0 : gid;
                var what = $"{(delimiter ? "delimiter" : "full")} request {request:F2}";

                if (got != expected.GlyphId)
                {
                    failures.Add($"{what}: expected {expected}, got {(got == 0 ? $"the assembly, {bitmap.Height} px" : $"glyph {got}, {bitmap.Height} px")}");
                    continue;
                }
                var drawnCopies = parts is null ? 0 : parts.Count(p => p.IsExtender) / extenders;
                if (drawnCopies != expected.Copies)
                    failures.Add($"{what}: expected {expected}, got {drawnCopies} copies of the extenders, {bitmap.Height} px");
                // A glyph drawn as it is has its own rows; a scaled one, or an assembly, the height chosen for it.
                else if ((got == 0 || expected.Scaled) && Math.Abs(bitmap.Height - expected.HeightPx) > 1f)
                    failures.Add($"{what}: expected {expected}, got {bitmap.Height} px");
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

    /// <summary>What a request should get: the base glyph or a size variant (<c>GlyphId</c>), or the
    /// assembly (<c>GlyphId</c> 0) with <c>Copies</c> copies of every extender; <c>HeightPx</c> the height it
    /// lands on, and whether it is <c>Scaled</c> to get there.</summary>
    private readonly record struct Expected(uint GlyphId, int Copies, float HeightPx, bool Scaled)
    {
        public override string ToString() =>
            (GlyphId == 0 ? $"the assembly with {Copies} copies of the extenders" : $"glyph {GlyphId}") +
            $", {HeightPx:F2} px{(Scaled ? ", scaled" : "")}";
    }

    /// <summary>
    /// The choice, worked out from the font's own numbers. The least height that covers is
    /// <paramref name="coversFrom"/> (the request, or less for a delimiter), and the most a glyph may be is
    /// the bound, the larger of 1.25 times the request and the request plus half an em.
    /// <list type="number">
    /// <item>The base glyph, by its rows, or the first size variant, by its advance, that covers is drawn as
    /// it is.</item>
    /// <item>Else the assembly with the fewest copies whose most reaches the request (it can be built to
    /// the whole request, so it is), at the request within its range, is drawn as it is where its ink is
    /// within the bound.</item>
    /// <item>Else every candidate (the base glyph, each variant, that assembly and the one with a copy fewer,
    /// at the request within their ranges) is a height it would be scaled from to the nearest whole row
    /// from the least that covers to the bound, and the one scaled by the factor nearest 1 wins: a glyph
    /// twice as tall as it should be is as far off as one half as tall. One already within those is drawn
    /// as it is.</item>
    /// <item>A construction with no assembly gives its largest variant as it is.</item>
    /// </list>
    /// </summary>
    private static Expected ExpectedChoice(string path, float size, Rune rune, float request, float coversFrom)
    {
        var construction = Rasterizer.GetVerticalMathConstruction(path, rune).ShouldNotBeNull();
        var (unitsPerEm, minOverlap) = Rasterizer.GetMathStretchInfo(path)!.Value;
        var pxPerUnit = size / unitsPerEm;
        var bound = MathF.Max(1.25f * request, request + 0.5f * size);
        Rasterizer.RasterizeStretchyVertical(path, size, rune, 1f, out var baseGid);
        float baseRows = Rasterizer.RasterizeGlyph(path, size, rune).Height;
        if (baseRows >= coversFrom) return new(baseGid, 0, baseRows, false);
        var coversUnits = (int)Math.Ceiling(coversFrom / pxPerUnit);
        if (construction.Variants.FirstOrDefault(v => v.AdvanceMeasurement >= coversUnits) is { GlyphId: > 0 } covering)
            return new(covering.GlyphId, 0, covering.AdvanceMeasurement * pxPerUnit, false);

        // With no assembly, the largest variant as it is.
        if (construction.Assembly is not { } asm)
            return new(construction.Variants[^1].GlyphId, 0, construction.Variants[^1].AdvanceMeasurement * pxPerUnit, false);
        var candidates = new List<Expected> { new(baseGid, 0, baseRows, true) };
        candidates.AddRange(construction.Variants.Select(v => new Expected(v.GlyphId, 0, v.AdvanceMeasurement * pxPerUnit, true)));
        {
            var copies = 0;
            while (copies < 100 && AssemblyRange(asm, minOverlap, copies).Most < request / pxPerUnit) copies++;
            var height = ComposedInkPx(path, asm, minOverlap, copies, request, pxPerUnit);
            if (AssemblyRange(asm, minOverlap, copies).Most >= request / pxPerUnit && height <= bound)
                return new(0, copies, height, false);
            candidates.Add(new(0, copies, height, true));
            if (copies > 0) candidates.Add(new(0, copies - 1, ComposedInkPx(path, asm, minOverlap, copies - 1, request, pxPerUnit), true));
        }

        var low = MathF.Ceiling(coversFrom);
        var high = MathF.Max(low, MathF.Floor(bound));
        var best = candidates.MinBy(c => MathF.Abs(MathF.Log(Math.Clamp(c.HeightPx, low, high) / c.HeightPx)));
        var fits = best.HeightPx >= coversFrom && best.HeightPx <= bound;
        return fits ? best with { Scaled = false } : best with { HeightPx = Math.Clamp(best.HeightPx, low, high) };
    }

    /// <summary>The ink height in pixels of the assembly with <paramref name="copies"/> of every extender,
    /// its joins spread so its advances come to <paramref name="request"/> within the range they reach: each
    /// part's ink starts where the one below it ends, so the topmost part's ink, not its advance, is the top.</summary>
    private static float ComposedInkPx(string path, SharpAstro.Fonts.Tables.OpenTypeMath.MathGlyphAssembly asm,
        ushort minOverlap, int copies, float request, float pxPerUnit)
    {
        var (least, most) = AssemblyRange(asm, minOverlap, copies);
        var top = asm.Parts[^1];
        Rasterizer.TryGetOpenTypeFont(path, out var font).ShouldBeTrue();
        var bounds = new ManagedFontRasterizer.TopEdgeSink();
        font.DrawGlyph(top.GlyphId, bounds);
        return (Math.Clamp(request / pxPerUnit, least, most) - top.FullAdvance + bounds.YMax - bounds.YMin) * pxPerUnit;
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
    /// most (137.5 px) and its one-copy least (140.8 px). Where that next height overshoots the bound, the
    /// candidate that needs the least scaling is scaled to fit (see <see cref="ExpectedChoice"/>). At 32 px.
    /// </summary>
    [Theory]
    [MemberData(nameof(CompositionCases))]
    public void TheComposedHeightLandsOnTheRequest(string font, int codepoint, float request)
    {
        var rune = new Rune(codepoint);
        var bitmap = Rasterizer.RasterizeStretchyVerticalWithParts(FontPaths[font], 32f, rune, request, out var parts);
        parts.ShouldNotBeNull("the request should need the assembly");
        var expected = ExpectedChoice(FontPaths[font], 32f, rune, request, request);
        output.WriteLine($"{font} U+{codepoint:X4}: request {request} px, composed {bitmap.Height} px from {parts.Length} parts; " +
                         $"expected {expected}");
        expected.GlyphId.ShouldBe(0u, "the assembly");
        if (font == "STIX2Math" && request != 138.15f) expected.HeightPx.ShouldBe(request, 0.05f, "the joins should have room to reach the request");
        Math.Abs(bitmap.Height - expected.HeightPx).ShouldBeLessThanOrEqualTo(1f);
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
