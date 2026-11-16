using System.Collections.Immutable;

namespace DIR.Lib.Prototyping;

/// <summary>The type of a node property's value.</summary>
public enum PrototypePropertyType
{
    /// <summary>Any string.</summary>
    String,
    /// <summary>Any number.</summary>
    Number,
    /// <summary>A whole number.</summary>
    Integer,
    /// <summary>true or false.</summary>
    Bool,
    /// <summary>A <c>UiPalette</c> role name or <c>#rgb</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>.</summary>
    Color,
    /// <summary>A number (fixed), <c>"auto"</c>, <c>"star"</c> or <c>"star:weight"</c>.</summary>
    Size,
    /// <summary>An array of strings.</summary>
    StringList,
    /// <summary>One of <see cref="PrototypeProperty.Values"/>.</summary>
    Enum,
}

/// <summary>One property a kind accepts.</summary>
public sealed record PrototypeProperty(string Name, PrototypePropertyType Type)
{
    /// <summary>True when the node must set it.</summary>
    public bool Required { get; init; }

    /// <summary>The allowed words, for an <see cref="PrototypePropertyType.Enum"/>.</summary>
    public ImmutableArray<string> Values { get; init; } = [];
}

/// <summary>One node kind: its own properties and how many children it takes.</summary>
public sealed record PrototypeKind(string Name, ImmutableArray<PrototypeProperty> Properties)
{
    /// <summary>Fewest children.</summary>
    public int MinChildren { get; init; }

    /// <summary>Most children; -1 for any number.</summary>
    public int MaxChildren { get; init; }

    /// <summary>True when the kind reads and writes a named state through <c>bind</c>.</summary>
    public bool SupportsBind { get; init; }
}

/// <summary>
/// The one table of what a prototype document may say: the node kinds, each kind's properties, the properties
/// every kind shares and the colour roles. The loader validates against it, and
/// <c>prototype-format.md</c> is held to it by a test.
/// </summary>
public static class PrototypeVocabulary
{
    private static ImmutableArray<string> W(params string[] w) => [.. w];

    private static PrototypeProperty P(string n, PrototypePropertyType t, bool required = false, ImmutableArray<string> values = default)
        => new(n, t) { Required = required, Values = values.IsDefault ? [] : values };

    /// <summary>The <c>UiPalette</c> role names a colour may use.</summary>
    public static ImmutableArray<string> ColorRoles { get; } = W(
        "ContentBg", "PanelBg", "HeaderBg", "Separator", "SeparatorStrong", "BodyText", "DimText", "HeaderText",
        "Accent", "AccentAlt", "Selection", "SelectionText", "Focus", "Info", "Warn", "Error", "Success");

    /// <summary>The properties every kind accepts.</summary>
    public static ImmutableArray<PrototypeProperty> Common { get; } =
    [
        P("padding", PrototypePropertyType.Number),
        P("background", PrototypePropertyType.Color),
        P("border", PrototypePropertyType.Color),
        P("borderWidth", PrototypePropertyType.Number),
        P("borderRadius", PrototypePropertyType.Number),
        P("width", PrototypePropertyType.Size),
        P("height", PrototypePropertyType.Size),
        P("key", PrototypePropertyType.String),
        P("tooltip", PrototypePropertyType.String),
        P("side", PrototypePropertyType.Enum, values: W("left", "right", "top", "bottom")),
        P("size", PrototypePropertyType.Number),
    ];

    private static PrototypeKind K(string name, int min, int max, bool bind, params PrototypeProperty[] props)
        => new(name, [.. props]) { MinChildren = min, MaxChildren = max, SupportsBind = bind };

    private static readonly PrototypeProperty Gap = P("gap", PrototypePropertyType.Number);
    private static readonly PrototypeProperty FontSize = P("fontSize", PrototypePropertyType.Number);
    private static readonly PrototypeProperty Color = P("color", PrototypePropertyType.Color);

    /// <summary>The accepted node kinds by name.</summary>
    public static ImmutableDictionary<string, PrototypeKind> Kinds { get; } = new[]
    {
        K("vstack", 0, -1, false, Gap),
        K("hstack", 0, -1, false, Gap),
        K("grid", 0, -1, false, P("columns", PrototypePropertyType.Integer, true), Gap),
        K("wrap", 0, -1, false, Gap, P("vertical", PrototypePropertyType.Bool)),
        K("dock", 1, -1, false),
        K("split", 2, 2, false, P("axis", PrototypePropertyType.Enum, values: W("horizontal", "vertical")), P("firstExtent", PrototypePropertyType.Number)),
        K("overlay", 2, 2, false),
        K("modal", 2, 2, false, P("open", PrototypePropertyType.Bool)),
        K("text", 0, 0, false, P("text", PrototypePropertyType.String, true), FontSize, Color,
            P("align", PrototypePropertyType.Enum, values: W("near", "center", "far"))),
        K("markdown", 0, 0, false, P("source", PrototypePropertyType.String, true)),
        K("icon", 0, 0, false, P("icon", PrototypePropertyType.String, true), P("iconSize", PrototypePropertyType.Number), Color),
        K("box", 0, 0, false, Color),
        K("fill", 0, 0, false, P("minWidth", PrototypePropertyType.Number), P("minHeight", PrototypePropertyType.Number)),
        K("spacer", 0, 0, false),
        K("textInput", 0, 0, true, P("placeholder", PrototypePropertyType.String), FontSize),
        K("checkbox", 0, 0, true, P("label", PrototypePropertyType.String, true)),
        K("buttonGroup", 0, 0, true, P("options", PrototypePropertyType.StringList, true)),
        K("dropdown", 0, 0, true, P("options", PrototypePropertyType.StringList, true), P("label", PrototypePropertyType.String)),
        K("slider", 0, 0, true, P("min", PrototypePropertyType.Number), P("max", PrototypePropertyType.Number), Color),
        K("progress", 0, 0, false, P("value", PrototypePropertyType.Number, true), P("label", PrototypePropertyType.String),
            Color, P("track", PrototypePropertyType.Color)),
    }.ToImmutableDictionary(k => k.Name);
}
