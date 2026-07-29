#if DEBUG
using System;
using System.Collections.Concurrent;
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
/// </summary>
public interface IDebugInspectorHost
{
    /// <summary>Names this app in the discovery banner and in a sidecar's listing.</summary>
    string AppName { get; }

    /// <summary>
    /// What KIND of surface this is — <c>"console"</c>, <c>"pixel"</c>. Carried in the discovery reply so a
    /// sidecar can filter to instances it knows how to drive.
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
    /// </summary>
    string? Invoke(string method, JsonElement parameters);
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
        public TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly IDebugInspectorHost _host;
    private readonly ConcurrentQueue<Command> _queue = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly TcpListener _listener;

    /// <summary>How long a socket client waits for a command to reach the host's thread.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The ephemeral port the command server bound to.</summary>
    public int Port { get; }

    private DebugInspectorCore(IDebugInspectorHost host, TcpListener listener, int port)
    {
        _host = host;
        _listener = listener;
        Port = port;
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
    /// Runs every queued command on the calling thread. The host calls this once per loop iteration; it
    /// returns immediately when the queue is empty, so it is safe on a hot path.
    /// </summary>
    public void Pump()
    {
        while (_queue.TryDequeue(out var command))
        {
            try
            {
                var result = command.Method == "ping"
                    ? $"{{\"ok\":true,\"protocol\":{ProtocolVersion},\"app\":{Quote(_host.AppName)}}}"
                    : _host.Invoke(command.Method, command.Parameters);

                command.Result.TrySetResult(result ?? "");
            }
            catch (Exception ex)
            {
                // A command must never take the app down: report it and keep serving.
                command.Result.TrySetException(ex);
            }
        }
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

        var descriptor = Encoding.UTF8.GetBytes(
            $"{{\"app\":{Quote(_host.AppName)},\"kind\":{Quote(_host.SurfaceKind)}," +
            $"\"tcpPort\":{Port},\"pid\":{Environment.ProcessId},\"proto\":{ProtocolVersion}}}");

        while (!_stopping.IsCancellationRequested)
        {
            UdpReceiveResult recv;
            try { recv = await udp.ReceiveAsync(_stopping.Token); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            if (!IsDiscoveryQuery(recv.Buffer)) continue;

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
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
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

            var command = new Command(method, parameters);
            _queue.Enqueue(command);
            _host.Poke();

            var completed = await Task.WhenAny(command.Result.Task, Task.Delay(CommandTimeout, _stopping.Token));
            if (completed != command.Result.Task)
            {
                return (id, "!timed out waiting for the host loop — is Pump being called?");
            }

            var result = await command.Result.Task;
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
        try { _listener.Stop(); } catch (SocketException) { }
        try { _discovery?.Wait(TimeSpan.FromSeconds(1)); } catch { /* best-effort */ }
        _stopping.Dispose();
    }
}
#endif
