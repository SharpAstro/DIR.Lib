namespace DIR.Lib;

/// <summary>
/// Where a line of text sits inside its line box. ONE copy, because every backend needs the same answer
/// and one of them needs to invert it.
/// </summary>
/// <remarks>
/// <para>This arithmetic used to be written out four times: once each in <see cref="RgbaImageRenderer"/>,
/// VkRenderer and WebGlRenderer -- the last of which carried the comment "identical formula across
/// RgbaImageRenderer / VkRenderer / here", which is true right up until it is not -- and a fourth time,
/// inverted, in <c>MathLayout.GlyphBox</c>, which solves for the rect that puts a glyph's baseline where
/// math layout wants it. That fourth copy was reconstructed from a comment describing the other three, and
/// it broke the moment the original changed.</para>
/// <para>The metrics passed in should be the FACE's (<see cref="ManagedFontRasterizer.GetVerticalMetrics"/>),
/// not the run's own ink. Measuring the ink makes the baseline depend on which letters are present: "b"
/// sinks because its ascender inflates the box and "g" rises because its descender does, so labels drawn
/// independently at one size cannot share a baseline. It is visible wherever a row of them sits together --
/// a board's file letters, a toolbar of buttons where one caption happens to contain a descender.</para>
/// </remarks>
public static class TextBaseline
{
    /// <summary>Per-line padding, as a multiple of the font size.</summary>
    public const float LineHeightFactor = 1.3f;

    /// <summary>The line box for <paramref name="fontSize"/>.</summary>
    public static float LineHeight(float fontSize) => fontSize * LineHeightFactor;

    /// <summary>
    /// How far below the line box's top the baseline sits. <paramref name="ascent"/> and
    /// <paramref name="descent"/> are both POSITIVE distances from the baseline.
    /// </summary>
    public static float WithinLine(float lineHeight, float ascent, float descent)
        => (lineHeight + ascent - descent) / 2f;
}
