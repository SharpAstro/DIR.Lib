using System.Reflection;
using DIR.Lib.MathLayout;
using StbImageSharp;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Golden-image tests for the <see cref="MathLayout"/> box engine. Each test
/// renders a small box tree to RGBA, encodes it as PNG, and compares pixel-
/// for-pixel against a baseline PNG checked into <c>Baselines/MathLayout/</c>.
/// On mismatch the actual render is dumped to <c>obj/test-output/</c> for
/// inspection. Set <c>BLESS=1</c> to overwrite the committed baseline with
/// the current render — used during iterative tuning of the renderer; the
/// baselines get "set in stone" once the visual quality is good.
/// </summary>
public sealed class MathLayoutBaselineTests
{
    /// <summary>Bundled DejaVu Sans for cross-machine determinism.</summary>
    private static string FontPath => Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");

    /// <summary>Folder of committed baseline PNGs (next to the test binary at runtime).</summary>
    private static string BaselineDir => Path.Combine(AppContext.BaseDirectory, "Baselines", "MathLayout");

    /// <summary>
    /// Source-tree baseline directory — used when BLESS=1 so the new
    /// render lands directly in the repo, not just in bin/.
    /// </summary>
    private static string SourceBaselineDir
    {
        get
        {
            // AppContext.BaseDirectory is bin/<config>/<tfm>/. Walk up to the
            // project directory and back into Baselines/. Compute it from the
            // assembly location to stay correct under MTP / xUnit v3.
            var asm = Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(asm)!;
            // bin/<config>/<tfm> → projectDir
            for (int i = 0; i < 3; i++) dir = Path.GetDirectoryName(dir)!;
            return Path.Combine(dir, "Baselines", "MathLayout");
        }
    }

