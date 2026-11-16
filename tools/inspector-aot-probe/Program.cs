#if WITH_INSPECTOR
using System.Text.Json;
using DIR.Lib.Diagnostics;

using var core = DebugInspectorCore.StartIfOptedIn(new ProbeHost());
Console.WriteLine(core is null ? "inspector: not started" : "inspector: started");
return 0;

sealed class ProbeHost : IDebugInspectorHost
{
    public string AppName => "aotprobe";
    public void Poke() { }
    public string? Invoke(string method, JsonElement parameters) => null;
}
#else
Console.WriteLine("inspector: not referenced");
return 0;
#endif
