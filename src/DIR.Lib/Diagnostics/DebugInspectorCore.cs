#if DEBUG
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DIR.Lib.Diagnostics;

/// <summary>
/// What a surface must supply for <see cref="DebugInspectorCore"/> to drive it. Everything
/// surface-specific lives behind this: how a key or a pointer event is delivered, what a screenshot is,
/// and what the app's own state looks like.
///
/// <para>The split is measured rather than guessed. Of SdlVulkan.Renderer's original 916-line inspector,
/// only ~48 lines touched SDL at all, and they clustered in exactly three places: the loop hook, input
/// delivery, and framebuffer readback. Those three are this interface; the rest — transport, framing,
/// the command queue, thread marshalling — is the core and is shared.</para>
///
/// <para><b>That measurement was once read too generously, and the correction is worth keeping.</b> Counting
/// lines that TOUCH SDL answers "how surface-coupled is this code", not "can the core carry it". A batch that
/// runs one step per rendered frame calls no SDL function at all, yet it was unshareable for a different
/// reason: it presumes a loop with FRAMES, and the first version of this core could only express commands
/// that finished immediately. So the code looked shareable and was not, and the SDL inspector kept a private
/// scheduler — and, because a scheduler needs feeding, a private copy of the transport too. What closed the
/// gap was <see cref="IDebugInspectorOperation"/>: frame-spanning timing became something the core can
/// express, rather than something a host had to route around.</para>
/// </summary>
public interface IDebugInspectorHost
{
    /// <summary>Names this app in the discovery banner and in a sidecar's listing.</summary>
    string AppName { get; }

    /// <summary>
    /// What KIND of surface this is, so a sidecar can filter the discovery replies to instances it knows how
    /// to drive. The established vocabulary:
    /// <list type="bullet">
    /// <item><c>"tui"</c> — a character-cell terminal (Console.Lib). Speaks a cell grid: screen, row, cell.</item>
    /// <item><c>"sdl"</c> — an SDL-hosted pixel window (SdlVulkan.Renderer). Speaks a framebuffer:
    /// screenshot, and window verbs like minimize.</item>
    /// <item><c>"webgl"</c> — reserved for a browser surface, should WebGl.Renderer ever host one.</item>
    /// </list>
    /// It is free text rather than an enum because the KIND is the host's own claim about itself, and DIR.Lib
    /// should not have to be edited to admit a surface it has never heard of.
    /// <para>
    /// Load-bearing, not decorative: discovery is one shared multicast group, so a terminal app and a GPU app
    /// on the same machine answer the same query. A sidecar that assumed every reply spoke its own verbs
    /// would offer <c>screen</c> to a Vulkan window or <c>minimize</c> to a terminal. This family has already
    /// been bitten by an unfiltered shared broadcast domain once, in LAN peer discovery.
    /// </para>
    /// </summary>
    string SurfaceKind => "unknown";

    /// <summary>
    /// Wakes the host's loop, if it can idle. A pull loop that already ticks on a timer can leave this
    /// empty; an event-driven one must return promptly or a queued command waits for unrelated input.
    /// </summary>
    void Poke();

    /// <summary>
    /// Runs one command ON THE HOST'S OWN THREAD and returns the JSON fragment for the response's
    /// <c>result</c> field, or null for "no such method" (which the core turns into an error reply).
    /// <para>
    /// The host owns its method table rather than the core imposing one, because the useful verbs differ
    /// per surface: a terminal has a cell grid to report and no window to minimise.
    /// </para>
    /// <para>
    /// Only ever called for verbs that finish immediately. Anything that spans frames is declared through
    /// <see cref="IDebugInspectorSteppedHost"/> instead and never reaches here.
    /// </para>
    /// </summary>
    string? Invoke(string method, JsonElement parameters);

    /// <summary>
    /// Extra members for the discovery reply, as a pre-escaped JSON fragment WITHOUT the leading comma
    /// (e.g. <c>"title":"Chess","startedAt":"..."</c>), or null for none. Read afresh per reply, so a value
    /// that changes while the app runs — a window title — stays current.
    /// <para>
    /// The registry descriptor carries the same members. There it is read on the host's own thread from
    /// <see cref="DebugInspectorCore.Pump"/>, at most once a second, and the file is rewritten only when the
    /// fragment changed. Do not emit a member the core writes itself (<c>app</c>, <c>kind</c>, <c>tcpPort</c>,
    /// <c>pid</c>, <c>proto</c>, <c>processStart</c>): a reader would see it twice.
    /// </para>
    /// <para>
    /// A raw fragment rather than a dictionary because this file never asks the framework serializer for a
    /// type (see <see cref="DebugInspectorCore.Quote"/>); build it with that method.
    /// </para>
    /// </summary>
    string? DiscoveryExtras => null;
}

