namespace DIR.Lib
{
    /// <summary>
    /// A run of selectable text registered during a render pass via
    /// <see cref="PixelWidgetBase{TSurface}.DrawSelectableText"/>. Backends interpret it differently:
    /// a DOM host (web) overlays a real, selectable <c>&lt;span&gt;</c> over the rect; a terminal host can
    /// register it for native drag-select / OSC-52 yank; a GPU host that rasters its own glyphs (the
    /// default) may simply ignore it.
    /// <para>
    /// <see cref="X"/>/<see cref="Y"/>/<see cref="Width"/>/<see cref="Height"/> are in backing-buffer
    /// pixels -- the same coordinate space as <see cref="ClickableRegion"/> -- so a host converts to CSS
    /// pixels by dividing by the device-pixel-ratio, exactly as it already does for clickable regions.
    /// </para>
    /// <para>
    /// <see cref="Href"/>, when non-null, marks the run as a hyperlink: a DOM host renders it as a real
    /// <c>&lt;a href&gt;</c> instead of a plain span (so the browser handles new-tab/open/copy-link
    /// natively). A raster host has no navigation model and ignores it -- the run still draws as ordinary
    /// text -- so a link is a progressive enhancement that only the web host acts on.
    /// </para>
    /// <para>
    /// Two things set it. An immediate-mode widget passes <c>href:</c> to
    /// <see cref="PixelWidgetBase{TSurface}.DrawSelectableText"/>. A LAYOUT TREE states a
    /// <see cref="HitResult.LinkHit"/> on the node instead, and <c>PaintLayout</c> routes the text under it
    /// through this same region (7.7+) -- which is what lets one authored tree be a real anchor on the web
    /// and a real OSC 8 hyperlink on a terminal. Only LINKED layout text takes that route; ordinary layout
    /// text stays on <c>DrawText</c> and never reaches the host's selection layer.
    /// </para>
    /// </summary>
    public readonly record struct SelectableTextRegion(
        float X, float Y, float Width, float Height,
        string Text,
        string FontPath,
        float FontSize,
        RGBAColor32 Color,
        TextAlign HorizontalAlign,
        TextAlign VerticalAlign,
        string? Href = null);
}
