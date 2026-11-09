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
    public void RegisterPdfEncoding_InvalidatesMemoizedIdentities()
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
        rasterizer.RegisterPdfEncoding(t1Id, new Dictionary<int, string> { ['A'] = "B" });
        var overridden = rasterizer.ResolveGlyphIdentity(t1Id, new Rune('A'), charCode: 'A', GlyphMapHint.Auto);
        Assert.Equal("B", overridden.Type1Name);
    }

    [Fact]
    public void RegisteredPdfEncoding_ResolvesByName_ForBareCff()
    {
        // The Canon-manual Lithos-Bold subset: a bare name-keyed CFF with no cmap and no CFF
        // Encoding operator, which makes its built-in encoding StandardEncoding. Before
        // SharpAstro.Fonts 1.17 the built-in encoding was never read and the only strategy left
        // was charCode-as-GID, which selected glyph 49 ('W') for code 0x31 ('1'); now the code goes
        // through StandardEncoding to /one, gid 15, as pdfium (FreeType) selects it. The registered
        // map below DELIBERATELY differs from StandardEncoding (0x31 -> "two", not "one"), so only
        // the name route can produce its answer and only a dropped memo can show it.
        using var rasterizer = new ManagedFontRasterizer();
        const string fontId = "mem:lithos-bold-subset";
        Assert.True(rasterizer.RegisterFontFromMemory(fontId,
            File.ReadAllBytes(Path.Combine("Fonts", "LithosBold_subset.cff"))));

        var bare = rasterizer.ResolveGlyphIdentity(fontId, new Rune('1'), charCode: 0x31, GlyphMapHint.EmbeddedSubset);
        Assert.Equal(15u, bare.Gid); // the font's own (Standard) encoding, no longer charCode-as-GID

        rasterizer.RegisterPdfEncoding(fontId, new Dictionary<int, string>
        {
            [0x31] = "two", [0x32] = "three", [0x33] = "one",
        });

        // Registration must invalidate the memoized identity above, and the name route wins: every
        // code now resolves to a different glyph than the built-in encoding gave it (15, 16, 17).
        var named = rasterizer.ResolveGlyphIdentity(fontId, new Rune('1'), charCode: 0x31, GlyphMapHint.EmbeddedSubset);
        Assert.Equal(16u, named.Gid);
        Assert.Equal(17u, rasterizer.ResolveGlyphIdentity(fontId, new Rune('2'), charCode: 0x32, GlyphMapHint.EmbeddedSubset).Gid);
        Assert.Equal(15u, rasterizer.ResolveGlyphIdentity(fontId, new Rune('3'), charCode: 0x33, GlyphMapHint.EmbeddedSubset).Gid);

        // And the identity actually rasterizes — the glyph exists and has ink.
        var bmp = rasterizer.RasterizeGlyphMtsdfByGid(fontId, 48f, named.Gid);
        Assert.True(bmp.Width > 0 && bmp.Height > 0, "named glyph must produce ink");
    }

    [Fact]
    public void RegisteredPdfEncoding_UnknownName_FallsBackToBuiltInEncoding()
    {
        // A name the font doesn't carry must not blank the glyph — resolution falls through to
        // the strategies used when no encoding is registered: the font's built-in encoding first
        // (StandardEncoding here, /one, gid 15), and only then charCode-as-GID.
        using var rasterizer = new ManagedFontRasterizer();
        const string fontId = "mem:lithos-bold-unknown-name";
        Assert.True(rasterizer.RegisterFontFromMemory(fontId,
            File.ReadAllBytes(Path.Combine("Fonts", "LithosBold_subset.cff"))));
        rasterizer.RegisterPdfEncoding(fontId, new Dictionary<int, string> { [0x31] = "nosuchglyph" });

        var id = rasterizer.ResolveGlyphIdentity(fontId, new Rune('1'), charCode: 0x31, GlyphMapHint.EmbeddedSubset);
        Assert.Equal(15u, id.Gid); // the built-in encoding again
    }
}
