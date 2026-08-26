using System;
using System.IO;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Labels drawn independently at the same font and size must sit on the SAME baseline.
///
/// <para>This is the invariant behind <see cref="ManagedFontRasterizer.GetVerticalMetrics"/>. The
/// renderer used to seat the baseline by centring the run's measured ink, which makes the baseline a
/// function of the text: a run of "a" (x-height only) landed at one height, "b" lower because its
/// ascender inflated the box, and "g" higher because its descender did. Nothing looks wrong in a single
/// label -- it is only when several are drawn side by side, each centred in its own rect, that the row
/// stair-steps. Chess's board coordinates a-h are exactly that shape, and they stair-stepped at b, d
/// and g.</para>
///
/// <para>These read pixels rather than compare a PNG on purpose. A snapshot would catch the change but
/// state it as "these bytes differ"; the property that has to hold is that two letters SHARE a baseline,
/// and a test should say so.</para>
/// </summary>
public class TextBaselineTests
{
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");

    private static readonly RGBAColor32 Black = new(0, 0, 0, 255);
    private static readonly RGBAColor32 White = new(255, 255, 255, 255);

    /// <summary>
    /// The last row holding ink, which for a letter that sits ON the baseline (no descender) IS the
    /// baseline, to within antialiasing.
    /// </summary>
    private static int BottomInkRow(string text, float fontSize = 48f)
    {
        var renderer = new RgbaImageRenderer(160, 160);
        renderer.Surface.Clear(Black);

        // One rect, one call, vertically centred -- the way a chess file label is drawn.
        renderer.DrawText(text, FontPath, fontSize, White,
            new RectInt(new PointInt(160, 160), new PointInt(0, 0)), TextAlign.Center, TextAlign.Center);

        for (var y = renderer.Surface.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < renderer.Surface.Width; x++)
            {
                // Any ink at all: the glyph is white on black, so a lit pixel is the letter.
                if (renderer.Surface.Pixels[(y * renderer.Surface.Width + x) * 4] > 32) return y;
            }
        }

        throw new InvalidOperationException($"'{text}' drew nothing, so there is no baseline to find");
    }

    /// <summary>
    /// "a" has no ascender and no descender, "b" has an ascender. Both sit flat on the baseline, so
    /// their lowest ink is the same row -- unless the baseline moved because the ascender did.
    /// </summary>
    [Fact]
    public void AnAscenderDoesNotMoveTheBaseline()
    {
        Math.Abs(BottomInkRow("b") - BottomInkRow("a")).ShouldBeLessThanOrEqualTo(1,
            "'b' rides the same baseline as 'a'; its ascender must not push the run down");
    }

    /// <summary>
    /// The other half, and the one that is visible in chess: "g" descends BELOW the baseline, so its
    /// lowest ink must be lower than "a"'s -- never equal, and never higher, which is what centring the
    /// ink produced (it lifted the whole glyph to fit the descender into the box).
    /// </summary>
    [Fact]
    public void ADescenderHangsBelowTheBaselineRatherThanLiftingTheGlyph()
        => BottomInkRow("g").ShouldBeGreaterThan(BottomInkRow("a"),
            "'g' must hang below the shared baseline, not be lifted onto it");

    /// <summary>
    /// The whole row chess draws, letter by letter, each centred in its own square: every one of them
    /// that has no descender must land on one baseline.
    /// </summary>
    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("c")]
    [InlineData("d")]
    [InlineData("e")]
    [InlineData("f")]
    [InlineData("h")]
    public void EveryDescenderlessFileLabelSharesOneBaseline(string label)
        => Math.Abs(BottomInkRow(label) - BottomInkRow("a")).ShouldBeLessThanOrEqualTo(1,
            $"file label '{label}' must sit on the same baseline as the rest of the row");

    /// <summary>
    /// The metrics themselves: a positive rise, a positive DEPTH (not hhea's negative offset), and both
    /// scaling with the size, since every caller converts through the requested pixel size.
    /// </summary>
    [Fact]
    public void FaceMetricsAreAPositiveRiseAndDepthThatScaleWithSize()
    {
        using var rasterizer = new ManagedFontRasterizer();

        var at24 = rasterizer.GetVerticalMetrics(FontPath, 24f);
        var at48 = rasterizer.GetVerticalMetrics(FontPath, 48f);

        at24.ShouldNotBeNull("DejaVuSans declares an hhea table");
        at48.ShouldNotBeNull();

        at24!.Value.Ascent.ShouldBeGreaterThan(0f);
        at24.Value.Descent.ShouldBeGreaterThan(0f, "descent is reported as a depth, not as hhea's negative");
        at48!.Value.Ascent.ShouldBe(at24.Value.Ascent * 2f, tolerance: 0.01f);
        at48.Value.Descent.ShouldBe(at24.Value.Descent * 2f, tolerance: 0.01f);
    }
}
