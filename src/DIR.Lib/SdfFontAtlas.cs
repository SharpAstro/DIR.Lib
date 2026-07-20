using System.Collections.Concurrent;
using System.Text;

namespace DIR.Lib;

/// <summary>
/// Backend-neutral MTSDF glyph atlas core: glyph identity keying, shelf packing into fixed-size
/// square pages, per-page CPU staging buffers with dirty-rect tracking, per-page LRU eviction,
/// bounded async rasterization, and optional <see cref="SdfGlyphDiskCache"/> integration.
/// Glyphs are rasterized once at a fixed raster size as multi-channel signed distance fields —
/// the GPU backend's fragment shader reconstructs crisp anti-aliased edges at any display size.
///
/// <para>The core owns every CPU-side decision (where a glyph lands, what is dirty, what gets
/// evicted); a GPU backend implements <see cref="ISdfAtlasBackend"/> for page-resource lifecycle
/// and pulls dirty regions once per frame via <see cref="TryGetDirtyRegion"/> /
/// <see cref="GetPageStaging"/> / <see cref="MarkPageFlushed"/> / <see cref="CompleteFlush"/>.
/// Extracted from SdlVulkan.Renderer's VkSdfFontAtlas so Vulkan and WebGL backends share one
/// implementation.</para>
/// </summary>
public sealed class SdfFontAtlas : IDisposable
{
    // Glyphs are keyed by resolved identity (A1): the numeric glyph id for OpenType fonts
    // (Name == null), or the PostScript glyph name for Type1/PFB fonts (Gid == 0, no numeric
    // ids). Codepoint→identity is resolved once at the GetGlyph boundary via
    // ManagedFontRasterizer.ResolveGlyphIdentity — so the old (charCode, character) CID
    // workaround collapses: two codepoints that resolve to the same glyph now share one entry.
    private readonly record struct GlyphKey(string Font, float Size, uint Gid, string? Name);

    // Sentinel Gid for whitespace, which carries no ink and derives its advance from the 'n'
    // reference glyph. Kept off gid 0 (the shared notdef/blank entry for genuinely-missing
    // glyphs) and off any real glyph id (< NumGlyphs) so whitespace never collides with either.
    private const uint WhitespaceGid = uint.MaxValue;

    public readonly record struct GlyphInfo(float U0, float V0, float U1, float V1,
        int Width, int Height, float AdvanceX, int BearingX, int BearingY, float Spread);

    /// <summary>A page's pending upload rectangle, in page-local pixels. <see cref="X1"/>/<see cref="Y1"/>
    /// are exclusive. Returned by <see cref="TryGetDirtyRegion"/> for the backend's flush loop.</summary>
    public readonly record struct DirtyRegion(int X0, int Y0, int X1, int Y1)
    {
        public int Width => X1 - X0;
        public int Height => Y1 - Y0;
    }

    private readonly ManagedFontRasterizer _rasterizer;
    private readonly ISdfAtlasBackend? _backend;
    // Frame-pipelining depth of the backend: a page is only LRU-recyclable once its LastUsedFrame is
    // older than this many frames (a prior in-flight frame might still be sampling it). Vulkan passes
    // its MaxFramesInFlight (2); WebGL has no frame pipelining and passes 1.
    private readonly int _framesInFlight;
    // Single-threaded hosts (browser WASM without COOP/COEP has no real thread pool workers) set this
    // so rasterization and .sdfg reads run inline on the calling thread instead of via Task.Run.
    // Results still flow through the same bounded per-frame drains, so budgets/pop-in behavior match.
    private readonly bool _synchronousRasterize;

    /// <summary>The shared rasterizer this atlas resolves/rasterizes through — also what the
    /// renderer hands an <see cref="ITextShaper"/> so shaping keys off the same font state.
    /// Not owned: never disposed by the atlas.</summary>
    public ManagedFontRasterizer Rasterizer => _rasterizer;

    // The raster size THIS atlas bakes glyphs at. Defaults to the SdfRasterSize const (64px) so
    // existing single-tier callers are unchanged; a tiered host runs a second atlas at a larger
    // raster (e.g. 128px) for glyphs drawn large on screen, where the 64px field undersamples thin
    // strokes. The rasterizer already takes the size from the glyph key, so this is the only field
    // that has to become per-instance for tiering. See GetGlyphScale / ScreenPxHalfBand.
    private readonly float _rasterSize;
    /// <summary>The size (px) this atlas rasterizes glyphs at — the GPU scales the quad from here.</summary>
    public float RasterSize => _rasterSize;

    // Per-instance page cap (defaults to the MaxPages const; power of two for DecodePage exactness).
    // A fallback-backed tier (large text) runs with a small cap + refuseWhenSaturated: at the cap
    // with every page hot it REFUSES inserts (sentinel entry → the renderer draws its base-tier
    // fallback) instead of the evict-all wipe. A capped tier must never thrash: on docs whose
    // per-frame working set exceeds the cap (dense CJK at high zoom — measured: 2,285 glyph
    // identities ≈ 47 pages at 128px vs a 16-page cap), evict-all would wipe + refill EVERY frame —
    // sustained allocation/upload churn on the exact path that wedges Adreno drivers.
    private readonly int _maxPages;
    private readonly bool _refuseWhenSaturated;
    // Set by InsertRasterized's refusal branch for the CURRENT insert; the drains read it to stop
    // retrying (a refused glyph is sentineled, a refused disk-load tail is dropped). Render-thread only.
    private bool _lastInsertSaturated;
    private bool _saturationLogged;
    // Refusal sentinel: a Width==0 entry whose negative Spread marks "refused at saturation" as
    // opposed to "genuinely blank". Purged when a page frees up (PurgeRefusalSentinels), so the
    // glyphs re-attempt the tier instead of staying base-tier-soft for the whole session.
    private static readonly GlyphInfo RefusedSentinel = new(0, 0, 0, 0, 0, 0, 0, 0, 0, -1f);

    private readonly Dictionary<GlyphKey, GlyphInfo> _glyphs = new();
    private readonly HashSet<GlyphKey> _unflushedGlyphs = new();

    // Optional disk-persistent cache of SDF bitmaps. When supplied, every freshly
    // rasterized glyph is appended to disk, and the first GetGlyph/PreRasterizeBatch
    // call for a font bulk-loads its existing entries — making re-opens of the same
    // document near-instant after the first session.
    private readonly SdfGlyphDiskCache? _diskCache;
    private readonly HashSet<string> _diskLoadedFonts = new();
    // Completed background .sdfg reads awaiting render-thread insertion (DrainPendingDiskLoads).
    // Keeps the (potentially ~100ms) synchronous disk read off the render thread.
    private readonly ConcurrentQueue<(string Font, IReadOnlyList<DiskGlyphEntry> Entries)> _pendingDiskLoads = new();

