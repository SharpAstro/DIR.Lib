using DIR.Lib.Prototyping;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>Holds <c>prototype-format.md</c> to the kinds and properties the loader accepts, both ways.</summary>
public sealed class PrototypeFormatDocTests
{
    private static string[] DocLines()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var f = Path.Combine(dir.FullName, "src", "DIR.Lib", "Prototype", "prototype-format.md");
            if (File.Exists(f)) return File.ReadAllLines(f);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("prototype-format.md not found above " + AppContext.BaseDirectory);
    }

    private static string? FirstCell(string line)
    {
        if (!line.StartsWith("| `", StringComparison.Ordinal)) return null;
        var end = line.IndexOf('`', 3);
        return end < 0 ? null : line[3..end];
    }

    // kind -> properties listed; "" is the common-properties section.
    private static Dictionary<string, SortedSet<string>> Parse(out List<string> roles)
    {
        var map = new Dictionary<string, SortedSet<string>>();
        roles = [];
        string? current = null;
        foreach (var line in DocLines())
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
                current = line == "## Common properties" ? "" : null;
            else if (line.StartsWith("### `", StringComparison.Ordinal))
            {
                current = line[5..line.LastIndexOf('`')];
                map[current] = [];
            }
            else if (current is not null && FirstCell(line) is { } name)
            {
                if (!map.TryGetValue(current, out var set)) map[current] = set = [];
                set.Add(name);
            }
            else if (line.StartsWith("Roles: ", StringComparison.Ordinal))
                roles = [.. line[7..].Split(',').Select(s => s.Trim().Trim('`'))];
        }
        return map;
    }

    [Fact]
    public void TheDocListsExactlyTheKindsTheLoaderAccepts()
    {
        var doc = Parse(out _);
        doc.Keys.Where(k => k != "").Order().ShouldBe(PrototypeVocabulary.Kinds.Keys.Order());
    }

    [Fact]
    public void EachKindsPropertiesMatchTheDocBothWays()
    {
        var doc = Parse(out _);
        foreach (var (name, kind) in PrototypeVocabulary.Kinds)
        {
            var listed = doc.TryGetValue(name, out var s) ? s : [];
            listed.ToArray().ShouldBe(kind.Properties.Select(p => p.Name).Order().ToArray(), $"properties of '{name}'");
        }
    }

    [Fact]
    public void TheCommonPropertiesAndColourRolesMatchTheDoc()
    {
        var doc = Parse(out var roles);
        doc[""].ShouldBe(PrototypeVocabulary.Common.Select(p => p.Name).Order());
        roles.Order().ShouldBe(PrototypeVocabulary.ColorRoles.Order());
    }

    [Fact]
    public void TheDocsExampleLoads()
    {
        var lines = DocLines();
        var start = Array.IndexOf(lines, "```json") + 1;
        var end = Array.IndexOf(lines, "```", start);
        var result = Prototype.Load(string.Join('\n', lines[start..end]));
        result.Problems.ShouldBeEmpty();
        result.Document!.Screens.Length.ShouldBe(2);
    }
}
