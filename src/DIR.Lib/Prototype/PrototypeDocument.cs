using System.Collections.Immutable;

namespace DIR.Lib.Prototyping;

/// <summary>The kind of a literal property value as the document wrote it.</summary>
public enum PrototypeValueKind
{
    /// <summary>A JSON string (also how colours, sizes and enum words are held).</summary>
    String,
    /// <summary>A JSON number (also how integers are held).</summary>
    Number,
    /// <summary>A JSON true or false.</summary>
    Bool,
    /// <summary>A JSON array of strings.</summary>
    StringList,
}

/// <summary>One literal property value of a node. Exactly the member named by <see cref="Kind"/> is meaningful.</summary>
public sealed record PrototypeValue
{
    /// <summary>Which member below holds the value.</summary>
    public PrototypeValueKind Kind { get; init; }

    /// <summary>The string, when <see cref="Kind"/> is <see cref="PrototypeValueKind.String"/>.</summary>
    public string Text { get; init; } = "";

    /// <summary>The number, when <see cref="Kind"/> is <see cref="PrototypeValueKind.Number"/>.</summary>
    public double Number { get; init; }

    /// <summary>The flag, when <see cref="Kind"/> is <see cref="PrototypeValueKind.Bool"/>.</summary>
    public bool Flag { get; init; }

    /// <summary>The strings, when <see cref="Kind"/> is <see cref="PrototypeValueKind.StringList"/>.</summary>
    public ImmutableArray<string> List { get; init; } = [];
}

/// <summary>One node of a screen's tree, as the document declared it. It maps one to one onto a <c>Builder</c> call.</summary>
public sealed record PrototypeNode
{
    /// <summary>The node kind, one of <see cref="PrototypeVocabulary.Kinds"/>.</summary>
    public string Kind { get; init; } = "";

    /// <summary>The JSON path of this node, such as <c>$.screens[0].root.children[1]</c>.</summary>
    public string Path { get; init; } = "$";

    /// <summary>The kind's own and the common properties the node set, by name.</summary>
    public ImmutableDictionary<string, PrototypeValue> Properties { get; init; } = ImmutableDictionary<string, PrototypeValue>.Empty;

    /// <summary>The child nodes, in order.</summary>
    public ImmutableArray<PrototypeNode> Children { get; init; } = [];

    /// <summary>What a click does: <c>goto:screen</c>, <c>set:name=value</c> or <c>toggle:name</c>; null for none.</summary>
    public string? Action { get; init; }

    /// <summary>A state equality, <c>name=value</c>, the node is shown under; null for always.</summary>
    public string? When { get; init; }

    /// <summary>The state name an input kind reads and writes; null for none.</summary>
    public string? Bind { get; init; }
}

/// <summary>One screen of a prototype.</summary>
public sealed record PrototypeScreen
{
    /// <summary>The screen's id, unique in the document; what <c>goto:</c> names.</summary>
    public string Id { get; init; } = "";

    /// <summary>The screen's title (the id when the document gave none).</summary>
    public string Title { get; init; } = "";

    /// <summary>The root of the screen's tree.</summary>
    public required PrototypeNode Root { get; init; }
}

/// <summary>One surface a prototype is for: a name, a design size in design units and a device scale.</summary>
public sealed record PrototypeTarget
{
    /// <summary>The target's name, unique in the document.</summary>
    public string Name { get; init; } = "";

    /// <summary>The design width in design units.</summary>
    public float Width { get; init; }

    /// <summary>The design height in design units.</summary>
    public float Height { get; init; }

    /// <summary>The device scale (pixels per design unit); 1 when the document gave none.</summary>
    public float Scale { get; init; } = 1f;
}

/// <summary>A loaded prototype: a design size, an entry screen and the screens.</summary>
public sealed record PrototypeDocument
{
    /// <summary>The design width in design units.</summary>
    public float Width { get; init; }

    /// <summary>The design height in design units.</summary>
    public float Height { get; init; }

    /// <summary>The id of the screen the prototype starts on.</summary>
    public string Entry { get; init; } = "";

    /// <summary>The screens, in document order.</summary>
    public ImmutableArray<PrototypeScreen> Screens { get; init; } = [];

    /// <summary>
    /// The targets the prototype is for. A document with no <c>targets</c> gets one, named
    /// <see cref="PrototypeTargets.DefaultName"/>, at the document's width and height and scale 1.
    /// </summary>
    public ImmutableArray<PrototypeTarget> Targets { get; init; } = [];
}

/// <summary>One thing wrong with a document, found at a JSON path.</summary>
public sealed record PrototypeProblem
{
    /// <summary>Where, as a path such as <c>$.screens[2].root.children[0].kind</c>.</summary>
    public string Path { get; init; } = "$";

    /// <summary>What is wrong.</summary>
    public string Message { get; init; } = "";

    /// <inheritdoc/>
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>The outcome of <see cref="Prototype.Load"/>: the document, or every problem found.</summary>
public sealed record PrototypeLoadResult
{
    /// <summary>The document; null when there are problems.</summary>
    public PrototypeDocument? Document { get; init; }

    /// <summary>Every problem found, in document order (empty when the document loaded).</summary>
    public ImmutableArray<PrototypeProblem> Problems { get; init; } = [];

    /// <summary>True when <see cref="Document"/> is set.</summary>
    public bool IsValid => Document is not null;

    /// <summary>The problems as a JSON array, through the source-generated context, for a tool to print.</summary>
    public string ProblemsToJson()
        => System.Text.Json.JsonSerializer.Serialize(Problems.ToArray(), PrototypeJsonContext.Default.PrototypeProblemArray);
}
