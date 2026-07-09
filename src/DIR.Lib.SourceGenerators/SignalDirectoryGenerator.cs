using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace DIR.Lib.SourceGenerators;

/// <summary>
/// Generates <c>SignalDirectory.BuildFactories(SignalBus, overrides)</c> -- a name-&gt;factory map over EVERY
/// <c>*Signal</c> type in the consuming assembly -- so a live UI inspector can list + post any bus signal by
/// name with NO runtime reflection (which is what keeps an AOT publish clean). Each factory constructs its
/// signal from an inspector <c>post_signal</c> JSON payload via the <c>DIR.Lib.SignalJson</c> readers, binding
/// each constructor parameter by (camelCase) name and falling back to its declared default. Only signals whose
/// every required parameter is a bindable scalar (primitive / string / enum / Guid / their nullable forms) are
/// emitted; one with a required complex payload is skipped (a null-default complex parameter is passed through
/// as <c>null</c>).
/// <para>
/// Ships as an analyzer in the DIR.Lib package, so any <see cref="N:DIR.Lib"/> consumer gets the directory for
/// its own signals. Emits ONLY when the DEBUG symbol is defined (matching the DEBUG-only inspector that
/// consumes it) AND <c>DIR.Lib.SignalBus</c> is referenced -- inert otherwise.
/// </para>
/// </summary>
[Generator]
public sealed class SignalDirectoryGenerator : IIncrementalGenerator
{
    private const string SignalJsonType = "global::DIR.Lib.SignalJson";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Every type declaration whose name ends in "Signal" -> a factory model (or null to skip).
        var signals = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is TypeDeclarationSyntax t && t.Identifier.Text.EndsWith("Signal", StringComparison.Ordinal),
                transform: static (ctx, ct) => GetSignalModel(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Emit only when it will actually be used: the sole consumer is a DEBUG-only inspector wiring
        // (#if DEBUG), so gate generation on the DEBUG symbol -- nothing is generated in a Release / AOT
        // publish, mirroring the inspector itself -- AND on DIR.Lib.SignalBus being referenced (so an
        // assembly that carries *Signal types but not the bus stays inert).
        var gate = context.ParseOptionsProvider
            .Select(static (po, _) => po is CSharpParseOptions cs && cs.PreprocessorSymbolNames.Contains("DEBUG"))
            .Combine(context.CompilationProvider.Select(static (c, _) => c.GetTypeByMetadataName("DIR.Lib.SignalBus") is not null))
            .Select(static (t, _) => t.Left && t.Right);

        context.RegisterSourceOutput(
            signals.Collect().Combine(gate),
            static (spc, tuple) =>
            {
                var (models, enabled) = tuple;
                if (!enabled || models.IsDefaultOrEmpty)
                {
                    return;
                }

                spc.AddSource("SignalDirectory.g.cs", SourceText.From(Generate(models), Encoding.UTF8));
            });
    }

    private static SignalModel? GetSignalModel(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((TypeDeclarationSyntax)ctx.Node, ct) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        if (symbol.DeclaredAccessibility != Accessibility.Public
            || symbol.IsAbstract
            || symbol.IsGenericType
            || !symbol.Name.EndsWith("Signal", StringComparison.Ordinal))
        {
            return null;
        }

        // The "primary" ctor is the public instance ctor with the most parameters (the record's). Unlike
        // reflection's Type.GetConstructors(), Roslyn's InstanceConstructors DOES include a struct's implicit
        // parameterless ctor, so a no-field record struct (e.g. a parameterless *Signal) is handled here too.
        var ctor = symbol.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();
        if (ctor is null)
        {
            return null;
        }

        var args = new List<string>(ctor.Parameters.Length);
        foreach (var p in ctor.Parameters)
        {
            if (!TryBuildArg(p, out var argExpr))
            {
                return null; // a required, non-bindable parameter -> this signal is not postable from JSON.
            }

            args.Add(argExpr);
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString();
        var fq = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var key = symbol.Name.Substring(0, symbol.Name.Length - "Signal".Length);

        return new SignalModel(ns, key, fq, string.Join(", ", args));
    }

    // Builds the "paramName: SignalJson.Xxx(el, "camel", default)" fragment. Returns false when the parameter
    // is required (no default) and not a bindable scalar -> the whole signal is skipped by the caller.
    private static bool TryBuildArg(IParameterSymbol p, out string argExpr)
    {
        var reader = MapReader(p.Type);
        if (reader is not null)
        {
            var camel = Camel(p.Name);
            argExpr = $"{p.Name}: {SignalJsonType}.{reader}(el, \"{camel}\", {FormatDefault(p)})";
            return true;
        }

        // Not a bindable scalar. If it has an explicit null default we can still construct by passing null;
        // otherwise it cannot be built from JSON.
        if (p.HasExplicitDefaultValue && p.ExplicitDefaultValue is null)
        {
            argExpr = $"{p.Name}: null";
            return true;
        }

        argExpr = "";
        return false;
    }

    // Maps a parameter type to a SignalJson reader method name (with the enum generic arg baked in), or null
    // when the type is not a bindable scalar.
    private static string? MapReader(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            var inner = nullable.TypeArguments[0];
            if (inner.TypeKind == TypeKind.Enum)
            {
                return $"NullableEnum<{inner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>";
            }

            return inner.SpecialType switch
            {
                SpecialType.System_Boolean => "NullableBool",
                SpecialType.System_Int16 => "NullableShort",
                SpecialType.System_Int32 => "NullableInt",
                SpecialType.System_Int64 => "NullableLong",
                SpecialType.System_Single => "NullableSingle",
                SpecialType.System_Double => "NullableDouble",
                _ => null
            };
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return $"Enum<{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>";
        }

        return type.SpecialType switch
        {
            SpecialType.System_Boolean => "Bool",
            // A non-nullable string param needs the non-null reader so the call site doesn't assign string?.
            SpecialType.System_String => type.NullableAnnotation == NullableAnnotation.Annotated ? "String" : "StringNonNull",
            SpecialType.System_Int16 => "Short",
            SpecialType.System_Int32 => "Int",
            SpecialType.System_Int64 => "Long",
            SpecialType.System_Single => "Single",
            SpecialType.System_Double => "Double",
            _ => IsGuid(type) ? "Guid" : null
        };
    }

