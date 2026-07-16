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
/// Unit tests for the backend-neutral <see cref="SdfFontAtlas"/> core — the shelf packer, page
/// append/LRU/evict-all lifecycle, per-frame drain budgets, and the virtual-V page encoding.
/// All of this logic previously lived inside SdlVulkan.Renderer's VkSdfFontAtlas where it was
/// only exercisable through a live Vulkan device; the hoist makes it testable with no GPU at all
/// (backend: null, or a recording fake asserting the <see cref="ISdfAtlasBackend"/> hook contract).
/// </summary>
public sealed class SdfFontAtlasTests : IDisposable
{
    private static readonly string FontPath = Path.Combine("Fonts", "DejaVuSans.ttf");

    private readonly ManagedFontRasterizer _rasterizer = new();

    public void Dispose() => _rasterizer.Dispose();

    /// <summary>Records every backend hook invocation in order, so tests can assert the
    /// "OnPagesWillBeDestroyed once, then OnPageDestroyed descending" teardown contract and
    /// that LRU recycling fires no hooks at all.</summary>
    private sealed class RecordingBackend : ISdfAtlasBackend
    {
        public readonly List<string> Calls = new();
        public void OnPageCreated(int pageIndex, int pageDimension) => Calls.Add($"create:{pageIndex}");
        public void OnPagesWillBeDestroyed() => Calls.Add("will-destroy");
        public void OnPageDestroyed(int pageIndex) => Calls.Add($"destroy:{pageIndex}");
    }

    private SdfFontAtlas CreateAtlas(int pageDim, ISdfAtlasBackend? backend = null) =>
        new(_rasterizer, maxTextureDimension: 8192, framesInFlight: 2,
            backend: backend, initialPageDim: pageDim);

    /// <summary>Converts a glyph's normalized UVs to page-local pixel rectangles via DecodePage —
    /// the same math a GPU backend's vertex path performs.</summary>
    private static (int Page, float X0, float Y0, float X1, float Y1) PixelRect(
        SdfFontAtlas atlas, in SdfFontAtlas.GlyphInfo g)
    {
        atlas.DecodePage(in g, out var page, out var v0, out var v1);
        var dim = atlas.PageDimension;
        return (page, g.U0 * dim, v0 * dim, g.U1 * dim, v1 * dim);
    }

    [Fact]
    public void Packer_PlacesGlyphsWithoutOverlap()
    {
        using var atlas = CreateAtlas(pageDim: 256);

        var rects = new List<(int Page, float X0, float Y0, float X1, float Y1)>();
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var g = atlas.GetGlyph(FontPath, SdfFontAtlas.SdfRasterSize, new Rune(c));
            g.Width.ShouldBeGreaterThan(0, $"glyph '{c}' should rasterize with ink");
            rects.Add(PixelRect(atlas, in g));
        }

