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
        text.ShouldContain("$(_IsPublishing)' == 'true'");
    }

    /// <summary>Set to 1 where a twin has been packed (CI, after its twin pack): no twin found is then a failure, not a skip.</summary>
    internal const string RequireTwinVariable = "DIR_LIB_REQUIRE_DEBUG_TWIN";

    /// <summary>
    /// The Debug twin is packed only by CI (<c>-c Debug -p:DebugTwin=true</c>, into artifacts/debug-twin), so a
    /// local run usually has none and skips. When one is present it must carry the targets file under its own id.
    /// CI runs this test a second time after its twin pack with <see cref="RequireTwinVariable"/> set, because its
    /// first run comes before the pack and skips: until 11.87 that skip was the only run, and the folder searched was
    /// one level above the repository, so no run anywhere had opened a twin.
    /// </summary>
    [Fact]
    public void DebugTwinNupkg_CarriesItsOwnNamedTargets()
    {
        var pkgDir = PackageDir();
        // pkgDir is <root>/src/DIR.Lib/bin/Release: four levels below the repository root.
        var root = Path.GetFullPath(Path.Combine(pkgDir, "..", "..", "..", ".."));
        File.Exists(Path.Combine(root, "DIR.Lib.sln")).ShouldBeTrue($"{root} is not the repository root, so the twin would be looked for in the wrong folder");
        var dirs = new[] { Path.Combine(root, "artifacts", "debug-twin"), Path.GetFullPath(Path.Combine(pkgDir, "..", "Debug")) }
            .Where(Directory.Exists);
        var nupkg = dirs.SelectMany(d => Directory.GetFiles(d, "DIR.Lib.Debug.*.nupkg"))
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (Environment.GetEnvironmentVariable(RequireTwinVariable) == "1")
        {
            nupkg.ShouldNotBeNull($"{RequireTwinVariable}=1 says a twin was packed, and none is in {Path.Combine(root, "artifacts", "debug-twin")} or bin/Debug");
        }

        Assert.SkipUnless(nupkg is not null, "no DIR.Lib.Debug twin packed here (CI packs it with -c Debug -p:DebugTwin=true)");

        using var zip = ZipFile.OpenRead(nupkg!);
        var entry = zip.GetEntry("buildTransitive/DIR.Lib.Debug.targets");
        entry.ShouldNotBeNull();
        using var reader = new StreamReader(entry.Open());
        reader.ReadToEnd().ShouldContain("DIR.Lib.Inspector.IsSupported");
    }
}
