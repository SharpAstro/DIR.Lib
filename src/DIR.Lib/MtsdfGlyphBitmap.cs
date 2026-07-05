namespace DIR.Lib;

/// <summary>
/// Multi-channel (MTSDF) distance-field bitmap of a rasterized glyph. Four bytes
/// per pixel (R, G, B, A), row-major, top-down. R/G/B carry the per-channel
/// signed pseudo-distance that keeps corners sharp; A carries the plain true
/// signed distance. In every channel 128 = on edge, &gt;128 = inside, &lt;128 =
/// outside — a fragment shader reconstructs the edge from <c>median(r, g, b)</c>
/// and can read A independently (outline / glow / weight).
///
/// <para>The A channel uses the same ±<see cref="Spread"/> → [0,1] encoding as
/// <see cref="SdfGlyphBitmap"/>, so it is a drop-in for the single-channel field.
/// BearingX/BearingY follow the same convention as <see cref="GlyphBitmap"/>.</para>
/// </summary>
public readonly record struct MtsdfGlyphBitmap(byte[] Rgba, int Width, int Height, int BearingX, int BearingY, float AdvanceX, float Spread);
