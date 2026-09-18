using System;
using System.IO;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// A colour glyph keeps its own RGB but fades with the text it is drawn in, the rule SdlVulkan.Renderer's
/// <c>tex.frag</c> applies on the GPU (<c>pc.color.a * texel.a</c>). The CPU renderer used to blit colour
/// glyphs as they were, so a label drawn at half alpha kept its emoji at full strength here alone.
/// </summary>
public class ColourGlyphOpacityTests
{
    private static readonly RGBAColor32 Black = new(0, 0, 0, 255);

    private static string EmojiFont => Path.Combine(AppContext.BaseDirectory, "Fonts", "Noto-COLRv1.ttf");

    // U+1F5BC FRAME WITH PICTURE: a colour glyph in the Noto COLRv1 face whose palette is plainly coloured,
    // which the keeps-its-colours assertion needs (a mostly grey emoji could sum to near-equal channels).
    private const string Picture = "\U0001F5BC";

    private static long[] ChannelSums(byte alpha)
    {
        using var renderer = new RgbaImageRenderer(64, 64);
        renderer.Surface.Clear(Black);
        renderer.DrawText(Picture, EmojiFont, 32f, new RGBAColor32(255, 255, 255, alpha),
            new RectInt(new PointInt(64, 64), new PointInt(0, 0)), TextAlign.Near, TextAlign.Near);

        var sums = new long[3];
        var pixels = renderer.Surface.Pixels;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            sums[0] += pixels[i];
            sums[1] += pixels[i + 1];
            sums[2] += pixels[i + 2];
        }
        return sums;
    }

    [Fact]
    public void AColourGlyphFadesWithTheInkAlphaAndKeepsItsColours()
    {
        File.Exists(EmojiFont).ShouldBeTrue("the Noto COLRv1 fixture is copied to the test output");

        var full = ChannelSums(255);
        var half = ChannelSums(128);

        // Drawn at all, and in colour rather than as a white-tinted silhouette.
        (full[0] + full[1] + full[2]).ShouldBeGreaterThan(0);
        (full[0] == full[1] && full[1] == full[2]).ShouldBeFalse("a colour glyph keeps its own RGB");

        // Every channel at half strength over black. It was byte-identical to the full-alpha draw before.
        for (var c = 0; c < 3; c++)
        {
            ((double)half[c] / full[c]).ShouldBe(128.0 / 255.0, 0.02, $"channel {c}");
        }
    }

    [Fact]
    public void AColourGlyphAtZeroAlphaDrawsNothing()
    {
        var none = ChannelSums(0);

        (none[0] + none[1] + none[2]).ShouldBe(0);
    }

    [Fact]
    public void FullOpacityIsTheOriginalBlit()
    {
        var src = new byte[] { 200, 100, 50, 255, 10, 20, 30, 77, 255, 0, 0, 0, 1, 2, 3, 128 };
        var a = new RgbaImage(2, 2);
        var b = new RgbaImage(2, 2);
        a.Clear(new RGBAColor32(9, 8, 7, 255));
        b.Clear(new RGBAColor32(9, 8, 7, 255));

        a.BlitRgba(0, 0, src, 2, 2);
        b.BlitRgba(0, 0, src, 2, 2, opacity: 255);

        b.Pixels.ShouldBe(a.Pixels);
    }
}
