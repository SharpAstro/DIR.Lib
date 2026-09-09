using System.Text;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The generated <c>Emoji_Presentation</c> table and the lookup over it. The data is produced by
/// <c>tools/gen-emoji-presentation</c> from the Unicode Character Database, so what is worth pinning
/// here is the SHAPE the search depends on, plus the handful of codepoints whose answer decides how a
/// UI's chrome is drawn.
/// </summary>
public sealed class EmojiPresentationTests
{
    [Fact]
    public void TheTableIsAscendingAndDisjoint()
    {
        var ranges = EmojiPresentation.DefaultPresentationRanges;

        ranges.Length.ShouldBeGreaterThan(0);
        (ranges.Length % 2).ShouldBe(0);

        for (var i = 0; i < ranges.Length; i += 2)
        {
            ranges[i].ShouldBeLessThanOrEqualTo(ranges[i + 1]);

            // Strictly PAST the previous end plus one: the generator merges ranges that abut, so two
            // that touch mean the table came from somewhere else and the rest of its shape is a guess.
            if (i > 0) ranges[i].ShouldBeGreaterThan(ranges[i - 1] + 1);
        }
    }

    /// <summary>
    /// The binary search is correct only while the table is sorted, and one pair out of place answers
    /// wrongly for a few codepoints and correctly for every other -- which no spot check would catch.
    /// So compare against a linear scan over every codepoint the table could possibly claim.
    /// </summary>
    [Fact]
    public void TheSearchAgreesWithALinearScanEverywhere()
    {
        var ranges = EmojiPresentation.DefaultPresentationRanges;
        var mismatches = new List<string>();

        for (var cp = 0; cp <= (int)ranges[^1] + 1; cp++)
        {
            var linear = false;
            for (var i = 0; i < ranges.Length; i += 2)
            {
                if (cp >= ranges[i] && cp <= ranges[i + 1]) { linear = true; break; }
            }

            if (EmojiPresentation.IsDefaultFor(cp) != linear) mismatches.Add($"U+{cp:X4}");
        }

        mismatches.ShouldBeEmpty();
    }

    /// <summary>
    /// What the font resolver's ASCII fast path rests on: plain ASCII can skip the presentation question
    /// entirely. It holds because the lowest range starts at U+231A, which is a fact about the data
    /// rather than about the code, so it is checked here rather than assumed there.
    /// </summary>
    [Fact]
    public void NoAsciiCodepointIsEmojiByDefault()
    {
        for (var cp = 0; cp <= 0x7F; cp++)
        {
            EmojiPresentation.IsDefaultFor(cp).ShouldBeFalse($"U+{cp:X4}");
        }
    }

    /// <summary>A caller should not have to validate a codepoint before asking.</summary>
    [Fact]
    public void OutOfRangeValuesAnswerFalseRatherThanThrow()
    {
        EmojiPresentation.IsDefaultFor(-1).ShouldBeFalse();
        EmojiPresentation.IsDefaultFor(0x110000).ShouldBeFalse();
        EmojiPresentation.IsDefaultFor(int.MaxValue).ShouldBeFalse();
        EmojiPresentation.IsDefaultFor(int.MinValue).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0x2615)]   // HOT BEVERAGE, which a text face may well carry an outline for
    [InlineData(0x2705)]   // WHITE HEAVY CHECK MARK, the green tick, NOT the same character as U+2713
    [InlineData(0x274C)]   // CROSS MARK
    [InlineData(0x26C5)]   // SUN BEHIND CLOUD
    [InlineData(0x2B50)]   // WHITE MEDIUM STAR, NOT the same character as U+2605
    [InlineData(0x1F680)]  // ROCKET, and essentially everything above U+1F000
    public void APictographIsEmojiByDefault(int codepoint)
    {
        EmojiPresentation.IsDefaultFor(codepoint).ShouldBeTrue();
        EmojiPresentation.IsDefaultFor(new Rune(codepoint)).ShouldBeTrue();
    }

    /// <summary>
    /// The other half of the rule, and the half that keeps it safe to apply everywhere: the marks a UI
    /// already draws from its text face are text-default and do not move. Every one of these lives in
    /// the same symbol blocks as the pictographs above.
    /// </summary>
    [Theory]
    [InlineData(0x2713)]   // CHECK MARK
    [InlineData(0x2714)]   // HEAVY CHECK MARK
    [InlineData(0x2605)]   // BLACK STAR
    [InlineData(0x26A0)]   // WARNING SIGN
    [InlineData(0x2744)]   // SNOWFLAKE
    [InlineData(0x25B6)]   // BLACK RIGHT-POINTING TRIANGLE
    [InlineData(0x21C4)]   // RIGHTWARDS ARROW OVER LEFTWARDS ARROW
    [InlineData(0x2609)]   // SUN
    public void AMarkAUiAlreadyDrawsIsTextByDefault(int codepoint)
    {
        EmojiPresentation.IsDefaultFor(codepoint).ShouldBeFalse();
        EmojiPresentation.IsDefaultFor(new Rune(codepoint)).ShouldBeFalse();
    }

    /// <summary>The table says which release it is, so a stale one is readable rather than inferred.</summary>
    [Fact]
    public void TheTableNamesItsUnicodeVersion()
    {
        EmojiPresentation.UnicodeVersion.ShouldNotBeNullOrWhiteSpace();
        EmojiPresentation.UnicodeVersion.ShouldNotBe("unknown");
    }
}
