using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Pins the footprint counters of the text stack against a buddy-sized text set (one UI face, about a hundred
/// distinct glyphs, three sizes) and logs the numbers, so a cap is chosen from them. In memory, no waits; no
/// GC-heap totals are asserted.
/// </summary>
public sealed class TextFootprintTests(Xunit.ITestOutputHelper output)
{
    private static readonly string FontPath = Path.Combine("Fonts", "DejaVuSans.ttf");
    private static readonly float[] Sizes = [12f, 16f, 24f];

    private static readonly string[] Strings =
    [
        "Pilot Buddy", "Mount connected", "Guiding: RMS 0.62 arcsec",
        "Exposure 120 s, gain 100, offset 30, binning 2x2",
        "The quick brown fox jumps over the lazy dog. Sphinx of black quartz, judge my vow! "
            + "Pack my box with five dozen liquor jugs 0123456789.",
        "Start", "Stop", "Settings", "Cancel", "OK", "Focus: HFR 2.31", "Temp -10.2 °C",
    ];

    private static int DistinctPairCount() =>
        Sizes.Length * Strings.SelectMany(s => s.EnumerateRunes()).Distinct().Count();

    [Fact]
    public void RendererGlyphCache_MatchesDistinctSizeRunePairsDrawn()
    {
        using var renderer = new RgbaImageRenderer(400, 100);
        renderer.GlyphCache.ShouldBe(new GlyphCacheFootprint(0, 0));

        var white = new RGBAColor32(255, 255, 255, 255);
        var layout = new RectInt(new PointInt(400, 100), new PointInt(0, 0));
        foreach (var size in Sizes)
            foreach (var s in Strings)
            {
                renderer.MeasureText(s, FontPath, size);
                renderer.DrawText(s, FontPath, size, white, layout, TextAlign.Near, TextAlign.Near);
            }

        var fp = renderer.GlyphCache;
        output.WriteLine($"renderer glyph cache: {fp.Entries} entries, {fp.BitmapBytes} bitmap bytes");
        fp.Entries.ShouldBe(DistinctPairCount());
        fp.BitmapBytes.ShouldBeGreaterThan(0);

        // Drawing the same text again adds nothing.
        renderer.DrawText(Strings[0], FontPath, Sizes[0], white, layout, TextAlign.Near, TextAlign.Near);
        renderer.GlyphCache.ShouldBe(fp);
    }

    [Theory]
    [InlineData(SdfFontAtlas.DefaultInitialAtlasDim)]
    [InlineData(256)]
    [InlineData(512)]
    public void Atlas_StagingBytesArePagesTimesDimSquaredTimesFour(int pageDim)
    {
        using var rasterizer = new ManagedFontRasterizer();
        using var atlas = new SdfFontAtlas(rasterizer, maxTextureDimension: 8192, framesInFlight: 2,
            initialPageDim: pageDim, synchronousRasterize: true);
        atlas.Footprint.Pages.ShouldBe(1);

        var runes = Strings.SelectMany(s => s.EnumerateRunes()).Distinct().ToList();
        foreach (var rune in runes)
            atlas.GetGlyph(FontPath, SdfFontAtlas.SdfRasterSize, rune);

        var fp = atlas.Footprint;
        output.WriteLine($"atlas pageDim={pageDim}: {fp.Pages} page(s) of {fp.PageDimension}^2, "
            + $"{fp.StagingBytes} staging bytes, {fp.Glyphs} glyphs ({runes.Count} distinct runes)");
        fp.PageDimension.ShouldBe(atlas.PageDimension);
        fp.StagingBytes.ShouldBe((long)fp.Pages * fp.PageDimension * fp.PageDimension * 4);
        fp.Pages.ShouldBe(atlas.PageCount);
        fp.Glyphs.ShouldBe(runes.Count);

        var r = rasterizer.Footprint;
        output.WriteLine($"rasterizer: {r}");
        r.Fonts.ShouldBe(1);
        r.Type1Fonts.ShouldBe(0);
        r.GlyphRasters.ShouldBe(0, "a plain rasterizer keeps no raster cache");
    }

    [Fact]
    public void Rasterizer_CountsFontsLoaded()
    {
        using var rasterizer = new ManagedFontRasterizer();
        rasterizer.Footprint.ShouldBe(default);
        rasterizer.RasterizeGlyph(FontPath, 16f, new Rune('A'));
        rasterizer.Footprint.Fonts.ShouldBe(1);
    }

    [Fact]
    public void CappedRenderer_StaysWithinCapsAndDrawsIdenticalPixels()
    {
        const int maxEntries = 40;
        const long maxBytes = 64 * 1024;
        using var capped = new RgbaImageRenderer(400, 100) { GlyphCacheMaxEntries = maxEntries, GlyphCacheMaxBytes = maxBytes };
        using var plain = new RgbaImageRenderer(400, 100);
        capped.GlyphCacheMaxEntries.ShouldBe(maxEntries);
        plain.GlyphCacheMaxEntries.ShouldBe(RgbaImageRenderer.DefaultGlyphCacheMaxEntries);
        DistinctPairCount().ShouldBeGreaterThan(maxEntries);

        var white = new RGBAColor32(255, 255, 255, 255);
        var layout = new RectInt(new PointInt(400, 100), new PointInt(0, 0));
        foreach (var size in Sizes)
            foreach (var s in Strings)
            {
                capped.FillRectangle(new RectInt(new PointInt(400, 100), new PointInt(0, 0)), new RGBAColor32(0, 0, 0, 255));
                plain.FillRectangle(new RectInt(new PointInt(400, 100), new PointInt(0, 0)), new RGBAColor32(0, 0, 0, 255));
                capped.DrawText(s, FontPath, size, white, layout, TextAlign.Near, TextAlign.Near);
                plain.DrawText(s, FontPath, size, white, layout, TextAlign.Near, TextAlign.Near);
                capped.GlyphCache.Entries.ShouldBeLessThanOrEqualTo(maxEntries);
                capped.GlyphCache.BitmapBytes.ShouldBeLessThanOrEqualTo(maxBytes);
                capped.Surface.Pixels.ShouldBe(plain.Surface.Pixels);
            }
        output.WriteLine($"capped: {capped.GlyphCache}; uncapped: {plain.GlyphCache}");
    }

    [Fact]
    public void ByteCappedRenderer_EvictsOnBytesAloneAndDrawsIdenticalPixels()
    {
        const long maxBytes = 8 * 1024;
        using var capped = new RgbaImageRenderer(400, 100) { GlyphCacheMaxBytes = maxBytes };
        using var plain = new RgbaImageRenderer(400, 100);
        capped.GlyphCacheMaxEntries.ShouldBe(RgbaImageRenderer.DefaultGlyphCacheMaxEntries);

        var white = new RGBAColor32(255, 255, 255, 255);
        var black = new RGBAColor32(0, 0, 0, 255);
        var all = new RectInt(new PointInt(400, 100), new PointInt(0, 0));
        foreach (var size in Sizes)
            foreach (var s in Strings)
            {
                capped.FillRectangle(all, black);
                plain.FillRectangle(all, black);
                capped.DrawText(s, FontPath, size, white, all, TextAlign.Near, TextAlign.Near);
                plain.DrawText(s, FontPath, size, white, all, TextAlign.Near, TextAlign.Near);
                capped.GlyphCache.BitmapBytes.ShouldBeLessThanOrEqualTo(maxBytes);
                capped.Surface.Pixels.ShouldBe(plain.Surface.Pixels);
            }
        // Only the byte limit can have evicted: the entry cap is the default.
        capped.GlyphCache.Entries.ShouldBeLessThan(plain.GlyphCache.Entries);
        output.WriteLine($"byte-capped: {capped.GlyphCache}; uncapped: {plain.GlyphCache}");
    }

    [Fact]
    public void GlyphCacheMaxBytes_BelowTheMinimumThrows()
    {
        using var renderer = new RgbaImageRenderer(10, 10);
        Should.Throw<ArgumentOutOfRangeException>(() => renderer.GlyphCacheMaxBytes = 191);
    }

    [Fact]
    public void CappedRasterizer_IdentityMemoStaysWithinCapAndResolvesTheSame()
    {
        const int cap = 20;
        using var capped = new ManagedFontRasterizer { IdentityMemoMaxEntries = cap };
        using var plain = new ManagedFontRasterizer();
        var runes = Strings.SelectMany(s => s.EnumerateRunes()).Distinct().ToList();
        runes.Count.ShouldBeGreaterThan(cap);
        for (var pass = 0; pass < 2; pass++)
            foreach (var rune in runes)
            {
                var a = capped.ResolveGlyphIdentity(FontPath, rune, 0, GlyphMapHint.Auto);
                a.ShouldBe(plain.ResolveGlyphIdentity(FontPath, rune, 0, GlyphMapHint.Auto));
                capped.Footprint.IdentityMemoEntries.ShouldBeLessThanOrEqualTo(cap);
            }
        plain.Footprint.IdentityMemoEntries.ShouldBe(runes.Count);
    }
}
