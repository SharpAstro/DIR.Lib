// Compiles against the Debug twin package. Since 11.70 the inspector core is in the Release package too, so this
// no longer proves the twin is needed; it proves the twin still packs and exposes DIR.Lib.Diagnostics.
using DIR.Lib.Diagnostics;

Console.WriteLine(typeof(DebugInspectorCore).FullName);
