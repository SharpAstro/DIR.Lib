namespace DIR.Lib;

/// <summary>
/// Rasterizes individual glyphs into RGBA pixel data.
/// Consumers provide an implementation backed by their preferred image library.
/// </summary>
public interface IGlyphRasterizer
{
    GlyphBitmap RasterizeGlyph(string fontPath, float fontSize, char character);
}

/// <summary>
/// Raw RGBA bitmap of a single rasterized glyph.
/// </summary>
public readonly record struct GlyphBitmap(byte[] Rgba, int Width, int Height);
