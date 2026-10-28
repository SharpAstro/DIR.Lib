using System;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// What cutting a run that badly overflows costs: a search, not one measurement per character cut. A
/// 600-character card detail in a narrow panel was cut one character at a time every frame and held
/// pilot-gui's render thread busy (DE1, 2026-10-28).
/// </summary>
public class TextFitTrimCostTests
{
    /// <summary>Ten units per character, counting every measurement.</summary>
    private sealed class CountingRenderer() : RgbaImageRenderer(10, 10)
    {
        public int Measures { get; set; }

        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
        {
            Measures++;
            return (text.Length * 10f, fontSize);
        }
    }

    [Theory]
    [InlineData(TextTrim.End)]
    [InlineData(TextTrim.Start)]
    public void ARunSixHundredCharactersLongCutToTwentyIsFoundInAFewMeasurementsAndIsTheLongestThatFits(TextTrim trim)
    {
        var renderer = new CountingRenderer();
        var text = new string('a', 300) + new string('b', 300);

        var cut = TextFit.TrimToWidth(renderer, text, "stub.ttf", null, 12f, 200f, trim);

        // 200 units hold 20 characters: 19 kept and the ellipsis.
        cut.Length.ShouldBe(20);
        cut.ShouldBe(trim == TextTrim.End ? new string('a', 19) + "…" : "…" + new string('b', 19));
        // The full run once, then about log2(600) cuts; the walk took 580.
        renderer.Measures.ShouldBeLessThan(15);
    }

    [Fact]
    public void NothingFitsButTheEllipsis()
    {
        var renderer = new CountingRenderer();

        TextFit.TrimToWidth(renderer, "abcdef", "stub.ttf", null, 12f, 15f, TextTrim.End).ShouldBe("…");
    }
}
