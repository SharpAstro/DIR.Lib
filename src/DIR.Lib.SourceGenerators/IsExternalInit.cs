namespace System.Runtime.CompilerServices
{
    // Polyfill: netstandard2.0 (the Roslyn source-generator target) has no IsExternalInit, which C# needs
    // for `record` / `init` accessors. Records give the generator's models value equality, which the
    // incremental pipeline relies on to cache and skip regeneration when nothing changed.
    internal static class IsExternalInit
    {
    }
}
