using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DIR.Lib.Diagnostics;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The run-time opt-in: <c>DIR_INSPECTOR=1</c> plus <c>DIR_INSPECTOR_TOKEN</c>. Driven through the internal
/// overload that takes both values, so no test reads or sets the real process environment.
/// </summary>
public class InspectorOptInTests
{
    private sealed class Host : IDebugInspectorHost
    {
        public readonly List<string> Invoked = [];
        public string AppName => "OptIn";
        public string SurfaceKind => "test";
        public void Poke() { }

        public string? Invoke(string method, JsonElement parameters)
        {
            Invoked.Add(method);
            return "\"pong\"";
        }
    }

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "dir-inspector-optin-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    public void NotExactlyOne_ReturnsNullAndTouchesNothing(string? optIn)
    {
        var dir = TempDir();
        DebugInspectorCore.StartIfOptedIn(new Host(), DebugInspectorDiscovery.Registry, optIn, "secret", dir).ShouldBeNull();
        Directory.Exists(dir).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void OptInWithoutToken_ReturnsNullAndSaysWhy(string? token)
    {
        var dir = TempDir();
        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            DebugInspectorCore.StartIfOptedIn(new Host(), DebugInspectorDiscovery.Registry, "1", token, dir).ShouldBeNull();
        }
        finally
        {
            Console.SetError(original);
        }
        captured.ToString().ShouldContain(DebugInspectorCore.TokenVariable);
        Directory.Exists(dir).ShouldBeFalse();
    }

    [Fact]
    public async Task WithToken_RefusesMissingAndWrongTokenAndAnswersTheRightOne()
    {
        var dir = TempDir();
        var host = new Host();
        using var core = DebugInspectorCore.StartIfOptedIn(host, DebugInspectorDiscovery.Registry, "1", "s3cret", dir);
        try
        {
            core.ShouldNotBeNull();
            core.CommandTimeout = TimeSpan.FromSeconds(30);

            var (_, none) = await core.HandleAsync("""{"id":1,"method":"probe"}""");
            var (_, wrong) = await core.HandleAsync("""{"id":2,"method":"probe","token":"nope"}""");
            var (_, notString) = await core.HandleAsync("""{"id":3,"method":"probe","token":5}""");
            none.ShouldStartWith("!unauthorized");
            wrong.ShouldStartWith("!unauthorized");
            notString.ShouldStartWith("!unauthorized");

            core.Pump();
            host.Invoked.ShouldBeEmpty();

            // HandleAsync enqueues before its first await, so the pump below finds the command.
            var pending = core.HandleAsync("""{"id":4,"method":"probe","token":"s3cret"}""");
            core.Pump();
            var (id, ok) = await pending;
            id.ShouldBe(4);
            ok.ShouldBe("\"pong\"");
            host.Invoked.ShouldBe(["probe"]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task StartWithoutOptIn_NeedsNoToken()
    {
        var host = new Host();
        using var core = DebugInspectorCore.Detached(host);
        var pending = core.HandleAsync("""{"id":1,"method":"probe"}""");
        core.Pump();
        (await pending).Response.ShouldBe("\"pong\"");
    }
}
