using System.Text;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="BoundedCache{TKey, TValue}"/>, which keeps the math rasterizer's glyphs and stretched glyphs
/// across renders: it never holds more than its entries or its bytes allow, an entry in use survives the
/// entries added around it, and it stays consistent when many threads read and add at once. And the
/// rasterizer's own two caches, at their real bounds, stay within them however many glyphs it draws.
/// </summary>
public sealed class BoundedCacheTests
{
    [Fact]
    public void ItKeepsNoMoreEntriesThanAllowed()
    {
        var cache = new BoundedCache<int, string>(maxEntries: 64, maxBytes: 1 << 20, sizeOf: _ => 0);
        for (var i = 0; i < 1000; i++)
        {
            cache.Add(i, i.ToString());
            cache.Count.ShouldBeLessThanOrEqualTo(64, $"entries held after adding {i + 1}");
        }

        // The latest half generation is all there.
        for (var i = 1000 - 32; i < 1000; i++)
            cache.TryGet(i, out var value).ShouldBeTrue($"entry {i}, among the latest");
    }

    [Fact]
    public void ItKeepsNoMoreBytesThanAllowed()
    {
        const long maxBytes = 16 * (1000 + BoundedCache<int, byte[]>.EntryOverheadBytes);
        var cache = new BoundedCache<int, byte[]>(maxEntries: 1 << 20, maxBytes, sizeOf: value => value.Length);
        for (var i = 0; i < 500; i++)
        {
            cache.Add(i, new byte[1000]);
            cache.Bytes.ShouldBeLessThanOrEqualTo(maxBytes, $"bytes held after adding {i + 1}");
        }
        cache.Count.ShouldBeLessThanOrEqualTo(16);
    }

    [Fact]
    public void AValueLargerThanAGenerationIsNotKept()
    {
        var cache = new BoundedCache<int, byte[]>(maxEntries: 64, maxBytes: 4096, sizeOf: value => value.Length);
        cache.Add(1, new byte[4096]);
        cache.TryGet(1, out _).ShouldBeFalse();
        cache.Count.ShouldBe(0);
    }

    [Fact]
    public void AnEntryReadOncePerGenerationStays()
    {
        var cache = new BoundedCache<int, string>(maxEntries: 64, maxBytes: 1 << 20, sizeOf: _ => 0);
        cache.Add(-1, "kept");
        for (var i = 0; i < 1000; i++)
        {
            cache.Add(i, i.ToString());
            // Every 16 adds, well within a generation of 32.
            if (i % 16 == 0) cache.TryGet(-1, out var value).ShouldBeTrue($"the entry in use, after {i + 1} adds");
        }
        cache.TryGet(0, out _).ShouldBeFalse("an entry not read since it was added goes");
    }

    /// <summary>Sixteen threads read and add over a key range several times the bound, on a cache that turns
    /// over constantly: a hit is always the value for its own key, and the cache never holds more than its
    /// bound.</summary>
    [Fact]
    public void ManyThreadsAtOnceGetTheirOwnKeysValues()
    {
        const int threads = 16;
        var cache = new BoundedCache<int, string>(maxEntries: 128, maxBytes: 1 << 20, sizeOf: _ => 0);
        var wrong = 0;
        var largest = 0;
        using var start = new Barrier(threads);
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            var random = new Random(t);
            start.SignalAndWait();
            for (var n = 0; n < 50_000; n++)
            {
                var key = random.Next(600);
                if (cache.TryGet(key, out var value))
                {
                    if (value != key.ToString()) Interlocked.Increment(ref wrong);
                }
                else
                {
                    cache.Add(key, key.ToString());
                }
                if (n % 1000 == 0)
                {
                    var count = cache.Count;
                    int seen;
                    while ((seen = Volatile.Read(ref largest)) < count && Interlocked.CompareExchange(ref largest, count, seen) != seen) { }
                }
            }
        })).ToList();
        workers.ForEach(w => w.Start());
        workers.ForEach(w => w.Join());

        wrong.ShouldBe(0, "hits that returned another key's value");
        largest.ShouldBeLessThanOrEqualTo(128, "entries held at any check");
    }

    /// <summary>
    /// A caching rasterizer (as <see cref="MathLayout.BoxStyle.SharedRasterizer"/> is) asked for far more
    /// glyphs and stretched glyphs than it keeps: each cache stays within
    /// <see cref="ManagedFontRasterizer.GlyphRastersKept"/> / <see cref="ManagedFontRasterizer.StretchyRastersKept"/>
    /// entries and its bytes, and the glyphs asked for last are still served from it.
    /// </summary>
    [Fact]
    public void TheRasterizersCachesStayWithinTheirBounds()
    {
        var stix = MathRasterCorpus.FontPath("STIX2Math.otf");
        using var rasterizer = new ManagedFontRasterizer(cacheRasters: true);
        var runes = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+=()".EnumerateRunes().ToArray();
        for (var size = 6f; size < 6f + 0.25f * 90; size += 0.25f)
            foreach (var rune in runes)
                rasterizer.RasterizeGlyph(stix, size, rune);
        for (var request = 20f; request < 20f + 0.5f * 1200; request += 0.5f)
            rasterizer.RasterizeStretchyVertical(stix, 16f, new Rune('{'), request);

        var (glyphs, glyphBytes, stretchy, stretchyBytes) = rasterizer.RasterCacheSize;
        glyphs.ShouldBeLessThanOrEqualTo(ManagedFontRasterizer.GlyphRastersKept, "glyph rasters kept, of 5940 asked for");
        glyphBytes.ShouldBeLessThanOrEqualTo(ManagedFontRasterizer.GlyphRasterBytesKept);
        stretchy.ShouldBeLessThanOrEqualTo(ManagedFontRasterizer.StretchyRastersKept, "stretched glyphs kept, of 1200 asked for");
        stretchyBytes.ShouldBeLessThanOrEqualTo(ManagedFontRasterizer.StretchyRasterBytesKept);
        glyphs.ShouldBeGreaterThan(1000, "the latest glyphs are held");
    }
}
