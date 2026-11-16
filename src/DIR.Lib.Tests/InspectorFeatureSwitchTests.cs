using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DIR.Lib.Diagnostics;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>The DIR.Lib.Inspector.IsSupported switch, driven through the internal seam so no AppContext or environment state is touched.</summary>
public class InspectorFeatureSwitchTests
{
    private sealed class Host : IDebugInspectorHost
    {
        public string AppName => "Switch";
        public string SurfaceKind => "test";
        public void Poke() { }
        public string? Invoke(string method, JsonElement parameters) => "\"pong\"";
    }

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "dir-inspector-switch-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Off_OptedIn_ReturnsNullPrintsOneLineAndStartsNothing()
    {
        var dir = TempDir();
        var err = new StringWriter();

        var core = DebugInspectorCore.StartIfOptedIn(new Host(), DebugInspectorDiscovery.Registry, "1", "secret", dir, false, err);

        core.ShouldBeNull();
        Directory.Exists(dir).ShouldBeFalse();
        var lines = err.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(1);
        lines[0].ShouldContain("DIR_INSPECTOR=1");
        lines[0].ShouldContain("trimmed out of this build");
        lines[0].ShouldContain("-p:DirInspectorSupport=true");
        err.ToString().ShouldNotContain("secret");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    public void Off_NotOptedIn_IsSilent(string? optIn)
    {
        var dir = TempDir();
        var err = new StringWriter();

        DebugInspectorCore.StartIfOptedIn(new Host(), DebugInspectorDiscovery.Registry, optIn, "secret", dir, false, err)
            .ShouldBeNull();

        err.ToString().ShouldBeEmpty();
        Directory.Exists(dir).ShouldBeFalse();
    }

    [Fact]
    public void On_StartsAsBefore()
    {
        var dir = TempDir();
        var err = new StringWriter();
        try
        {
            using var core = DebugInspectorCore.StartIfOptedIn(new Host(), DebugInspectorDiscovery.None, "1", "secret", dir, true, err);
            core.ShouldNotBeNull();
            err.ToString().ShouldNotContain("trimmed");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IsSupported_DefaultsToTrueWhenTheSwitchIsUnset()
    {
        if (AppContext.TryGetSwitch(DebugInspectorCore.FeatureSwitchName, out _)) return;
        DebugInspectorCore.IsSupported.ShouldBeTrue();
    }
}
