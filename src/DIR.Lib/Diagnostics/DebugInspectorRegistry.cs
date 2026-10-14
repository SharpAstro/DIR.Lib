#if DEBUG
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DIR.Lib.Diagnostics;

/// <summary>
/// How a started <see cref="DebugInspectorCore"/> makes itself findable. Flags, because LAN discovery is
/// something added to the registry rather than a mode instead of it.
/// </summary>
[Flags]
public enum DebugInspectorDiscovery
{
    /// <summary>
    /// Not findable at all. The command server still runs, and a driver that is told its port (from the
    /// stderr banner) still reaches it.
    /// </summary>
    None = 0,

    /// <summary>
    /// A descriptor file in the per-user registry (<see cref="DebugInspectorCore.RegistryDirectory"/>). No
    /// socket is involved, so there is nothing for a firewall to ask about. The default.
    /// </summary>
    Registry = 1,

    /// <summary>
    /// Also answer UDP multicast discovery on <see cref="DebugInspectorCore.DiscoveryGroup"/> port
    /// <see cref="DebugInspectorCore.DiscoveryPortNumber"/>, which is how an instance on ANOTHER machine is
    /// found. That bind is on every interface, and Windows Firewall prompts for it once per executable path, so
    /// every DEBUG build at a new path (each git worktree) asks again. Opt-in for that reason.
    /// </summary>
    Lan = 2,
}

/// <summary>
/// One core's descriptor file in the per-user inspector registry: written when the core starts, rewritten
/// when the host's <see cref="IDebugInspectorHost.DiscoveryExtras"/> change, deleted when the core is
/// disposed. The file format is specified in <c>src/DIR.Lib/Diagnostics/inspector-registry.md</c>, which the
/// sidecars implement against; they do not reference DIR.Lib, so that document is the contract.
/// </summary>
internal sealed class DebugInspectorRegistration
{
    /// <summary>
    /// How far apart the recorded and the observed start time of a pid may be and still be the same process,
    /// for a reader (<see cref="IsLive"/>). It absorbs jitter, not a wall-clock step: on Linux the observed start
    /// moves with one, so a step over this makes readers skip a live instance until it restarts (DIR.Lib issue
    /// #112). The sweep never deletes on a mismatch for that reason. What it guards against, a pid reused by a
    /// process started at a different moment, is separated from the original by the dead app's whole lifetime,
    /// not by seconds.
    /// </summary>
    internal static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    /// When this process started, in UTC, as the OS reports it: what a reader compares against. Null where the
    /// platform will not say, and the descriptor then omits <c>processStart</c> rather than the app failing to
    /// start over a debugging aid.
    /// </summary>
    internal static readonly DateTime? ProcessStartUtc = ReadProcessStartUtc();

    private readonly Lock _gate = new();
    private readonly string _temp;
    private string? _written;
    private int _busy;
    private bool _closed;

    /// <summary>The descriptor file, <c>&lt;pid&gt;-&lt;tcpPort&gt;.json</c> in the registry folder.</summary>
    public string Path { get; }

    private DebugInspectorRegistration(string directory, int port)
    {
        var stem = $"{Environment.ProcessId}-{port}";
        Path = System.IO.Path.Combine(directory, stem + ".json");
        _temp = System.IO.Path.Combine(directory, stem + ".tmp");
    }

    /// <summary>
    /// Creates the folder, sweeps descriptors left by dead processes, and writes this core's descriptor
    /// before returning, so a sidecar started the moment <see cref="DebugInspectorCore.Start(IDebugInspectorHost, bool)"/>
    /// returns already finds it. Throws if the first write fails; the caller reports that and carries on
    /// unregistered.
    /// </summary>
    public static DebugInspectorRegistration Create(string directory, int port, string descriptor)
    {
        Directory.CreateDirectory(directory);
        SweepStale(directory);

        var registration = new DebugInspectorRegistration(directory, port);
        WriteAtomically(registration._temp, registration.Path, descriptor);
        registration._written = descriptor;
        return registration;
    }

