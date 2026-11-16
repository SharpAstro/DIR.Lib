using DIR.Lib.Prototyping;
using Shouldly;

namespace DIR.Lib.Tests;

public sealed class PrototypeLoaderTests
{
    private const string Minimal = """
        { "width": 360, "height": 640, "entry": "a",
          "screens": [ { "id": "a", "title": "A", "root": { "kind": "text", "text": "hi" } } ] }
        """;

    private static string Doc(string screens, string entry = "a") =>
        $$"""{ "width": 360, "height": 640, "entry": "{{entry}}", "screens": [ {{screens}} ] }""";

    private static string Screen(string id, string root) => $$"""{ "id": "{{id}}", "root": {{root}} }""";

    private static string[] Problems(string json)
        => [.. Prototype.Load(json).Problems.Select(p => p.ToString())];

    [Fact]
    public void AValidMinimalDocumentLoads()
    {
        var r = Prototype.Load(Minimal);
        r.Problems.ShouldBeEmpty();
        r.IsValid.ShouldBeTrue();
        var d = r.Document!;
        d.Width.ShouldBe(360f);
        d.Height.ShouldBe(640f);
        d.Entry.ShouldBe("a");
        d.Screens.Length.ShouldBe(1);
        d.Screens[0].Title.ShouldBe("A");
        d.Screens[0].Root.Kind.ShouldBe("text");
        d.Screens[0].Root.Properties["text"].Text.ShouldBe("hi");
    }

    [Fact]
    public void ATitleDefaultsToTheId()
    {
        var r = Prototype.Load(Doc(Screen("a", """{ "kind": "spacer" }""")));
        r.Document!.Screens[0].Title.ShouldBe("a");
    }

    [Fact]
    public void ActionWhenAndBindAreRead()
    {
        var r = Prototype.Load(Doc(Screen("a", """
            { "kind": "vstack", "children": [
              { "kind": "checkbox", "label": "x", "bind": "dark", "when": "mode=night", "action": "toggle:dark" } ] }
            """)));
        r.Problems.ShouldBeEmpty();
        var c = r.Document!.Screens[0].Root.Children[0];
        c.Bind.ShouldBe("dark");
        c.When.ShouldBe("mode=night");
        c.Action.ShouldBe("toggle:dark");
        c.Path.ShouldBe("$.screens[0].root.children[0]");
    }

    [Fact]
    public void AnUnknownKindIsReportedAtItsPath()
    {
        Problems(Doc(Screen("a", """{ "kind": "vstack", "children": [ { "kind": "blob" } ] }""")))
            .ShouldBe(["$.screens[0].root.children[0].kind: unknown node kind 'blob'"]);
    }

    [Fact]
    public void AMissingKindIsReported()
    {
        Problems(Doc(Screen("a", "{ }"))).ShouldBe(["$.screens[0].root.kind: missing: the node kind"]);
    }

    [Fact]
    public void AnUnknownPropertyIsReportedAtItsPath()
    {
        Problems(Doc(Screen("a", """{ "kind": "text", "text": "x", "colour": "Accent" }""")))
            .ShouldBe(["$.screens[0].root.colour: unknown property 'colour' for kind 'text'"]);
    }

    [Fact]
    public void AWrongValueTypeIsReportedAtItsPath()
    {
        Problems(Doc(Screen("a", """{ "kind": "text", "text": "x", "fontSize": "big" }""")))
            .ShouldBe(["$.screens[0].root.fontSize: wrong value type: expected a number"]);
    }

    [Fact]
    public void AMalformedColourIsReported()
    {
        Problems(Doc(Screen("a", """{ "kind": "box", "background": "#12" }""")))
            .ShouldBe(["$.screens[0].root.background: wrong value type: expected a UiPalette role name or #rgb, #rrggbb or #rrggbbaa"]);
    }

    [Fact]
    public void AGotoToAMissingScreenIsReported()
    {
        Problems(Doc(Screen("a", """{ "kind": "text", "text": "x", "action": "goto:nowhere" }""")))
            .ShouldBe(["$.screens[0].root.action: goto names screen 'nowhere', which does not exist"]);
    }

