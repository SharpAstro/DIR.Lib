using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="TabBar.Font"/>, <see cref="TabBar.Pad"/> and <see cref="TabBar.Border"/> — the metrics a
/// host needs when it has to draw a tab somewhere the bar does not.
/// </summary>
/// <remarks>
/// The case that made them public is a torn-out tab carried as its own small borderless window: it has
/// to paint itself as a tab, and the bar is not the thing painting it. Copied instead, they drift — a
/// consumer had two literals and a comment naming the constants they came from, so changing the bar's
/// type size silently stopped matching the window pretending to be one of its tabs.
///
/// <para>Pinned as SCALED, which is the whole design point over exposing the base constants: a copier
/// working from those has to apply a scale of its own, and nothing makes that the same number as
/// <see cref="TabBar.Scale"/>.</para>
/// </remarks>
public class TabBarMetricsTests
{
    private static TabBar<RgbaImage> Bar(float scale) =>
        new(new RgbaImageRenderer(1, 1)) { FontPath = "font.ttf", DpiScale = scale };

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void TheDrawingMetricsCarryTheBarsOwnScale(float scale)
    {
        var bar = Bar(scale);

        // The same multiples Height already exposes, so a host that reserves Height and then draws a tab
        // with these cannot end up with a tab that disagrees with the strip it came out of.
        bar.Height.ShouldBe(30f * scale);
        bar.Font.ShouldBe(13f * scale);
        bar.Pad.ShouldBe(10f * scale);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(2.6f)]
    public void ABorderIsNeverThinnerThanOnePixel(float scale)
    {
        // A hairline that rounds to zero is a hairline that disappears, which on a fractional scale is
        // exactly what a plain multiply gives.
        Bar(scale).Border.ShouldBeGreaterThanOrEqualTo(1);
    }
}
