using System.Reflection;
using DIR.Lib.MathLayout;
using SharpAstro.Fonts;
using StbImageSharp;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Golden-image tests for the <see cref="MathLayout"/> box engine. Each test
/// renders a small box tree to RGBA, encodes it as PNG, and compares pixel-
/// for-pixel against a baseline PNG checked into
/// <c>Baselines/MathLayout/&lt;FontName&gt;/</c>. The font subfolder lets us run
/// every scene against multiple fonts — currently <c>DejaVuSans</c> (no MATH
/// table; exercises the SqrtBox/BracketBox parametric/scaled-glyph fallbacks)
/// and <c>STIX2Math</c> (full OpenType MATH coverage; exercises the
/// stretchy-delimiter / radical-glyph / MATH-driven-metrics paths). Same scene
/// + different font = different file = independent baseline.
///
/// <para>On mismatch the actual render is dumped to <c>obj/test-output/</c> for
/// inspection. Set <c>BLESS=1</c> to overwrite the committed baseline with
/// the current render — used during iterative tuning of the renderer; the
/// baselines get "set in stone" once the visual quality is good.</para>
/// </summary>
public sealed class MathLayoutBaselineTests
{
    /// <summary>Font-name → file path. Names appear verbatim as the
    /// per-font subfolder under <c>Baselines/MathLayout/</c>; keep them
    /// filesystem-safe (no spaces or punctuation).</summary>
    private static readonly Dictionary<string, string> Fonts = new()
    {
        // No MATH table — drives SqrtBox path 2/3 and parametric brackets.
        ["DejaVuSans"] = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf"),
        // Full OpenType MATH table — drives the StretchyVerticalBox /
        // MATH-constants paths through SqrtBox path 1, BracketBox stretchy
        // glyphs, font-driven AxisHeight / FractionRuleThickness etc.
        // Bundled under SIL Open Font License (see Fonts/STIX2-OFL.txt).
        ["STIX2Math"]  = Path.Combine(AppContext.BaseDirectory, "Fonts", "STIX2Math.otf"),
    };

    /// <summary>
    /// Render at a large em size (96 px) so the committed baselines are
    /// readable at native zoom in image viewers — the math layout's
    /// proportions are scale-invariant, so the quality signal is the same as
    /// at the 24 px display size used in the live console; just easier to
    /// see what's wrong without zooming to 800%. Grid spacing scales with
    /// font size to keep the same visual density.
    /// </summary>
    private const float BaselineFontSize = 96f;

    /// <summary>Per-font folder of committed baseline PNGs (next to the test binary at runtime).</summary>
    private static string BaselineDir(string font) => Path.Combine(AppContext.BaseDirectory, "Baselines", "MathLayout", font);

