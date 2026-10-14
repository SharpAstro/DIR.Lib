# The inspector registry

How a DEBUG app with a DIR.Lib debug inspector (`DebugInspectorCore`) is found on its own machine, from DIR.Lib 11.21
on. This file is the contract: the sidecars that read the registry (`SdlVulkan.Renderer.Inspector`,
`Console.Lib.Inspector`) do not reference DIR.Lib, so they implement what is written here, and the writer
(`DebugInspectorCore.cs`, `DebugInspectorRegistry.cs`) must not drift from it.

## Why a folder of files

The command server has always been TCP on `127.0.0.1` with an ephemeral port. What made an instance findable was
UDP multicast discovery, which binds `0.0.0.0:47892`, and Windows Firewall prompts once per executable PATH for a
non-loopback bind. Every DEBUG build at a new path (each git worktree) prompted again. A file needs no socket, so
there is nothing to prompt for. Multicast still exists, as opt-in LAN discovery (below).

## Folder

`Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify)`,
then `DIR.Lib`, then `inspector`:

| OS | Resolves to |
| --- | --- |
| Windows | `%LOCALAPPDATA%\DIR.Lib\inspector`, i.e. `C:\Users\<user>\AppData\Local\DIR.Lib\inspector` |
| Linux | `$XDG_DATA_HOME/DIR.Lib/inspector` when `XDG_DATA_HOME` is set, otherwise `~/.local/share/DIR.Lib/inspector`, whether or not that folder exists yet |
| macOS | `~/Library/Application Support/DIR.Lib/inspector` (.NET 8 and later; .NET 7 said `~/.local/share`) |

**`DoNotVerify` is required, on both sides.** With the default `SpecialFolderOption.None`, .NET on Unix answers `""`
for a folder that does not exist yet, and `~/.local/share` does not exist on a fresh account, in a container or on a
headless board. A writer that took that answer registered nothing, and a reader that takes it looks nowhere. The
writer creates the folder when it registers; it only needs the path.

A reader computes the path the same way, on .NET 8 or later, and never hard-codes it.
`DebugInspectorCore.RegistryDirectory` returns it for code that does reference DIR.Lib. Only where the OS has no
per-user data folder to name at all is the answer empty, and then nothing registers. A folder that does not exist
means no instance is registered.

It is per user on purpose (an instance is driven by the user who runs it), and it is NOT the temp folder: Windows
Storage Sense empties `%TEMP%` when the disk runs low, which would make a live instance vanish.

## File name

`<pid>-<tcpPort>.json`, both decimal, e.g. `48120-53817.json`. One file per running core, and a process can host
several (one per window), which is why the port is in the name. A reader considers only names of that shape ending
in `.json`. The writer's in-progress temp file is `<pid>-<tcpPort>.tmp`; ignore it.

## Contents

One JSON object, UTF-8 without a byte order mark, on one line:

```json
{"app":"Chess","kind":"sdl","tcpPort":53817,"pid":48120,"proto":1,"processStart":"2026-10-15T09:12:33.1234567Z","title":"Chess - game 3","startedAt":"2026-10-15T09:12:34.0001234+00:00"}
```

| Member | Type | Meaning |
| --- | --- | --- |
| `app` | string | The host's `AppName`. |
| `kind` | string | The surface kind: `sdl`, `tui`, `webgl` (reserved), or `unknown`. Filter on it exactly as on a multicast reply. |
| `tcpPort` | number | The command server. Connect to `127.0.0.1:<tcpPort>`; the protocol is unchanged. |
| `pid` | number | The process id. The same as in the file name. |
| `proto` | number | The command protocol version, `1`. |
| `processStart` | string | When the process started, as the OS reports it, ISO 8601 round-trip in UTC (`DateTime.ToString("O")`). Registry only: a multicast reply does not carry it. Absent where the platform does not report a start time. |
| anything else | | The host's `DiscoveryExtras`. SdlVulkan.Renderer adds `title` (the window title) and `startedAt` (when the inspector attached). Console.Lib adds none. |