/// <summary>
/// A command that cannot finish inside one <see cref="DebugInspectorCore.Pump"/> — it is advanced once per
/// pump until it reports a result.
///
/// <para><b>Why the core models this rather than leaving it to the host.</b> A surface-agnostic pump that
/// only ran instantaneous commands forced any host with frame-spanning commands to keep a second, private
/// scheduler, and with it a second copy of the transport that fed it. Both kinds of timing below are
/// expressible here, so there is one scheduler and one transport.</para>
/// </summary>
public interface IDebugInspectorOperation
{
    /// <summary>
    /// Whether this operation OWNS the pump until it finishes.
    ///
    /// <para><c>true</c> — nothing else runs while it is in flight, so exactly one step happens per host
    /// iteration and a real frame renders in between. This is what makes a batched zoom-then-read observe the
    /// zoom rather than racing it.</para>
    ///
    /// <para><c>false</c> — it advances every pump but does NOT block the queue, so observe verbs
    /// (<c>ping</c>, a screenshot) are answered WHILE it runs. That is the entire point of a press-and-hold:
    /// inspecting the UI the hold has put on screen, mid-hold.</para>
    /// </summary>
    bool Exclusive { get; }

    /// <summary>
    /// How long a waiting client is given before the core reports a timeout. Distinct from
    /// <see cref="DebugInspectorCore.CommandTimeout"/>, which cannot be right for both an instantaneous read
    /// and a five-minute hold.
    /// </summary>
    TimeSpan Timeout { get; }

    /// <summary>
    /// Performs one pump's worth of work. Returns the JSON <c>result</c> fragment to FINISH, or null to be
    /// advanced again next pump. The core pokes the host between advances, so an idling loop keeps turning
    /// without the operation arranging it.
    /// </summary>
    string? Advance();
}

/// <summary>
/// Opt-in extension of <see cref="IDebugInspectorHost"/> for a surface with frame-spanning verbs. A host
/// without any — a terminal — implements only <see cref="IDebugInspectorHost"/> and is unaffected.
/// </summary>
public interface IDebugInspectorSteppedHost
{
    /// <summary>
    /// The verbs that span pumps. Consulted BEFORE <see cref="Begin"/>, and the reason this exists
    /// separately: <c>Begin</c> ACTS on the app — a press-and-hold presses the button there — so asking it
    /// speculatively whether it recognises a method would press the button as a side effect of the question.
    /// </summary>
    IReadOnlyCollection<string> SteppedMethods { get; }

    /// <summary>
    /// Starts one of <see cref="SteppedMethods"/>. Called ON THE HOST'S THREAD and only for a method it
    /// declared, so it may touch app state directly — a press-and-hold presses the button here and releases
    /// it in <see cref="IDebugInspectorOperation.Advance"/>. Throwing rejects the request.
    /// </summary>
    IDebugInspectorOperation Begin(string method, JsonElement parameters);
}

/// <summary>
/// The surface-agnostic half of a live debug inspector: a loopback TCP command server speaking
/// newline-delimited JSON, whose commands execute on the host application's own thread.
///
/// <para><b>Protocol.</b> One JSON object per line in, one per line out.
/// <c>{"id":1,"method":"ping","params":{}}</c> → <c>{"id":1,"result":{...}}</c>, or
/// <c>{"id":1,"error":"..."}</c>. On start it prints
/// <c>[inspector] '&lt;app&gt;' command server on 127.0.0.1:&lt;port&gt;</c> to <b>stderr</b>, so a driver
/// greps the port out of a redirected log rather than needing a fixed one.</para>
///
/// <para><b>Threading.</b> The socket loop only ENQUEUES. Every command runs inside <see cref="Pump"/>,
/// which the host calls from whatever thread owns its state — so a command can read and mutate app state
/// without a lock, and without the host needing to be thread-safe. Enqueueing calls
/// <see cref="IDebugInspectorHost.Poke"/> to wake an idle loop.</para>
///
/// <para><b>DEBUG only.</b> The whole file is <c>#if DEBUG</c>, so no release artifact carries a socket
/// server. Bound to loopback regardless.</para>
///
/// <para><b>Discovery.</b> By default a started core is findable on its own machine through a per-user FILE
/// registry (<see cref="RegistryDirectory"/>): one descriptor file per instance, written at start and deleted
/// on <see cref="Dispose"/>. That opens no socket beyond the loopback listener, and a loopback bind never
/// makes Windows Firewall prompt. UDP multicast discovery, which binds every interface and DOES prompt once
/// per executable path, answers only when asked for (<see cref="DebugInspectorDiscovery.Lan"/>, or the
/// <see cref="LanDiscoveryVariable"/> environment variable). The registry's format is the contract the
/// sidecars implement; it is specified in <c>src/DIR.Lib/Diagnostics/inspector-registry.md</c>.</para>
/// </summary>
public sealed class DebugInspectorCore : IDisposable
{
    private const int ProtocolVersion = 1;

