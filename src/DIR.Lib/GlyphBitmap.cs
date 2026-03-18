namespace DIR.Lib;

/// <summary>
/// Raw RGBA bitmap of a single rasterized glyph.
/// BearingX is the horizontal distance from the pen position to the left edge of the glyph (FreeType bitmap_left).
/// BearingY is the distance from the baseline to the top of the glyph (FreeType bitmap_top).
/// IsColored is true for COLR/CBDT/SVG color glyphs — these have embedded colors and should not be tinted.
/// </summary>
public readonly record struct GlyphBitmap(byte[] Rgba, int Width, int Height, int BearingX, int BearingY, float AdvanceX, bool IsColored = false);
