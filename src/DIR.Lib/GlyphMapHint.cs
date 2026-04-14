namespace DIR.Lib;

/// <summary>
/// Hints for how to map a PDF charCode to a glyph index in the rasterizer.
/// PDF fonts use different encoding strategies; this picks the cmap-lookup
/// order that fits the font in question. Value layout matches
/// SharpAstro.Fonts.Tables.Cmap.GlyphMapHint so the cast in
/// <see cref="ManagedFontRasterizer"/> is a no-op at runtime.
/// </summary>
public enum GlyphMapHint
{
    /// <summary>Try all strategies: Unicode → Symbol → Mac Roman → charCode → direct GID.</summary>
    Auto = 0,
    /// <summary>Embedded subset font: Unicode → Symbol PUA → direct GID. Skips Mac Roman
    /// (which often maps charCodes to wrong GIDs in subset fonts).</summary>
    EmbeddedSubset,
    /// <summary>CharCode is the glyph index directly (Identity CIDToGIDMap, custom subset encoding).</summary>
    CharCodeIsGID,
    /// <summary>Standard encoding (WinAnsi/MacRoman) — Unicode cmap is reliable.</summary>
    Unicode,
}
