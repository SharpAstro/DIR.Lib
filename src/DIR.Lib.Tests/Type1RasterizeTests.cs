using System.Text;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The embedded-Type1 (/FontFile, PFB) glyph path in <see cref="ManagedFontRasterizer"/> plus the PDF
/// <c>/Encoding /Differences</c> overlay (<see cref="ManagedFontRasterizer.RegisterType1Encoding"/>).
/// This is the seam behind the LaTeX / Computer-Modern "De nition" bug: without it the whole doc falls
/// back to a Latin font and the f-ligatures blank. cmr10.pfb is a small, freely-redistributable fixture.
/// </summary>
public class Type1RasterizeTests
{
    private const string FontId = "mem:cmr10";

    private static ManagedFontRasterizer WithCmr10()
    {
        var r = new ManagedFontRasterizer();
        r.RegisterFontFromMemory(FontId, File.ReadAllBytes(Path.Combine("Fonts", "cmr10.pfb"))).ShouldBeTrue();
        return r;
    }

    private static bool HasInk(GlyphBitmap bmp) =>
        bmp.Rgba is not null && bmp.Rgba.Where((_, i) => i % 4 == 3).Any(a => a > 0);

    [Fact]
    public void RegisterFromMemory_DetectsType1_AndRasterizesLigatureByName()
    {
        using var r = WithCmr10();

        var fi = r.RasterizeGlyphByType1Name(FontId, 64f, "fi");
        fi.Width.ShouldBeGreaterThan(0);
        fi.Height.ShouldBeGreaterThan(0);
        HasInk(fi).ShouldBeTrue("the 'fi' ligature must carry real ink, not render blank");
    }

    [Fact]
    public void RasterizeByType1Name_UnknownGlyph_ReturnsEmpty()
    {
        using var r = WithCmr10();
        // default(GlyphBitmap): a name the font doesn't have renders nothing, not a notdef box.
        r.RasterizeGlyphByType1Name(FontId, 64f, "no_such_glyph_xyz").Width.ShouldBe(0);
    }

    [Fact]
    public void RasterizeByType1Name_NonType1Font_ReturnsEmpty()
    {
        using var r = new ManagedFontRasterizer();
        // DejaVuSans is SFNT, not Type1 — the by-name path only serves _type1Fonts, so it must no-op.
        r.RasterizeGlyphByType1Name(Path.Combine("Fonts", "DejaVuSans.ttf"), 64f, "fi").Width.ShouldBe(0);
    }

    [Fact]
    public void Type1Encoding_DifferencesOverride_WinsOverBuiltIn()
    {
        using var r = WithCmr10();
        const int code = 65;   // cmr10's built-in encoding maps 65 -> "A"

        var baseName = r.ResolveGlyphIdentity(FontId, new Rune('A'), code, GlyphMapHint.Auto).Type1Name;
        baseName.ShouldNotBeNull();
        baseName.ShouldNotBe("fi");

        // A PDF /Differences [65 /fi] remaps the code; the override must win over the built-in encoding.
        r.RegisterType1Encoding(FontId, new Dictionary<int, string> { [code] = "fi" });
        r.ResolveGlyphIdentity(FontId, new Rune('A'), code, GlyphMapHint.Auto).Type1Name.ShouldBe("fi");
    }

    [Fact]
    public void Type1Encoding_DifferencesNamingAbsentGlyph_FallsBackToBuiltIn()
    {
        using var r = WithCmr10();
        const int code = 65;

        var baseName = r.ResolveGlyphIdentity(FontId, new Rune('A'), code, GlyphMapHint.Auto).Type1Name;
        baseName.ShouldNotBeNull();

        // An override naming a glyph the font lacks must fall back to the built-in name — resolving to
        // the missing name would render blank, the exact failure this overlay exists to prevent.
        r.RegisterType1Encoding(FontId, new Dictionary<int, string> { [code] = "glyph_not_in_cmr10" });
        r.ResolveGlyphIdentity(FontId, new Rune('A'), code, GlyphMapHint.Auto).Type1Name.ShouldBe(baseName);
    }

    [Fact]
    public void ResolveGlyphIdentity_Type1Font_IsNameKeyedNotGid()
    {
        using var r = WithCmr10();
        // Type1 identity is a PostScript glyph name (Gid is meaningless); OpenType is the reverse.
        var id = r.ResolveGlyphIdentity(FontId, new Rune('A'), 65, GlyphMapHint.Auto);
        id.IsType1.ShouldBeTrue();
        id.Type1Name.ShouldNotBeNull();
        id.Gid.ShouldBe(0u);
    }
}