    [Fact]
    public void AScreenUnreachableFromTheEntryIsReported()
    {
        var json = Doc(Screen("a", """{ "kind": "spacer" }""") + "," + Screen("b", """{ "kind": "spacer" }"""));
        Problems(json).ShouldBe(["$.screens[1]: screen 'b' cannot be reached from entry 'a' by goto"]);
    }

    [Fact]
    public void AScreenReachedThroughAnotherIsReachable()
    {
        var json = Doc(
            Screen("a", """{ "kind": "text", "text": "x", "action": "goto:b" }""") + "," +
            Screen("b", """{ "kind": "text", "text": "x", "action": "goto:c" }""") + "," +
            Screen("c", """{ "kind": "spacer" }"""));
        Prototype.Load(json).Problems.ShouldBeEmpty();
    }

    [Fact]
    public void ADuplicateScreenIdIsReported()
    {
        var json = Doc(Screen("a", """{ "kind": "spacer" }""") + "," + Screen("a", """{ "kind": "spacer" }"""));
        Problems(json).ShouldBe(["$.screens[1].id: duplicate screen id 'a'"]);
    }

    [Fact]
    public void ADuplicateKeyOnAScreenIsReported()
    {
        Problems(Doc(Screen("a", """
            { "kind": "vstack", "children": [ { "kind": "spacer", "key": "k" }, { "kind": "spacer", "key": "k" } ] }
            """)))
            .ShouldBe(["$.screens[0].root.children[1].key: duplicate key 'k' on this screen"]);
    }

    [Fact]
    public void AMissingEntryScreenIsReported()
    {
        Problems(Doc(Screen("a", """{ "kind": "spacer" }"""), entry: "zzz"))
            .ShouldBe(["$.entry: entry screen 'zzz' does not exist"]);
        Problems("""{ "width": 1, "height": 1, "screens": [ { "id": "a", "root": { "kind": "spacer" } } ] }""")
            .ShouldBe(["$.entry: missing: the id of the entry screen"]);
    }

    [Fact]
    public void AllProblemsAreCollected()
    {
        var json = Doc(Screen("a", """
            { "kind": "vstack", "children": [
              { "kind": "blob" },
              { "kind": "text", "text": 5 },
              { "kind": "spacer", "bogus": 1, "action": "goto:x" } ] }
            """));
        Problems(json).ShouldBe(
        [
            "$.screens[0].root.children[0].kind: unknown node kind 'blob'",
            "$.screens[0].root.children[1].text: wrong value type: expected a string",
            "$.screens[0].root.children[2].bogus: unknown property 'bogus' for kind 'spacer'",
            "$.screens[0].root.children[2].action: goto names screen 'x', which does not exist",
        ]);
    }

    [Fact]
    public void BindOnAKindWithoutOneIsReported()
    {
        Problems(Doc(Screen("a", """{ "kind": "text", "text": "x", "bind": "s" }""")))
            .ShouldBe(["$.screens[0].root.bind: kind 'text' has no bind (only textInput, checkbox, slider, buttonGroup and dropdown do)"]);
    }

    [Fact]
    public void AMalformedActionIsReported()
    {
        Problems(Doc(Screen("a", """{ "kind": "spacer", "action": "fly:away" }""")))
            .ShouldBe(["$.screens[0].root.action: unknown action 'fly:away': use goto:<screen>, set:<name>=<value> or toggle:<name>"]);
    }

    [Fact]
    public void MissingRequiredPropertyAndChildCountAreReported()
    {
        Problems(Doc(Screen("a", """{ "kind": "split", "children": [ { "kind": "spacer" } ] }""")))
            .ShouldBe(["$.screens[0].root.children: kind 'split' takes exactly 2 children, found 1"]);
        Problems(Doc(Screen("a", """{ "kind": "grid" }""")))
            .ShouldBe(["$.screens[0].root.columns: missing: 'columns' is required for kind 'grid'"]);
    }

    [Fact]
    public void NotJsonIsAProblemAtTheRoot()
    {
        var p = Prototype.Load("{ nope").Problems;
        p.Length.ShouldBe(1);
        p[0].Path.ShouldBe("$");
    }

    [Fact]
    public void ProblemsSerializeToJson()
    {
        var r = Prototype.Load(Doc(Screen("a", """{ "kind": "blob" }""")));
        r.ProblemsToJson().ShouldContain("$.screens[0].root.kind");
    }
}
