using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

public sealed class FontResolverTests
{
    [Fact]
    public void ResolveSystemFont_Returns_EmptyOrExistingFile()
    {
        var path = FontResolver.ResolveSystemFont();
        // Either nothing was found (empty) or the returned path exists on disk.
        if (path.Length > 0)
            File.Exists(path).ShouldBeTrue($"resolver returned non-empty path \"{path}\" but no file exists there");
    }

    [Fact]
    public void ResolveSystemFont_DoesNotThrow_OnAnyOs()
    {
        Should.NotThrow(() => FontResolver.ResolveSystemFont());
    }

    [Fact]
    public void FontDirectories_IncludesPlatformExpected()
    {
        var dirs = FontResolver.FontDirectories.ToList();
        dirs.ShouldNotBeEmpty();
        if (OperatingSystem.IsWindows())
        {
            // System dir present.
            dirs.ShouldContain(d => d.Equals(@"C:\Windows\Fonts", StringComparison.OrdinalIgnoreCase));
            // Win11 per-user dir present too (regardless of whether it exists on disk).
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var expectedUserDir = Path.Combine(local, "Microsoft", "Windows", "Fonts");
            dirs.ShouldContain(expectedUserDir);
        }
        else if (OperatingSystem.IsMacOS())
        {
            dirs.ShouldContain("/System/Library/Fonts");
            dirs.ShouldContain("/Library/Fonts");
        }
        else
        {
            dirs.ShouldContain("/usr/share/fonts");
        }
    }