    /// <summary>
    /// Rewrites the descriptor if it differs from what is on disk, on the thread pool so a slow disk never
    /// stalls the host's frame. On Windows any reader holding the file open blocks the rename, whatever it
    /// shares, so <see cref="TryWrite"/> retries briefly; a write that still fails is offered again at the next
    /// refresh, because <c>_written</c> only moves on success.
    /// </summary>
    public void Offer(string descriptor)
    {
        if (descriptor == Volatile.Read(ref _written) || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try { TryWrite(descriptor); }
            finally { Volatile.Write(ref _busy, 0); }
        });
    }

    /// <summary>Writes now, on the calling thread. False when closed or when the write still failed after retrying.</summary>
    internal bool TryWrite(string descriptor)
    {
        lock (_gate)
        {
            if (_closed || !Retrying(() => WriteAtomically(_temp, Path, descriptor))) return false;
            Volatile.Write(ref _written, descriptor);
            return true;
        }
    }

    /// <summary>
    /// Deletes the descriptor and stops any later write from recreating it. A delete blocked by a reader that
    /// does not share <see cref="FileShare.Delete"/> is retried briefly; one that still fails leaves a file
    /// whose pid is about to be dead, which the next writer's sweep and every reader already drop.
    /// </summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            Retrying(() =>
            {
                File.Delete(Path);
                File.Delete(_temp);
            });
        }
    }

    /// <summary>
    /// Runs a file operation, retrying for about 100 ms while another process holds the file. False if it
    /// never got through. Only ever called off the host's thread or from <c>Dispose</c>.
    /// </summary>
    private static bool Retrying(Action operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                operation();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 4) return false;
                Thread.Sleep(25);
            }
        }
    }

    /// <summary>
    /// Temp file then rename, so a reader sees the old descriptor or the new one and never half of either.
    /// The temp name ends in <c>.tmp</c>, which a reader enumerating <c>*.json</c> does not pick up.
    /// </summary>
    private static void WriteAtomically(string temp, string path, string descriptor)
    {
        File.WriteAllText(temp, descriptor);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Deletes the descriptors (and orphaned temp files) of processes that are no longer running, which is
    /// what a crash leaves behind. Best-effort throughout: a file that cannot be deleted is left for the next
    /// sweep, and files whose names this writer did not produce are never touched.
    /// <para>
    /// <b>Only a DEAD pid's file is deleted</b>, never one whose recorded start time merely disagrees with the
    /// running process's. On Linux .NET's <c>StartTime</c> is the boot time plus the start ticks, and the boot
    /// time moves with a wall-clock step (an NTP first sync on a board without an RTC, WSL2 resyncing after the
    /// host slept), so after a step over <see cref="StartTimeTolerance"/> a LIVE instance mismatches. A reader
    /// then skips it, which a restart cures; a sweep that deleted it would make it unfindable for good. A file
    /// left by a genuinely reused pid only waits for that process to exit.
    /// </para>
    /// </summary>
    internal static void SweepStale(string directory)
    {
        string[] files;
        try { files = Directory.GetFiles(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        foreach (var file in files)
        {
            if (!TryParseName(System.IO.Path.GetFileName(file), out var pid)
                || pid == Environment.ProcessId
                || IsRunning(pid))
            {
                continue;
            }

            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Whether any process holds <paramref name="pid"/> now: the sweep's whole rule. When it cannot tell
    /// (access denied) it says yes, so a file it cannot judge is kept.
    /// </summary>
    internal static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }          // no process with that pid
        catch (InvalidOperationException) { return false; }  // it exited while being asked
        catch (Exception) { return true; }                    // cannot tell (access denied): keep it
    }

    /// <summary>
    /// The READER's liveness rule, as the spec states it and the sidecars implement it: the pid is running,
    /// and, when the descriptor recorded a start time, it is the same process rather than a later one given the
    /// same pid. When the start time cannot be read the pid alone decides. The writer never deletes on this
    /// rule (see <see cref="SweepStale"/>); it is kept here as the reference the tests pin.
    /// <para>
    /// Known limit, on Linux: after a wall-clock step over <see cref="StartTimeTolerance"/> this answers false
    /// for a live instance until it restarts, so a reader should report "descriptor skipped: start-time
    /// mismatch" rather than drop it silently. The full fix, comparing a boot-relative start, is DIR.Lib
    /// issue #112.
    /// </para>
    /// </summary>
    internal static bool IsLive(int pid, DateTime? processStartUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return false;
            if (processStartUtc is not { } recorded) return true;
            return (process.StartTime.ToUniversalTime() - recorded).Duration() <= StartTimeTolerance;
        }
        catch (ArgumentException) { return false; }          // no process with that pid
        catch (InvalidOperationException) { return false; }  // it exited while being asked
        catch (Exception) { return true; }                    // cannot tell (access denied): keep it
    }

    /// <summary>Parses <c>&lt;pid&gt;-&lt;tcpPort&gt;.json</c> or <c>.tmp</c>.</summary>
    internal static bool TryParseName(string name, out int pid)
    {
        pid = 0;
        if (!name.EndsWith(".json", StringComparison.Ordinal) && !name.EndsWith(".tmp", StringComparison.Ordinal))
        {
            return false;
        }

        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        var dash = stem.IndexOf('-');
        return dash > 0
            && int.TryParse(stem.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out pid)
            && int.TryParse(stem.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// The <c>processStart</c> member of a descriptor, or null if it is missing or unreadable: the reader's
    /// half of <see cref="IsLive"/>, which the writer itself no longer needs.
    /// </summary>
    internal static DateTime? ReadProcessStart(string descriptor)
    {
        try
        {
            using var doc = JsonDocument.Parse(descriptor);
            return doc.RootElement.TryGetProperty("processStart", out var s)
                && s.ValueKind == JsonValueKind.String
                && s.TryGetDateTimeOffset(out var value)
                    ? value.UtcDateTime
                    : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The <c>processStart</c> value as written (ISO 8601 round-trip, UTC, trailing Z), or null if unknown.</summary>
    internal static string? FormatProcessStart() => ProcessStartUtc?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime? ReadProcessStartUtc()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            return DateTime.SpecifyKind(self.StartTime.ToUniversalTime(), DateTimeKind.Utc);
        }
        catch (Exception) { return null; }   // PlatformNotSupportedException and kin
    }
}
#endif
