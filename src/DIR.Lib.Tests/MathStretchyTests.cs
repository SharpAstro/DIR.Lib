using System.Text;
using SharpAstro.Fonts;
using SharpAstro.Fonts.Tables.OpenTypeMath;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Proof-of-concept tests showing DIR.Lib can rasterize stretchy math glyphs
/// (parens, brackets, radicals) by walking the OpenType MATH table's
/// <see cref="MathTable.GetVerticalConstruction"/> recipes — pre-drawn variants
/// when present, assembly piece-stacks otherwise. Demonstrates that the MATH
/// data parsed by <c>SharpAstro.Fonts</c> threads through
/// <see cref="ManagedFontRasterizer"/> correctly, which is the foundation for
/// replacing parametric <c>BracketBox</c> / <c>SqrtBox</c> drawing in MathLayout
/// with font-driven scalable delimiters.
///
/// <para>The bundled DejaVu Sans fixture ships a MATH table whose '(' has an
/// assembly recipe but no pre-drawn variants — so the assembly path must work
/// for any test against that font to produce growable parens.</para>
/// </summary>
public sealed class MathStretchyTests
{
    private static readonly string DejaVuPath =
        Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");

    private const string DejaVuId = "mem:dejavu-stretchy";

    /// <summary>Where composed bitmaps land for visual inspection.</summary>
    private static string OutputDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "math-stretchy-output");

    [Fact]
    public void DejaVuSans_LeftParen_HasMathConstruction()
    {
        var font = OpenTypeFont.LoadFromFile(DejaVuPath);
        font.Math.ShouldNotBeNull("DejaVu Sans should ship a MATH table");

        var gid = (ushort)font.GetGlyphId('(');
        gid.ShouldBeGreaterThan((ushort)0, "'(' should map to a glyph id");

        var c = font.Math!.GetVerticalConstruction(gid);
        c.ShouldNotBeNull("'(' should have a vertical-stretch construction in DejaVu's MATH");
        (c!.Variants.Count > 0 || c.Assembly is not null)
            .ShouldBeTrue("construction should offer at least variants or an assembly");
    }

    /// <summary>
    /// Walk the four standard delimiters and dump what each one carries in
    /// DejaVu's MATH table — variants vs. assembly, with sizes. Useful as a
    /// reference for which characters can be tested with the variants path
    /// vs. the assembly-stacking path.
    /// </summary>
    [Fact]
    public void DejaVuSans_Delimiter_ConstructionSummary()
    {
        var font = OpenTypeFont.LoadFromFile(DejaVuPath);
        font.Math.ShouldNotBeNull();

        var sb = new StringBuilder();
        foreach (var ch in new[] { '(', '[', '{', '|', '√', '⌊', '⌋' })
        {
            var gid = (ushort)font.GetGlyphId(ch);
            if (gid == 0)
            {
                sb.AppendLine($"  '{ch}': no glyph in font");
                continue;
            }
            var c = font.Math!.GetVerticalConstruction(gid);
            if (c is null)
            {
                sb.AppendLine($"  '{ch}' (gid {gid}): no vertical construction");
                continue;
            }
            sb.Append($"  '{ch}' (gid {gid}): variants=[");
            sb.Append(string.Join(", ", c.Variants.Select(v => $"{v.GlyphId}@{v.AdvanceMeasurement}fu")));
            sb.Append("], assembly=");
            if (c.Assembly is { } asm)
            {
                sb.Append("[");
                sb.Append(string.Join(", ", asm.Parts.Select(p =>
                    $"{(p.IsExtender ? "ext" : "fix")} gid={p.GlyphId} adv={p.FullAdvance} sc={p.StartConnectorLength} ec={p.EndConnectorLength}")));
                sb.AppendLine("]");
            }
            else
            {
                sb.AppendLine("none");
            }
        }
        // Always print so the test log carries the data; never fails
        // structurally — this is a discovery probe, not a contract test.
        Console.WriteLine($"DejaVu vertical-stretch coverage:\n{sb}");
        Directory.CreateDirectory(OutputDir);
        File.WriteAllText(Path.Combine(OutputDir, "construction-summary.txt"), sb.ToString());
    }

    [Theory]
    [InlineData(1.0f)]   // 1em — should hit the base-glyph path (no stretching needed)
    [InlineData(2.0f)]   // 2em — still smaller than DejaVu's natural assembly minimum (~3.5em)
    [InlineData(4.0f)]   // 4em — assembly with no extender repeats (one-of-each suffices)
    [InlineData(6.0f)]   // 6em — assembly with one or two extender repeats
    [InlineData(10.0f)]  // 10em — many extender repeats — really stretching now
    public void RasterizeStretchyVertical_ParenGrowsWithRequiredHeight(float heightMultiplier)
    {
        const float fontSize = 24f;

        using var rasterizer = new ManagedFontRasterizer();
        rasterizer.RegisterFontFromMemory(DejaVuId, File.ReadAllBytes(DejaVuPath))
            .ShouldBeTrue();

        var requiredHeightPx = fontSize * heightMultiplier;
        var bitmap = rasterizer.RasterizeStretchyVertical(DejaVuId, fontSize,
            new System.Text.Rune('('), requiredHeightPx);

        bitmap.Width.ShouldBeGreaterThan(0, "stretched paren should have non-zero width");
        bitmap.Height.ShouldBeGreaterThan(0, "stretched paren should have non-zero height");

        // Save for visual inspection. Baselines pinned in MathLayoutBaselineTests
        // once StretchyVerticalBox lands; this output is for ad-hoc inspection.
        Directory.CreateDirectory(OutputDir);
        var pngPath = Path.Combine(OutputDir, $"paren-{heightMultiplier:F1}x.png");
        File.WriteAllBytes(pngPath, PngWriter.Encode(bitmap.Rgba, bitmap.Width, bitmap.Height));
    }

    /// <summary>
    /// Diagnostic probe: dump bitmap dimensions and bearings for each
    /// assembly part of DejaVu's '(' so we can see how the parts should
    /// horizontally align. Used while diagnosing the matrix-bracket "extra
    /// vertical strokes" rendering bug.
    /// </summary>
    [Fact]
    public void DejaVuSans_LeftParen_AssemblyPartGeometry()
    {
        var rasterizer = new ManagedFontRasterizer();
        rasterizer.RegisterFontFromMemory(DejaVuId, File.ReadAllBytes(DejaVuPath));
        ushort[] gids = [3509, 3508, 3507]; // top fix, extender, bottom fix
        foreach (var gid in gids)
        {
            var bm = rasterizer.RasterizeGlyphByGid(DejaVuId, 96f, gid);
            Console.WriteLine($"  gid={gid}  bitmap={bm.Width}x{bm.Height}  BearingX={bm.BearingX}  BearingY={bm.BearingY}  AdvanceX={bm.AdvanceX}");
        }
    }

    /// <summary>
    /// 1x → 2x → 3.5x must produce non-decreasing bitmap heights — the whole
    /// point of OT MATH stretching is that asking for a taller delimiter
    /// gives you a taller delimiter. This test exercises the algorithm's
    /// monotonicity in one shot, separate from the per-size dump above.
    /// </summary>
    [Fact]
    public void StretchyParen_HeightIsMonotonicInRequestedHeight()
    {
        const float fontSize = 24f;
        using var rasterizer = new ManagedFontRasterizer();
        rasterizer.RegisterFontFromMemory(DejaVuId, File.ReadAllBytes(DejaVuPath));

        var lparen = new System.Text.Rune('(');
        var heights = new[] { fontSize, fontSize * 2f, fontSize * 4f, fontSize * 6f, fontSize * 10f }
            .Select(req => rasterizer.RasterizeStretchyVertical(DejaVuId, fontSize, lparen, req).Height)
            .ToArray();

        for (var i = 1; i < heights.Length; i++)
        {
            heights[i].ShouldBeGreaterThanOrEqualTo(heights[i - 1],
                $"requested taller paren but got shorter bitmap (heights: [{string.Join(",", heights)}])");
        }
    }
}
