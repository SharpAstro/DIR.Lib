namespace DIR.Lib;

/// <summary>
/// Single-channel SDF bitmap of a rasterized glyph.
/// Alpha contains distance field data where 128 = on edge, >128 = inside, &lt;128 = outside.
/// BearingX/BearingY follow the same convention as <see cref="GlyphBitmap"/>.
/// </summary>
public readonly record struct SdfGlyphBitmap(byte[] Alpha, int Width, int Height, int BearingX, int BearingY, float AdvanceX, float Spread);
