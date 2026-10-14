#if DEBUG
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DIR.Lib.Diagnostics;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// How a started inspector core makes itself findable: a descriptor in the per-user file registry by default,
/// UDP multicast only when LAN discovery is asked for. The registry's format is a contract the sidecars
/// implement without referencing DIR.Lib (<c>src/DIR.Lib/Diagnostics/inspector-registry.md</c>), so these pin
/// the bytes as well as the behaviour.
///
/// <para>Every test registers in its own temporary folder through <see cref="DebugInspectorCore.StartExactly"/>,
/// never in the real per-user one, and reads no environment: a developer's shell with the LAN variable set must
/// not change what these assert.</para>
/// </summary>
public sealed class DebugInspectorDiscoveryTests : IDisposable
{
    private readonly string _registry = Path.Combine(Path.GetTempPath(), "dir-lib-inspector-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_registry, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private sealed class Host : IDebugInspectorHost
    {
        public volatile string Title = "first";

        public string AppName => "Fake \"app\"";
        public string SurfaceKind => "test";
        public void Poke() { }
        public string? Invoke(string method, JsonElement parameters) => null;
        public string? DiscoveryExtras => $"\"title\":{DebugInspectorCore.Quote(Title)}";
    }

    [Fact]
    public void A_started_core_writes_its_descriptor_and_removes_it_on_dispose()
    {
        var core = DebugInspectorCore.StartExactly(new Host(), DebugInspectorDiscovery.Registry, _registry);
        string file;
        try
        {
            file = core.RegistryFile.ShouldNotBeNull();
            Path.GetDirectoryName(file).ShouldBe(_registry);
            Path.GetFileName(file).ShouldBe($"{Environment.ProcessId}-{core.Port}.json");

            var text = File.ReadAllText(file);
            text.ShouldBe(
                "{\"app\":\"Fake \\\"app\\\"\",\"kind\":\"test\"," +
                $"\"tcpPort\":{core.Port},\"pid\":{Environment.ProcessId},\"proto\":1," +
                $"\"processStart\":\"{DebugInspectorRegistration.FormatProcessStart()}\",\"title\":\"first\"}}");

            // processStart is what a reader compares against the OS, so it must BE the OS's answer.
            using var doc = JsonDocument.Parse(text);
            var recorded = doc.RootElement.GetProperty("processStart").GetDateTimeOffset().UtcDateTime;
            using var self = Process.GetCurrentProcess();
            (self.StartTime.ToUniversalTime() - recorded).Duration().ShouldBeLessThan(TimeSpan.FromSeconds(1));

            // Written through a temp file, which must not outlive the rename.
            Directory.GetFiles(_registry).ShouldBe([file]);
        }
        finally
        {
            core.Dispose();
        }

        File.Exists(file).ShouldBeFalse();
        Directory.GetFiles(_registry).ShouldBeEmpty();
    }

    [Fact]
    public void By_default_a_core_registers_and_starts_no_udp_responder()
    {
        // What Start(host) and Start(host, enableDiscovery: true) resolve to when the variable is unset.
        var discovery = DebugInspectorCore.WithEnvironment(DebugInspectorCore.Requested(enableDiscovery: true), null);
        discovery.ShouldBe(DebugInspectorDiscovery.Registry);

        using var core = DebugInspectorCore.StartExactly(new Host(), discovery, _registry);

        core.Discovery.ShouldBe(DebugInspectorDiscovery.Registry);
        core.LanResponderStarted.ShouldBeFalse();
        core.RegistryFile.ShouldNotBeNull();
        File.Exists(core.RegistryFile).ShouldBeTrue();
    }

    /// <summary>
    /// On Unix, <c>GetFolderPath</c> with the default <c>SpecialFolderOption.None</c> answers "" for a folder
    /// that does not exist yet, and <c>~/.local/share</c> does not exist on a fresh account, in a container or on
    /// a headless board. Resolving the registry through that answer left such an instance unregistered, and with
    /// LAN discovery off by default, not findable at all. The writer creates the folder itself; it only needs
    /// the path. Played here with a stand-in for the Unix answer, so it runs on every OS.
    /// </summary>
    [Fact]
    public void The_registry_resolves_and_registers_where_the_data_folder_does_not_exist_yet()
    {
        var dataHome = Path.Combine(_registry, "share");   // not created: _registry itself does not exist yet
        string Unix(Environment.SpecialFolder folder, Environment.SpecialFolderOption option)
            => folder == Environment.SpecialFolder.LocalApplicationData
               && (option == Environment.SpecialFolderOption.DoNotVerify || Directory.Exists(dataHome))
                ? dataHome
                : "";

        var directory = DebugInspectorCore.ResolveRegistryDirectory(Unix).ShouldNotBeNull();
        directory.ShouldBe(Path.Combine(dataHome, "DIR.Lib", "inspector"));

        using var core = DebugInspectorCore.StartExactly(new Host(), DebugInspectorDiscovery.Registry, directory);
        File.Exists(core.RegistryFile.ShouldNotBeNull()).ShouldBeTrue();
    }

    [Fact]
    public void Only_an_empty_answer_means_no_registry_folder()
    {
        DebugInspectorCore.ResolveRegistryDirectory((_, _) => "").ShouldBeNull();
    }

    [Fact]
    public void Disabled_discovery_is_not_findable_at_all_even_with_the_lan_variable_set()
    {
        var discovery = DebugInspectorCore.WithEnvironment(DebugInspectorCore.Requested(enableDiscovery: false), "1");
        discovery.ShouldBe(DebugInspectorDiscovery.None);

        using var core = DebugInspectorCore.StartExactly(new Host(), discovery, _registry);

        core.LanResponderStarted.ShouldBeFalse();
        core.RegistryFile.ShouldBeNull();
        Directory.Exists(_registry).ShouldBeFalse();
    }

    /// <summary>
    /// SdlVulkan.Renderer calls <c>Start(host, opts.EnableDiscovery)</c> and Console.Lib <c>Start(host)</c>, both
    /// compiled against this exact method. Adding the discovery parameter to it instead of a second overload
    /// would compile everywhere and throw <c>MissingMethodException</c> in every already-built consumer; only
    /// reflection can see the difference.
    /// </summary>
    [Fact]
    public void The_original_start_signature_is_kept_for_already_compiled_consumers()
    {
        var start = typeof(DebugInspectorCore).GetMethod(nameof(DebugInspectorCore.Start),
            [typeof(IDebugInspectorHost), typeof(bool)]).ShouldNotBeNull();

        start.ReturnType.ShouldBe(typeof(DebugInspectorCore));
        start.GetParameters()[1].DefaultValue.ShouldBe(true);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    public void The_lan_variable_adds_lan_discovery_to_the_registry(string? value, bool lan)
    {
        DebugInspectorCore.WithEnvironment(DebugInspectorDiscovery.Registry, value)
            .ShouldBe(lan ? DebugInspectorDiscovery.Registry | DebugInspectorDiscovery.Lan : DebugInspectorDiscovery.Registry);
    }

    [Fact]
    public async Task A_changed_extra_rewrites_the_descriptor_from_the_pump()
    {
        var host = new Host();
        var core = DebugInspectorCore.StartExactly(host, DebugInspectorDiscovery.Registry, _registry);
        var file = core.RegistryFile.ShouldNotBeNull();
        try
        {
            host.Title = "second";

            // Pumped the way a host loop pumps it: the refresh reads the extras at most once a second, and the
            // write itself happens off this thread.
            await WaitUntil(() =>
            {
                core.Pump();
                return ReadShared(file)?.Contains("\"title\":\"second\"") == true;
            }, "the descriptor to carry the new title");
        }
        finally
        {
            core.Dispose();
        }

        File.Exists(file).ShouldBeFalse();
    }

    [Fact]
    public void Start_sweeps_descriptors_of_dead_processes_and_nothing_else()
    {
        Directory.CreateDirectory(_registry);

        // int.MaxValue is odd, so never a Windows pid, and above any Linux pid_max: certainly not running.
        var dead = Path.Combine(_registry, $"{int.MaxValue}-1234.json");
        var deadTemp = Path.Combine(_registry, $"{int.MaxValue}-1234.tmp");
        // Another core in THIS process is live by definition, whatever its file says.
        var sibling = Path.Combine(_registry, $"{Environment.ProcessId}-1.json");
        var foreign = Path.Combine(_registry, "notes.txt");
        var foreignJson = Path.Combine(_registry, "abc-1.json");
        foreach (var f in new[] { dead, deadTemp, sibling, foreign, foreignJson }) File.WriteAllText(f, "{}");

        using var core = DebugInspectorCore.StartExactly(new Host(), DebugInspectorDiscovery.Registry, _registry);

        File.Exists(dead).ShouldBeFalse();
        File.Exists(deadTemp).ShouldBeFalse();
        File.Exists(sibling).ShouldBeTrue();
        File.Exists(foreign).ShouldBeTrue();
        File.Exists(foreignJson).ShouldBeTrue();
    }

    /// <summary>
    /// A start-time mismatch makes a reader SKIP a descriptor; only a dead pid gets one DELETED. On Linux .NET's
    /// StartTime is the boot time plus the start ticks, and the boot time moves with a wall-clock step (an NTP
    /// first sync without an RTC, WSL2 resyncing after the host slept), so after a step over the tolerance a
    /// LIVE instance mismatches. A sweep that deleted on that would make it unfindable until it restarted, even
    /// once a fixed reader (issue #112) could have found it again.
    /// </summary>
    [Fact]
    public void A_sweep_keeps_a_live_pids_descriptor_even_when_its_start_time_mismatches()
    {
        Directory.CreateDirectory(_registry);
        var (pid, started) = AnotherLiveProcess();

        var mismatched = Path.Combine(_registry, $"{pid}-1.json");
        var stepped = (started - TimeSpan.FromHours(1)).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(mismatched,
            $"{{\"app\":\"x\",\"kind\":\"test\",\"tcpPort\":1,\"pid\":{pid},\"proto\":1,\"processStart\":\"{stepped}\"}}");
        var dead = Path.Combine(_registry, $"{int.MaxValue}-1.json");
        File.WriteAllText(dead, "{}");

        // A reader skips it ...
        DebugInspectorRegistration.IsLive(pid, DebugInspectorRegistration.ReadProcessStart(File.ReadAllText(mismatched)))
            .ShouldBeFalse();

        // ... but the sweep a starting core runs does not delete it; it deletes only the dead pid's.
        using var core = DebugInspectorCore.StartExactly(new Host(), DebugInspectorDiscovery.Registry, _registry);

        File.Exists(mismatched).ShouldBeTrue();
        File.Exists(dead).ShouldBeFalse();
    }

    /// <summary>
    /// A running process other than this one, whose start time can be read: the longest-running, so it is
    /// still there when the sweep looks (pid 1 on Linux).
    /// </summary>
    private static (int Pid, DateTime StartedUtc) AnotherLiveProcess()
    {
        (int Pid, DateTime StartedUtc)? oldest = null;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    var started = process.StartTime.ToUniversalTime();
                    if (oldest is null || started < oldest.Value.StartedUtc) oldest = (process.Id, started);
                }
                catch (Exception) { /* access denied, or exited: not a candidate */ }
            }
        }
        return oldest ?? throw new InvalidOperationException("no other process with a readable start time");
    }

    [Fact]
    public void A_reused_pid_is_not_live()
    {
        var start = DebugInspectorRegistration.ProcessStartUtc;

        DebugInspectorRegistration.IsLive(Environment.ProcessId, start).ShouldBeTrue();
        DebugInspectorRegistration.IsLive(Environment.ProcessId, null).ShouldBeTrue();
        // The same pid, started an hour earlier: a different process that has since been given this pid.
        DebugInspectorRegistration.IsLive(Environment.ProcessId, start - TimeSpan.FromHours(1)).ShouldBeFalse();
        DebugInspectorRegistration.IsLive(int.MaxValue, null).ShouldBeFalse();
    }

    /// <summary>Set to <c>1</c> to run <see cref="Lan_discovery_answers_a_multicast_query_as_before"/>.</summary>
    private const string LanTestsVariable = "DIR_LIB_INSPECTOR_LAN_TESTS";

    /// <summary>
    /// The opt-in path still answers a multicast query, with exactly the reply it always sent, so a sidecar
    /// keeps finding builds on other machines (and builds older than the registry) the way it did.
    /// <para>
    /// <b>Opt-in, because it binds <c>0.0.0.0:47892</c>.</b> <c>dotnet test</c> hosts this suite in
    /// <c>DIR.Lib.Tests.exe</c> under the worktree's own <c>bin</c>, so running this there makes Windows Firewall
    /// prompt once per worktree and configuration: exactly what the registry exists to stop. Run it hosted by
    /// <c>dotnet.exe</c>, whose firewall rule already exists on a machine that has ever allowed it:
    /// <c>DIR_LIB_INSPECTOR_LAN_TESTS=1 dotnet src/DIR.Lib.Tests/bin/Debug/net10.0/DIR.Lib.Tests.dll
    /// -method "*Lan_discovery_answers*"</c> (run directly, the app takes xunit's own options, not MTP's).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Lan_discovery_answers_a_multicast_query_as_before()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(LanTestsVariable) == "1",
            $"binds 0.0.0.0:{DebugInspectorCore.DiscoveryPortNumber}, which makes Windows Firewall prompt for this " +
            $"test exe's path; set {LanTestsVariable}=1 and host it in dotnet.exe to run it");

        var ct = TestContext.Current.CancellationToken;
        using var core = DebugInspectorCore.StartExactly(new Host(),
            DebugInspectorDiscovery.Registry | DebugInspectorDiscovery.Lan, _registry);
        core.LanResponderStarted.ShouldBeTrue();

        // Other DEBUG apps on this machine may answer the same query; ours is the one naming this core.
        var ours = $"\"tcpPort\":{core.Port},\"pid\":{Environment.ProcessId},";
        var query = Encoding.UTF8.GetBytes("{\"q\":\"dir-inspect\",\"proto\":1}");
        var group = new IPEndPoint(DebugInspectorCore.DiscoveryGroup, DebugInspectorCore.DiscoveryPortNumber);

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        string? reply = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        // Re-asked until answered: the responder binds and joins on the thread pool, after Start returns.
        while (reply is null && DateTime.UtcNow < deadline)
        {
            await udp.SendAsync(query, group, ct);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
            window.CancelAfter(TimeSpan.FromMilliseconds(300));
            try
            {
                while (reply is null)
                {
                    var text = Encoding.UTF8.GetString((await udp.ReceiveAsync(window.Token)).Buffer);
                    if (text.Contains(ours)) reply = text;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }

        // Byte for byte the reply builds before the registry sent: no processStart, which is registry only.
        reply.ShouldBe(
            "{\"app\":\"Fake \\\"app\\\"\",\"kind\":\"test\"," +
            $"\"tcpPort\":{core.Port},\"pid\":{Environment.ProcessId},\"proto\":1,\"title\":\"first\"}}");
    }

    /// <summary>Reads as a sidecar should, sharing delete so the writer's rename is never blocked.</summary>
    private static string? ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (FileNotFoundException) { return null; }
    }

    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"timed out waiting for {what}");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
#endif
