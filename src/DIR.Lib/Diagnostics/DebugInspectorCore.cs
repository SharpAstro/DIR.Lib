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

    private DebugInspectorCore(IDebugInspectorHost host, TcpListener? listener, int port)
    {
        _host = host;
        _listener = listener;
        Port = port;
    }

    /// <summary>
    /// A core with NO transport: no TCP listener, no multicast bind, no banner. Commands arrive through
    /// <see cref="Submit"/> instead of a socket.
    ///
    /// <para>This exists so the SCHEDULING can be exercised without a port. What a batch or a press-and-hold
    /// does is ordinary logic — one step per pump, who owns the queue, what happens when two collide — and
    /// making each of those assertions depend on port availability and joining a multicast group would tie
    /// them to something they are not about. It is also a usable in-process driver in its own right.</para>
    /// </summary>
    public static DebugInspectorCore Detached(IDebugInspectorHost host) => new(host, null, 0);

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
    /// </summary>
    public static DebugInspectorCore Start(IDebugInspectorHost host)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var core = new DebugInspectorCore(host, listener, port);

        // stderr, not stdout: a TUI owns stdout, and writing the banner there would corrupt the screen.
        Console.Error.WriteLine($"[inspector] '{host.AppName}' command server on 127.0.0.1:{port}");
        Console.Error.Flush();

        _ = Task.Run(core.AcceptLoopAsync);
        core._discovery = Task.Run(core.DiscoveryLoopAsync);
        return core;
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
    /// The discovery port. Deliberately NOT SdlVulkan.Renderer's 47891: until that inspector migrates onto
    /// this core the two speak different query tokens, and sharing a port would mean each sidecar receiving
    /// queries it must parse and ignore. One port per protocol is cheaper to reason about than one port with
    /// two dialects.
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

        var fixedPart =
            $"{{\"app\":{Quote(_host.AppName)},\"kind\":{Quote(_host.SurfaceKind)}," +
            $"\"tcpPort\":{Port},\"pid\":{Environment.ProcessId},\"proto\":{ProtocolVersion}";

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
            string extras;
            try { extras = _host.DiscoveryExtras is { Length: > 0 } e ? "," + e : ""; }
            catch { extras = ""; }   // a host's own accessor must not silence discovery

            var descriptor = Encoding.UTF8.GetBytes(fixedPart + extras + "}");
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

    public void Dispose()
    {
        _stopping.Cancel();
        try { _listener?.Stop(); } catch (SocketException) { }
        try { _discovery?.Wait(TimeSpan.FromSeconds(1)); } catch { /* best-effort */ }
        _stopping.Dispose();
    }
}
#endif
