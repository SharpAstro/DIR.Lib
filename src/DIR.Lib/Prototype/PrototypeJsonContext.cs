using System.Text.Json.Serialization;

namespace DIR.Lib.Prototyping;

/// <summary>Source-generated JSON metadata (no reflection) for the types the prototype loader writes.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PrototypeProblem[]))]
internal sealed partial class PrototypeJsonContext : JsonSerializerContext;