    /// <summary>Where actual renders are dumped for inspection on failure.</summary>
    private static string FailedDir => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "test-output");

    private static bool BlessMode => Environment.GetEnvironmentVariable("BLESS") == "1";

    [Theory]
    [InlineData("glyph-hello")]
    [InlineData("hbox-a-plus-b")]
    [InlineData("frac-half")]
    [InlineData("frac-nested")]
    [InlineData("sqrt-x2-plus-y2")]
    [InlineData("supsub-e-i-pi")]
    [InlineData("bracket-paren")]
    [InlineData("bracket-square")]
    [InlineData("matrix-2x2")]
    [InlineData("limits-int-0-inf")]
    [InlineData("limits-sum-i-n")]
    [InlineData("hbox-int-eq-half")]
    public void Baseline(string name)
    {
        var (box, style) = BuildScene(name);
        var (rgba, w, h) = BoxRasterizer.RenderToRgba(box, style);

        Assert.True(w > 0 && h > 0, "box rasterized to empty buffer");

        var baselinePath = Path.Combine(BaselineDir, name + ".png");
        var sourceBaselinePath = Path.Combine(SourceBaselineDir, name + ".png");

        if (BlessMode || !File.Exists(baselinePath))
        {
            // First-run / re-bless: write the current render as the new
            // baseline. Update both the source-tree copy (committed) and
            // the bin/-copy (so subsequent test runs in this build pass).
            var png = PngWriter.Encode(rgba, w, h);
            Directory.CreateDirectory(Path.GetDirectoryName(sourceBaselinePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllBytes(sourceBaselinePath, png);
            File.WriteAllBytes(baselinePath, png);
            return; // pass — baseline written
        }

        // Decode the committed baseline via StbImageSharp (transitively
        // available through SharpAstro.Fonts) and compare RGBA byte-for-
        // byte. PNG byte equality is unreliable since deflate can emit
        // different valid encodings of the same pixels.
        var baselineBytes = File.ReadAllBytes(baselinePath);
        var baselineImg = ImageResult.FromMemory(baselineBytes, ColorComponents.RedGreenBlueAlpha);

        if (baselineImg.Width != w || baselineImg.Height != h
            || !rgba.AsSpan().SequenceEqual(baselineImg.Data))
        {
            DumpFailed(name, rgba, w, h);
            Assert.Fail(
                $"baseline mismatch for '{name}'. " +
                $"baseline {baselineImg.Width}×{baselineImg.Height}, actual {w}×{h}. " +
                $"Inspect obj/test-output/{name}.actual.png; if intentional, run BLESS=1 dotnet test.");
        }
    }

    private static (Box, BoxStyle) BuildScene(string name)
    {
        // Black foreground on transparent canvas — production code uses
        // white-on-terminal-black, but for golden-image inspection black
        // strokes are legible against any image-viewer background.
        var style = new BoxStyle(FontPath, 24f, new RGBAColor32(0, 0, 0, 255));

        Box box = name switch
        {
            "glyph-hello" => new GlyphBox("Hello", style),
            "hbox-a-plus-b" => new HBox(
                new GlyphBox("a", style),
                new KernBox(style.FontSize * 0.2f),
                new GlyphBox("+", style),
                new KernBox(style.FontSize * 0.2f),
                new GlyphBox("b", style)),
            "frac-half" => new FracBox(
                new GlyphBox("1", style),
                new GlyphBox("2", style),
                style),
            "frac-nested" => new FracBox(
                new GlyphBox("a", style),
                new FracBox(
                    new GlyphBox("b", style),
                    new GlyphBox("c", style),
                    style),
                style),
            "sqrt-x2-plus-y2" => new SqrtBox(
                new HBox(
                    new SupSubBox(new GlyphBox("x", style), new GlyphBox("2", style.Smaller()), null, style),
                    new KernBox(style.FontSize * 0.2f),
                    new GlyphBox("+", style),
                    new KernBox(style.FontSize * 0.2f),
                    new SupSubBox(new GlyphBox("y", style), new GlyphBox("2", style.Smaller()), null, style)),
                style),
            "supsub-e-i-pi" => new SupSubBox(
                new GlyphBox("e", style),
                new HBox(
                    new GlyphBox("i", style.Smaller()),
                    new GlyphBox("p", style.Smaller())),
                null,
                style),
            "bracket-paren" => new BracketBox(
                new GlyphBox("x", style), BracketKind.Paren, style),
            "bracket-square" => new BracketBox(
                new HBox(
                    new GlyphBox("a", style),
                    new GlyphBox(",", style),
                    new GlyphBox("b", style)),
                BracketKind.Square, style),
            "matrix-2x2" => BuildMatrix2x2(style),
            "limits-int-0-inf" => new LimitsBox(
                // \int rendered at 1.5x the base font — big operators
                // are scaled up in display style.
                new GlyphBox("∫", style, style.FontSize * 1.5f),
                new GlyphBox("0", style.Smaller()),
                new GlyphBox("∞", style.Smaller()),
                style),
            "limits-sum-i-n" => new LimitsBox(
                new GlyphBox("∑", style, style.FontSize * 1.5f),
                new HBox(
                    new GlyphBox("i", style.Smaller()),
                    new GlyphBox("=", style.Smaller()),
                    new GlyphBox("0", style.Smaller())),
                new GlyphBox("n", style.Smaller()),
                style),
            // Captures the math-axis alignment: a tall LimitsBox(∫) sitting
            // inside an HBox alongside a regular '=' GlyphBox and a fraction.
            // The integral's *visual centre* should align with the '=', not
            // its baseline — otherwise the '=' looks low against the tall
            // operator's extent.
            "hbox-int-eq-half" => new HBox(
                new LimitsBox(
                    new GlyphBox("∫", style, style.FontSize * 1.5f),
                    new GlyphBox("0", style.Smaller()),
                    new GlyphBox("∞", style.Smaller()),
                    style),
                new KernBox(style.FontSize * 0.3f),
                new GlyphBox("=", style),
                new KernBox(style.FontSize * 0.3f),
                new FracBox(
                    new GlyphBox("1", style),
                    new GlyphBox("2", style),
                    style)),
            _ => throw new ArgumentException($"unknown scene '{name}'"),
        };
        return (box, style);
    }

    private static Box BuildMatrix2x2(BoxStyle style)
    {
        var cells = new Box[2, 2];
        cells[0, 0] = new GlyphBox("a", style);
        cells[0, 1] = new GlyphBox("b", style);
        cells[1, 0] = new GlyphBox("c", style);
        cells[1, 1] = new GlyphBox("d", style);
        return new BracketBox(new MatrixBox(cells, style), BracketKind.Paren, style);
    }

    private static void DumpFailed(string name, byte[] rgba, int w, int h)
    {
        Directory.CreateDirectory(FailedDir);
        var png = PngWriter.Encode(rgba, w, h);
        File.WriteAllBytes(Path.Combine(FailedDir, name + ".actual.png"), png);
    }
}
