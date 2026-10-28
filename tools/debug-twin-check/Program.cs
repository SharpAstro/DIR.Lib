// Compiles only against a Debug-built DIR.Lib: DebugInspectorCore is #if DEBUG, so the Release package
// has no DIR.Lib.Diagnostics namespace at all.
using DIR.Lib.Diagnostics;

Console.WriteLine(typeof(DebugInspectorCore).FullName);
