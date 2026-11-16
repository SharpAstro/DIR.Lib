using System.Collections.Generic;
using System.Globalization;

namespace DIR.Lib.Prototyping;

/// <summary>What an <c>action</c> string does.</summary>
public enum PrototypeActionKind
{
    /// <summary><c>goto:screen</c>: move to another screen.</summary>
    Goto,
    /// <summary><c>set:name=value</c>: write a state value.</summary>
    Set,
    /// <summary><c>toggle:name</c>: flip a state flag.</summary>
    Toggle,
}

/// <summary>A parsed <c>action</c>: its kind, the screen or state name it targets and, for a set, the value.</summary>
public readonly record struct PrototypeAction(PrototypeActionKind Kind, string Target, string Value = "")
{
    /// <summary>Parses <c>goto:screen</c>, <c>set:name=value</c> or <c>toggle:name</c>; false for anything else.</summary>
    public static bool TryParse(string? text, out PrototypeAction action)
    {
        action = default;
        if (text is null) return false;
        if (text.StartsWith("goto:", System.StringComparison.Ordinal))
        {
            action = new(PrototypeActionKind.Goto, text[5..]);
            return action.Target.Length > 0;
        }
        if (text.StartsWith("toggle:", System.StringComparison.Ordinal))
        {
            action = new(PrototypeActionKind.Toggle, text[7..]);
            return action.Target.Length > 0;
        }
        if (text.StartsWith("set:", System.StringComparison.Ordinal))
        {
            var rest = text[4..];
            int eq = rest.IndexOf('=');
            if (eq <= 0) return false;
            action = new(PrototypeActionKind.Set, rest[..eq], rest[(eq + 1)..]);
            return true;
        }
        return false;
    }
}

/// <summary>
/// The named values a prototype's screens read and write: strings, flags and numbers, all held as the text a
/// <c>set:</c> action or a <c>when</c> condition writes, so equality is plain string equality. A flag is
/// <c>true</c> or <c>false</c>, and a name never set reads as the empty string, which a <c>when</c> of
/// <c>name=false</c> also matches (an unset flag is off).
/// </summary>
public sealed class PrototypeState
{
    private readonly Dictionary<string, string> _values = new(System.StringComparer.Ordinal);

    /// <summary>Raised after any value changes.</summary>
    public event System.Action? Changed;

    /// <summary>True when <paramref name="name"/> has been set.</summary>
    public bool Has(string name) => _values.ContainsKey(name);

    /// <summary>The value of <paramref name="name"/>, or the empty string when it was never set.</summary>
    public string Get(string name) => _values.TryGetValue(name, out var v) ? v : "";

    /// <summary>The value as a flag: true only for <c>true</c>.</summary>
    public bool GetBool(string name) => Get(name) == "true";

    /// <summary>The value as a number, or <paramref name="fallback"/> when unset or not a number.</summary>
    public double GetNumber(string name, double fallback = 0d)
        => double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

    /// <summary>Writes a string value.</summary>
    public void Set(string name, string value)
    {
        if (_values.TryGetValue(name, out var old) && old == value) return;
        _values[name] = value;
        Changed?.Invoke();
    }

    /// <summary>Writes a flag.</summary>
    public void Set(string name, bool value) => Set(name, value ? "true" : "false");

    /// <summary>Writes a number.</summary>
    public void Set(string name, double value) => Set(name, value.ToString("R", CultureInfo.InvariantCulture));

    /// <summary>Flips a flag; an unset one becomes true.</summary>
    public void Toggle(string name) => Set(name, !GetBool(name));

    /// <summary>Whether <paramref name="name"/> equals <paramref name="value"/> (an unset name equals the empty string and <c>false</c>).</summary>
    public bool Matches(string name, string value)
    {
        var current = Get(name);
        return current == value || (!Has(name) && value == "false");
    }

    /// <summary>Whether a <c>when</c> of the form <c>name=value</c> holds; null always holds.</summary>
    public bool WhenHolds(string? when)
    {
        if (when is null) return true;
        int eq = when.IndexOf('=');
        return eq <= 0 || Matches(when[..eq], when[(eq + 1)..]);
    }

    /// <summary>Runs a set or a toggle against this state. A goto is not state, so it returns false.</summary>
    public bool Apply(in PrototypeAction action)
    {
        switch (action.Kind)
        {
            case PrototypeActionKind.Set: Set(action.Target, action.Value); return true;
            case PrototypeActionKind.Toggle: Toggle(action.Target); return true;
            default: return false;
        }
    }
}
