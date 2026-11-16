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
        Directory.Exists(dir).ShouldBeTrue($"no package folder at {dir}: build src/DIR.Lib with -c Release (it packs on build) before this test");
        var nupkg = Directory.GetFiles(dir, "DIR.Lib.*.nupkg")
            .Where(f => !Path.GetFileName(f).StartsWith("DIR.Lib.Debug.", StringComparison.Ordinal)
                        && !Path.GetFileName(f).StartsWith("DIR.Lib.Shaping.", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        nupkg.ShouldNotBeNull($"no DIR.Lib nupkg in {dir}: a missing package proves nothing, so this fails rather than passing");

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

    /// <summary>
    /// The Debug twin is packed only by CI (<c>-c Debug -p:DebugTwin=true</c>, into artifacts/debug-twin), so a
    /// local run usually has none and skips. When one is present it must carry the targets file under its own id.
    /// </summary>
    [Fact]
    public void DebugTwinNupkg_CarriesItsOwnNamedTargets()
    {
        var pkgDir = PackageDir();
        var root = Path.GetFullPath(Path.Combine(pkgDir, "..", "..", "..", "..", ".."));
        var dirs = new[] { Path.Combine(root, "artifacts", "debug-twin"), Path.GetFullPath(Path.Combine(pkgDir, "..", "Debug")) }
            .Where(Directory.Exists);
        var nupkg = dirs.SelectMany(d => Directory.GetFiles(d, "DIR.Lib.Debug.*.nupkg"))
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        Assert.SkipUnless(nupkg is not null, "no DIR.Lib.Debug twin packed here (CI packs it with -c Debug -p:DebugTwin=true)");

        using var zip = ZipFile.OpenRead(nupkg!);
        var entry = zip.GetEntry("buildTransitive/DIR.Lib.Debug.targets");
        entry.ShouldNotBeNull();
        using var reader = new StreamReader(entry.Open());
        reader.ReadToEnd().ShouldContain("DIR.Lib.Inspector.IsSupported");
    }
}