    // Async SDF rasterization. The ~10ms-per-glyph distance-field computation runs OFF the render
    // thread: PreRasterizeBatch (and a draw-path miss) claims a key in _rasterizeInFlight, a background
    // task rasterizes it, and the finished bitmap lands in _pendingRasterized for the render thread to
    // insert — bounded — in BeginFrame. This keeps the render loop responsive on glyph-heavy (CJK) docs
    // where the old synchronous prewarm stalled a frame for seconds; glyphs now fill in progressively
    // over a handful of frames. _rasterizeInFlight dedups: the visible-glyph set is re-offered every
    // frame, so a key must be rasterized at most once.
    private readonly ConcurrentDictionary<GlyphKey, byte> _rasterizeInFlight = new();
    // The key already carries the resolved identity, so the finished bitmap needs nothing else
    // to be inserted and persisted (identity → gid/name lives in Key).
    private readonly ConcurrentQueue<(GlyphKey Key, MtsdfGlyphBitmap Bitmap)> _pendingRasterized = new();
    // Max glyphs inserted (staging blit + dirty-region upload) per frame from the rasterized queue.
    // Bounds per-frame upload so a 2000-glyph CJK page drains over ~frames (IsDirty keeps the loop
    // awake until empty), never in one stall.
    public const int MaxGlyphInsertsPerFrame = 96;
    // Companion BYTE bound for the same drains. The count cap alone let the per-frame upload volume
    // quietly quadruple when the atlas went R8 → MTSDF (4 bytes/texel): 96 glyphs × ~20 KB ≈ 2 MB per
    // frame during a cold CJK flood, which is the load profile in flight when the 2026-07-10 Adreno
    // wedge hit. Bound the bytes explicitly so a format change can never scale the storm again.
    public const int MaxUploadBytesPerFrame = 1 << 20; // 1 MiB

    // Upper bound for a page dimension, clamped to the device limit in the ctor. Pages don't grow
    // (a full page is left in place and a new one appended), so this only caps the initial page dim;
    // the default (DefaultInitialAtlasDim = 1024) sits well under it. Working-set headroom comes from
    // MaxPages, not page size. Kept generous so an unusually large initialPageDim is still device-safe.
    public const int MaxAtlasSize = 8192;
    // MaxAtlasSize clamped to the device limit (set in the ctor). Use this, not the const, for grows.
    private readonly int _maxAtlasSize;

    /// <summary>Spread (in texels) the MTSDF fields are rasterized with. Backends and bake tools that
    /// construct an <see cref="SdfGlyphDiskCache"/> must pass this so cache headers match.</summary>
    public const float SdfSpread = 4f;

    // Bytes per atlas texel. The atlas stores MTSDF (RGBA8): RGB = per-channel
    // pseudo-distance, A = true distance. Every staging/upload byte offset is
    // scaled by this. (Was 1 when the atlas was single-channel R8.)
    public const int BytesPerTexel = 4;

    /// <summary>
    /// MTSDF glyphs are rasterized at this fixed size. The GPU scales the quad
    /// for any requested display size. Because a distance field encodes distance,
    /// not pixels, a single rasterization looks sharp at all display sizes — and
    /// MTSDF keeps corners sharp too, so a smaller raster costs far less fidelity
    /// than it would for plain SDF. 64px with 4-channel RGBA is memory-neutral vs
    /// the old 128px single-channel R8 (¼ the page area × 4 bytes = same bytes per
    /// glyph, same page size, same page cap). Bake tools constructing an
    /// <see cref="SdfGlyphDiskCache"/> must pass this so cache headers match.
    /// </summary>
    public const float SdfRasterSize = 64f;

    /// <summary>
    /// Default page dimension. The atlas is a list of fixed-size square pages of this size;
    /// when a page fills, a NEW page is appended (no realloc, no GPU drain, no re-upload), so
    /// this is the placement granularity, not a glyph cap. Power-of-two so a glyph's page index
    /// can be recovered exactly from its virtual V coordinate (see <see cref="InsertRasterized"/>
    /// and <see cref="DecodePage"/>).
    /// </summary>
    public const int DefaultInitialAtlasDim = (int)SdfRasterSize * 16; // 1024 (64px raster × 16)

    // Max resident pages before falling back to evict-all. 16 × 1024² × 4 bytes (RGBA) ≈ 64 MB worst
    // case — identical to the old 16 × 2048² × 1 byte R8 atlas (the 64px raster shrinks pages to 1024²,
    // the RGBA format restores the 4× per-texel, so per-glyph capacity and the memory cap are unchanged).
    // 8 was tight for glyph-heavy docs (many embedded subset fonts, or CJK with thousands of unique
    // glyphs): the atlas filled every page and then EvictAll thrashed — a device-idle drain plus
    // a full glyph re-raster on each scroll (the jank). 16 roughly doubles the working set first.
    public const int MaxPages = 16;

    /// <summary>One atlas page's CPU-side state. Pages are never reallocated: a full page is left
    /// in place and a new page appended. That is what makes growth free — existing pages stay
    /// uploaded and bound; only new glyphs land on a fresh page. The GPU resource paired with a
    /// page (image/texture, descriptor set, upload rings) lives entirely in the backend, correlated
    /// by page index via <see cref="ISdfAtlasBackend"/>.</summary>
    private sealed class Page
    {
        public byte[] Staging = [];                       // BytesPerTexel per pixel (RGBA), _pageDim²
        public int CursorX, CursorY, RowHeight;
        public int DirtyX0, DirtyY0, DirtyX1, DirtyY1;
        // LRU bookkeeping for per-page eviction. LastUsedFrame is stamped whenever a glyph on this
        // page is drawn (GetGlyph hit) or inserted; Keys lists the glyphs placed here so eviction can
        // drop exactly this page's entries from _glyphs without scanning the whole dictionary.
        public long LastUsedFrame;
        public readonly List<GlyphKey> Keys = new();
        // Frame tick this page was appended on. Glyphs on a just-appended page are quarantined from
        // the draw path for that one frame (see GetGlyphByKey): the page's first-ever
        // Undefined→ShaderReadOnly transition and its first sample otherwise share one submission,
        // which is exactly where a quirky driver (Adreno) can wedge. One frame of pop-in, invisible.
        public long CreatedTick;
    }

    private readonly List<Page> _pages = new();
    private int _pageDim;          // fixed square page size (power of two)
    private bool _needsEviction;
    private long _frameTick;       // monotonic, ++ each BeginFrame — drives per-page LRU
    private int _activePageIdx;    // page currently being filled (last appended OR last LRU-reused)

    // Per-frame activity counters (reset in BeginFrame). _frameStagedBytes doubles as the byte-budget
    // accumulator for the drains; all three feed FrameStats, the wedge breadcrumb the event loop logs
    // when a fence sticks. _lastPageAppendTick keeps IsDirty true across a page-append frame so the
    // quarantined glyphs (see Page.CreatedTick) get their redraw next frame.
    private int _frameStagedBytes;
    private int _frameGlyphsInserted;
    private int _framePagesAppended;
    private long _lastPageAppendTick = -1;

