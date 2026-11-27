using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DIR.Lib.Layout;

namespace DIR.Lib.Prototyping;

/// <summary>
/// Reads a prototype document: a JSON file of screens, each a tree of nodes that maps onto <c>Builder</c> calls.
/// The reader walks the parsed <see cref="JsonDocument"/> by hand (no reflection, AOT safe), so every problem
/// carries the JSON path it was found at, and all problems are collected rather than the first. The vocabulary
/// it accepts is <see cref="PrototypeVocabulary"/>, documented in <c>prototype-format.md</c>.
/// </summary>
public static partial class Prototype
{
    /// <summary>Loads <paramref name="json"/>: the document when it is sound, else every problem found.</summary>
    public static PrototypeLoadResult Load(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false });
        }
        catch (JsonException e)
        {
            return Fail(new PrototypeProblem { Path = "$", Message = "not valid JSON: " + e.Message });
        }

        using (doc)
        {
            var l = new Loader();
            var document = l.ReadDocument(doc.RootElement);
            return l.Problems.Count == 0
                ? new PrototypeLoadResult { Document = document }
                : new PrototypeLoadResult { Problems = [.. l.Problems] };
        }
    }

    private static PrototypeLoadResult Fail(PrototypeProblem p) => new() { Problems = [p] };

    private sealed class Loader
    {
        public readonly List<PrototypeProblem> Problems = [];

        // (target screen id, path of the action) per screen index.
        private readonly List<List<(string Target, string Path)>> _gotos = [];
        private HashSet<string> _keys = [];
        private List<(string Target, string Path)> _currentGotos = [];

        private void Add(string path, string message) => Problems.Add(new PrototypeProblem { Path = path, Message = message });

        public PrototypeDocument? ReadDocument(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                Add("$", "the document must be a JSON object");
                return null;
            }

            double width = 0, height = 0;
            string entry = "";
            bool hasEntry = false;
            var screens = ImmutableArray.CreateBuilder<PrototypeScreen>();
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            bool hasW = false, hasH = false, hasScreens = false;
            var targets = ImmutableArray.CreateBuilder<PrototypeTarget>();
            bool hasTargets = false;

            foreach (var p in root.EnumerateObject())
            {
                var path = "$." + p.Name;
                switch (p.Name)
                {
                    case "width":
                        hasW = true;
                        width = PositiveNumber(p.Value, path);
                        break;
                    case "height":
                        hasH = true;
                        height = PositiveNumber(p.Value, path);
                        break;
                    case "entry":
                        hasEntry = true;
                        if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } e) entry = e;
                        else Add(path, "expected a non-empty string (the id of the entry screen)");
                        break;
                    case "targets":
                        hasTargets = true;
                        ReadTargets(p.Value, path, targets);
                        break;
                    case "screens":
                        hasScreens = true;
                        if (p.Value.ValueKind != JsonValueKind.Array)
                        {
                            Add(path, "expected an array of screens");
                            break;
                        }
                        int i = 0;
                        foreach (var s in p.Value.EnumerateArray())
                        {
                            var sp = $"{path}[{i}]";
                            var screen = ReadScreen(s, sp, ids, i);
                            if (screen is not null) screens.Add(screen);
                            i++;
                        }
                        break;
                    default:
                        Add(path, $"unknown property '{p.Name}' (a document has width, height, entry, targets and screens)");
                        break;
                }
            }

            if (!hasW) Add("$.width", "missing: the design width");
            if (!hasH) Add("$.height", "missing: the design height");
            if (!hasScreens) Add("$.screens", "missing: the screens");
            else if (screens.Count == 0 && Problems.Count == 0) Add("$.screens", "a prototype needs at least one screen");
            if (!hasEntry) Add("$.entry", "missing: the id of the entry screen");

            // Gotos, then reachability. Indices of _gotos follow the document's screen array.
            var idList = new List<string?>();
            foreach (var kv in ids) { while (idList.Count <= kv.Value) idList.Add(null); idList[kv.Value] = kv.Key; }

            for (int s = 0; s < _gotos.Count; s++)
                foreach (var (target, p) in _gotos[s])
                    if (!ids.ContainsKey(target))
                        Add(p, $"goto names screen '{target}', which does not exist");

            if (hasEntry && entry.Length > 0)
            {
                if (!ids.TryGetValue(entry, out var entryIndex))
                {
                    Add("$.entry", $"entry screen '{entry}' does not exist");
                }
                else
                {
                    var seen = new HashSet<int> { entryIndex };
                    var queue = new Queue<int>();
                    queue.Enqueue(entryIndex);
                    while (queue.Count > 0)
                    {
                        var cur = queue.Dequeue();
                        foreach (var (target, _) in _gotos[cur])
                            if (ids.TryGetValue(target, out var ti) && seen.Add(ti)) queue.Enqueue(ti);
                    }
                    for (int s = 0; s < _gotos.Count; s++)
                    {
                        if (seen.Contains(s) || idList.Count <= s || idList[s] is null) continue;
                        // Only the first screen with this id owns the index; a duplicate was reported already.
                        Add($"$.screens[{s}]", $"screen '{idList[s]}' cannot be reached from entry '{entry}' by goto");
                    }
                }
            }

            return new PrototypeDocument
            {
                Width = (float)width,
                Height = (float)height,
                Entry = entry,
                Screens = screens.ToImmutable(),
                Targets = hasTargets
                    ? targets.ToImmutable()
                    : [new PrototypeTarget { Name = PrototypeTargets.DefaultName, Width = (float)width, Height = (float)height, Scale = 1f }],
            };
        }

        private void ReadTargets(JsonElement v, string path, ImmutableArray<PrototypeTarget>.Builder targets)
        {
            if (v.ValueKind != JsonValueKind.Array)
            {
                Add(path, "expected an array of targets");
                return;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            int i = 0;
            foreach (var t in v.EnumerateArray())
            {
                var tp = $"{path}[{i++}]";
                if (t.ValueKind != JsonValueKind.Object) { Add(tp, "expected a target object"); continue; }
                string name = "";
                double w = 0, h = 0, scale = 1;
                bool hasName = false, hasW = false, hasH = false, ok = true;
                foreach (var p in t.EnumerateObject())
                {
                    var pp = tp + "." + p.Name;
                    switch (p.Name)
                    {
                        case "name":
                            hasName = true;
                            if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } n)
                            {
                                name = n;
                                if (!names.Add(n)) { Add(pp, $"duplicate target name '{n}'"); ok = false; }
                            }
                            else { Add(pp, "expected a non-empty string"); ok = false; }
                            break;
                        case "width": hasW = true; w = PositiveNumber(p.Value, pp); ok &= w > 0; break;
                        case "height": hasH = true; h = PositiveNumber(p.Value, pp); ok &= h > 0; break;
                        case "scale": scale = PositiveNumber(p.Value, pp); ok &= scale > 0; break;
                        default:
                            Add(pp, $"unknown property '{p.Name}' (a target has name, width, height and scale)");
                            ok = false;
                            break;
                    }
                }
                if (!hasName) { Add(tp + ".name", "missing: the target's name"); ok = false; }
                if (!hasW) { Add(tp + ".width", "missing: the target's design width"); ok = false; }
                if (!hasH) { Add(tp + ".height", "missing: the target's design height"); ok = false; }
                if (ok) targets.Add(new PrototypeTarget { Name = name, Width = (float)w, Height = (float)h, Scale = (float)scale });
            }
            if (i == 0) Add(path, "targets, when given, needs at least one target");
        }

        private double PositiveNumber(JsonElement v, string path)
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d > 0) return d;
            Add(path, "expected a number greater than zero");
            return 0;
        }

        private PrototypeScreen? ReadScreen(JsonElement s, string path, Dictionary<string, int> ids, int index)
        {
            _keys = new HashSet<string>(StringComparer.Ordinal);
            _currentGotos = [];
            _gotos.Add(_currentGotos);

            if (s.ValueKind != JsonValueKind.Object)
            {
                Add(path, "expected a screen object");
                return null;
            }

            string id = "", title = "";
            bool hasId = false, hasTitle = false;
            PrototypeNode? root = null;
            bool hasRoot = false;

            foreach (var p in s.EnumerateObject())
            {
                var pp = path + "." + p.Name;
                switch (p.Name)
                {
                    case "id":
                        hasId = true;
                        if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } sid)
                        {
                            id = sid;
                            if (!ids.TryAdd(sid, index)) Add(pp, $"duplicate screen id '{sid}'");
                        }
                        else Add(pp, "expected a non-empty string");
                        break;
                    case "title":
                        hasTitle = true;
                        if (p.Value.ValueKind == JsonValueKind.String) title = p.Value.GetString()!;
                        else Add(pp, "expected a string");
                        break;
                    case "root":
                        hasRoot = true;
                        root = ReadNode(p.Value, pp, null);
                        break;
                    default:
                        Add(pp, $"unknown property '{p.Name}' (a screen has id, title and root)");
                        break;
                }
            }

            if (!hasId) Add(path + ".id", "missing: the screen's id");
            if (!hasRoot) Add(path + ".root", "missing: the screen's root node");
            if (!hasTitle) title = id;
            return root is null ? null : new PrototypeScreen { Id = id, Title = title, Root = root };
        }

        private PrototypeNode? ReadNode(JsonElement el, string path, PrototypeKind? parent)
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                Add(path, "expected a node object");
                return null;
            }

            PrototypeKind? kind = null;
            string kindName = "";
            if (el.TryGetProperty("kind", out var k))
            {
                if (k.ValueKind != JsonValueKind.String) Add(path + ".kind", "expected a string");
                else
                {
                    kindName = k.GetString() ?? "";
                    if (!PrototypeVocabulary.Kinds.TryGetValue(kindName, out kind))
                        Add(path + ".kind", $"unknown node kind '{kindName}'");
                }
            }
            else Add(path + ".kind", "missing: the node kind");

            // Child-kind rules: a tree holds treeRows, a table tableRows, and those rows live nowhere else.
            if (kind is not null)
            {
                if (parent?.ChildKind is { } wanted && kindName != wanted)
                    Add(path + ".kind", $"a '{parent.Name}' takes only '{wanted}' children, not '{kindName}'");
                else if (kind.ParentKind is { } owner && parent?.Name != owner)
                    Add(path + ".kind", $"kind '{kindName}' is only valid directly inside a '{owner}'");
            }

            var props = ImmutableDictionary.CreateBuilder<string, PrototypeValue>();
            var children = ImmutableArray.CreateBuilder<PrototypeNode>();
            string? action = null, when = null, bind = null, selectedWhen = null, doubleAction = null;
            int childCount = 0;

            foreach (var p in el.EnumerateObject())
            {
                var pp = path + "." + p.Name;
                switch (p.Name)
                {
                    case "kind":
                        break;
                    case "children":
                        if (p.Value.ValueKind != JsonValueKind.Array) { Add(pp, "expected an array of nodes"); break; }
                        int i = 0;
                        foreach (var c in p.Value.EnumerateArray())
                        {
                            var node = ReadNode(c, $"{pp}[{i}]", kind);
                            if (node is not null) children.Add(node);
                            i++;
                        }
                        childCount = i;
                        break;
                    case "action":
                        action = ReadAction(p.Value, pp);
                        break;
                    case "when":
                        when = ReadWhen(p.Value, pp);
                        break;
                    case "selectedWhen":
                        selectedWhen = ReadWhen(p.Value, pp);
                        break;
                    case "doubleAction":
                        doubleAction = ReadAction(p.Value, pp);
                        break;
                    case "bind":
                        bind = ReadBind(p.Value, pp, kind, kindName);
                        break;
                    default:
                        if (kind is null) break; // the kind is already reported; its properties cannot be judged
                        var prop = FindProperty(kind, p.Name);
                        if (prop is null)
                        {
                            Add(pp, $"unknown property '{p.Name}' for kind '{kindName}'");
                            break;
                        }
                        var value = ReadValue(p.Value, pp, prop);
                        if (value is not null)
                        {
                            if ((kindName is "icon" or "button" && p.Name == "icon"
                                    || kindName == "treeRow" && p.Name is "mark" or "trailingIcon")
                                && !Enum.TryParse<IconKind>(value.Text, out _))
                                Add(pp, $"unknown icon '{value.Text}' (an IconKind name)");
                            if (kindName == "treeRow" && p.Name == "trailingAction") ReadAction(p.Value, pp);
                            props[p.Name] = value;
                            if (p.Name == "key") CheckKey(value.Text, pp);
                        }
                        break;
                }
            }

            if (kind is not null)
            {
                foreach (var req in kind.Properties)
                    if (req.Required && !props.ContainsKey(req.Name) && !el.TryGetProperty(req.Name, out _))
                        Add(path + "." + req.Name, $"missing: '{req.Name}' is required for kind '{kindName}'");

                if (kind.MaxChildren == 0 && childCount > 0)
                    Add(path + ".children", $"kind '{kindName}' takes no children");
                else if (childCount < kind.MinChildren || (kind.MaxChildren >= 0 && childCount > kind.MaxChildren))
                    Add(path + ".children", kind.MinChildren == kind.MaxChildren
                        ? $"kind '{kindName}' takes exactly {kind.MinChildren} children, found {childCount}"
                        : $"kind '{kindName}' takes at least {kind.MinChildren} children, found {childCount}");

                if (kindName == "dock") CheckDock(children, path);
                if (kindName == "treeRow" && props.ContainsKey("trailingAction") && !props.ContainsKey("trailingIcon"))
                    Add(path + ".trailingAction", "needs 'trailingIcon': the icon the action is pressed on");
                if (kindName == "table" && props.TryGetValue("columns", out var cols) && props.TryGetValue("columnWidths", out var widths)
                    && cols.List.Length != widths.List.Length)
                    Add(path + ".columnWidths", $"needs one size per column: {cols.List.Length} columns, {widths.List.Length} sizes");
            }

            return new PrototypeNode
            {
                Kind = kindName,
                Path = path,
                Properties = props.ToImmutable(),
                Children = children.ToImmutable(),
                Action = action,
                When = when,
                Bind = bind,
                SelectedWhen = selectedWhen,
                DoubleAction = doubleAction,
            };
        }

        private void CheckDock(ImmutableArray<PrototypeNode>.Builder children, string path)
        {
            // Exactly one child has no 'side': it takes the remainder. Every other one is pinned to an edge.
            var fills = children.Where(c => !c.Properties.ContainsKey("side")).ToList();
            if (children.Count > 0 && fills.Count != 1)
                Add(path + ".children", $"a dock needs exactly one child without 'side' (the fill), found {fills.Count}");
            foreach (var c in children)
                if (c.Properties.ContainsKey("side") && !c.Properties.ContainsKey("size"))
                    Add(c.Path + ".size", "missing: a docked child needs 'size' (its extent along the dock axis)");
        }

        private static PrototypeProperty? FindProperty(PrototypeKind kind, string name)
        {
            foreach (var p in kind.Properties) if (p.Name == name) return p;
            foreach (var p in PrototypeVocabulary.Common) if (p.Name == name) return p;
            return null;
        }

        private void CheckKey(string key, string path)
        {
            if (key.Length == 0) Add(path, "expected a non-empty key");
            else if (!_keys.Add(key)) Add(path, $"duplicate key '{key}' on this screen");
        }

        private string? ReadAction(JsonElement v, string path)
        {
            if (v.ValueKind != JsonValueKind.String) { Add(path, "expected a string: goto:<screen>, set:<name>=<value> or toggle:<name>"); return null; }
            var a = v.GetString()!;
            if (a.StartsWith("goto:", StringComparison.Ordinal))
            {
                var target = a[5..];
                if (target.Length == 0) Add(path, "goto needs a screen id: goto:<screen>");
                else _currentGotos.Add((target, path));
            }
            else if (a.StartsWith("set:", StringComparison.Ordinal))
            {
                var rest = a[4..];
                int eq = rest.IndexOf('=');
                if (eq < 0 || !IsName(rest[..eq])) Add(path, "set needs a state name and a value: set:<name>=<value>");
                else if (rest[..eq] == PrototypeTargets.StateName) Add(path, "'target' is read-only state (the player sets it from the design width); it cannot be set");
            }
            else if (a.StartsWith("toggle:", StringComparison.Ordinal))
            {
                if (!IsName(a[7..])) Add(path, "toggle needs a state name: toggle:<name>");
                else if (a[7..] == PrototypeTargets.StateName) Add(path, "'target' is read-only state (the player sets it from the design width); it cannot be toggled");
            }
            else Add(path, $"unknown action '{a}': use goto:<screen>, set:<name>=<value> or toggle:<name>");
            return a;
        }

        private string? ReadWhen(JsonElement v, string path)
        {
            if (v.ValueKind != JsonValueKind.String) { Add(path, "expected a string: <name>=<value>"); return null; }
            var w = v.GetString()!;
            int eq = w.IndexOf('=');
            if (eq < 0 || !IsName(w[..eq])) Add(path, "when is a state equality: <name>=<value>");
            else if (w[..eq] == PrototypeTargets.StateName && !PrototypeTargets.IsValue(w[(eq + 1)..]))
                Add(path, $"'target' is {PrototypeTargets.Compact} or {PrototypeTargets.Regular}, not '{w[(eq + 1)..]}'");
            return w;
        }

        private string? ReadBind(JsonElement v, string path, PrototypeKind? kind, string kindName)
        {
            if (v.ValueKind != JsonValueKind.String) { Add(path, "expected a state name"); return null; }
            var b = v.GetString()!;
            if (!IsName(b)) Add(path, "a state name is letters, digits and underscores, not starting with a digit");
            else if (b == PrototypeTargets.StateName) Add(path, "'target' is read-only state; a node cannot bind it");
            if (kind is { SupportsBind: false }) Add(path, $"kind '{kindName}' has no bind (only textInput, checkbox, switch, slider, buttonGroup, segmented, dropdown, tree and tabs do)");
            return b;
        }

        private static bool IsName(string s)
        {
            if (s.Length == 0 || char.IsAsciiDigit(s[0])) return false;
            foreach (var c in s) if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        private PrototypeValue? ReadValue(JsonElement v, string path, PrototypeProperty prop)
        {
            switch (prop.Type)
            {
                case PrototypePropertyType.String:
                    if (v.ValueKind == JsonValueKind.String) return Str(v.GetString()!);
                    return Wrong(path, "a string");
                case PrototypePropertyType.Number:
                    if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d)) return Wrong(path, "a number");
                    // An extent, a gap, a padding or a font size below zero draws nothing a reader could have meant.
                    if (prop.NonNegative && d < 0) return Wrong(path, "a number that is not negative");
                    return Num(d);
                case PrototypePropertyType.Integer:
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
                    {
                        if (prop.NonNegative && n < 0) return Wrong(path, "a whole number that is not negative");
                        return Num(n);
                    }
                    return Wrong(path, "a whole number");
                case PrototypePropertyType.Bool:
                    if (v.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        return new PrototypeValue { Kind = PrototypeValueKind.Bool, Flag = v.GetBoolean() };
                    return Wrong(path, "true or false");
                case PrototypePropertyType.Color:
                    if (v.ValueKind == JsonValueKind.String && IsColor(v.GetString()!)) return Str(v.GetString()!);
                    return Wrong(path, "a UiPalette role name or #rgb, #rrggbb or #rrggbbaa");
                case PrototypePropertyType.Size:
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var sz) && sz >= 0) return Num(sz);
                    if (v.ValueKind == JsonValueKind.String && IsSizeWord(v.GetString()!)) return Str(v.GetString()!);
                    return Wrong(path, "a number that is not negative, \"auto\", \"star\" or \"star:<weight>\"");
                case PrototypePropertyType.Enum:
                    if (v.ValueKind == JsonValueKind.String && prop.Values.Contains(v.GetString()!)) return Str(v.GetString()!);
                    return Wrong(path, "one of " + string.Join(", ", prop.Values));
                case PrototypePropertyType.StringList:
                    if (v.ValueKind == JsonValueKind.Array && v.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String))
                        return new PrototypeValue
                        {
                            Kind = PrototypeValueKind.StringList,
                            List = [.. v.EnumerateArray().Select(e => e.GetString()!)],
                        };
                    return Wrong(path, "an array of strings");
                case PrototypePropertyType.SizeList:
                    if (v.ValueKind == JsonValueKind.Array && v.EnumerateArray().All(e =>
                            e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var f) && f >= 0
                            || e.ValueKind == JsonValueKind.String && IsSizeWord(e.GetString()!)))
                        return new PrototypeValue
                        {
                            Kind = PrototypeValueKind.StringList,
                            List = [.. v.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number
                                ? e.GetDouble().ToString("R", CultureInfo.InvariantCulture) : e.GetString()!)],
                        };
                    return Wrong(path, "an array of sizes (a number that is not negative, \"auto\", \"star\" or \"star:<weight>\")");
                default:
                    return null;
            }
        }

        private PrototypeValue? Wrong(string path, string expected)
        {
            Add(path, "wrong value type: expected " + expected);
            return null;
        }

        private static PrototypeValue Str(string s) => new() { Kind = PrototypeValueKind.String, Text = s };
        private static PrototypeValue Num(double d) => new() { Kind = PrototypeValueKind.Number, Number = d };

        private static bool IsColor(string s)
        {
            if (s.StartsWith('#'))
            {
                var hex = s.AsSpan(1);
                if (hex.Length is not (3 or 6 or 8)) return false;
                foreach (var c in hex) if (!char.IsAsciiHexDigit(c)) return false;
                return true;
            }
            return PrototypeVocabulary.ColorRoles.Contains(s);
        }

        private static bool IsSizeWord(string s)
        {
            if (s is "auto" or "star") return true;
            return s.StartsWith("star:", StringComparison.Ordinal)
                && double.TryParse(s.AsSpan(5), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w > 0;
        }
    }
}
