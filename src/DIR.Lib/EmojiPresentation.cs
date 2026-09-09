using System.Text;

namespace DIR.Lib;

/// <summary>
/// Unicode's <b>default presentation</b> for a codepoint: whether it is drawn as a pictograph or as
/// text when nothing says otherwise. This is the <c>Emoji_Presentation</c> property of UTS #51, and
/// the data half of it is generated from the Unicode Character Database
/// (see <c>tools/gen-emoji-presentation</c>).
/// </summary>
/// <remarks>
/// <para><b>The property answers a question coverage cannot.</b> A text face and an emoji face can both
/// carry a codepoint, so "which font covers it" does not decide which one is right. U+2615 HOT BEVERAGE
/// is the canonical case: DejaVu Sans has a real outline for it, so a coverage-first chain draws a small
/// monochrome cup and never consults the colour face, while a browser draws the colour emoji. Unicode
/// already recorded which of those the codepoint is FOR, and this is that record.</para>
/// <para><b>It is deliberately narrow.</b> Most of the symbols a UI draws are
/// <c>Emoji_Presentation=No</c> and keep whatever face already draws them: the arrows, box drawing,
/// stars (U+2605), check marks (U+2713, U+2714), the warning sign (U+26A0) and the snowflake (U+2744)
/// are all text-default and are unaffected. What flips is the set that was always meant to be a
/// pictograph -- U+2615, U+2705, U+274C, U+26C5, U+2B50 and essentially everything above U+1F000.</para>
/// <para><b>Explicit presentation selectors are NOT handled here.</b> A trailing U+FE0F / U+FE0E
/// overrides the default per UTS #51, but that is a property of a SEQUENCE and this asks about one
/// codepoint. A caller that wants it has to look at the following rune itself.</para>
/// </remarks>
public static partial class EmojiPresentation
{
    /// <summary>
    /// Whether <paramref name="rune"/>'s default presentation is emoji, i.e. it should be drawn from a
    /// colour emoji face when one is available.
    /// </summary>
    public static bool IsDefaultFor(Rune rune) => IsDefaultFor(rune.Value);

    /// <summary>
    /// Whether <paramref name="codepoint"/>'s default presentation is emoji. Out-of-range and negative
    /// values simply answer false, so a caller need not validate first.
    /// </summary>
    public static bool IsDefaultFor(int codepoint)
    {
        var ranges = DefaultPresentationRanges;

        // Bounds first, from the table's own ends rather than a literal: every ASCII codepoint, which is
        // very nearly all UI text, is below the first range and leaves without touching the search.
        if (codepoint < ranges[0] || codepoint > ranges[^1]) return false;

        var value = (uint)codepoint;
        int lo = 0, hi = ranges.Length / 2 - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (value < ranges[mid * 2]) hi = mid - 1;
            else if (value > ranges[mid * 2 + 1]) lo = mid + 1;
            else return true;
        }
        return false;
    }
}