These are the members of the UDP multicast reply, plus `processStart`, so a reader that parses a reply can parse a
descriptor with the same code. Members are only ever added, so a reader ignores what it does not know. A change a
reader could misread would get a new folder, never a reinterpretation of this one.

## Writing

- **When.** At `DebugInspectorCore.Start`, before it returns, so a sidecar launched the moment the app prints its
  banner finds it.
- **Atomically.** The temp file is written, then renamed over the `.json` (replace). A reader sees all of the old
  descriptor or all of the new one, never part of either.
- **The extras change while the app runs** (a window title), so the file carries them and is kept current: the
  host's `Pump` reads them at most once a second, on the host's own thread, and the file is rewritten only when
  they changed. A title is therefore up to about a second stale. The alternative, a sidecar connecting to every
  instance to ask, would make a listing cost one TCP round trip per instance, stall on any instance that is not
  pumping, and need a verb the command protocol does not have.
- **A rewrite a reader blocks is retried.** On Windows the rename fails while ANY process holds the `.json` open,
  whatever it shares, so the writer retries for about 100 ms and then again at the next refresh.
- **Removed** on `Dispose`. If a reader holds the file open without `FileShare.Delete` at that moment (Windows), the
  delete is retried for about 100 ms and then left to the stale-file rule.

## Reading

1. Enumerate `*.json` in the folder. A file that vanishes before it is read (`FileNotFoundException`) is an instance
   that just exited: skip it.
2. Open with `FileShare.ReadWrite | FileShare.Delete`, read, close at once. Sharing `Delete` lets the writer's
   `Dispose` delete the file under you (`File.ReadAllText` shares only `Read`, which blocks it); no sharing mode lets
   a rewrite's rename through on Windows, so hold the file no longer than the read.
3. Skip a file that does not parse, or lacks `pid` or `tcpPort`.
4. Drop it unless the process is live: `Process.GetProcessById(pid)` succeeds and `HasExited` is false, and, when
   `processStart` is present, the process's `StartTime.ToUniversalTime()` is within 2 seconds of it (otherwise the
   pid was reused by a later process). If the start time cannot be read (access denied), the pid alone decides.
5. Filter on `kind`.
6. Connect to `127.0.0.1:<tcpPort>`. A refused connection is an instance that exited after step 4.

Readers never delete; the writers sweep.

## Stale files

A crash skips `Dispose` and leaves a descriptor behind. Readers drop it by the liveness rule above. Each core, when
it starts, also sweeps the folder: it deletes every `<pid>-<port>.json` whose process fails the same rule, and every
`<pid>-<port>.tmp` whose pid is not running. It skips its own pid (other cores in the same process are live) and
never touches a name of any other shape. The sweep is best-effort: a file it cannot read, judge or delete is left
for the next one.

## LAN discovery (opt-in)

UDP multicast discovery, the only discovery before 11.21, now answers only when asked for:

- in code, `DebugInspectorCore.Start(host, DebugInspectorDiscovery.Registry | DebugInspectorDiscovery.Lan)`;
- without a consumer release, the environment variable `DIR_LIB_INSPECTOR_LAN=1` (or `true`), read once at `Start`.

Neither makes a core findable that its host started with `enableDiscovery: false` (`DebugInspectorDiscovery.None`):
that means not findable at all, and a variable left set in a shell must not override a host's local-only decision.

The protocol is unchanged: a sidecar multicasts `{"q":"dir-inspect","proto":1}` to `239.255.77.91:47892` and each
instance replies unicast with its descriptor (without `processStart`). Builds older than 11.21 answer it by default
and do not register, so a sidecar that should find them reads the registry AND multicasts. The same instance then
appears twice when it does both; for replies from this machine, treat the pair `(pid, tcpPort)` as the identity.

The stderr banner says which applies: `[inspector] '<app>' command server on 127.0.0.1:<port>` followed by
` (registry)`, ` (registry, LAN discovery on 239.255.77.91:47892)`, or ` (discovery off)`.
