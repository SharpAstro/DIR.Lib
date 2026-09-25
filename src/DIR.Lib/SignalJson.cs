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
/// <para>A key matches its parameter in any case, and a key that matches none is refused
/// (<see cref="RequireKnownKeys"/>, which every generated factory calls first). Both halves are needed:
/// the generator's own camel-casing once turned a parameter named <c>RA</c> into the key <c>rA</c>, so a
/// payload spelling it <c>ra</c> bound nothing and the signal went out with RA 0, which looked like a
/// working call.</para>
/// </summary>
public static class SignalJson
{
    private static bool TryGet(JsonElement el, string name, out JsonElement value)
    {
        if (el.ValueKind == JsonValueKind.Object && TryFind(el, name, out value) && value.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        value = default;
        return false;
    }

    // The exact name first (the key the generator emits, so the usual payload is one lookup), then any other
    // casing: case carries no meaning to someone typing a payload, and RA, ra and rA must all reach a
    // parameter named RA.
    private static bool TryFind(JsonElement el, string name, out JsonElement value)
    {
        if (el.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in el.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Refuses a payload that names anything <paramref name="signal"/> does not take, so a misspelt key is an
    /// error at the call instead of a parameter silently left at its default. Keys match in any case, as the
    /// readers do. No payload at all (or JSON <c>null</c>) is every parameter at its default, and passes.
    /// </summary>
    /// <param name="el">The payload.</param>
    /// <param name="signal">The signal's name, for the message.</param>
    /// <param name="keys">Every key the signal binds.</param>
    /// <exception cref="ArgumentException">A key matches none of <paramref name="keys"/>, or the payload is not a
    /// JSON object. The message lists the keys the signal does take, so the next call can be right.</exception>
    public static void RequireKnownKeys(JsonElement el, string signal, params string[] keys)
    {
        if (el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return;
        }

        if (el.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"{signal} takes a JSON object of arguments, not {el.ValueKind}");
        }

        foreach (var property in el.EnumerateObject())
        {
            if (!IsOneOf(property.Name, keys))
            {
                throw new ArgumentException(keys.Length == 0
                    ? $"{signal} takes no arguments, so '{property.Name}' is not one"
                    : $"{signal} takes {string.Join(", ", keys)}; '{property.Name}' is none of them");
            }
        }
    }

    private static bool IsOneOf(string name, string[] keys)
    {
        foreach (var key in keys)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

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
