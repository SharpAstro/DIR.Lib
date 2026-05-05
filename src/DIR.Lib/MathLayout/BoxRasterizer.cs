namespace DIR.Lib.MathLayout;

/// <summary>
/// Box → RGBA buffer rasterizer. Allocates a fresh <see cref="RgbaImage"/>
/// just large enough for the supplied <see cref="Box"/> (plus a small em-
/// proportional margin), draws the box into it, and returns the pixels.
/// Pure — no terminal / sixel / VT-escape concerns. Console-output paths
/// live separately in <c>Console.Lib.BoxRenderer</c>.
/// </summary>
public static class BoxRasterizer
{
    /// <summary>
    /// Rasterize <paramref name="box"/> at <paramref name="style"/> into a
    /// transparent 8-bit RGBA buffer (row-major, no padding) and return it
    /// along with its dimensions.
    /// </summary>
    public static (byte[] Rgba, int Width, int Height) RenderToRgba(Box box, BoxStyle style)
    {
        int margin = (int)MathF.Ceiling(style.FontSize * 0.15f);
        int totalW = (int)MathF.Ceiling(box.Width) + margin * 2;
        int totalH = (int)MathF.Ceiling(box.TotalHeight) + margin * 2;
        if (totalW <= 0 || totalH <= 0) return ([], 0, 0);

        // Buffer starts transparent (RGBA 0,0,0,0). Box.Draw paints on top
        // of it; transparency-aware downstream encoders (BoxRenderer's
        // half-block / sextant, the SixelEncoder's RGBA path, or anything
        // that respects the alpha channel) will leave un-painted pixels
        // showing through the surrounding background.
        using var renderer = new RgbaImageRenderer((uint)totalW, (uint)totalH);
        float baselineY = margin + box.Height;
        box.Draw(renderer, margin, baselineY, style);

        // Surface.Pixels is owned by the renderer and freed on dispose;
        // copy into a stable array before returning to the caller.
        var pixels = renderer.Surface.Pixels;
        var copy = new byte[pixels.Length];
        Buffer.BlockCopy(pixels, 0, copy, 0, pixels.Length);
        return (copy, totalW, totalH);
    }

    /// <summary>
    /// Rasterize <paramref name="box"/> and encode the result as a PNG,
    /// returning the file bytes. Returns an empty array if the box would
    /// rasterize to zero area.
    /// </summary>
    public static byte[] RenderToPng(Box box, BoxStyle style)
    {
        var (rgba, w, h) = RenderToRgba(box, style);
        if (w == 0 || h == 0) return [];
        return PngWriter.Encode(rgba, w, h);
    }
}