    private sealed record Command(string Method, JsonElement Parameters)
    {
        /// <summary>
        /// Completed by the pump once this command has run. Named <c>Completion</c> and NOT <c>Result</c>:
        /// as <c>Result</c>, every use read <c>command.Completion.Task</c>, which is indistinguishable at a
        /// glance from blocking on <c>Task&lt;T&gt;.Result</c> — an invitation to "simplify" it into a real
        /// sync-over-async deadlock.
        /// </summary>
        public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Ticks rather than a TimeSpan? because a nullable struct cannot be volatile, and this crosses
        // threads: written on the host thread when the pump claims the command, read on the socket thread
        // that is waiting. Zero means "not a multi-pump operation".
        private long _runningTimeoutTicks;

        /// <summary>
        /// Set by the pump once this turns out to be a multi-pump operation, so the socket side can extend
        /// its patience past <see cref="CommandTimeout"/> — a press-and-hold may legitimately run for minutes.
        /// </summary>
        public TimeSpan? RunningTimeout
        {
            get
            {
                var ticks = Interlocked.Read(ref _runningTimeoutTicks);
                return ticks == 0 ? null : TimeSpan.FromTicks(ticks);
            }
            set => Interlocked.Exchange(ref _runningTimeoutTicks, value?.Ticks ?? 0);
        }
    }

    /// <summary>An operation in flight, paired with the request that is waiting on it.</summary>
    private sealed record Running(Command Command, IDebugInspectorOperation Operation);

    private readonly IDebugInspectorHost _host;
    private readonly ConcurrentQueue<Command> _queue = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly TcpListener? _listener;

    // Both are touched only on the host's thread, inside Pump.
    private Running? _exclusive;
    private Running? _background;

    /// <summary>How long a socket client waits for a command to reach the host's thread.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The ephemeral port the command server bound to.</summary>
    public int Port { get; }

    /// <summary>
    /// How this core was asked to be findable, after <see cref="LanDiscoveryVariable"/> is applied. Each part
    /// is best-effort: a registry folder that cannot be written leaves <see cref="RegistryFile"/> null, and a
    /// multicast bind that fails is reported on stderr, and either way the command server keeps working.
    /// </summary>
    public DebugInspectorDiscovery Discovery { get; }

    /// <summary>This core's descriptor in the registry, or null when it is not registered.</summary>
    public string? RegistryFile => _registration?.Path;

    /// <summary>Whether the UDP multicast responder was started. Only ever true with <see cref="DebugInspectorDiscovery.Lan"/>.</summary>
    internal bool LanResponderStarted => _discovery is not null;

    private DebugInspectorRegistration? _registration;

    // Host thread only, inside Pump: when the registry descriptor's extras are next read.
    private long _nextRegistryRefresh;

    private DebugInspectorCore(IDebugInspectorHost host, TcpListener? listener, int port, DebugInspectorDiscovery discovery)
    {
        _host = host;
        _listener = listener;
        Port = port;
        Discovery = discovery;
    }

    /// <summary>
    /// A core with NO transport: no TCP listener, no multicast bind, no registry file, no banner. Commands
    /// arrive through <see cref="Submit"/> instead of a socket.
    ///
    /// <para>This exists so the SCHEDULING can be exercised without a port. What a batch or a press-and-hold
    /// does is ordinary logic — one step per pump, who owns the queue, what happens when two collide — and
    /// making each of those assertions depend on port availability and joining a multicast group would tie
    /// them to something they are not about. It is also a usable in-process driver in its own right.</para>
    /// </summary>
    public static DebugInspectorCore Detached(IDebugInspectorHost host) => new(host, null, 0, DebugInspectorDiscovery.None);

    /// <summary>
    /// Queues a command exactly as an arriving request would, completing when a <see cref="Pump"/> runs it.
    /// The result is the JSON <c>result</c> fragment; an empty string means the host did not know the method.
    /// </summary>
    public Task<string> Submit(string method, JsonElement parameters)
        => Enqueue(method, parameters).Completion.Task;

    /// <summary>Queues a command and wakes the host. The one path onto the queue, socket or not.</summary>
    private Command Enqueue(string method, JsonElement parameters)
    {
        var command = new Command(method, parameters);
        _queue.Enqueue(command);
        _host.Poke();
        return command;
    }

