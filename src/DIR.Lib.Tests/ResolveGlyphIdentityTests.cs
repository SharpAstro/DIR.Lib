using System.Text;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// A1 contract for <see cref="ManagedFontRasterizer.ResolveGlyphIdentity"/>: it surfaces the
/// exact identity step the rasterize methods perform internally, so resolving a codepoint to a
/// glyph id and then rasterizing <em>by that id</em> is byte-for-byte identical to rasterizing
/// by the codepoint. That invariant is what lets a glyph atlas re-key by glyph id (Track A)
/// without changing a single rendered pixel.
/// </summary>
public class ResolveGlyphIdentityTests
{
    private static readonly string FontPath = Path.Combine("Fonts", "DejaVuSans.ttf");

    [Fact]
    public void ResolveThenRasterizeByGid_MatchesRasterizeByCodepoint()
    {
        using var rasterizer = new ManagedFontRasterizer();
        var id = rasterizer.ResolveGlyphIdentity(FontPath, new Rune('A'), charCode: -1, GlyphMapHint.Auto);

        Assert.True(id.Gid > 0, "expected a real glyph id for 'A'");
        Assert.Null(id.Type1Name);   // OpenType → keyed by gid, not name
        Assert.False(id.IsType1);

        var byGid = rasterizer.RasterizeGlyphMtsdfByGid(FontPath, 48f, id.Gid);
        var byCodepoint = rasterizer.RasterizeGlyphMtsdf(FontPath, 48f, new Rune('A'));

        Assert.Equal(byCodepoint.Width, byGid.Width);
        Assert.Equal(byCodepoint.Height, byGid.Height);
        Assert.Equal(byCodepoint.Rgba, byGid.Rgba);   // identical pixels — resolution unchanged
    }

    [Fact]
    public void ResolveGlyphIdentity_UnmappedCodepoint_IsMiss()
    {
        using var rasterizer = new ManagedFontRasterizer();
        // A codepoint a normal text font doesn't cover → notdef. Keys to gid 0 (the shared blank
        // entry), never a throw — the draw path degrades to drawing nothing.
        var id = rasterizer.ResolveGlyphIdentity(FontPath, new Rune(0x10FFFF), charCode: -1, GlyphMapHint.Auto);
        Assert.Equal(0u, id.Gid);
        Assert.Null(id.Type1Name);
    }
}