        // No two glyphs on the same page may overlap — a shelf-packer stride/wrap bug would
        // show up as intersecting rectangles.
        for (var i = 0; i < rects.Count; i++)
        for (var j = i + 1; j < rects.Count; j++)
        {
            var (pa, ax0, ay0, ax1, ay1) = rects[i];
            var (pb, bx0, by0, bx1, by1) = rects[j];
            if (pa != pb) continue;
            var overlaps = ax0 < bx1 && bx0 < ax1 && ay0 < by1 && by0 < ay1;
            overlaps.ShouldBeFalse($"glyphs {i} and {j} overlap on page {pa}");
        }
    }

    [Fact]
    public void PageOverflow_AppendsNewPage_NeverMutatesEarlierPages()
    {
        var backend = new RecordingBackend();
        using var atlas = CreateAtlas(pageDim: 64, backend: backend);
        atlas.PageCount.ShouldBe(1);
        backend.Calls.ShouldBe(["create:0"]);

        // Fill page 0, snapshot its staging, then force appends and re-check the snapshot.
        var first = atlas.GetGlyph(FontPath, SdfFontAtlas.SdfRasterSize, new Rune('A'));
        first.Width.ShouldBeGreaterThan(0);
        var page0Snapshot = atlas.GetPageStaging(0).ToArray();

        for (var c = 'B'; c <= 'K' && atlas.PageCount < 3; c++)
            atlas.GetGlyph(FontPath, SdfFontAtlas.SdfRasterSize, new Rune(c));

        atlas.PageCount.ShouldBeGreaterThanOrEqualTo(2, "64px pages should overflow within a few glyphs");
        // Growth is append-only: page 0's pixels are untouched by later appends.
        atlas.GetPageStaging(0).SequenceEqual(page0Snapshot).ShouldBeTrue();
        // And the backend saw exactly one create per page, in order, no destroys.
        backend.Calls.ShouldBe(Enumerable.Range(0, atlas.PageCount).Select(i => $"create:{i}"));
    }

    [Fact]
    public void AllPagesHot_FallsBackToEvictAll_WithOrderedBackendHooks()
    {
        var backend = new RecordingBackend();
        using var atlas = CreateAtlas(pageDim: 64, backend: backend);

        // Fill every page within a single frame (no BeginFrame between inserts) so every page's
        // LastUsedFrame is the current tick — nothing is LRU-recyclable. The insert that can't be
        // placed returns the blank sentinel and arms the evict-all fallback.
        uint gid = 4;
        var sawBlank = false;
        var firstPlacedGid = 0u;
        for (; gid < 400; gid++)
        {
            var g = atlas.GetGlyphByGid(FontPath, gid);
            if (g.Width > 0 && firstPlacedGid == 0) firstPlacedGid = gid;
            if (atlas.PageCount == SdfFontAtlas.MaxPages && g.Width == 0 && firstPlacedGid != 0)
            {
                sawBlank = true;
                break;
            }
        }
        sawBlank.ShouldBeTrue("filling all pages in one frame should hit the all-pages-hot fallback");
        var pagesBefore = atlas.PageCount;
        pagesBefore.ShouldBe(SdfFontAtlas.MaxPages);
        backend.Calls.Clear();

        // Next BeginFrame runs the EvictAll fallback: one will-destroy, then destroys DESCENDING
        // for pages N-1..1 (never page 0 — it resets in place).
        atlas.BeginFrame();
        atlas.PageCount.ShouldBe(1);
        var expected = new List<string> { "will-destroy" };
        expected.AddRange(Enumerable.Range(1, pagesBefore - 1).Reverse().Select(i => $"destroy:{i}"));
        backend.Calls.ShouldBe(expected);

        // Every previously cached glyph is gone (fresh miss, no rasterize).
        atlas.GetGlyphByGid(FontPath, firstPlacedGid, rasterizeOnMiss: false).Width.ShouldBe(0);
    }

    [Fact]
    public void LruEviction_RecyclesColdestPageInPlace_NoBackendHooks()
    {
        var backend = new RecordingBackend();
        using var atlas = CreateAtlas(pageDim: 64, backend: backend);

        // Fill to the page cap, recording which page each glyph landed on.
        var gidToPage = new Dictionary<uint, int>();
        uint gid = 4;
        for (; gid < 400 && atlas.PageCount < SdfFontAtlas.MaxPages; gid++)
        {
            var g = atlas.GetGlyphByGid(FontPath, gid);
            if (g.Width == 0) continue;
            atlas.DecodePage(in g, out var pg, out _, out _);
            gidToPage[gid] = pg;
        }
        atlas.PageCount.ShouldBe(SdfFontAtlas.MaxPages);

        // Age every page well past framesInFlight, then touch a glyph on every page EXCEPT page 0
        // so page 0 is the unique coldest.
        for (var i = 0; i < 4; i++) atlas.BeginFrame();
        foreach (var (g, pg) in gidToPage)
            if (pg != 0)
                atlas.GetGlyphByGid(FontPath, g).Width.ShouldBeGreaterThan(0);

        backend.Calls.Clear();
        // Insert new glyphs until one lands via the LRU-recycle path (the active page is full).
        SdfFontAtlas.GlyphInfo recycled = default;
        for (; gid < 800; gid++)
        {
            recycled = atlas.GetGlyphByGid(FontPath, gid);
            if (recycled.Width > 0) break;
        }
        recycled.Width.ShouldBeGreaterThan(0, "an insert should succeed by recycling the cold page");
        atlas.DecodePage(in recycled, out var newPage, out _, out _);
        newPage.ShouldBe(0, "page 0 was the coldest and should have been recycled");

        // Recycling is structurally GPU-work-free: zero backend hooks fired.
        backend.Calls.ShouldBeEmpty();
        // Page count unchanged; the recycled page's old glyphs are gone, hot pages' glyphs remain.
        atlas.PageCount.ShouldBe(SdfFontAtlas.MaxPages);
        var page0Gid = gidToPage.First(kv => kv.Value == 0).Key;
        var hotGid = gidToPage.First(kv => kv.Value != 0).Key;
        atlas.GetGlyphByGid(FontPath, page0Gid, rasterizeOnMiss: false).Width.ShouldBe(0);
        atlas.GetGlyphByGid(FontPath, hotGid, rasterizeOnMiss: false).Width.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void FrameBudget_BoundsInsertsPerFrame_AndDrainsAcrossFrames()
    {
        // synchronousRasterize: the whole batch rasterizes inline into the pending queue, so the
        // drain behavior is deterministic (no thread-pool timing in the assertion path).
        using var atlas = new SdfFontAtlas(_rasterizer, maxTextureDimension: 8192, framesInFlight: 2,
            synchronousRasterize: true);

        var batch = new List<(string Font, uint Gid, string? Type1Name)>();
        for (var g = 4u; g < 4u + 150; g++) batch.Add((FontPath, g, null));
        atlas.PreRasterizeBatchByGid(batch);
        atlas.IsDirty.ShouldBeTrue("queued rasterizations must report dirty");

        // Each BeginFrame inserts at most MaxGlyphInsertsPerFrame (and at most the byte budget);
        // 150 pending glyphs therefore need at least two frames to land.
        var perFrame = new List<int>();
        for (var frame = 0; frame < 10 && atlas.IsDirty; frame++)
        {
            atlas.BeginFrame();
            // FrameStats leads with "+N glyphs" — the per-frame insert counter.
            var stats = atlas.FrameStats;
            var inserted = int.Parse(stats[1..stats.IndexOf(' ')]);
            perFrame.Add(inserted);

            // Run the backend flush handshake so dirty rects don't hold IsDirty true forever.
            var any = false;
            for (var p = 0; p < atlas.PageCount; p++)
            {
                if (!atlas.TryGetDirtyRegion(p, out _)) continue;
                atlas.MarkPageFlushed(p);
                any = true;
            }
            atlas.CompleteFlush(any);
        }

        perFrame.Count(n => n > 0).ShouldBeGreaterThanOrEqualTo(2, "150 glyphs must take multiple frames");
        perFrame.ShouldAllBe(n => n <= SdfFontAtlas.MaxGlyphInsertsPerFrame);
        // A handful of gids in any range rasterize blank (no ink) — they drain from the queue as
        // zero sentinels without counting as inserts, so allow a small deficit from 150.
        perFrame.Sum().ShouldBeInRange(140, 150);
        atlas.IsDirty.ShouldBeFalse("after draining + flushing, the atlas must settle");
    }

    [Fact]
    public void UvEncoding_DecodePage_RoundTripsAcrossPages()
    {
        using var atlas = CreateAtlas(pageDim: 64);

        var seen = new Dictionary<uint, SdfFontAtlas.GlyphInfo>();
        for (var gid = 4u; gid < 400 && atlas.PageCount < 4; gid++)
        {
            var g = atlas.GetGlyphByGid(FontPath, gid);
            if (g.Width > 0) seen[gid] = g;
        }
        atlas.PageCount.ShouldBeGreaterThanOrEqualTo(3);

        var maxPageSeen = -1;
        foreach (var g in seen.Values)
        {
            atlas.DecodePage(in g, out var page, out var v0, out var v1);
            page.ShouldBeInRange(0, atlas.PageCount - 1);
            maxPageSeen = Math.Max(maxPageSeen, page);

            // Page-local coordinates must be sane: within [0,1), ordered, and the pixel height
            // implied by the V range must equal the glyph's stored pixel height exactly.
            v0.ShouldBeInRange(0f, 1f);
            v1.ShouldBeGreaterThan(v0);
            v1.ShouldBeLessThanOrEqualTo(1f);
            ((v1 - v0) * atlas.PageDimension).ShouldBe(g.Height, tolerance: 0.001);
            g.U0.ShouldBeInRange(0f, 1f);
            g.U1.ShouldBeGreaterThan(g.U0);
            ((g.U1 - g.U0) * atlas.PageDimension).ShouldBe(g.Width, tolerance: 0.001);
        }
        maxPageSeen.ShouldBeGreaterThanOrEqualTo(1, "the round-trip must be exercised beyond page 0");
    }
}