    /// <summary>
    /// Per-font source-tree baseline directory — used when BLESS=1 so the new
    /// render lands directly in the repo, not just in bin/.
    /// </summary>
    private static string SourceBaselineDir(string font)
    {
        // AppContext.BaseDirectory is bin/<config>/<tfm>/. Walk up to the
        // project directory and back into Baselines/. Compute it from the
        // assembly location to stay correct under MTP / xUnit v3.
        var asm = Assembly.GetExecutingAssembly().Location;
        var dir = Path.GetDirectoryName(asm)!;
        // bin/<config>/<tfm> → projectDir
        for (int i = 0; i < 3; i++) dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir, "Baselines", "MathLayout", font);
    }

    /// <summary>Where actual renders are dumped for inspection on failure.</summary>
    private static string FailedDir => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "test-output");

    private static bool BlessMode => Environment.GetEnvironmentVariable("BLESS") == "1";

    /// <summary>Scene names exercised against every font in <see cref="Fonts"/>.
    /// Picking a small set that covers each box type at least once, plus a few
    /// composite formulas that stress alignment / metric integration.</summary>
    private static readonly string[] SceneNames =
    [
        "glyph-hello",
        "hbox-a-plus-b",
        "frac-half",
        "frac-nested",
        "sqrt-x2-plus-y2",
        "sqrt-fourth-root",
        "supsub-e-i-pi",
        "bracket-paren",
        "bracket-square",
        "matrix-2x2",
        "int-0-inf",
        "limits-sum-i-n",
        "hbox-int-eq-half",
        "integral-formula-full",
        "fine-structure-constant",
        "standard-model-lagrangian",
    ];

    /// <summary>Cross product of (font, scene) — every scene runs once per
    /// font, with its own committed baseline file.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> BaselineCases()
    {
        foreach (var font in Fonts.Keys)
            foreach (var scene in SceneNames)
                yield return new TheoryDataRow<string, string>(font, scene);
    }

    [Theory]
    [MemberData(nameof(BaselineCases))]
    public void Baseline(string font, string scene)
    {
        var (box, style) = BuildScene(scene, Fonts[font]);
        var (rgba, w, h) = BoxRasterizer.RenderToRgba(box, style);

        Assert.True(w > 0 && h > 0, "box rasterized to empty buffer");

        // Composite the (transparent-background) render onto a grid-paper
        // backdrop so the committed baselines stay readable in any image
        // viewer regardless of the surrounding window background. Production
        // callers (Console.Lib's terminal compositors) still consume the
        // transparent buffer from BoxRasterizer directly — this grid is a
        // golden-image-only concern. Grid spacing scales with the baseline
        // font size so squares stay at ~1/3 em (visually consistent).
        rgba = ComposeOnGridPaper(rgba, w, h, gridSpacing: (int)(BaselineFontSize / 3f));

        var baselinePath = Path.Combine(BaselineDir(font), scene + ".png");
        var sourceBaselinePath = Path.Combine(SourceBaselineDir(font), scene + ".png");

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
            DumpFailed(font, scene, rgba, w, h);
            Assert.Fail(
                $"baseline mismatch for '{font}/{scene}'. " +
                $"baseline {baselineImg.Width}×{baselineImg.Height}, actual {w}×{h}. " +
                $"Inspect obj/test-output/{font}/{scene}.actual.png; if intentional, run BLESS=1 dotnet test.");
        }
    }

    private static (Box, BoxStyle) BuildScene(string name, string fontPath)
    {
        // Black foreground on transparent canvas — production code uses
        // white-on-terminal-black, but for golden-image inspection black
        // strokes are legible against any image-viewer background.
        var style = new BoxStyle(fontPath, BaselineFontSize, new RGBAColor32(0, 0, 0, 255));

        // Latin-letter atoms in math contexts render as math italic via
        // MathGlyphBox: the rune is remapped to the Unicode U+1D434
        // italic-Latin block when the font's cmap covers it (STIX),
        // otherwise it falls back to upright (DejaVu and other body
        // fonts have no math-italic coverage). This matches MathJax
        // and TeX convention: "x" in sqrt(x) renders italic, while
        // numbers, operators, brackets, and Greek (which math fonts
        // already render italic by design) keep their plain codepoints.
        Box it(string text, BoxStyle s) => new MathGlyphBox(text, s, MathStyle.Italic);

        Box box = name switch
        {
            // "Hello" is a text label, not a math expression — kept upright.
            "glyph-hello" => new GlyphBox("Hello", style),
            "hbox-a-plus-b" => new HBox(
                it("a", style),
                new KernBox(style.FontSize * 0.2f),
                new GlyphBox("+", style),
                new KernBox(style.FontSize * 0.2f),
                it("b", style)),
            "frac-half" => new FracBox(
                new GlyphBox("1", style),
                new GlyphBox("2", style),
                style),
            // Nested fraction. The inner b/c renders at script-style
            // (smaller) by TeX / MathJax convention — at displaystyle
            // the outer fraction is "main", so anything that's already
            // a sub-component drops a script-size level. Without the
            // shrink, b and c look the same size as a, which both
            // looks heavy and reads ambiguously.
            "frac-nested" => new FracBox(
                it("a", style),
                new FracBox(
                    it("b", style.Smaller()),
                    it("c", style.Smaller()),
                    style.Smaller()),
                style),
            "sqrt-x2-plus-y2" => new SqrtBox(
                new HBox(
                    new SupSubBox(it("x", style), new GlyphBox("2", style.Smaller()), null, style),
                    new KernBox(style.FontSize * 0.2f),
                    new GlyphBox("+", style),
                    new KernBox(style.FontSize * 0.2f),
                    new SupSubBox(it("y", style), new GlyphBox("2", style.Smaller()), null, style)),
                style),
            // Fourth root — exercises SqrtBox's optional index parameter.
            // The "4" is rendered at scriptscript size (Smaller().Smaller())
            // and tucked into the radical's hook per the font's MATH
            // RadicalKern* / RadicalDegreeBottomRaisePercent (STIX) or the
            // TeX-style fallback (DejaVu).
            "sqrt-fourth-root" => new SqrtBox(
                it("x", style),
                new GlyphBox("4", style.Smaller().Smaller()),
                style),
            "supsub-e-i-pi" => new SupSubBox(
                it("e", style),
                new HBox(
                    it("i", style.Smaller()),
                    it("p", style.Smaller())),
                null,
                style),
            "bracket-paren" => new BracketBox(
                it("x", style), BracketKind.Paren, style),
            "bracket-square" => new BracketBox(
                new HBox(
                    it("a", style),
                    new GlyphBox(",", style),
                    it("b", style)),
                BracketKind.Square, style),
            "matrix-2x2" => BuildMatrix2x2(style),
            // ∫ with bounds in script-style placement (TeX \nolimits / MathJax
            // default for inline integrals): ∞ at top-right, 0 at bottom-right.
            // SupSubBox applies italic correction so the integral's slanted
            // top pushes the super further right than the sub. \sum / \prod
            // / \lim use limit-style placement instead — see limits-sum-i-n.
            "int-0-inf" => new SupSubBox(
                new BigOperatorBox(0x222B, style),
                new GlyphBox("∞", style.Smaller()),
                new GlyphBox("0", style.Smaller()),
                style),
            "limits-sum-i-n" => new LimitsBox(
                new BigOperatorBox(0x2211, style),
                new HBox(
                    it("i", style.Smaller()),
                    new GlyphBox("=", style.Smaller()),
                    new GlyphBox("0", style.Smaller())),
                it("n", style.Smaller()),
                style),
            // Captures the math-axis alignment: a tall integral with
            // script-style bounds inside an HBox alongside a regular
            // '=' GlyphBox and a fraction. The integral's *visual centre*
            // should align with the '=', not its baseline — otherwise
            // the '=' looks low against the tall operator's extent.
            // (limits-int-0-inf below covers the limit-style placement
            // separately as a primitive test of LimitsBox.)
            "hbox-int-eq-half" => new HBox(
                new SupSubBox(
                    new BigOperatorBox(0x222B, style),
                    new GlyphBox("∞", style.Smaller()),
                    new GlyphBox("0", style.Smaller()),
                    style),
                new KernBox(style.FontSize * 0.3f),
                new GlyphBox("=", style),
                new KernBox(style.FontSize * 0.3f),
                new FracBox(
                    new GlyphBox("1", style),
                    new GlyphBox("2", style),
                    style)),
            // Full ∫₀^∞ e^(-x²) dx = √π/2 — the formula in the user-
            // reported alignment bug. The integral's centre, the '='
            // sign, and the fraction bar should all sit on the math axis;
            // baseline-letter glyphs (e, dx) stay at the line baseline.
            // e, x, d, x are math letters → italic; π is Greek (font
            // renders italic by design); 0, ∞, 2 stay upright.
            //
            // \int uses script-style bounds (top-right / bottom-right)
            // rather than limit-style (above / below) — TeX's \nolimits
            // convention, also what MathJax does for inline integrals.
            // \sum / \prod / \lim use the opposite, limit-style placement
            // (see the limits-* scenes); SupSubBox vs LimitsBox is the
            // toggle.
            "integral-formula-full" => new HBox(
                new SupSubBox(
                    new BigOperatorBox(0x222B, style),
                    new GlyphBox("∞", style.Smaller()),
                    new GlyphBox("0", style.Smaller()),
                    style),
                new KernBox(style.FontSize * 0.1f),
                new SupSubBox(
                    it("e", style),
                    new HBox(
                        new GlyphBox("−", style.Smaller()),
                        new SupSubBox(
                            it("x", style.Smaller()),
                            new GlyphBox("2", style.Smaller().Smaller()),
                            null,
                            style.Smaller())),
                    null,
                    style),
                new KernBox(style.FontSize * 0.2f),
                it("dx", style),
                new KernBox(style.FontSize * 0.3f),
                new GlyphBox("=", style),
                new KernBox(style.FontSize * 0.3f),
                new FracBox(
                    new SqrtBox(new GlyphBox("π", style), style),
                    new GlyphBox("2", style),
                    style)),
            // Fine-structure constant: α = (1/(4πε₀)) · (e²/(ℏc)) ≈ 1/137.
            // Pulls together a lot of the layout vocabulary in one scene —
            // Greek letters (α, π, ε), the Planck-constant ℏ (U+210F), two
            // stacked fractions side-by-side, a subscripted ε₀, an
            // exponentiated e², and an ≈ relation. Stresses font coverage
            // (DejaVu draws plain glyphs; STIX draws designed math forms).
            "fine-structure-constant" => new HBox(
                new GlyphBox("α", style),
                new KernBox(style.FontSize * 0.3f),
                new GlyphBox("=", style),
                new KernBox(style.FontSize * 0.3f),
                new FracBox(
                    new GlyphBox("1", style),
                    new HBox(
                        new GlyphBox("4", style),
                        new GlyphBox("π", style),
                        new SupSubBox(
                            new GlyphBox("ε", style),
                            null,
                            new GlyphBox("0", style.Smaller()),
                            style)),
                    style),
                new KernBox(style.FontSize * 0.15f),
                new FracBox(
                    new SupSubBox(
                        it("e", style),
                        new GlyphBox("2", style.Smaller()),
                        null,
                        style),
                    new HBox(
                        new GlyphBox("ℏ", style),
                        it("c", style)),
                    style),
                new KernBox(style.FontSize * 0.3f),
                new GlyphBox("≈", style),
                new KernBox(style.FontSize * 0.3f),
                new FracBox(
                    new GlyphBox("1", style),
                    new GlyphBox("137", style),
                    style)),
            "standard-model-lagrangian" => BuildStandardModelLagrangian(style),
            _ => throw new ArgumentException($"unknown scene '{name}'"),
        };
        return (box, style);
    }

    private static Box BuildMatrix2x2(BoxStyle style)
    {
        // Matrix entries are math letters → italic where the font has it.
        Box it(string text) => new MathGlyphBox(text, style, MathStyle.Italic);
        var cells = new Box[2, 2];
        cells[0, 0] = it("a");
        cells[0, 1] = it("b");
        cells[1, 0] = it("c");
        cells[1, 1] = it("d");
        return new BracketBox(new MatrixBox(cells, style), BracketKind.Paren, style);
    }

    /// <summary>
    /// CERN-coffee-mug compact form of the Standard Model Lagrangian:
    /// ℒ = -¼ F<sub>μν</sub> F<sup>μν</sup> + iψ̄ D̸ ψ + ψ̄<sub>i</sub> y<sub>ij</sub> ψ<sub>j</sub> φ + h.c. + |D<sub>μ</sub>φ|² − V(φ).
    /// Stresses every script-bearing primitive simultaneously: same-base
    /// SupSubBox with sub-then-super (F<sub>μν</sub> next to F<sup>μν</sup>),
    /// long sub strings ("ij"), and <see cref="AccentBox"/> bars over ψ
    /// (anchored via OpenType MATH <c>MathTopAccentAttachment</c> on
    /// math fonts; centred on advance/2 otherwise). The Dirac slash on D̸
    /// is still drawn via the combining solidus rune for now — that's an
    /// overlay, not a top accent, and needs a separate primitive. The
    /// "|…|²" group uses plain '|' GlyphBoxes flanking an HBox rather
    /// than a stretchy norm bracket — BracketKind has no Bar variant
    /// yet, and the inner content here is single-line so non-stretchy
    /// pipes look correct.
    /// </summary>
    private static Box BuildStandardModelLagrangian(BoxStyle style)
    {
        var k = new KernBox(style.FontSize * 0.2f);
        var thin = new KernBox(style.FontSize * 0.12f);

        // Latin-letter atoms render as math italic via MathGlyphBox: F,
        // D, V, y, i and the index letters i/j get remapped to the
        // U+1D434 italic block when the font's cmap covers it (STIX,
        // Cambria Math, Latin Modern Math). Body fonts (DejaVu) miss
        // the cmap lookup and fall back to upright — same visual result
        // as before MathGlyphBox existed for those fonts. Greek letters
        // (ψ, μ, ν, φ) keep their plain codepoints since math fonts
        // already render them italic by font-design convention. Numbers,
        // operators, and the "h.c." text stay upright.
        Box it(string text, BoxStyle s) => new MathGlyphBox(text, s, MathStyle.Italic);

        Box psiBar(BoxStyle s) => new AccentBox(
            new GlyphBox("ψ", s),
            new GlyphBox("¯", s),
            s);

        // F_μν F^μν — same letter, one with sub then one with super,
        // back-to-back. Reads as the contraction in index notation.
        Box fmunuDownUp() => new HBox(
            new SupSubBox(it("F", style), null, new GlyphBox("μν", style.Smaller()), style),
            thin,
            new SupSubBox(it("F", style), new GlyphBox("μν", style.Smaller()), null, style));

        // Yukawa term: ψ̄_i y_ij ψ_j φ. Indices i / j / ij are italic
        // letters too — they're math letters, not opaque labels.
        Box yukawa() => new HBox(
            new SupSubBox(psiBar(style), null, it("i", style.Smaller()), style),
            thin,
            new SupSubBox(it("y", style), null, it("ij", style.Smaller()), style),
            thin,
            new SupSubBox(new GlyphBox("ψ", style), null, it("j", style.Smaller()), style),
            thin,
            new GlyphBox("φ", style));

        // |D_μ φ|² — plain pipes around the content, then square the
        // whole HBox via SupSubBox.
        Box higgsKinetic() => new SupSubBox(
            new HBox(
                new GlyphBox("|", style),
                new SupSubBox(it("D", style), null, new GlyphBox("μ", style.Smaller()), style),
                thin,
                new GlyphBox("φ", style),
                new GlyphBox("|", style)),
            new GlyphBox("2", style.Smaller()),
            null,
            style);

        return new HBox(
            new GlyphBox("ℒ", style),
            k,
            new GlyphBox("=", style),
            k,
            new GlyphBox("−", style),
            thin,
            new FracBox(new GlyphBox("1", style), new GlyphBox("4", style), style),
            thin,
            fmunuDownUp(),
            k,
            new GlyphBox("+", style),
            k,
            it("i", style),
            thin,
            // ψ̄ D̸ ψ — bar via AccentBox, slash via OverlayBox (a
            // forward-slash glyph drawn through italic D's centre).
            psiBar(style),
            thin,
            new OverlayBox(it("D", style), new GlyphBox("/", style)),
            thin,
            new GlyphBox("ψ", style),
            k,
            new GlyphBox("+", style),
            k,
            yukawa(),
            k,
            new GlyphBox("+", style),
            k,
            // "h.c." stays upright — it's a text label (Hermitian
            // conjugate), not a math expression.
            new GlyphBox("h.c.", style),
            k,
            new GlyphBox("+", style),
            k,
            higgsKinetic(),
            k,
            new GlyphBox("−", style),
            k,
            it("V", style),
            new BracketBox(new GlyphBox("φ", style), BracketKind.Paren, style));
    }

    private static void DumpFailed(string font, string scene, byte[] rgba, int w, int h)
    {
        var dir = Path.Combine(FailedDir, font);
        Directory.CreateDirectory(dir);
        var png = PngWriter.Encode(rgba, w, h);
        File.WriteAllBytes(Path.Combine(dir, scene + ".actual.png"), png);
    }

    /// <summary>
    /// Composite an RGBA box render onto a "grid paper" backdrop: light
    /// background, faint grid lines every <paramref name="gridSpacing"/> px,
    /// brighter centre crosshairs. The box's foreground (black in BuildScene)
    /// alpha-blends on top so the math content stays crisp while the grid
    /// gives a readable, neutral background in any image viewer. Returns a
    /// fresh RGBA byte[] sized w*h*4.
    /// </summary>
    private static byte[] ComposeOnGridPaper(byte[] fg, int w, int h, int gridSpacing)
    {
        var bg = new RGBAColor32(245, 245, 245, 255);
        var grid = new RGBAColor32(215, 215, 220, 255);
        var centre = new RGBAColor32(180, 190, 210, 255);

        var img = new RgbaImage(w, h);
        img.Clear(bg);
        for (var x = 0; x < w; x += gridSpacing) img.DrawVLine(x, 0, h, grid);
        for (var y = 0; y < h; y += gridSpacing) img.DrawHLine(0, w, y, grid);
        var cx = w / 2;
        var cy = h / 2;
        img.DrawHLine(0, w, cy, centre);
        img.DrawVLine(cx, 0, h, centre);

        // Alpha-blend the box pixels (RGBA byte[]) over the grid backdrop.
        // RgbaImage.Pixels is already in RGBA byte order, same layout as fg.
        var dst = img.Pixels;
        for (var i = 0; i < fg.Length; i += 4)
        {
            byte sa = fg[i + 3];
            if (sa == 0) continue;
            byte sr = fg[i], sg = fg[i + 1], sb = fg[i + 2];
            if (sa == 255)
            {
                dst[i] = sr; dst[i + 1] = sg; dst[i + 2] = sb; dst[i + 3] = 255;
                continue;
            }
            // Standard "source over" with premultiplied math.
            int inv = 255 - sa;
            dst[i]     = (byte)((sr * sa + dst[i]     * inv) / 255);
            dst[i + 1] = (byte)((sg * sa + dst[i + 1] * inv) / 255);
            dst[i + 2] = (byte)((sb * sa + dst[i + 2] * inv) / 255);
            dst[i + 3] = 255;
        }
        return dst;
    }
}