    public int PageCount => _pages.Count;

    /// <summary>The fixed square page size (power of two) every page uses — backends need it for
    /// texture allocation in <see cref="ISdfAtlasBackend.OnPageCreated"/> and row-stride math when
    /// reading <see cref="GetPageStaging"/>.</summary>
    public int PageDimension => _pageDim;

    /// <summary>One-line composition of the current frame's atlas activity — the wedge breadcrumb
    /// the event loop logs when a fence sticks. A GPU hang leaves no GPU-side dump; this is the
    /// CPU-side echo of what the hung submission was carrying (glyph-upload storm? page append?).</summary>
    public string FrameStats =>
        $"+{_frameGlyphsInserted} glyphs / {_frameStagedBytes / 1024} KB staged / +{_framePagesAppended} pages ({_pages.Count} resident, {_glyphs.Count} glyphs)";

    public bool IsDirty
    {
        get
        {
            if (_needsEviction) return true;
            // Pending async rasterization (or not-yet-inserted results) means the page isn't final —
            // report dirty so the event loop keeps redrawing and the deferred glyphs pop in. Same for
            // bounded disk-load tails still draining over frames.
            if (!_pendingRasterized.IsEmpty || !_rasterizeInFlight.IsEmpty || !_pendingDiskLoads.IsEmpty) return true;
            // A page appended this frame carries one-frame-quarantined glyphs (Page.CreatedTick):
            // stay dirty so the loop redraws and they land next frame.
            if (_lastPageAppendTick == _frameTick) return true;
            foreach (var p in _pages)
                if (p.DirtyX0 < p.DirtyX1 && p.DirtyY0 < p.DirtyY1) return true;
            return false;
        }
    }

    /// <summary>Recover which page a glyph lives on and its page-local V range from the virtual V
    /// encoded at insert time (V is normalized over the instance's max-pages-tall virtual stack).
    /// Exact while <see cref="PageDimension"/> and the page cap are powers of two. U is already page-local.</summary>
    public void DecodePage(in GlyphInfo g, out int page, out float localV0, out float localV1)
    {
        var vy = g.V0 * _maxPages;
        page = (int)vy;
        localV0 = vy - page;
        localV1 = g.V1 * _maxPages - page;
    }

    public SdfFontAtlas(ManagedFontRasterizer rasterizer,
        int maxTextureDimension,
        int framesInFlight,
        ISdfAtlasBackend? backend = null,
        int initialPageDim = DefaultInitialAtlasDim,
        SdfGlyphDiskCache? diskCache = null,
        bool synchronousRasterize = false,
        float rasterSize = SdfRasterSize,
        int maxPages = MaxPages,
        bool refuseWhenSaturated = false)
    {
        _rasterizer = rasterizer;
        _backend = backend;
        _framesInFlight = framesInFlight;
        _diskCache = diskCache;
        _synchronousRasterize = synchronousRasterize;
        _rasterSize = rasterSize;
        // Power of two, rounded DOWN (DecodePage's (int)(V*_maxPages) must recover the page index
        // exactly), clamped to a sane range — the V-encoding's virtual stack is _maxPages*_pageDim.
        _maxPages = 1 << System.Numerics.BitOperations.Log2((uint)Math.Clamp(maxPages, 1, 64));
        _refuseWhenSaturated = refuseWhenSaturated;
        // Page dimension = the requested size, clamped to the device limit and rounded DOWN to a
        // power of two (so DecodePage's (int)(V*MaxPages) recovers the page index exactly).
        // Pages are square _pageDim. The atlas never grows the page; it appends new pages instead,
        // so this is granularity, not a cap.
        _maxAtlasSize = Math.Min(MaxAtlasSize, maxTextureDimension);
        _pageDim = PrevPowerOfTwo(Math.Min(initialPageDim, _maxAtlasSize));

        AllocateNewPage();   // start with one page — fires _backend.OnPageCreated(0) inline
    }

    private static int PrevPowerOfTwo(int n)
    {
        var p = 1;
        while (p * 2 <= n) p *= 2;
        return p;
    }

    /// <summary>Append a fresh page: allocate its CPU staging and tell the backend to allocate the
    /// paired GPU resource — no GPU drain. This is the "grow" replacement — O(1), never touches
    /// existing pages.</summary>
    private Page AllocateNewPage()
    {
        var page = new Page
        {
            Staging = new byte[_pageDim * _pageDim * BytesPerTexel],
            DirtyX0 = _pageDim, DirtyY0 = _pageDim, DirtyX1 = 0, DirtyY1 = 0,
        };
        _pages.Add(page);
        _activePageIdx = _pages.Count - 1;
        page.LastUsedFrame = _frameTick;
        page.CreatedTick = _frameTick;
        _framePagesAppended++;
        _lastPageAppendTick = _frameTick;
        _backend?.OnPageCreated(_pages.Count - 1, _pageDim);
        return page;
    }

    public void BeginFrame()
    {
        _frameTick++;
        _frameStagedBytes = 0;
        _frameGlyphsInserted = 0;
        _framePagesAppended = 0;
        if (_needsEviction)
        {
            // All pages were hot last frame (working set genuinely > MaxPages in one frame — rare).
            // Per-page LRU couldn't free a safe page, so fall back to the full wipe.
            EvictAll();
            _needsEviction = false;
        }
        DrainPendingDiskLoads();
        DrainPendingRasterized();
    }

    // Inserts up to MaxGlyphInsertsPerFrame background-rasterized glyphs into the atlas. Render-thread
    // only (InsertRasterized mutates atlas/staging/cursor). Bounded per frame so the staging blit +
    // dirty-region upload never spike; the remainder drains on later frames (IsDirty keeps the loop
    // awake). Newly inserted glyphs are persisted to disk here (the background task only rasterizes).
    private void DrainPendingRasterized()
    {
        var inserted = 0;
        while (inserted < MaxGlyphInsertsPerFrame && _frameStagedBytes < MaxUploadBytesPerFrame
               && _pendingRasterized.TryDequeue(out var r))
        {
            _rasterizeInFlight.TryRemove(r.Key, out _);
            if (_glyphs.ContainsKey(r.Key)) continue;       // raced with a disk load / duplicate
            var info = InsertRasterized(r.Key, r.Bitmap);
            if (info.Width > 0)
                _diskCache?.AppendGlyph(r.Key.Font, r.Key.Gid, r.Key.Name, in r.Bitmap);
            else if (!_needsEviction && !_glyphs.ContainsKey(r.Key))
                // Genuinely blank glyph (empty SDF — InsertRasterized doesn't record those). Cache a
                // zero sentinel so the draw path / prewarm don't re-queue it every frame — otherwise
                // _rasterizeInFlight never settles and IsDirty pins the loop in a redraw busy-spin.
                // (The ContainsKey guard keeps a saturation RefusedSentinel — written by the refusal
                // branch itself — from being overwritten with the permanent blank flavour.)
                _glyphs[r.Key] = default;
            inserted++;
            // Page cap hit (InsertRasterized set _needsEviction): stop; BeginFrame evicts next frame
            // and the remaining queued glyphs (or their re-requests) land afterwards.
            if (_needsEviction) break;
        }
    }

