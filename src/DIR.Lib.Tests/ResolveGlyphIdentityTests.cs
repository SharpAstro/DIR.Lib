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

    [Fact]
    public void ResolveGlyphIdentity_MissIsNotMemoized_ResolvesAfterRegistration()
    {
        using var rasterizer = new ManagedFontRasterizer();
        const string memId = "mem:memo-race-test";

        // Unregistered memory font: the resolve misses (a PDF consumer's parse thread can race
        // the first draw's resolve)...
        var miss = rasterizer.ResolveGlyphIdentity(memId, new Rune('A'), charCode: -1, GlyphMapHint.Auto);
        Assert.Equal(0u, miss.Gid);
        Assert.Null(miss.Type1Name);

        // ...and the miss must NOT be memoized: once the bytes land, the same key resolves to a
        // real glyph. A memoized miss would pin this font's glyphs blank for the whole session.
        Assert.True(rasterizer.RegisterFontFromMemory(memId, File.ReadAllBytes(FontPath)));
        var hit = rasterizer.ResolveGlyphIdentity(memId, new Rune('A'), charCode: -1, GlyphMapHint.Auto);
        Assert.True(hit.Gid > 0, "post-registration resolve must yield a real glyph id");
    }

    [Fact]
    public void RegisterType1Encoding_InvalidatesMemoizedIdentities()
    {
        using var rasterizer = new ManagedFontRasterizer();
        const string t1Id = "mem:memo-t1-test";
        Assert.True(rasterizer.RegisterFontFromMemory(t1Id, File.ReadAllBytes(Path.Combine("Fonts", "cmr10.pfb"))));

        // Resolve a code through the font's built-in encoding first — this memoizes the name...
        var builtIn = rasterizer.ResolveGlyphIdentity(t1Id, new Rune('A'), charCode: 'A', GlyphMapHint.Auto);
        Assert.True(builtIn.IsType1);
        Assert.Equal("A", builtIn.Type1Name);

        // ...then install a /Differences override remapping the code. The override must win on the
        // next resolve; a stale memo entry would keep serving the built-in name.
        rasterizer.RegisterType1Encoding(t1Id, new Dictionary<int, string> { ['A'] = "B" });
        var overridden = rasterizer.ResolveGlyphIdentity(t1Id, new Rune('A'), charCode: 'A', GlyphMapHint.Auto);
        Assert.Equal("B", overridden.Type1Name);
    }
}
