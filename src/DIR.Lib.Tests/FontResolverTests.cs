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
}