    // Inserts glyphs whose .sdfg read completed on a background thread (see EnsureFontLoadedFromDisk).
    // Render-thread only — InsertRasterized mutates atlas/staging/cursor state.
    private void DrainPendingDiskLoads()
    {
        // Bounded exactly like DrainPendingRasterized: at most MaxGlyphInsertsPerFrame inserts (and
        // therefore one bounded staging blit + dirty-region upload) per frame. A big .sdfg holds
        // thousands of glyphs; loading them all in one frame is a tens-of-MB single-command-buffer
        // upload that wedges the GPU. The tail re-queues and drains over the next frames (IsDirty keeps
        // the loop awake until empty), so the worst case is a brief progressive fill, never a stall.
        var budget = MaxGlyphInsertsPerFrame;
        while (budget > 0 && _frameStagedBytes < MaxUploadBytesPerFrame
               && _pendingDiskLoads.TryDequeue(out var load))
        {
            var i = 0;
            var atlasFull = false;
            for (; i < load.Entries.Count && budget > 0 && _frameStagedBytes < MaxUploadBytesPerFrame; i++)
            {
                var e = load.Entries[i];
                var key = new GlyphKey(load.Font, _rasterSize, e.Gid, e.Name);
                if (_glyphs.ContainsKey(key)) continue;
                InsertRasterized(key, e.Bitmap);
                budget--;
                if (_lastInsertSaturated)
                {
                    // Refusal-mode saturation: same policy as atlas-full below — drop this font's
                    // remaining bulk-loaded glyphs (re-enqueueing them would spin the tail forever;
                    // the ones actually drawn fault in on demand once pressure drops).
                    atlasFull = true;
                    i++;
                    AtlasDiag.Log("sdf.diskload", $"{load.Font}: saturated at entry {i}/{load.Entries.Count} — rest on demand");
                    break;
                }
                if (_needsEviction)
                {
                    // Atlas filled and nothing cold could be recycled. Do NOT EvictAll to cram in the
                    // rest — a big .sdfg holds far more glyphs than any view shows, and EvictAll clears
                    // _diskLoadedFonts → the font reloads → refills → wipes again (the on-open busy-spin).
                    // Drop this font's remaining cached glyphs; the ones actually drawn fault in on
                    // demand via the draw path, where per-page LRU keeps just the working set resident.
                    _needsEviction = false;
                    atlasFull = true;
                    i++;
                    AtlasDiag.Log("sdf.diskload", $"{load.Font}: atlas full at entry {i}/{load.Entries.Count} — rest on demand");
                    break;
                }
            }
            // Hit the per-frame budget mid-font (not full): resume the unprocessed tail next frame.
            if (!atlasFull && i < load.Entries.Count)
            {
                var tail = new List<DiskGlyphEntry>(load.Entries.Count - i);
                for (var j = i; j < load.Entries.Count; j++) tail.Add(load.Entries[j]);
                _pendingDiskLoads.Enqueue((load.Font, tail));
            }
        }
    }

    /// <summary>
    /// Returns the scale factor between the requested fontSize and the SDF raster size.
    /// </summary>
    public float GetGlyphScale(float requestedFontSize) => requestedFontSize / _rasterSize;

    /// <summary>
    /// Analytic half-width of the SDF smoothstep band, in field units, equal to half a screen
    /// pixel at <paramref name="fontSize"/>: one texel is 1/(2·spread) field units and
    /// fontSize/rasterSize screen pixels wide. Passed to the SDF pipeline's sdfEdge uniform /
    /// push constant so edge AA doesn't depend on fwidth of the reconstructed median, whose
    /// derivative jumps at channel-switch seams (see the SDF fragment shader comment in the
    /// backend). Clamped so tiny text can't smear the band across the whole field range.
    /// </summary>
    public float ScreenPxHalfBand(float fontSize) =>
        Math.Clamp(0.5f / (GetGlyphScale(fontSize) * 2f * SdfSpread), 1e-3f, 0.25f);

    public GlyphInfo GetGlyph(string fontPath, float fontSize, Rune character,
        bool skipUnflushed = false, int charCode = -1, GlyphMapHint hint = GlyphMapHint.Auto,
        bool rasterizeOnMiss = true)
    {
        // First-time use of a font with a disk cache configured: bulk-import every
        // previously-rasterized glyph for it. Idempotent and noop after the first call.
        EnsureFontLoadedFromDisk(fontPath);

        // Resolve (character, charCode, hint) → glyph identity once, here at the draw boundary,
        // and key by it. All SDF glyphs are rasterized at SdfRasterSize; the caller scales the quad.
        var key = MakeKey(fontPath, character, charCode, hint);
        return GetGlyphByKey(key, Rune.IsWhiteSpace(character), skipUnflushed, rasterizeOnMiss);
    }

    /// <summary>
    /// GID-direct variant of <see cref="GetGlyph(string, float, Rune, bool, int, GlyphMapHint, bool)"/>:
    /// fetches by a pre-resolved glyph identity (the glyph id, or the PostScript name for Type1)
    /// instead of mapping a codepoint through the font cmap. This is the shaped-text entry point —
    /// once an <c>ITextShaper</c> has run (GSUB ligatures, Arabic joining, contextual forms, …) the
    /// source codepoint no longer identifies the glyph, only the substituted id does. There is no
    /// whitespace sentinel here: a shaper emits the real space glyph id, whose empty ink and hmtx
    /// advance are already correct (only the codepoint path uses WhitespaceGid + the 'n' reference).
    /// </summary>
    public GlyphInfo GetGlyphByGid(string fontPath, uint gid, string? type1Name = null,
        bool skipUnflushed = false, bool rasterizeOnMiss = true)
    {
        EnsureFontLoadedFromDisk(fontPath);
        var key = new GlyphKey(fontPath, _rasterSize, gid, type1Name);
        return GetGlyphByKey(key, isWhitespace: false, skipUnflushed, rasterizeOnMiss);
    }