    private static bool IsGuid(ITypeSymbol type)
        => type is { Name: "Guid", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };

    // The literal passed as the reader's fallback: the parameter's declared default, or `default` when it has
    // none (so a missing required property yields default(T)).
    private static string FormatDefault(IParameterSymbol p)
    {
        if (!p.HasExplicitDefaultValue)
        {
            // A required non-nullable string binds through StringNonNull, whose fallback must be non-null.
            return p.Type.SpecialType == SpecialType.System_String
                && p.Type.NullableAnnotation != NullableAnnotation.Annotated
                ? "\"\""
                : "default";
        }

        var value = p.ExplicitDefaultValue;
        if (value is null)
        {
            return "null";
        }

        var effective = p.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
            ? n.TypeArguments[0]
            : p.Type;

        if (effective.TypeKind == TypeKind.Enum)
        {
            // ExplicitDefaultValue is the boxed underlying integral; cast it back to the enum type.
            var underlying = Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            return $"({effective.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){underlying}";
        }

        return effective.SpecialType switch
        {
            SpecialType.System_Boolean => (bool)value ? "true" : "false",
            SpecialType.System_String => "\"" + ((string)value).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            SpecialType.System_Int16 => value.ToString(),
            SpecialType.System_Int32 => value.ToString(),
            SpecialType.System_Int64 => value.ToString() + "L",
            SpecialType.System_Single => ((float)value).ToString("R", CultureInfo.InvariantCulture) + "F",
            SpecialType.System_Double => ((double)value).ToString("R", CultureInfo.InvariantCulture) + "D",
            _ => "default"
        };
    }

    private static string Camel(string name)
        => name.Length > 0 && char.IsUpper(name[0]) ? char.ToLowerInvariant(name[0]) + name.Substring(1) : name;

    private static string Generate(ImmutableArray<SignalModel> models)
    {
        // Dedupe by key (a partial type could surface twice) and order deterministically.
        var ordered = models
            .GroupBy(m => m.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .ToList();

        var ns = ordered.Count > 0 ? ordered[0].Namespace : "DIR.Lib";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (ns.Length > 0)
        {
            sb.AppendLine($"namespace {ns};");
            sb.AppendLine();
        }

        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Source-generated directory of every postable <c>*Signal</c> in this assembly, so a live UI");
        sb.AppendLine("/// inspector can list + post any bus signal by name with no runtime reflection. Generated by");
        sb.AppendLine("/// <c>DIR.Lib.SourceGenerators.SignalDirectoryGenerator</c>; do not edit.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class SignalDirectory");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Builds the name -&gt; factory map. Each factory binds an inspector JSON payload onto the signal's");
        sb.AppendLine("    /// constructor and posts it to <paramref name=\"bus\"/> (which dispatches on the runtime type).");
        sb.AppendLine("    /// <paramref name=\"overrides\"/> (a host's curated entries with friendlier JSON keys) win over the");
        sb.AppendLine("    /// generated factory of the same key.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static global::System.Collections.Generic.Dictionary<string, global::System.Action<global::System.Text.Json.JsonElement>> BuildFactories(");
        sb.AppendLine("        global::DIR.Lib.SignalBus bus,");
        sb.AppendLine("        global::System.Collections.Generic.IReadOnlyDictionary<string, global::System.Action<global::System.Text.Json.JsonElement>>? overrides = null)");
        sb.AppendLine("    {");
        sb.AppendLine("        var d = new global::System.Collections.Generic.Dictionary<string, global::System.Action<global::System.Text.Json.JsonElement>>(global::System.StringComparer.Ordinal);");
        sb.AppendLine();

        foreach (var m in ordered)
        {
            sb.AppendLine($"        d[\"{m.Key}\"] = el => bus.Post(new {m.FullyQualifiedName}({m.Args}));");
        }

        sb.AppendLine();
        sb.AppendLine("        if (overrides is not null)");
        sb.AppendLine("        {");
        sb.AppendLine("            foreach (var kv in overrides)");
        sb.AppendLine("            {");
        sb.AppendLine("                d[kv.Key] = kv.Value;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return d;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}

internal sealed record SignalModel(string Namespace, string Key, string FullyQualifiedName, string Args);
