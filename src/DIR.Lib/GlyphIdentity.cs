namespace DIR.Lib;

/// <summary>
/// The stable identity of a glyph within one font, for keying a glyph atlas / cache.
///
/// <para>OpenType (TrueType/CFF) glyphs are identified by their numeric glyph id;
/// <see cref="Type1Name"/> is then <c>null</c>. Type1/PFB fonts have no numeric glyph ids
/// — glyphs are addressed by PostScript glyph name — so those resolve to a
/// <see cref="Type1Name"/> with <see cref="Gid"/> left 0. A resolution miss (unmapped
/// codepoint, or a font that isn't loadable yet) yields the default identity
/// (<see cref="Gid"/> 0, <see cref="Type1Name"/> null), which rasterizes to nothing —
/// matching the empty-bitmap behaviour of the by-gid / by-name rasterize paths.</para>
///
/// <para>Produced by <see cref="ManagedFontRasterizer.ResolveGlyphIdentity"/>; consumed by
/// <see cref="ManagedFontRasterizer.RasterizeGlyphMtsdfByGid"/> /
/// <see cref="ManagedFontRasterizer.RasterizeGlyphMtsdfByType1Name"/> (and their color-path
/// siblings). This is the seam that lets a caller resolve codepoint→identity once and then
/// key/rasterize by identity — the substrate a text shaper (which emits glyph ids) plugs into.</para>
/// </summary>
public readonly record struct GlyphIdentity(uint Gid, string? Type1Name)
{
    /// <summary>True for a Type1/PFB glyph (addressed by name, not id).</summary>
    public bool IsType1 => Type1Name is not null;
}