    // Shared cache lookup / per-page LRU touch / background-rasterize / skipUnflushed logic behind
    // both the codepoint path (GetGlyph) and the GID-direct path (GetGlyphByGid). isWhitespace
    // suppresses background-queuing on a draw-path miss: whitespace carries no ink and is warmed
    // synchronously by PreRasterizeBatch, so it never needs queuing here.
    private GlyphInfo GetGlyphByKey(GlyphKey key, bool isWhitespace, bool skipUnflushed, bool rasterizeOnMiss)
    {
        if (_glyphs.TryGetValue(key, out var existing))
        {
            // Touch the page this glyph lives on so per-page LRU keeps the on-screen working set
            // resident and only evicts pages that have gone cold (Width==0 is the blank sentinel —
            // it has no page).
            if (existing.Width > 0)
            {
                DecodePage(existing, out var pg, out _, out _);
                if ((uint)pg < (uint)_pages.Count)
                {
                    var page = _pages[pg];
                    page.LastUsedFrame = _frameTick;
                    // One-frame quarantine: this glyph's page was appended THIS frame, so its
                    // first-ever Undefined→ShaderReadOnly transition is being recorded into the same
                    // submission that would sample it — the same-submission pattern under suspicion
                    // for the Adreno wedge. Report not-yet-available for one frame (IsDirty keeps the
                    // loop redrawing, so it lands next frame; pop-in is a single ~16ms frame).
                    if (skipUnflushed && page.CreatedTick == _frameTick)
                        return existing with { Width = 0 };
                }
            }
            if (skipUnflushed && _unflushedGlyphs.Contains(key))
                return existing with { Width = 0 };
            return existing;
        }
        if (!rasterizeOnMiss)
        {
            // Draw path: NEVER rasterize on the render thread. Queue the glyph for background
            // rasterization (deduped) and skip drawing it this frame — it appears once the
            // background result is inserted (DrainPendingRasterized). Width==0 -> caller skips it.
            if (!isWhitespace && _rasterizeInFlight.TryAdd(key, 0))
                QueueRasterizeAsync(key);
            return default;
        }
        var result = RasterizeGlyph(key);
        if (skipUnflushed && result.Width > 0)
            return result with { Width = 0 };
        return result;
    }

    // Resolve a (character, PDF charCode, cmap hint) request to the atlas identity key: the glyph
    // id for OpenType fonts, the glyph name for Type1/PFB. Whitespace has no ink and shares one
    // sentinel key (its advance derives from the 'n' reference glyph in RasterizeGlyph).
    private GlyphKey MakeKey(string fontPath, Rune character, int charCode, GlyphMapHint hint)
    {
        if (Rune.IsWhiteSpace(character))
            return new GlyphKey(fontPath, _rasterSize, WhitespaceGid, null);
        var id = _rasterizer.ResolveGlyphIdentity(fontPath, character, charCode, hint);
        return new GlyphKey(fontPath, _rasterSize, id.Gid, id.Type1Name);
    }

    // Rasterize a glyph to MTSDF by its resolved identity: Type1 by glyph name, otherwise by GID.
    // ManagedFontRasterizer is thread-safe, so this runs on both the render thread (RasterizeGlyph)
    // and background rasterize tasks (QueueRasterizeAsync / PreRasterizeBatch).
    private MtsdfGlyphBitmap RasterizeByKey(GlyphKey key) =>
        key.Name is not null
            ? _rasterizer.RasterizeGlyphMtsdfByType1Name(key.Font, key.Size, key.Name, SdfSpread)
            : _rasterizer.RasterizeGlyphMtsdfByGid(key.Font, key.Size, key.Gid, SdfSpread);

    // Rasterize one glyph on a background thread and enqueue the result for render-thread insertion.
    // Caller must have already claimed the key in _rasterizeInFlight. Used for the rare draw-path miss
    // (a glyph the per-frame prewarm batch didn't cover); the bulk path is PreRasterizeBatch.
    // On a synchronous host the same work runs inline — the result still lands in _pendingRasterized
    // so insertion stays on the bounded BeginFrame drain either way.
    private void QueueRasterizeAsync(GlyphKey key)
    {
        if (_synchronousRasterize)
        {
            RasterizeClaimedKey(key);
            return;
        }
        Task.Run(() => RasterizeClaimedKey(key));
    }

    private void RasterizeClaimedKey(GlyphKey key)
    {
        try
        {
            _pendingRasterized.Enqueue((key, RasterizeByKey(key)));
        }
        catch (Exception ex)
        {
            // Don't leave the key permanently claimed if rasterization throws — release it so a
            // later frame can retry; otherwise the glyph would never appear.
            _rasterizeInFlight.TryRemove(key, out _);
            Console.Error.WriteLine($"[SdfAtlas] async rasterize failed for {DescribeKey(key)}: {ex.Message}");
        }
    }

    // Human-readable identity for diagnostics: "gid N" for OpenType, "'name'" for Type1.
    private static string DescribeKey(GlyphKey key) => key.Name is not null ? $"'{key.Name}'" : $"gid {key.Gid}";

    /// <summary>
    /// If a disk cache is configured and we haven't yet imported <paramref name="fontPath"/>'s
    /// entries this session, read every cached SDF bitmap for the font and insert it into
    /// the atlas. Each loaded glyph goes through <see cref="InsertRasterized"/> — same
    /// path a freshly rasterized one would take — so it shows up in <c>_unflushedGlyphs</c>
    /// for the next backend flush just like a runtime rasterization would. Loaded
    /// entries are NOT re-appended to disk (they're already there).
    /// </summary>
    private void EnsureFontLoadedFromDisk(string fontPath)
    {
        if (_diskCache is null) return;
        if (_diskLoadedFonts.Contains(fontPath)) return;
        // Don't commit the "loaded" guard until the cache can actually resolve this font's hash. For a
        // "mem:" subset font that means RegisterMemoryFont must have run first; if it hasn't yet (the
        // resolver registers during parse, which can race the first glyph use), bail WITHOUT marking —
        // so a later call retries once the font is registered. Previously we marked unconditionally,
        // so a single premature call permanently blocked the disk load → every glyph re-rasterized and
        // re-appended each session (the 2.7× .sdfg duplication + a needless ~1s cold rasterize pass).
        if (!_diskCache.HasHashFor(fontPath)) return;
        _diskLoadedFonts.Add(fontPath);

        // Read + deserialize the .sdfg on a BACKGROUND thread — a large/old UI-font cache can take
        // ~100ms, and that must never block the render thread. The decoded entries come back via
        // _pendingDiskLoads and are inserted into the atlas on the render thread by
        // DrainPendingDiskLoads(). Glyphs needed before the read lands are rasterized on-demand and
        // de-duped (by key) when the batch arrives. On a synchronous host the read runs inline —
        // there is no render loop to stall, and the caller wants the cache resident immediately.
        var cache = _diskCache;
        if (_synchronousRasterize)
        {
            LoadFontEntries(cache, fontPath);
            return;
        }
        Task.Run(() => LoadFontEntries(cache, fontPath));
    }