    /// <summary>
    /// Starts the command server on an ephemeral loopback port and announces it on stderr. The host must
    /// then call <see cref="Pump"/> from its loop, or no command will ever run.
    /// <para>
    /// This overload's signature is kept exactly as it was, so already-compiled consumers keep binding to it;
    /// only what <c>true</c> means changed (see the parameter).
    /// </para>
    /// </summary>
    /// <param name="host">The surface being driven.</param>
    /// <param name="enableDiscovery">
    /// Whether the instance can be found at all. On by default, because being found without being told a port
    /// is the point. <c>true</c> registers it in the per-user file registry
    /// (<see cref="DebugInspectorDiscovery.Registry"/>), plus LAN multicast discovery when
    /// <see cref="LanDiscoveryVariable"/> asks for it. <c>false</c> makes it not findable at all, whatever the
    /// environment says: the command server keeps working and is merely unadvertised.
    /// </param>
    public static DebugInspectorCore Start(IDebugInspectorHost host, bool enableDiscovery = true)
        => Start(host, Requested(enableDiscovery));

    /// <summary>
    /// Starts the command server and makes the instance findable as <paramref name="discovery"/> asks. Use
    /// <see cref="DebugInspectorDiscovery.Registry"/> | <see cref="DebugInspectorDiscovery.Lan"/> for a host
    /// that must be found from another machine. <see cref="LanDiscoveryVariable"/> can add
    /// <see cref="DebugInspectorDiscovery.Lan"/> to anything but <see cref="DebugInspectorDiscovery.None"/>.
    /// </summary>
    public static DebugInspectorCore Start(IDebugInspectorHost host, DebugInspectorDiscovery discovery)
        => StartExactly(host, WithEnvironment(discovery, Environment.GetEnvironmentVariable(LanDiscoveryVariable)),
            RegistryDirectory);

    /// <summary>What <see cref="Start(IDebugInspectorHost, bool)"/> asks for, before the environment.</summary>
    internal static DebugInspectorDiscovery Requested(bool enableDiscovery)
        => enableDiscovery ? DebugInspectorDiscovery.Registry : DebugInspectorDiscovery.None;

    /// <summary>
    /// Applies <see cref="LanDiscoveryVariable"/>: <c>1</c> or <c>true</c> adds
    /// <see cref="DebugInspectorDiscovery.Lan"/>. It never makes a <see cref="DebugInspectorDiscovery.None"/>
    /// core findable, because "not findable at all" is a host's decision (an app in a local-only mode), and an
    /// environment variable left set in a shell must not override it.
    /// </summary>
    internal static DebugInspectorDiscovery WithEnvironment(DebugInspectorDiscovery requested, string? lanVariable)
        => requested != DebugInspectorDiscovery.None
           && (lanVariable == "1" || string.Equals(lanVariable, "true", StringComparison.OrdinalIgnoreCase))
            ? requested | DebugInspectorDiscovery.Lan
            : requested;

    /// <summary>
    /// Starts with exactly <paramref name="discovery"/>, reading no environment, and registers in
    /// <paramref name="registryDirectory"/>. The seam tests use, so they never touch the real per-user registry.
    /// </summary>
    internal static DebugInspectorCore StartExactly(
        IDebugInspectorHost host, DebugInspectorDiscovery discovery, string? registryDirectory)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var core = new DebugInspectorCore(host, listener, port, discovery);

        // stderr, not stdout: a TUI owns stdout, and writing the banner there would corrupt the screen.
        // Everything after the port is a parenthesised suffix, so a driver grepping the port is unaffected.
        var suffix = discovery switch
        {
            DebugInspectorDiscovery.None => " (discovery off)",
            DebugInspectorDiscovery.Lan => $" (LAN discovery on {DiscoveryGroup}:{DiscoveryPortNumber})",
            DebugInspectorDiscovery.Registry => " (registry)",
            _ => $" (registry, LAN discovery on {DiscoveryGroup}:{DiscoveryPortNumber})",
        };
        Console.Error.WriteLine($"[inspector] '{host.AppName}' command server on 127.0.0.1:{port}{suffix}");

        if (discovery.HasFlag(DebugInspectorDiscovery.Registry))
        {
            core.Register(registryDirectory);
        }
        Console.Error.Flush();

