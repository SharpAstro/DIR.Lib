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
    /// </summary>
    public readonly record struct SelectableTextRegion(
        float X, float Y, float Width, float Height,
        string Text,
        string FontPath,
        float FontSize,
        RGBAColor32 Color,
        TextAlign HorizontalAlign,
        TextAlign VerticalAlign);
}
