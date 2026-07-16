namespace DIR.Lib;

/// <summary>
/// Backend hook contract for <see cref="SdfFontAtlas"/>. Implemented once by whatever rendering
/// backend owns the GPU-side resource for an atlas page (Vulkan: image + view + descriptor set;
/// WebGL: a texture object). All hooks run synchronously, on the caller's thread, inline with the
/// <see cref="SdfFontAtlas"/> call that triggered them (the constructor's first page, BeginFrame's
/// evict-all fallback, InsertRasterized's page-cap append, or Dispose) — never from a background task.
/// </summary>
public interface ISdfAtlasBackend
{
    /// <summary>A new page was just appended; <paramref name="pageIndex"/> equals
    /// <c>PageCount - 1</c> at the time of the call. Allocate whatever GPU resource this page
    /// needs now: <paramref name="pageDimension"/>² texels. The dimension is passed (rather than
    /// read off the atlas) because this fires inline from the <see cref="SdfFontAtlas"/>
    /// constructor for page 0 — before the backend's reference to the core can even be assigned.
    /// The backend must have its own prerequisites (samplers, registries) ready before
    /// constructing the core.</summary>
    void OnPageCreated(int pageIndex, int pageDimension);

    /// <summary>Fired exactly once per evict-all, BEFORE any <see cref="OnPageDestroyed"/> call in
    /// that batch — the seam a frames-in-flight backend (Vulkan) uses to wait for prior in-flight
    /// frames to retire before freeing a resource they might still be sampling. NOT called from
    /// <see cref="SdfFontAtlas.Dispose"/> (that contract already requires the caller to have idled
    /// the GPU first). A backend with no frame pipelining (WebGL) no-ops this.</summary>
    void OnPagesWillBeDestroyed();

    /// <summary>Free the GPU resource <see cref="OnPageCreated"/> allocated for
    /// <paramref name="pageIndex"/>. Per-page LRU recycling never calls this — a recycled page
    /// reuses its resource in place. Within one teardown batch (evict-all's pages 1..N-1, or
    /// Dispose's full 0..N-1) this is always invoked in DESCENDING index order, so an
    /// implementation that mirrors the core's page list with a plain <c>List&lt;T&gt;</c> +
    /// <c>RemoveAt(pageIndex)</c> stays index-consistent with the core without extra bookkeeping.</summary>
    void OnPageDestroyed(int pageIndex);
}