        _ = Task.Run(core.AcceptLoopAsync);
        if (discovery.HasFlag(DebugInspectorDiscovery.Lan))
        {
            core._discovery = Task.Run(core.DiscoveryLoopAsync);
        }
        return core;
    }

    /// <summary>
    /// The environment variable that turns LAN multicast discovery on without a consumer release:
    /// <c>DIR_LIB_INSPECTOR_LAN=1</c> (or <c>true</c>). Read once, when a core starts.
    /// </summary>
    public const string LanDiscoveryVariable = "DIR_LIB_INSPECTOR_LAN";

    /// <summary>
    /// The per-user registry folder: <c>DIR.Lib/inspector</c> under
    /// <see cref="Environment.SpecialFolder.LocalApplicationData"/>. That resolves to
    /// <c>%LOCALAPPDATA%\DIR.Lib\inspector</c> on Windows, <c>$XDG_DATA_HOME/DIR.Lib/inspector</c> (by default
    /// <c>~/.local/share/DIR.Lib/inspector</c>) on Linux, and <c>~/Library/Application Support/DIR.Lib/inspector</c>
    /// on macOS (.NET 8 and later). Null only where the OS has no per-user data folder to name at all, in
    /// which case nothing registers.
    /// <para>
    /// Resolved with <see cref="Environment.SpecialFolderOption.DoNotVerify"/>. The default option answers ""
    /// on Unix for a folder that does not exist yet, and <c>~/.local/share</c> does not exist on a fresh
    /// account, in a container or on a headless board: such an instance registered nothing and, with LAN
    /// discovery off by default, could not be found at all. The writer creates the folder; it needs the path.
    /// </para>
    /// <para>
    /// Deliberately not the temp folder: Windows Storage Sense empties <c>%TEMP%</c> when the disk runs low,
    /// which would make a live instance vanish from every sidecar.
    /// </para>
    /// </summary>
    public static string? RegistryDirectory => ResolveRegistryDirectory(Environment.GetFolderPath);

    /// <summary><see cref="RegistryDirectory"/> over a given <c>GetFolderPath</c>, so tests can play another OS.</summary>
    internal static string? ResolveRegistryDirectory(
        Func<Environment.SpecialFolder, Environment.SpecialFolderOption, string> getFolderPath)
        => getFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify)
            is { Length: > 0 } root
            ? Path.Combine(root, "DIR.Lib", "inspector")
            : null;

    /// <summary>
    /// Writes this core's descriptor. Best-effort like the multicast bind: a registry that cannot be written,
    /// for whatever reason, is reported on stderr and the command server carries on unregistered. A debugging
    /// aid must never be why a DEBUG app fails to start.
    /// </summary>
    private void Register(string? registryDirectory)
    {
        if (registryDirectory is null)
        {
            Console.Error.WriteLine("[inspector] not registered: no per-user data folder on this system");
            return;
        }

        try
        {
            _registration = DebugInspectorRegistration.Create(registryDirectory, Port, Descriptor(forRegistry: true));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[inspector] not registered in {registryDirectory}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The JSON object that describes this instance. The multicast reply is byte-for-byte what it always
    /// was; the registry descriptor adds <c>processStart</c> (and, on Linux, <c>bootStartTicks</c>), so a reader
    /// can tell this process from a later one that was handed the same pid.
    /// <para>
    /// Extras are read afresh each call, so a value that changes while the app runs (a window title) is
    /// current. A host's own accessor that throws must not silence discovery, so it costs only the extras.
    /// </para>
    /// </summary>
    private string Descriptor(bool forRegistry)
    {
        string extras;
        try { extras = _host.DiscoveryExtras is { Length: > 0 } e ? "," + e : ""; }
        catch { extras = ""; }

        var processStart = forRegistry && DebugInspectorRegistration.FormatProcessStart() is { } started
            ? $",\"processStart\":{Quote(started)}"
            : "";

        // Linux only: BootStartTicks is null elsewhere, so the member is absent there.
        var bootStart = forRegistry && DebugInspectorRegistration.BootStartTicks is { } ticks
            ? $",\"bootStartTicks\":{ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : "";

        return $"{{\"app\":{Quote(_host.AppName)},\"kind\":{Quote(_host.SurfaceKind)}," +
               $"\"tcpPort\":{Port},\"pid\":{Environment.ProcessId},\"proto\":{ProtocolVersion}" +
               processStart + bootStart + extras + "}";
    }

    /// <summary>How often <see cref="Pump"/> re-reads the extras for the registry descriptor.</summary>
    private const long RegistryRefreshMilliseconds = 1000;

    /// <summary>
    /// Re-reads the descriptor at most once a second and hands it to the registration, which rewrites the
    /// file only if it changed and does so off this thread. Read HERE rather than on a timer so the host's
    /// extras accessor runs on the host's own thread, like every command.
    /// </summary>
    private void RefreshRegistration()
    {
        if (_registration is not { } registration) return;

        var now = Environment.TickCount64;
        if (now < _nextRegistryRefresh) return;
        _nextRegistryRefresh = now + RegistryRefreshMilliseconds;

        registration.Offer(Descriptor(forRegistry: true));
    }

    /// <summary>
    /// Runs queued commands on the calling thread. The host calls this once per loop iteration; it returns
    /// immediately when there is nothing to do, so it is safe on a hot path.
    ///
    /// <para>The order of the three blocks below IS the scheduling contract. An exclusive operation is
    /// advanced and the pump returns, so nothing overtakes it. A background operation is advanced and the
    /// pump falls THROUGH, so commands queued behind it are still answered while it runs. Only then is the
    /// queue drained.</para>
    /// </summary>
    public void Pump()
    {
        RefreshRegistration();

        if (_exclusive is not null)
        {
            Advance(ref _exclusive);
            return;
        }

        if (_background is not null)
        {
            Advance(ref _background);
        }

        while (_queue.TryDequeue(out var command))
        {
            try
            {
                if (StartOperation(command))
                {
                    // An exclusive operation must not share this pump with the commands behind it.
                    if (_exclusive is not null) return;
                    continue;
                }

                var result = command.Method == "ping"
                    ? $"{{\"ok\":true,\"protocol\":{ProtocolVersion},\"app\":{Quote(_host.AppName)}}}"
                    : _host.Invoke(command.Method, command.Parameters);

                command.Completion.TrySetResult(result ?? "");
            }
            catch (Exception ex)
            {
                // A command must never take the app down: report it and keep serving.
                command.Completion.TrySetException(ex);
            }
        }
    }

    /// <summary>
    /// Advances one in-flight operation, completing and clearing it when it reports a result. An operation
    /// that throws fails its own request and is cleared, so a broken one cannot wedge the pump forever.
    /// </summary>
    private void Advance(ref Running? slot)
    {
        var running = slot!;
        try
        {
            if (running.Operation.Advance() is { } result)
            {
                slot = null;
                running.Command.Completion.TrySetResult(result);
            }
            else
            {
                // Keep an idling host turning; an operation should not have to know how.
                _host.Poke();
            }
        }
        catch (Exception ex)
        {
            slot = null;
            running.Command.Completion.TrySetException(ex);
        }
    }

    /// <summary>
    /// Claims <paramref name="command"/> as a multi-pump operation if it is one — the core's own
    /// <c>batch</c>, or anything the host's <see cref="IDebugInspectorSteppedHost.Begin"/> accepts. Returns
    /// false for an ordinary command, which the caller then runs through <c>Invoke</c>.
    /// </summary>
    private bool StartOperation(Command command)
    {
        IDebugInspectorOperation operation;
        if (command.Method == "batch")
        {
            operation = BuildBatch(command.Parameters);
        }
        else if (_host is IDebugInspectorSteppedHost stepped
                 && stepped.SteppedMethods.Contains(command.Method))
        {
            operation = stepped.Begin(command.Method, command.Parameters);
        }
        else
        {
            return false;
        }

        // One background operation at a time, and an exclusive one may not start on top of a background one:
        // a batch stepping the UI while a button is held would interleave two scripts on one surface, and the
        // result would depend on frame timing. Rejected loudly instead.
        if (_background is { } active)
        {
            command.Completion.TrySetException(new InvalidOperationException(
                operation.Exclusive
                    ? $"cannot start '{command.Method}' while '{active.Command.Method}' is in progress"
                    : $"'{active.Command.Method}' is already in progress"));
            return true;
        }

        var running = new Running(command, operation);
        command.RunningTimeout = operation.Timeout;

        if (operation.Exclusive)
        {
            _exclusive = running;
            Advance(ref _exclusive);
        }
        else
        {
            _background = running;
            Advance(ref _background);
        }
        return true;
    }

    /// <summary>
    /// The core's own <c>batch</c>: run each step one per host iteration, so a real frame renders between
    /// them. Pure scheduling with nothing surface-specific in it, which is why it lives here rather than
    /// being re-implemented per surface — and why <c>wait</c> exists only as a step inside it.
    /// </summary>
    private BatchOperation BuildBatch(JsonElement p)
    {
        if (!p.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("batch requires a 'steps' array of {method, params}");

        var list = new List<(string Method, JsonElement Parameters)>(steps.GetArrayLength());
        foreach (var step in steps.EnumerateArray())
        {
            var m = step.GetProperty("method").GetString() ?? "";
            // A batch inside a batch would need a scheduler stack for no use case anyone has had.
            if (m == "batch") throw new ArgumentException("nested batch is not supported");
            list.Add((m, step.TryGetProperty("params", out var sp)
                ? sp.Clone()
                : JsonDocument.Parse("{}").RootElement.Clone()));
        }

        if (list.Count == 0) throw new ArgumentException("batch 'steps' must be non-empty");
        return new BatchOperation(this, list);
    }

    private sealed class BatchOperation(DebugInspectorCore core,
        List<(string Method, JsonElement Parameters)> steps) : IDebugInspectorOperation
    {
        private readonly List<string> _results = [];
        private int _index;
        private int _waitFrames;

        public bool Exclusive => true;

        // Roughly a frame per step plus whatever it waits, padded and capped. This is only the client's
        // patience: the host's loop stays responsive throughout either way.
        public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Min(300,
            15 + steps.Count + TotalWaitFrames() / 30.0));

        public string? Advance()
        {
            if (_waitFrames > 0)
            {
                _waitFrames--;
                return null;
            }

            if (_index < steps.Count)
            {
                var (method, parameters) = steps[_index++];
                if (method == "wait")
                {
                    // The frame this step consumed counts as the first waited one.
                    _waitFrames = Math.Max(0, WaitFrames(parameters) - 1);
                    _results.Add("\"waited\"");
                }
                else
                {
                    // A step's failure is recorded and the batch continues: a 20-step script should report
                    // which step broke, not collapse to one error with no trace of the other 19.
                    try { _results.Add(core.RunStep(method, parameters)); }
                    catch (Exception ex) { _results.Add(Quote($"error: {ex.Message}")); }
                }
            }

            return _index >= steps.Count && _waitFrames == 0
                ? "[" + string.Join(",", _results) + "]"
                : null;
        }

        private int TotalWaitFrames()
        {
            var total = 0;
            foreach (var (method, parameters) in steps)
            {
                if (method == "wait") total += WaitFrames(parameters);
            }
            return total;
        }

        private static int WaitFrames(JsonElement p)
            => p.ValueKind == JsonValueKind.Object && p.TryGetProperty("frames", out var f)
                && f.ValueKind == JsonValueKind.Number
                ? Math.Clamp(f.GetInt32(), 1, 600) : 1;
    }

    /// <summary>
    /// Runs one batch step. A step must be instantaneous: a frame-spanning verb inside a batch would need
    /// nested scheduling, and the two would fight over who owns the pump.
    /// </summary>
    private string RunStep(string method, JsonElement parameters)
    {
        if (method == "ping")
            return $"{{\"ok\":true,\"protocol\":{ProtocolVersion},\"app\":{Quote(_host.AppName)}}}";

        // Asked of the DECLARED set, never by calling Begin: probing with Begin would start the very thing
        // being rejected.
        if (_host is IDebugInspectorSteppedHost stepped && stepped.SteppedMethods.Contains(method))
            throw new InvalidOperationException($"'{method}' spans frames and cannot be a batch step");

        return _host.Invoke(method, parameters) ?? throw new ArgumentException($"unknown method '{method}'");
    }

    /// <summary>
    /// A JSON string literal, escaped by hand.
    /// <para>
    /// Deliberately not the framework serializer: a trimmed or AOT-configured app sets
    /// <c>JsonSerializerIsReflectionEnabledByDefault=false</c>, and the generic serialize overload then
    /// throws at runtime even for a plain string — which is what Chess.Console did the first time it
    /// answered a request. Every AOT-compatible consumer in this family would hit it, so the inspector
    /// escapes its own strings and never asks the serializer for a type.
    /// </para>
    /// </summary>
    public static string Quote(string? value)
    {
        if (value is null) return "null";

        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>The multicast group discovery queries arrive on. Site-local; not 5353, not DNS-SD.</summary>
    public static readonly IPAddress DiscoveryGroup = IPAddress.Parse("239.255.77.91");

    /// <summary>
    /// The discovery port — now the only one. It was chosen to avoid SdlVulkan.Renderer's 47891 while that
    /// inspector still spoke its own <c>sdlvk-inspect</c> dialect on its own port; since it moved onto this
    /// core there is one token, one group and one port, and a sidecar tells surfaces apart by
    /// <see cref="IDebugInspectorHost.SurfaceKind"/> instead of by which port answered.
    /// <para>
    /// Which is what the kind field is for. Sharing a port only works because every reply is self-describing;
    /// without that a terminal and a GPU window answering the same query would be indistinguishable.
    /// </para>
    /// </summary>
    public const int DiscoveryPortNumber = 47892;

    /// <summary>The query a sidecar multicasts; a reply is sent unicast back to its source.</summary>
    public const string DiscoveryQueryToken = "dir-inspect";

    private Task? _discovery;

    /// <summary>
    /// Answers discovery queries so a sidecar can find this instance without being told a port. Best-effort:
    /// if the socket cannot bind (another process holds it, or the environment forbids multicast) the command
    /// server keeps working and only discovery is lost, which is why the failure is reported rather than
    /// thrown.
    /// <para>
    /// Started only for <see cref="DebugInspectorDiscovery.Lan"/>. The bind below is on every interface, and
    /// Windows Firewall prompts for that once per executable path; that prompt, raised by every DEBUG build at
    /// a new path, is why this is opt-in and the registry is the default.
    /// </para>
    /// </summary>
    private async Task DiscoveryLoopAsync()
    {
        using var udp = new UdpClient { ExclusiveAddressUse = false };
        try
        {
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPortNumber));
            udp.JoinMulticastGroup(DiscoveryGroup);
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"[inspector] discovery disabled (bind failed): {ex.Message}");
            Console.Error.Flush();
            return;
        }

        while (!_stopping.IsCancellationRequested)
        {
            UdpReceiveResult recv;
            try { recv = await udp.ReceiveAsync(_stopping.Token); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            if (!IsDiscoveryQuery(recv.Buffer)) continue;

            // Built per reply, not once: an extra like the window title changes while the app runs, and a
            // descriptor cached at startup would answer with a stale one forever.
            var descriptor = Encoding.UTF8.GetBytes(Descriptor(forRegistry: false));
            try { await udp.SendAsync(descriptor, descriptor.Length, recv.RemoteEndPoint); }
            catch (SocketException) { /* reply is best-effort */ }
        }
    }

    private static bool IsDiscoveryQuery(byte[] buffer)
    {
        try
        {
            using var doc = JsonDocument.Parse(buffer);
            return doc.RootElement.TryGetProperty("q", out var q)
                && q.ValueKind == JsonValueKind.String
                && q.GetString() == DiscoveryQueryToken;
        }
        catch { return false; }
    }

    private async Task AcceptLoopAsync()
    {
        // Only ever started by Start, which always has a listener; Detached has none and never gets here.
        var listener = _listener!;
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(_stopping.Token);
                _ = Task.Run(async () =>
                {
                    // A serve loop that dies silently looks identical, from the client, to a server that
                    // dropped the connection on purpose. Say why on stderr instead.
                    try
                    {
                        await ServeAsync(client);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[inspector] connection failed: {ex.GetType().Name}: {ex.Message}");
                        Console.Error.Flush();
                    }
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            while (!_stopping.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_stopping.Token);
                if (line is null) return;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var (id, response) = await HandleAsync(line);
                await writer.WriteLineAsync(
                    response.StartsWith('!')
                        ? $"{{\"id\":{id},\"error\":{Quote(response[1..])}}}"
                        : $"{{\"id\":{id},\"result\":{response}}}");
            }
        }
    }

    private async Task<(int Id, string Response)> HandleAsync(string line)
    {
        int id = 0;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            id = root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var i) ? i : 0;

            if (!root.TryGetProperty("method", out var methodEl) || methodEl.GetString() is not { } method)
            {
                return (id, "!missing method");
            }

            // The params element must outlive the JsonDocument, which is disposed when this returns —
            // so clone it rather than handing the host a window into a freed buffer.
            var parameters = root.TryGetProperty("params", out var p)
                ? p.Clone()
                : JsonDocument.Parse("{}").RootElement.Clone();

            var command = Enqueue(method, parameters);

            // Two stages rather than one poll loop. An operation is claimed within a pump or two, so by the
            // time the ordinary timeout lapses the pump has already published the longer deadline if there
            // is one — and a verb that legitimately runs for minutes (a press-and-hold) must not be cut off
            // at ten seconds, while a host that simply never pumps must still fail fast.
            var completed = await Task.WhenAny(command.Completion.Task, Task.Delay(CommandTimeout, _stopping.Token));
            if (completed != command.Completion.Task)
            {
                if (command.RunningTimeout is not { } extended)
                {
                    return (id, "!timed out waiting for the host loop — is Pump being called?");
                }

                var remaining = extended - CommandTimeout;
                if (remaining > TimeSpan.Zero)
                {
                    completed = await Task.WhenAny(command.Completion.Task, Task.Delay(remaining, _stopping.Token));
                }
                if (completed != command.Completion.Task)
                {
                    return (id, $"!'{method}' did not finish within {extended.TotalSeconds:0}s");
                }
            }

            var result = await command.Completion.Task;
            return (id, string.IsNullOrEmpty(result) ? $"!unknown method '{method}'" : result);
        }
        catch (JsonException ex)
        {
            return (id, $"!malformed request: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (id, $"!{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the command server and the multicast responder, and deletes the registry descriptor, so a sidecar
    /// stops listing this instance the moment it is gone rather than when its pid is found dead.
    /// </summary>
    public void Dispose()
    {
        _stopping.Cancel();
        _registration?.Close();
        try { _listener?.Stop(); } catch (SocketException) { }
        try { _discovery?.Wait(TimeSpan.FromSeconds(1)); } catch { /* best-effort */ }
        _stopping.Dispose();
    }
}
#endif