    private void LoadFontEntries(SdfGlyphDiskCache cache, string fontPath)
    {
        try
        {
            var entries = cache.LoadEntriesForFont(fontPath);
            if (entries.Count > 0) _pendingDiskLoads.Enqueue((fontPath, entries));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SdfDiskCache] async load failed for {fontPath}: {ex.Message}");
        }
    }

    // ---- Backend flush handshake -------------------------------------------------------------
    // The backend's per-frame flush loop:
    //   var any = false;
    //   for (var i = 0; i < atlas.PageCount; i++)
    //       if (atlas.TryGetDirtyRegion(i, out var r)) { /* upload rows from GetPageStaging(i) */
    //                                                    atlas.MarkPageFlushed(i); any = true; }
    //   atlas.CompleteFlush(any);
    // MaxPages is 16, so iterating every index and skip-checking is at most 16 branch tests.

    /// <summary>The page's pending upload rectangle, if any. Returns false when the page has no
    /// un-uploaded texels.</summary>
    public bool TryGetDirtyRegion(int pageIndex, out DirtyRegion region)
    {
        var page = _pages[pageIndex];
        if (page.DirtyX0 >= page.DirtyX1 || page.DirtyY0 >= page.DirtyY1)
        {
            region = default;
            return false;
        }
        region = new DirtyRegion(page.DirtyX0, page.DirtyY0, page.DirtyX1, page.DirtyY1);
        return true;
    }

    /// <summary>The page's CPU staging pixels (tightly packed <see cref="PageDimension"/>² RGBA,
    /// <see cref="BytesPerTexel"/> per texel). A read-only view over the live buffer — zero copy;
    /// consume it before returning to atlas code (an LRU recycle or evict may overwrite it).</summary>
    public ReadOnlySpan<byte> GetPageStaging(int pageIndex) => _pages[pageIndex].Staging;

    /// <summary>Reset the page's dirty rectangle after the backend uploaded it.</summary>
    public void MarkPageFlushed(int pageIndex)
    {
        var page = _pages[pageIndex];
        page.DirtyX0 = _pageDim; page.DirtyY0 = _pageDim;
        page.DirtyX1 = 0; page.DirtyY1 = 0;
    }

    /// <summary>Call once after the per-page flush loop. A glyph is "unflushed" until its page is
    /// uploaded; the backend flushes every dirty page in one pass, so if any page flushed, all
    /// pages are current and the unflushed set clears in one step (mirrors the pre-split
    /// <c>Flush(cmd)</c> tail exactly).</summary>
    public void CompleteFlush(bool anyPageFlushed)
    {
        if (anyPageFlushed) _unflushedGlyphs.Clear();
    }

    public void Dispose()
    {
        // Caller contract unchanged from the Vulkan-era atlas: the owning renderer ensures the GPU
        // is idle before disposing, so no OnPagesWillBeDestroyed here — backends free each page's
        // resource directly. Descending order keeps an index-mirroring backend consistent.
        for (var i = _pages.Count - 1; i >= 0; i--)
            _backend?.OnPageDestroyed(i);
        _pages.Clear();
    }

    private GlyphInfo RasterizeGlyph(GlyphKey key)
    {
        if (key.Gid == WhitespaceGid)
        {
            var refGlyph = GetGlyph(key.Font, _rasterSize, new Rune('n'));
            var info = new GlyphInfo(0, 0, 0, 0, 0, 0, refGlyph.AdvanceX, 0, 0, SdfSpread);
            _glyphs[key] = info;
            return info;
        }

        // Rasterize by the resolved identity in the key (glyph id, or Type1 glyph name).
        var bitmap = RasterizeByKey(key);
        var glyphInfo = InsertRasterized(key, bitmap);
        // Persist for the next session. AppendGlyph silently skips invalid/empty bitmaps.
        _diskCache?.AppendGlyph(key.Font, key.Gid, key.Name, in bitmap);
        return glyphInfo;
    }

    /// <summary>
    /// Serial atlas-insertion path: per-page cursor placement, staging-buffer blit, _glyphs dict
    /// insert. Caller must already hold the rasterized SDF bitmap. Mutates page state (cursor,
    /// staging, dirty region) + _glyphs/_unflushedGlyphs and may append a new page
    /// — must NOT be called concurrently from multiple threads.
    /// </summary>
    private GlyphInfo InsertRasterized(GlyphKey key, MtsdfGlyphBitmap bitmap)
    {
        _lastInsertSaturated = false;
        var glyphWidth = bitmap.Width;
        var glyphHeight = bitmap.Height;

        if (glyphWidth == 0 || glyphHeight == 0) return default;
        // A glyph bigger than a whole page can never be placed (shouldn't happen at 64px raster).
        if (glyphWidth > _pageDim || glyphHeight > _pageDim) return default;

        var pageIdx = _activePageIdx;
        var page = _pages[pageIdx];

        // Advance to a new row if the glyph overflows the current row.
        if (page.CursorX + glyphWidth > _pageDim)
        {
            page.CursorX = 0;
            page.CursorY += page.RowHeight + 1;
            page.RowHeight = 0;
        }

        // Overflows the page → APPEND A NEW PAGE (no grow, no device drain, no re-upload of
        // existing pages). At the page cap, recycle the least-recently-used page IN PLACE (per-page
        // LRU) instead of wiping all pages — this keeps the hot on-screen working set resident, so a
        // glyph-heavy CJK doc no longer thrashes (EvictAll → refill-all → redraw busy-spin). EvictAll
        // survives only as the all-pages-hot fallback (working set > MaxPages in a single frame).
        if (page.CursorY + glyphHeight > _pageDim)
        {
            if (_pages.Count < _maxPages)
            {
                page = AllocateNewPage();   // sets _activePageIdx
                pageIdx = _activePageIdx;
            }
            else if (TryEvictLruPage(out pageIdx))
            {
                page = _pages[pageIdx];     // cursor reset + _activePageIdx set by TryEvictLruPage
            }
            else if (_refuseWhenSaturated)
            {
                // Fallback-backed tier at its cap with every page hot: refuse the insert. The key is
                // sentineled (below) so lookups HIT (Width==0 → the renderer draws its base-tier
                // fallback) instead of re-queuing a rasterize every frame, and the drains stop
                // retrying via _lastInsertSaturated. Never evict-all here: a working set larger than
                // the cap would wipe + refill the atlas EVERY frame — sustained allocation/upload
                // churn on the exact path that wedges Adreno drivers. Sentinels purge when a page
                // frees up (see TryEvictLruPage), so the tier re-engages once pressure drops.
                if (!_saturationLogged)
                {
                    _saturationLogged = true;
                    AtlasDiag.Log("sdf.pagecap", $"saturated at {_maxPages} pages glyphs={_glyphs.Count} — refusing inserts (base-tier fallback)");
                }
                _lastInsertSaturated = true;
                _glyphs[key] = RefusedSentinel;
                return default;
            }
            else
            {
                // Every page was touched within the last framesInFlight frames — nothing safe to
                // recycle. Caller treats Width=0 as not-yet-available and retries after the fallback
                // EvictAll runs next BeginFrame.
                if (!_needsEviction)
                    AtlasDiag.Log("sdf.pagecap", $"all {_maxPages} pages hot glyphs={_glyphs.Count} — evict-all fallback");
                _needsEviction = true;
                return default;
            }
        }

        // Blit 4-channel MTSDF data into this page's staging buffer (BytesPerTexel per pixel).
        var rowBytes = glyphWidth * BytesPerTexel;
        for (var row = 0; row < glyphHeight; row++)
        {
            var srcOffset = row * rowBytes;
            var dstOffset = ((page.CursorY + row) * _pageDim + page.CursorX) * BytesPerTexel;
            Buffer.BlockCopy(bitmap.Rgba, srcOffset, page.Staging, dstOffset, rowBytes);
        }

        page.DirtyX0 = Math.Min(page.DirtyX0, page.CursorX);
        page.DirtyY0 = Math.Min(page.DirtyY0, page.CursorY);
        page.DirtyX1 = Math.Max(page.DirtyX1, page.CursorX + glyphWidth);
        page.DirtyY1 = Math.Max(page.DirtyY1, page.CursorY + glyphHeight);

        _frameGlyphsInserted++;
        _frameStagedBytes += rowBytes * glyphHeight; // drives the per-frame byte budget + breadcrumb

        // U is page-local (every page shares the same width). V is the VIRTUAL y over the
        // max-pages-tall stack so the page index is recoverable from V alone (see DecodePage).
        var virtDenom = (float)(_maxPages * _pageDim);
        var glyphInfo = new GlyphInfo(
            U0: page.CursorX / (float)_pageDim,
            V0: (pageIdx * _pageDim + page.CursorY) / virtDenom,
            U1: (page.CursorX + glyphWidth) / (float)_pageDim,
            V1: (pageIdx * _pageDim + page.CursorY + glyphHeight) / virtDenom,
            Width: glyphWidth,
            Height: glyphHeight,
            AdvanceX: bitmap.AdvanceX,
            BearingX: bitmap.BearingX,
            BearingY: bitmap.BearingY,
            Spread: bitmap.Spread);

        _glyphs[key] = glyphInfo;
        _unflushedGlyphs.Add(key);
        page.Keys.Add(key);              // for O(page) eviction without scanning all of _glyphs
        page.LastUsedFrame = _frameTick; // freshly written → hot
        page.CursorX += glyphWidth + 1;
        page.RowHeight = Math.Max(page.RowHeight, glyphHeight);
        return glyphInfo;
    }

    /// <summary>
    /// Per-page LRU eviction: when all pages are full, reset the least-recently-used page IN PLACE and
    /// reuse it (keeping every other page's glyphs), rather than wiping the whole atlas. Only a page no
    /// in-flight frame can still be sampling is eligible — its LastUsedFrame must be strictly older than
    /// framesInFlight, which (because the backend's frame loop already waited that slot's fence) means
    /// its GPU work has retired, so the image bytes can be overwritten with no device wait. The stale
    /// pixels below the reset cursor are never sampled again — their glyph keys were removed from
    /// _glyphs — so there's no need to clear the page or re-upload it; new glyphs overwrite from the top.
    /// Returns false when every page is hot (genuinely &gt; MaxPages working set this frame), leaving the
    /// caller to fall back to EvictAll.
    /// </summary>
    private bool TryEvictLruPage(out int pageIdx)
    {
        pageIdx = -1;
        var oldest = long.MaxValue;
        for (var i = 0; i < _pages.Count; i++)
        {
            var lru = _pages[i].LastUsedFrame;
            if (lru >= _frameTick - _framesInFlight) continue; // hot / possibly in-flight
            if (lru < oldest) { oldest = lru; pageIdx = i; }
        }
        if (pageIdx < 0) return false;

        var page = _pages[pageIdx];
        foreach (var k in page.Keys)
        {
            _glyphs.Remove(k);
            _unflushedGlyphs.Remove(k);
        }
        AtlasDiag.Log("sdf.evictpage",
            $"recycled page {pageIdx} (dropped {page.Keys.Count} cold glyphs, {_glyphs.Count} remain)");
        page.Keys.Clear();
        page.CursorX = 0; page.CursorY = 0; page.RowHeight = 0;
        page.LastUsedFrame = _frameTick;
        _activePageIdx = pageIdx;
        PurgeRefusalSentinels();
        return true;
    }

    // A page just freed up — drop the saturation refusal sentinels (Spread < 0; they belong to no
    // page) so those glyphs re-attempt the tier instead of drawing base-tier-soft all session.
    // Only runs on page-recycle events, never per frame; EvictAll clears the whole dict anyway.
    private void PurgeRefusalSentinels()
    {
        if (!_saturationLogged) return; // no refusals since the last purge — skip the dict scan
        _saturationLogged = false;
        List<GlyphKey>? refused = null;
        foreach (var kv in _glyphs)
            if (kv.Value.Spread < 0f) (refused ??= new List<GlyphKey>()).Add(kv.Key);
        if (refused is null) return;
        foreach (var k in refused) _glyphs.Remove(k);
        AtlasDiag.Log("sdf.pagecap", $"purged {refused.Count} refusal sentinel(s) after page recycle");
    }

    /// <summary>
    /// Batch-warms a set of glyph keys with parallel SDF rasterization. Skips keys already
    /// in the atlas. Rasterization (the expensive part — SDF distance-field computation
    /// per glyph pixel) runs across the thread pool via <see cref="Parallel.For"/>; the
    /// resulting bitmaps are then inserted into the atlas serially on the calling thread
    /// (cursor placement and the staging buffer aren't thread-safe). On architectural PDFs
    /// where a page touches 60-100 unique glyphs at ~10 ms each, this cuts prewarm time
    /// from ~700 ms serial to ~200 ms on a 4-core box.
    ///
    /// <para>Thread safety: <see cref="ManagedFontRasterizer"/> backs this via
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> for its font cache and allocates
    /// only a per-call result bitmap, so concurrent rasterization is safe.</para>
    ///
    /// <para>Whitespace glyphs go through the serial path because their info is derived
    /// from a reference glyph ('n'), which requires reentry into <see cref="GetGlyph"/>.</para>
    /// </summary>
    public void PreRasterizeBatch(IReadOnlyList<(string Font, Rune Character, int CharCode, GlyphMapHint Hint)> keys)
    {
        if (keys.Count == 0) return;

        // Phase 0: warm the disk cache for every font referenced in this batch. Each call
        // is idempotent (single load per font per session) and bulk-inserts cached glyphs
        // into _glyphs, so Phase 1's "is it cached?" check will hit them automatically.
        if (_diskCache is not null)
        {
            string? lastFont = null;
            foreach (var (font, _, _, _) in keys)
            {
                if (ReferenceEquals(lastFont, font)) continue; // cheap consecutive-dup skip
                EnsureFontLoadedFromDisk(font);
                lastFont = font;
            }
        }

        // Phase 1: resolve each request to its identity key and claim the ones that need
        // rasterization (not cached, not whitespace, not already queued/in-flight). TryAdd both
        // dedups within this batch AND stakes the key, so the SAME visible-glyph set re-offered next
        // frame doesn't re-rasterize what's already pending. Two requests that resolve to the same
        // glyph now collapse to one key here (the A1 dedup).
        var toRasterize = new List<GlyphKey>();
        foreach (var (font, ch, charCode, hint) in keys)
        {
            if (Rune.IsWhiteSpace(ch)) continue;            // warmed synchronously in Phase 3
            var atlasKey = MakeKey(font, ch, charCode, hint);
            if (_glyphs.ContainsKey(atlasKey)) continue;
            if (!_rasterizeInFlight.TryAdd(atlasKey, 0)) continue;
            toRasterize.Add(atlasKey);
        }

        // Phase 2 (shared with the ByGid batch): rasterize OFF the render thread.
        RasterizeClaimedBatchAsync(toRasterize);

        // Phase 3: whitespace keys are cheap (their info derives from the 'n' reference glyph) and
        // need GetGlyph reentry, so warm them synchronously — there are only a handful per font.
        foreach (var (font, ch, charCode, hint) in keys)
        {
            if (Rune.IsWhiteSpace(ch)) GetGlyph(font, _rasterSize, ch, charCode: charCode, hint: hint);
        }
    }

    /// <summary>
    /// GID-direct variant of <see cref="PreRasterizeBatch"/>: batch-warms pre-resolved glyph
    /// identities (glyph id, or PostScript name for Type1) without the per-key cmap resolve.
    /// There is no whitespace phase — a GID caller passes the real space glyph id, which
    /// rasterizes to an empty bitmap once and is cached like any other glyph (only the codepoint
    /// path uses the WhitespaceGid sentinel + 'n' reference-glyph metrics).
    /// </summary>
    public void PreRasterizeBatchByGid(IReadOnlyList<(string Font, uint Gid, string? Type1Name)> keys)
    {
        if (keys.Count == 0) return;

        // Phase 0: warm the disk cache per referenced font — same as PreRasterizeBatch.
        if (_diskCache is not null)
        {
            string? lastFont = null;
            foreach (var (font, _, _) in keys)
            {
                if (ReferenceEquals(lastFont, font)) continue; // cheap consecutive-dup skip
                EnsureFontLoadedFromDisk(font);
                lastFont = font;
            }
        }

        // Phase 1: keys arrive as resolved identities, so they map straight to atlas keys —
        // no MakeKey / ResolveGlyphIdentity. TryAdd dedups within the batch AND stakes the key
        // against re-offers on later frames, exactly like the codepoint batch.
        var toRasterize = new List<GlyphKey>();
        foreach (var (font, gid, name) in keys)
        {
            var atlasKey = new GlyphKey(font, _rasterSize, gid, name);
            if (_glyphs.ContainsKey(atlasKey)) continue;
            if (!_rasterizeInFlight.TryAdd(atlasKey, 0)) continue;
            toRasterize.Add(atlasKey);
        }

        RasterizeClaimedBatchAsync(toRasterize);
    }

    // Phase 2 behind both batch entries: rasterize OFF the render thread. SDF distance-field
    // computation is the ~10ms/glyph cost; running it inline here was the multi-second frame stall
    // on glyph-heavy (CJK) pages. One background task rasterizes the whole batch in parallel
    // (ManagedFontRasterizer is documented thread-safe — it only touches a ConcurrentDictionary
    // font cache + a per-call result bitmap) and enqueues each finished bitmap to
    // _pendingRasterized. The render thread inserts them bounded-per-frame in BeginFrame ->
    // DrainPendingRasterized; IsDirty stays true until the queue empties, so the loop keeps
    // redrawing and glyphs fill in progressively. Disk persistence happens at insertion time,
    // not here. Caller must have claimed every key in _rasterizeInFlight.
    // On a synchronous host the batch rasterizes inline, sequentially — there is no thread pool
    // to fan out to, and no render loop being stalled.
    private void RasterizeClaimedBatchAsync(List<GlyphKey> toRasterize)
    {
        if (toRasterize.Count == 0) return;
        var work = toRasterize;
        if (_synchronousRasterize)
        {
            foreach (var atlasKey in work)
                RasterizeClaimedKey(atlasKey);
            return;
        }
        Task.Run(() => Parallel.For(0, work.Count, i =>
        {
            var atlasKey = work[i];
            try
            {
                _pendingRasterized.Enqueue((atlasKey, RasterizeByKey(atlasKey)));
            }
            catch (Exception ex)
            {
                // Release the claim so a later frame can retry; otherwise the glyph never appears.
                _rasterizeInFlight.TryRemove(atlasKey, out _);
                Console.Error.WriteLine($"[SdfAtlas] batch rasterize failed for {DescribeKey(atlasKey)}: {ex.Message}");
            }
        }));
    }

    private void EvictAll()
    {
        AtlasDiag.Log("sdf.evict", $"wiping {_glyphs.Count} glyphs across {_pages.Count} page(s)");
        _glyphs.Clear();
        _saturationLogged = false; // refusal sentinels went with the dict — re-arm the episode log
        // Drop pending async rasterization too. A background task still running may enqueue a stale
        // result afterwards — harmless: DrainPendingRasterized re-checks _glyphs and the key isn't
        // claimed anymore, so at worst the glyph is re-rasterized once. Re-requested on next draw.
        _pendingRasterized.Clear();
        _rasterizeInFlight.Clear();
        // Clear the disk-loaded guard too: after eviction the next use of a font should RE-LOAD its
        // cached glyphs from disk (cheap bulk read) instead of re-rasterizing each (~10ms) AND
        // re-appending them as duplicates. Leaving this set meant every eviction cycle re-rasterized
        // every glyph and grew the .sdfg file — the source of the observed 2.7× disk duplication and
        // the repeated large atlas flushes that fragment the LOH.
        _diskLoadedFonts.Clear();

        // Destroy every extra page (1..N-1); page 0 is reset in place. Those pages' GPU resources may
        // still be referenced by the previous frame's draws, so the backend gets one WillBeDestroyed
        // hook first — where a frames-in-flight backend (Vulkan) waits for prior in-flight frames —
        // and this is the ONLY remaining drain, firing only when all MaxPages are full (≈never).
        // Descending destroy order keeps an index-mirroring backend consistent (RemoveAt parity).
        if (_pages.Count > 1)
        {
            _backend?.OnPagesWillBeDestroyed();
            for (var i = _pages.Count - 1; i >= 1; i--)
            {
                _backend?.OnPageDestroyed(i);
                _pages.RemoveAt(i);
            }
        }

        // Reset page 0 in place: clear its staging + cursor and mark it fully dirty so the next flush
        // re-uploads the cleared pixels. Its GPU resource is already valid (no re-allocation needed).
        var p0 = _pages[0];
        p0.CursorX = 0; p0.CursorY = 0; p0.RowHeight = 0;
        p0.Keys.Clear();
        p0.LastUsedFrame = _frameTick;
        Array.Clear(p0.Staging, 0, p0.Staging.Length);
        p0.DirtyX0 = 0; p0.DirtyY0 = 0;
        p0.DirtyX1 = _pageDim; p0.DirtyY1 = _pageDim;
        _activePageIdx = 0;
    }
}