    [Fact]
    public void EnumerateInstalledFonts_OnlyReturnsFontFilesAndIsUnique()
    {
        var fonts = FontResolver.EnumerateInstalledFonts().ToList();
        // Every result must be one of the known font extensions.
        foreach (var f in fonts)
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            (ext is ".ttf" or ".otf" or ".ttc" or ".otc").ShouldBeTrue(
                $"unexpected extension on '{f}': {ext}");
        }
        // No duplicates.
        var comparer = OperatingSystem.IsLinux()
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        fonts.Distinct(comparer).Count().ShouldBe(fonts.Count);
    }

    [Fact]
    public void EnumerateInstalledFonts_FindsConsolasOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fonts = FontResolver.EnumerateInstalledFonts().ToList();
        fonts.ShouldContain(f =>
            Path.GetFileName(f).Equals("consola.ttf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateInstalledFonts_PicksUpPerUserFontsDir_WhenItExists()
    {
        // Only meaningful on Windows + when the user has actually installed
        // anything to %LOCALAPPDATA%\Microsoft\Windows\Fonts. We don't fail
        // if the dir is empty — we just assert that when fonts are there,
        // they show up in the enumeration. This was the entire point of
        // adding the per-user dir to FontDirectories.
        if (!OperatingSystem.IsWindows()) return;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userFontsDir = Path.Combine(local, "Microsoft", "Windows", "Fonts");
        if (!Directory.Exists(userFontsDir)) return;
        var userFiles = Directory.EnumerateFiles(userFontsDir, "*", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".ttf" or ".otf" or ".ttc" or ".otc";
            })
            .ToList();
        if (userFiles.Count == 0) return;

        var enumerated = FontResolver.EnumerateInstalledFonts().ToList();
        foreach (var f in userFiles)
            enumerated.ShouldContain(f);
    }

    // ---- By-name resolution -------------------------------------------------

    [Theory]
    [InlineData("Arial", "arial", FontStyle.Regular)]
    [InlineData("ArialMT", "arial", FontStyle.Regular)]
    [InlineData("Arial,Bold", "arial", FontStyle.Bold)]
    [InlineData("Arial-BoldMT", "arial", FontStyle.Bold)]
    [InlineData("Arial-ItalicMT", "arial", FontStyle.Italic)]
    [InlineData("ABCDEF+Arial", "arial", FontStyle.Regular)] // subset prefix dropped
    [InlineData("Arial,BoldItalic", "arial", FontStyle.BoldItalic)]
    [InlineData("Helvetica-Oblique", "helvetica", FontStyle.Italic)]
    [InlineData("TimesNewRomanPSMT", "timesnewroman", FontStyle.Regular)]
    [InlineData("TimesNewRomanPS-BoldItalicMT", "timesnewroman", FontStyle.BoldItalic)]
    [InlineData("CourierNew", "couriernew", FontStyle.Regular)]
    [InlineData("CourierNewPSMT", "couriernew", FontStyle.Regular)]
    [InlineData("Symbol", "symbol", FontStyle.Regular)]
    public void TryParseFamilyStyle_ParsesFamilyAndStyle(string name, string expectedFamily, FontStyle expectedStyle)
    {
        FontResolver.TryParseFamilyStyle(name, out var family, out var style).ShouldBeTrue();
        family.ShouldBe(expectedFamily);
        style.ShouldBe(expectedStyle);
    }

    [Theory]
    [InlineData("")]       // nothing at all
    [InlineData("Bold")]   // pure style word leaves no family
    [InlineData("+")]      // empty subset prefix
    public void TryParseFamilyStyle_ReturnsFalse_WhenNoFamilyRemains(string name)
    {
        FontResolver.TryParseFamilyStyle(name, out var family, out _).ShouldBeFalse();
        family.ShouldBeEmpty();
    }

    [Fact]
    public void ResolveInstalledFace_UnknownFamily_ReturnsNull()
    {
        FontResolver.ResolveInstalledFace("NoSuchFamily98765", FontStyle.Regular).ShouldBeNull();
    }

    [Fact]
    public void ResolveInstalledFace_Arial_PicksTheCorrectWeightedFace()
    {
        // Tolerant: skip when neither the Windows nor the Liberation face is on this box
        // (e.g. a bare CI runner). When Arial/Liberation IS present, the bold request must
        // land on the bold face, not the regular one.
        var bold = FontResolver.ResolveInstalledFace("Arial", FontStyle.Bold);
        if (bold is null) return;
        File.Exists(bold).ShouldBeTrue();
        Path.GetFileName(bold).ToLowerInvariant()
            .ShouldBeOneOf("arialbd.ttf", "liberationsans-bold.ttf");
    }

    [Fact]
    public void ResolveInstalledFont_ParsesThenResolves()
    {
        var path = FontResolver.ResolveInstalledFont("Arial,Bold");
        if (path is null) return; // Arial/Liberation not installed
        File.Exists(path).ShouldBeTrue();
        Path.GetFileName(path).ToLowerInvariant()
            .ShouldBeOneOf("arialbd.ttf", "liberationsans-bold.ttf");
    }

    [Fact]
    public void ResolveInstalledFont_FallsBackToDirectFileProbe_ForNonStandardFamily()
    {
        // Tahoma isn't in the standard-family table, so this exercises the
        // "<family>.ttf" probe. Windows-only + only when Tahoma is installed.
        if (!OperatingSystem.IsWindows()) return;
        var path = FontResolver.ResolveInstalledFont("HNLQCS+Tahoma");
        if (path is null) return;
        Path.GetFileName(path).ToLowerInvariant().ShouldBe("tahoma.ttf");
    }

    /// <summary>
    /// Every indexed face must name a file that exists and a face index the file actually has —
    /// the id is handed straight to the rasterizer, so a bad one is a load exception at draw time.
    /// </summary>
    [Fact]
    public void InstalledFaces_EveryEntryPointsAtALoadableFace()
    {
        var index = FontResolver.InstalledFaces;
        if (index.Count == 0) return; // no fonts installed (a bare CI container)

        foreach (var (key, faces) in index)
        {
            key.ShouldNotBeNullOrEmpty();
            faces.Length.ShouldBe(4); // one slot per FontStyle
            foreach (var face in faces)
            {
                if (face.Path is null) continue; // unfilled style slot
                File.Exists(face.Path).ShouldBeTrue($"'{key}' indexes a missing file: {face.Path}");
                face.FaceIndex.ShouldBeGreaterThanOrEqualTo(0);
                FontFaceId.TryParse(face.Id, out var parsedPath, out var parsedIndex);
                parsedPath.ShouldBe(face.Path);
                parsedIndex.ShouldBe(face.FaceIndex);
            }
        }
    }

    /// <summary>
    /// The case a file-name-derived lookup cannot reach: Segoe UI Symbol's file is seguisym.ttf,
    /// so neither the standard-family table nor a "&lt;family&gt;.ttf" probe finds it. Only the
    /// face's own declared family name does.
    /// </summary>
    [Fact]
    public void ResolveInstalledFont_FindsAFaceWhoseFileNameIsNotItsFamily()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = FontResolver.ResolveInstalledFont("Segoe UI Symbol");
        if (path is null) return; // not installed on this SKU
        Path.GetFileName(path).ToLowerInvariant().ShouldBe("seguisym.ttf");
    }

    /// <summary>
    /// A face past the first inside a collection has no file name at all, so it can only be named
    /// by a font id carrying its index. Cambria Math is face 1 of cambria.ttc.
    /// </summary>
    [Fact]
    public void ResolveInstalledFont_ReachesFacesInsideACollection()
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = FontResolver.ResolveInstalledFont("Cambria Math");
        if (id is null) return; // Office fonts not present

        FontFaceId.TryParse(id, out var path, out var faceIndex).ShouldBeTrue();
        Path.GetFileName(path).ToLowerInvariant().ShouldBe("cambria.ttc");
        faceIndex.ShouldBeGreaterThan(0);

        // ...and the id must reach the face it names, not merely parse.
        var rasterizer = new ManagedFontRasterizer();
        Should.NotThrow(() => rasterizer.RasterizeGlyph(id, 24f, new System.Text.Rune('A')));
    }

    /// <summary>
    /// The shaper's seam must resolve a collection-face id too. Shaping runs before any glyph
    /// of the face has been rasterized, so <see cref="ManagedFontRasterizer.TryGetOpenTypeFont"/>
    /// is the first loader the id reaches — on a fresh rasterizer there is no cached face for
    /// <see cref="ManagedFontRasterizer.RasterizeGlyph"/> to have left behind.
    /// </summary>
    [Fact]
    public void TryGetOpenTypeFont_LoadsACollectionFaceId()
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = FontResolver.ResolveInstalledFont("Cambria Math");
        if (id is null) return; // Office fonts not present

        using var rasterizer = new ManagedFontRasterizer();
        rasterizer.TryGetOpenTypeFont(id, out var font).ShouldBeTrue();
        font.ShouldNotBeNull();
        // Proves the id reached face 1, not face 0: Cambria Math carries a MATH table,
        // plain Cambria (face 0) does not.
        font.Math.ShouldNotBeNull();
    }
}
