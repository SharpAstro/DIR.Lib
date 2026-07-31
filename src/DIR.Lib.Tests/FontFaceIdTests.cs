using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

public sealed class FontFaceIdTests
{
    /// <summary>
    /// Face 0 must produce the bare path. Every id minted for a single-face font is then
    /// byte-identical to the path it has always been, so no cache key anywhere changes.
    /// </summary>
    [Fact]
    public void Create_FaceZero_IsThePathUnchanged()
    {
        FontFaceId.Create(@"C:\Windows\Fonts\segoeui.ttf", 0).ShouldBe(@"C:\Windows\Fonts\segoeui.ttf");
        FontFaceId.Create("/usr/share/fonts/DejaVuSans.ttf", 0).ShouldBe("/usr/share/fonts/DejaVuSans.ttf");
        // A negative index is nonsense; treat it as face 0 rather than minting "path#-1".
        FontFaceId.Create("/f.ttf", -3).ShouldBe("/f.ttf");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(17)]
    public void Create_And_TryParse_RoundTrip(int faceIndex)
    {
        const string Path = @"C:\Windows\Fonts\cambria.ttc";
        var id = FontFaceId.Create(Path, faceIndex);

        FontFaceId.TryParse(id, out var path, out var parsed).ShouldBeTrue();
        path.ShouldBe(Path);
        parsed.ShouldBe(faceIndex);
    }

    /// <summary>
    /// A plain path carries no face suffix. The outputs must still be usable — path is the id,
    /// index 0 — so callers can ignore the bool and use them unconditionally.
    /// </summary>
    [Fact]
    public void TryParse_PlainPath_ReportsFalseButUsableOutputs()
    {
        FontFaceId.TryParse(@"C:\Windows\Fonts\segoeui.ttf", out var path, out var index).ShouldBeFalse();
        path.ShouldBe(@"C:\Windows\Fonts\segoeui.ttf");
        index.ShouldBe(0);
    }

    /// <summary>
    /// "#0" is redundant. It must normalize to the bare path, or one face would occupy two cache
    /// entries — and each entry costs a separately-rasterized glyph atlas.
    /// </summary>
    [Fact]
    public void TryParse_ExplicitFaceZero_NormalizesToPath()
    {
        FontFaceId.TryParse("/fonts/x.ttc#0", out var path, out var index).ShouldBeFalse();
        path.ShouldBe("/fonts/x.ttc");
        index.ShouldBe(0);
    }

    /// <summary>
    /// A '#' in a font's own file name must not be read as a face suffix. Only an all-digit,
    /// short tail after the last '#' qualifies.
    /// </summary>
    [Theory]
    [InlineData(@"C:\fonts\my#font.ttf")]
    [InlineData(@"C:\fonts\track#1.ttf")]     // digits, but followed by an extension
    [InlineData("/fonts/#.ttf")]
    [InlineData("/fonts/x.ttf#")]             // nothing after the separator
    [InlineData("/fonts/x.ttf#12345")]        // too many digits to be a face index
    [InlineData("/fonts/x.ttf#a")]
    [InlineData("/fonts/x.ttf#-1")]
    [InlineData("#3")]                        // no path before the separator
    public void TryParse_NonFaceSuffixes_AreLeftAlone(string id)
    {
        FontFaceId.TryParse(id, out var path, out var index).ShouldBeFalse();
        path.ShouldBe(id);
        index.ShouldBe(0);
    }
}
