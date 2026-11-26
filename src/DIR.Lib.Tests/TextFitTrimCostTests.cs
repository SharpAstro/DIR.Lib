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

    [Fact]
    public void TheMiddleKeepsAsManyFromEachEnd()
    {
        var renderer = new CountingRenderer();
        var text = new string('a', 300) + new string('b', 300);

        // 210 units hold 21 characters: 10 from each end and the ellipsis.
        TextFit.TrimToWidth(renderer, text, "stub.ttf", null, 12f, 210f, TextTrim.Middle)
            .ShouldBe(new string('a', 10) + "…" + new string('b', 10));
    }

    /// <summary>
    /// A run cut the same way frame after frame (a tab title) is answered from the memo: no search, no
    /// measurement and no allocation, where every frame used to build a string per candidate and one for the answer
    /// (1.6 MB in 26 s of the PDF viewer's pan, 2026-11-26).
    /// </summary>
    [Theory]
    [InlineData(TextTrim.End)]
    [InlineData(TextTrim.Start)]
    [InlineData(TextTrim.Middle)]
    public void TheSameCutAgainIsRememberedAndAllocatesNothing(TextTrim trim)
    {
        var renderer = new CountingRenderer();
        var text = new string('x', 120) + trim;

        var first = TextFit.TrimToWidth(renderer, text, "stub.ttf", null, 12f, 200f, trim);
        renderer.Measures = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var again = TextFit.TrimToWidth(renderer, text, "stub.ttf", null, 12f, 200f, trim);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        again.ShouldBeSameAs(first);
        renderer.Measures.ShouldBe(0);
        allocated.ShouldBe(0);
    }

    /// <summary>The search itself builds no candidate strings: what a first cut allocates is the answer alone.</summary>
    [Fact]
    public void AFirstCutAllocatesOnlyItsAnswer()
    {
        var renderer = new CountingRenderer();
        var warm = new string('w', 2000);
        TextFit.TrimToWidth(renderer, warm, "stub.ttf", null, 12f, 200f, TextTrim.End); // the per-thread buffer, once
        var text = new string('y', 600);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var cut = TextFit.TrimToWidth(renderer, text, "stub.ttf", null, 12f, 200f, TextTrim.End);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        cut.Length.ShouldBe(20);
        // The 20-character answer is about 64 bytes; ten candidate strings of a few hundred characters each were KB.
        allocated.ShouldBeLessThan(200);
    }

    /// <summary>A string with the same content is a different key (the memo compares references), and gets the same
    /// answer.</summary>
    [Fact]
    public void AnEqualStringIsCutAlike()
    {
        var renderer = new CountingRenderer();
        var a = new string('z', 90);
        var b = new string('z', 90);

        TextFit.TrimToWidth(renderer, b, "stub.ttf", null, 12f, 200f, TextTrim.End)
            .ShouldBe(TextFit.TrimToWidth(renderer, a, "stub.ttf", null, 12f, 200f, TextTrim.End));
    }
}
