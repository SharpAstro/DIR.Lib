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
                    ? $"{{\"ok\":true,\"protocol\":{ProtocolVersion},\"app\":{JsonSerializer.Serialize(_host.AppName)}}}"
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

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                _ = Task.Run(() => ServeAsync(client));
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
                        ? $"{{\"id\":{id},\"error\":{JsonSerializer.Serialize(response[1..])}}}"
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
        _stopping.Dispose();
    }
}
#endif
