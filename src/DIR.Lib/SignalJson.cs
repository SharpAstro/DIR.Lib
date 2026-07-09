using System;
using System.Globalization;
using System.Text.Json;

namespace DIR.Lib;

/// <summary>
/// Reflection-free, strongly-typed JSON scalar readers used by the source-generated <c>SignalDirectory</c>
/// (see <c>DIR.Lib.SourceGenerators.SignalDirectoryGenerator</c>) to bind a live-inspector
/// <c>post_signal</c> payload onto a signal's constructor. Each reader returns the supplied default when the
/// property is absent, JSON <c>null</c>, or the wrong JSON kind -- so a partial or empty payload yields the
/// signal's declared defaults. No reflection, no dynamic code: safe for AOT-published binaries.
/// </summary>
public static class SignalJson
{
    private static bool TryGet(JsonElement el, string name, out JsonElement value)
    {
        // The inspector convention is camelCase property names (what the generator passes); accept the
        // exact-cased name too so a PascalCase payload still binds.
        if (el.ValueKind == JsonValueKind.Object
            && (el.TryGetProperty(name, out value) || TryGetPascal(el, name, out value))
            && value.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetPascal(JsonElement el, string camel, out JsonElement value)
    {
        if (camel.Length > 0 && char.IsLower(camel[0]))
        {
            var pascal = char.ToUpperInvariant(camel[0]) + camel.Substring(1);
            return el.TryGetProperty(pascal, out value);
        }

        value = default;
        return false;
    }

    public static bool Bool(JsonElement el, string name, bool def)
        => TryGet(el, name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : def;

    public static string? String(JsonElement el, string name, string? def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : def;

    // Non-nullable variant for a non-nullable string parameter (e.g. a signal's required Name), so the
    // generated call site never assigns a string? to a string.
    public static string StringNonNull(JsonElement el, string name, string def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;

    public static int Int(JsonElement el, string name, int def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var r) ? r : def;

    public static short Short(JsonElement el, string name, short def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt16(out var r) ? r : def;

    public static long Long(JsonElement el, string name, long def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var r) ? r : def;

    public static double Double(JsonElement el, string name, double def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var r) ? r : def;

    public static float Single(JsonElement el, string name, float def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var r) ? r : def;

    public static Guid Guid(JsonElement el, string name, Guid def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetGuid(out var r) ? r : def;

    public static bool? NullableBool(JsonElement el, string name, bool? def)
        => TryGet(el, name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : def;

    public static int? NullableInt(JsonElement el, string name, int? def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var r) ? r : def;

    public static short? NullableShort(JsonElement el, string name, short? def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt16(out var r) ? r : def;

    public static long? NullableLong(JsonElement el, string name, long? def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var r) ? r : def;

    public static double? NullableDouble(JsonElement el, string name, double? def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var r) ? r : def;

    public static float? NullableSingle(JsonElement el, string name, float? def)
        => TryGet(el, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var r) ? r : def;

    // Enum.TryParse<TEnum> is AOT-safe (no trim/dynamic-code warnings) and handles both the string name
    // ("Calibrator") and a numeric string ("1"); we normalise a JSON number to its string form for it.
    public static TEnum Enum<TEnum>(JsonElement el, string name, TEnum def) where TEnum : struct, System.Enum
    {
        if (!TryGet(el, name, out var v))
        {
            return def;
        }

        if (v.ValueKind == JsonValueKind.String)
        {
            return System.Enum.TryParse<TEnum>(v.GetString(), ignoreCase: true, out var s) ? s : def;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n))
        {
            return System.Enum.TryParse<TEnum>(n.ToString(CultureInfo.InvariantCulture), out var e) ? e : def;
        }

        return def;
    }

    public static TEnum? NullableEnum<TEnum>(JsonElement el, string name, TEnum? def) where TEnum : struct, System.Enum
    {
        if (!TryGet(el, name, out var v))
        {
            return def;
        }

        if (v.ValueKind == JsonValueKind.String)
        {
            return System.Enum.TryParse<TEnum>(v.GetString(), ignoreCase: true, out var s) ? s : def;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n))
        {
            return System.Enum.TryParse<TEnum>(n.ToString(CultureInfo.InvariantCulture), out var e) ? e : def;
        }

        return def;
    }
}
