namespace DIR.Lib.MathLayout;

/// <summary>
/// Box → <see cref="RgbaImage"/> rasterizer. Allocates a buffer just large
/// enough for the supplied <see cref="Box"/> (plus a small em-proportional
/// margin), draws the box into it, and hands the image back to the caller.
/// Pure — no terminal / sixel / VT-escape concerns; the encoder choice
/// (PNG, JPEG, TIFF, sixel, half-block...) is the caller's. Console-output
/// paths live separately in <c>Console.Lib.BoxRenderer</c>.
/// </summary>
public static class BoxRasterizer
{
    /// <summary>
    /// Rasterize <paramref name="box"/> at <paramref name="style"/> into a
    /// transparent 8-bit RGBA buffer and return the resulting image. An
    /// empty (0×0) <see cref="RgbaImage"/> is returned when the box would
    /// rasterize to zero area; callers should check <c>image.Width &gt; 0
    /// &amp;&amp; image.Height &gt; 0</c> before consuming pixel data.
    /// </summary>
    public static RgbaImage RenderToRgba(Box box, BoxStyle style)
    {
        int margin = (int)MathF.Ceiling(style.FontSize * 0.15f);
        int totalW = (int)MathF.Ceiling(box.Width) + margin * 2;
        int totalH = (int)MathF.Ceiling(box.TotalHeight) + margin * 2;
        if (totalW <= 0 || totalH <= 0) return new RgbaImage(0, 0);

        // Buffer starts transparent (RGBA 0,0,0,0). Box.Draw paints on top
        // of it; transparency-aware downstream encoders (BoxRenderer's
        // half-block / sextant, the SixelEncoder's RGBA path, or anything
        // that respects the alpha channel) will leave un-painted pixels
        // showing through the surrounding background.
        //
        // The renderer draws with the shared rasterizer the box tree was laid
        // out with, which keeps its parsed fonts and its glyph rasters across
        // renders. A renderer of its own re-read and re-parsed the font file on
        // every call (808 KB for STIX Two Math, most of a render's time) and
        // rasterized every glyph again.
        using var renderer = new RgbaImageRenderer((uint)totalW, (uint)totalH, BoxStyle.SharedRasterizer);
        float baselineY = margin + box.Height;
        box.Draw(renderer, margin, baselineY, style);

        // Renderer.Dispose() only clears its own per-render glyph table (the
        // shared rasterizer is lent, not owned); it does not touch Surface or
        // Surface.Pixels (both are managed and GC-tracked). Returning the live
        // Surface skips the prior defensive copy with no ownership risk.
        return renderer.Surface;
    }
}
