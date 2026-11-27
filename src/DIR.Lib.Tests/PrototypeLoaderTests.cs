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

    // DIR.Lib #257: a number that measures something drawn is a load problem below zero, where it used to load and draw nonsense.
    [Theory]
    [InlineData("""{ "kind": "text", "text": "x", "fontSize": -12 }""", "fontSize")]
    [InlineData("""{ "kind": "text", "text": "x", "padding": -4 }""", "padding")]
    [InlineData("""{ "kind": "vstack", "gap": -1 }""", "gap")]
    [InlineData("""{ "kind": "box", "borderWidth": -1 }""", "borderWidth")]
    [InlineData("""{ "kind": "fill", "minWidth": -0.5 }""", "minWidth")]
    public void ANegativeExtentIsReportedAtItsPath(string root, string property)
    {
        Problems(Doc(Screen("a", root)))
            .ShouldBe([$"$.screens[0].root.{property}: wrong value type: expected a number that is not negative"]);
    }

    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    public void ANegativeFixedSizeIsReportedAtItsPath(string property)
    {
        Problems(Doc(Screen("a", $$"""{ "kind": "box", "{{property}}": -5 }""")))
            .ShouldBe([$"$.screens[0].root.{property}: wrong value type: expected a number that is not negative, \"auto\", \"star\" or \"star:<weight>\""]);
    }

    [Fact]
    public void ZeroExtentsAndNegativeValuesLoad()
    {
        Problems(Doc(Screen("a", """
            { "kind": "vstack", "gap": 0, "padding": 0, "width": 0, "children": [
              { "kind": "slider", "bind": "v", "min": -10, "max": -1 },
              { "kind": "progress", "value": -0.5 } ] }
            """))).ShouldBeEmpty();
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
    public void ButtonSwitchAndTheNewCommonPropertiesLoad()
    {
        var r = Prototype.Load(Doc(Screen("a", """
            { "kind": "vstack", "children": [
              { "kind": "button", "text": "Go", "style": "accent", "action": "goto:a", "disabled": "why", "icon": "CaretRight", "opens": true,
                "hoverBackground": "Separator", "revealOnHover": true, "selectedWhen": "m=1", "doubleAction": "set:m=2" },
              { "kind": "switch", "bind": "dark", "label": "Dark", "labelBefore": true, "disabled": "no" },
              { "kind": "text", "text": "x", "weight": "bold", "face": "mono", "style": "label" } ] }
            """)));
        r.Problems.ShouldBeEmpty();
        var button = r.Document!.Screens[0].Root.Children[0];
        button.SelectedWhen.ShouldBe("m=1");
        button.DoubleAction.ShouldBe("set:m=2");
    }

    [Fact]
    public void RejectedValuesOfTheNewKindsNameTheirPath()
    {
        Problems(Doc(Screen("a", """
            { "kind": "vstack", "children": [
              { "kind": "button", "text": "Go", "style": "huge" },
              { "kind": "button" },
              { "kind": "button", "text": "x", "icon": "Nope" },
              { "kind": "text", "text": "x", "weight": "heavy", "face": "serif", "style": "big" },
              { "kind": "spacer", "selectedWhen": "nope", "doubleAction": "jump:x" } ] }
            """))).ShouldBe(
        [
            "$.screens[0].root.children[0].style: " + Problems(Doc(Screen("a", """{ "kind": "button", "text": "Go", "style": "huge" }"""))).Single().Split(": ", 2)[1],
            "$.screens[0].root.children[1].text: missing: 'text' is required for kind 'button'",
            "$.screens[0].root.children[2].icon: unknown icon 'Nope' (an IconKind name)",
            "$.screens[0].root.children[3].weight: " + Problems(Doc(Screen("a", """{ "kind": "text", "text": "x", "weight": "heavy" }"""))).Single().Split(": ", 2)[1],
            "$.screens[0].root.children[3].face: " + Problems(Doc(Screen("a", """{ "kind": "text", "text": "x", "face": "serif" }"""))).Single().Split(": ", 2)[1],
            "$.screens[0].root.children[3].style: " + Problems(Doc(Screen("a", """{ "kind": "text", "text": "x", "style": "big" }"""))).Single().Split(": ", 2)[1],
            "$.screens[0].root.children[4].selectedWhen: when is a state equality: <name>=<value>",
            "$.screens[0].root.children[4].doubleAction: unknown action 'jump:x': use goto:<screen>, set:<name>=<value> or toggle:<name>",
        ]);
    }

    [Fact]
    public void BindOnAKindWithoutOneIsReported()
    {
        Problems(Doc(Screen("a", """{ "kind": "text", "text": "x", "bind": "s" }""")))
            .ShouldBe(["$.screens[0].root.bind: kind 'text' has no bind (only textInput, checkbox, switch, slider, buttonGroup, dropdown, tree and tabs do)"]);
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

    private const string TreeOk = """
        { "kind": "tree", "bind": "sel", "children": [
          { "kind": "treeRow", "key": "a", "text": "Stars", "depth": 1, "mark": "Plus", "note": "3", "trailingIcon": "Plus", "trailingAction": "set:ran=a" } ] }
        """;

    [Fact]
    public void ATreeAndItsRowsLoad()
    {
        var root = Prototype.Load(Doc(Screen("a", TreeOk))).Document!.Screens[0].Root;
        root.Kind.ShouldBe("tree");
        root.Bind.ShouldBe("sel");
        var row = root.Children.Single();
        row.Properties["depth"].Number.ShouldBe(1d);
        row.Properties["trailingAction"].Text.ShouldBe("set:ran=a");
    }

    [Fact]
    public void ATreeRowOutsideATreeAndAForeignChildOfATreeAreReported()
    {
        Problems(Doc(Screen("a", """{ "kind": "treeRow", "text": "x" }""")))
            .ShouldBe(["$.screens[0].root.kind: kind 'treeRow' is only valid directly inside a 'tree'"]);
        Problems(Doc(Screen("a", """{ "kind": "vstack", "children": [ { "kind": "treeRow", "text": "x" } ] }""")))
            .ShouldBe(["$.screens[0].root.children[0].kind: kind 'treeRow' is only valid directly inside a 'tree'"]);
        Problems(Doc(Screen("a", """{ "kind": "tree", "children": [ { "kind": "text", "text": "x" } ] }""")))
            .ShouldBe(["$.screens[0].root.children[0].kind: a 'tree' takes only 'treeRow' children, not 'text'"]);
    }

    [Fact]
    public void ATreeRowsPropertiesAreChecked()
    {
        Problems(Doc(Screen("a", """{ "kind": "tree", "children": [ { "kind": "treeRow", "text": "x", "depth": -1, "mark": "Nope", "trailingAction": "goto:zz" } ] }""")))
            .ShouldBe([
                "$.screens[0].root.children[0].depth: wrong value type: expected a whole number that is not negative",
                "$.screens[0].root.children[0].mark: unknown icon 'Nope' (an IconKind name)",
                "$.screens[0].root.children[0].trailingAction: goto names screen 'zz', which does not exist",
                "$.screens[0].root.children[0].trailingAction: needs 'trailingIcon': the icon the action is pressed on",
            ]);
    }

    [Fact]
    public void TabsLoadAndNeedOptions()
    {
        Prototype.Load(Doc(Screen("a", """{ "kind": "tabs", "options": ["A", "B"], "bind": "t" }"""))).Problems.ShouldBeEmpty();
        Problems(Doc(Screen("a", """{ "kind": "tabs", "bind": "t" }""")))
            .ShouldBe(["$.screens[0].root.options: missing: 'options' is required for kind 'tabs'"]);
    }

    [Fact]
    public void ATableLoadsWithNumberAndWordWidths()
    {
        var r = Prototype.Load(Doc(Screen("a", """
            { "kind": "table", "columns": ["File", "Kind"], "columnWidths": [120, "star:2"], "children": [
              { "kind": "tableRow", "children": [ { "kind": "text", "text": "a" }, { "kind": "text", "text": "b" } ] } ] }
            """)));
        r.Problems.ShouldBeEmpty();
        r.Document!.Screens[0].Root.Properties["columnWidths"].List.ShouldBe(["120", "star:2"]);
    }

    [Fact]
    public void ATableRowOutsideATableAndATableWithOtherChildrenAreReported()
    {
        Problems(Doc(Screen("a", """{ "kind": "tableRow" }""")))
            .ShouldBe(["$.screens[0].root.kind: kind 'tableRow' is only valid directly inside a 'table'"]);
        Problems(Doc(Screen("a", """{ "kind": "table", "columns": ["A"], "children": [ { "kind": "treeRow", "text": "x" } ] }""")))
            .ShouldBe(["$.screens[0].root.children[0].kind: a 'table' takes only 'tableRow' children, not 'treeRow'"]);
    }

    [Fact]
    public void TableWidthsMustMatchTheColumnsAndBeSizes()
    {
        Problems(Doc(Screen("a", """{ "kind": "table", "columns": ["A", "B"], "columnWidths": [10] }""")))
            .ShouldBe(["$.screens[0].root.columnWidths: needs one size per column: 2 columns, 1 sizes"]);
        Problems(Doc(Screen("a", """{ "kind": "table", "columns": ["A"], "columnWidths": [-1] }""")))
            .Single().ShouldStartWith("$.screens[0].root.columnWidths: wrong value type: expected an array of sizes");
        Problems(Doc(Screen("a", """{ "kind": "table", "columns": ["A"], "columnWidths": ["wide"] }""")))
            .Single().ShouldStartWith("$.screens[0].root.columnWidths: wrong value type");
        Problems(Doc(Screen("a", """{ "kind": "table" }""")))
            .ShouldBe(["$.screens[0].root.columns: missing: 'columns' is required for kind 'table'"]);
    }
}
