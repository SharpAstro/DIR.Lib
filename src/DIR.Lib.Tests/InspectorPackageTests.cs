using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>Opens the DIR.Lib nupkg the Release build packed and checks the feature-switch targets file it carries.</summary>
public class InspectorPackageTests
{
    private static string PackageDir() =>
        typeof(InspectorPackageTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "DirLibPackageDir").Value!;

    [Fact]
    public void Nupkg_CarriesTheFeatureSwitchTargets()
    {
        var dir = PackageDir();
        if (!Directory.Exists(dir)) return; // not a Release build of src/DIR.Lib: nothing was packed
        var nupkg = Directory.GetFiles(dir, "DIR.Lib.*.nupkg")
            .Where(f => !Path.GetFileName(f).StartsWith("DIR.Lib.Debug.", StringComparison.Ordinal)
                        && !Path.GetFileName(f).StartsWith("DIR.Lib.Shaping.", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (nupkg is null) return;

        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("buildTransitive/DIR.Lib.targets");
        entry.ShouldNotBeNull();
        using var reader = new StreamReader(entry.Open());
        var text = reader.ReadToEnd();

        text.ShouldContain("DIR.Lib.Inspector.IsSupported");
        text.ShouldContain("Trim=\"true\"");
        text.ShouldContain("$(PublishTrimmed)' == 'true'");
        text.ShouldContain("$(PublishAot)' == 'true'");
        text.ShouldContain("$(DirInspectorSupport)' == ''");
    }
}
