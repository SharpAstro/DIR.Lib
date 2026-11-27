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
    /// <summary>An array of sizes: each a number (fixed), <c>"auto"</c>, <c>"star"</c> or <c>"star:weight"</c>. Held as strings, a number in its invariant text.</summary>
    SizeList,
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

    /// <summary>
    /// True for a <see cref="PrototypePropertyType.Number"/> that is an extent, a gap, a padding or a font size: a
    /// value below zero is a load problem (11.87). A slider's bounds and a progress value may be any number.
    /// </summary>
    public bool NonNegative { get; init; }
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

    /// <summary>When set, every child must be of this kind (a tree's rows, a table's rows).</summary>
    public string? ChildKind { get; init; }

    /// <summary>When set, a node of this kind is only valid as a child of this kind (a treeRow in a tree).</summary>
    public string? ParentKind { get; init; }
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

    /// <summary>A number that measures something drawn (an extent, a gap, a padding, a font size), so never below zero.</summary>
    private static PrototypeProperty Extent(string n) => new(n, PrototypePropertyType.Number) { NonNegative = true };

    /// <summary>The <c>UiPalette</c> role names a colour may use.</summary>
    public static ImmutableArray<string> ColorRoles { get; } = W(
        "ContentBg", "PanelBg", "HeaderBg", "Separator", "SeparatorStrong", "BodyText", "DimText", "HeaderText",
        "Accent", "AccentAlt", "Selection", "SelectionText", "Focus", "Info", "Warn", "Error", "Success");

    /// <summary>The properties every kind accepts.</summary>
    public static ImmutableArray<PrototypeProperty> Common { get; } =
    [
        Extent("padding"),
        P("background", PrototypePropertyType.Color),
        P("border", PrototypePropertyType.Color),
        Extent("borderWidth"),
        Extent("borderRadius"),
        P("width", PrototypePropertyType.Size),
        P("height", PrototypePropertyType.Size),
        P("key", PrototypePropertyType.String),
        P("tooltip", PrototypePropertyType.String),
        P("side", PrototypePropertyType.Enum, values: W("left", "right", "top", "bottom")),
        Extent("size"),
        P("hoverBackground", PrototypePropertyType.Color),
        P("revealOnHover", PrototypePropertyType.Bool),
        P("selectedWhen", PrototypePropertyType.String),
        P("doubleAction", PrototypePropertyType.String),
    ];

    private static PrototypeKind K(string name, int min, int max, bool bind, params PrototypeProperty[] props)
        => new(name, [.. props]) { MinChildren = min, MaxChildren = max, SupportsBind = bind };

    private static PrototypeKind Of(PrototypeKind k, string? childKind = null, string? parentKind = null)
        => k with { ChildKind = childKind, ParentKind = parentKind };

    private static readonly PrototypeProperty Gap = Extent("gap");
    private static readonly PrototypeProperty FontSize = Extent("fontSize");
    private static readonly PrototypeProperty Color = P("color", PrototypePropertyType.Color);

    /// <summary>The accepted node kinds by name.</summary>
    public static ImmutableDictionary<string, PrototypeKind> Kinds { get; } = new[]
    {
        K("vstack", 0, -1, false, Gap),
        K("hstack", 0, -1, false, Gap),
        K("grid", 0, -1, false, P("columns", PrototypePropertyType.Integer, true), Gap),
        K("wrap", 0, -1, false, Gap, P("vertical", PrototypePropertyType.Bool)),
        K("dock", 1, -1, false),
        K("split", 2, 2, false, P("axis", PrototypePropertyType.Enum, values: W("horizontal", "vertical")), Extent("firstExtent")),
        K("overlay", 2, 2, false),
        K("modal", 2, 2, false, P("open", PrototypePropertyType.Bool)),
        K("text", 0, 0, false, P("text", PrototypePropertyType.String, true), FontSize, Color,
            P("align", PrototypePropertyType.Enum, values: W("near", "center", "far")),
            P("weight", PrototypePropertyType.Enum, values: W("regular", "bold")),
            P("face", PrototypePropertyType.Enum, values: W("ui", "mono")),
            P("style", PrototypePropertyType.Enum, values: W("body", "heading", "label"))),
        K("button", 0, 0, false, P("text", PrototypePropertyType.String, true),
            P("style", PrototypePropertyType.Enum, values: W("accent", "standard", "subtle", "link")),
            P("disabled", PrototypePropertyType.String), P("icon", PrototypePropertyType.String), P("opens", PrototypePropertyType.Bool)),
        K("switch", 0, 0, true, P("label", PrototypePropertyType.String), P("labelBefore", PrototypePropertyType.Bool),
            P("disabled", PrototypePropertyType.String)),
        Of(K("tree", 0, -1, true), childKind: "treeRow"),
        Of(K("treeRow", 0, 0, false, P("text", PrototypePropertyType.String, true), new PrototypeProperty("depth", PrototypePropertyType.Integer) { NonNegative = true },
            P("mark", PrototypePropertyType.String), P("note", PrototypePropertyType.String),
            P("trailingIcon", PrototypePropertyType.String), P("trailingAction", PrototypePropertyType.String)), parentKind: "tree"),
        K("tabs", 0, 0, true, P("options", PrototypePropertyType.StringList, true)),
        Of(K("table", 0, -1, false, P("columns", PrototypePropertyType.StringList, true),
            P("columnWidths", PrototypePropertyType.SizeList)), childKind: "tableRow"),
        Of(K("tableRow", 0, -1, false), parentKind: "table"),
        K("markdown", 0, 0, false, P("source", PrototypePropertyType.String, true)),
        K("icon", 0, 0, false, P("icon", PrototypePropertyType.String, true), Extent("iconSize"), Color),
        K("box", 0, 0, false, Color),
        K("fill", 0, 0, false, Extent("minWidth"), Extent("minHeight")),
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
