using System.Text;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Smoke tests for the MTSDF path added to <see cref="ManagedFontRasterizer"/>.
/// The full distance-field correctness is covered in SharpAstro.Fonts; here we
/// just confirm the DIR.Lib plumbing produces a well-formed 4-channel bitmap
/// whose true-distance (alpha) channel agrees with the single-channel SDF.
/// </summary>
public class MtsdfRasterizeTests
{
    private static readonly string FontPath = Path.Combine("Fonts", "DejaVuSans.ttf");

    [Fact]
    public void RasterizeGlyphMtsdf_ProducesFourChannelBitmap()
    {
        using var rasterizer = new ManagedFontRasterizer();
        var m = rasterizer.RasterizeGlyphMtsdf(FontPath, 48f, new Rune('B'));

        Assert.True(m.Width > 0 && m.Height > 0);
        Assert.Equal(m.Width * m.Height * 4, m.Rgba.Length);
        Assert.Equal(4f, m.Spread);
    }

    [Fact]
    public void MtsdfAlpha_TracksSingleChannelSdf()
    {
        using var rasterizer = new ManagedFontRasterizer();
        var sdf = rasterizer.RasterizeGlyphSdf(FontPath, 48f, new Rune('B'));
        var mtsdf = rasterizer.RasterizeGlyphMtsdf(FontPath, 48f, new Rune('B'));

        Assert.True(sdf.Width > 0);
        Assert.True(mtsdf.Width > 0);

        var sdfInside = sdf.Alpha.Count(a => a > 128) / (float)sdf.Alpha.Length;
        var insideCount = 0;
        for (var i = 0; i < mtsdf.Width * mtsdf.Height; i++)
            if (mtsdf.Rgba[i * 4 + 3] > 128) insideCount++;
        var mtsdfInside = insideCount / (float)(mtsdf.Width * mtsdf.Height);

        Assert.InRange(mtsdfInside, sdfInside - 0.04f, sdfInside + 0.04f);
    }

    [Fact]
    public void RasterizeGlyphMtsdf_EmptyGlyph_ReturnsDefault()
    {
        using var rasterizer = new ManagedFontRasterizer();
        var m = rasterizer.RasterizeGlyphMtsdf(FontPath, 48f, new Rune(' '));
        Assert.Equal(0, m.Width);
        Assert.Null(m.Rgba);
    }
}
