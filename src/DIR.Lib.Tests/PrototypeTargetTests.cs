using DIR.Lib.Layout;
using DIR.Lib.Prototyping;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>Targets: the loader's reading and checks, the read-only <c>target</c> state, and a node's <c>when</c> on it.</summary>
public sealed class PrototypeTargetTests
{
    private const string Body = """
        "entry": "a",
        "screens": [ { "id": "a", "root": { "kind": "vstack", "children": [
          { "kind": "text", "text": "always" },
          { "kind": "text", "text": "phone", "when": "target=compact" },
          { "kind": "text", "text": "desktop", "when": "target=regular" }
        ] } } ]
        """;

    private static PrototypeDocument Doc(string extra = "")
    {
        var r = Prototype.Load("{ \"width\": 360, \"height\": 640, " + extra + Body + " }");
        r.Problems.ShouldBeEmpty();
        return r.Document!;
    }

    private static Node Tree(PrototypeDocument doc, float w, float h)
    {
        var player = new PrototypePlayer<RgbaImage>(new RgbaImageRenderer(10, 10), doc, PrototypeKit.Theme, "font.ttf")
        {
            Target = new PrototypeTarget { Name = "t", Width = w, Height = h },
        };
        return player.BuildTree();
    }

    private static bool Has(Node tree, string text) => PrototypeKit.Texts(tree).Contains(text);

    private static string[] Paths(string json)
    {
        var r = Prototype.Load(json);
        r.IsValid.ShouldBeFalse();
        return [.. r.Problems.Select(p => p.Path)];
    }

    private static string Wrap(string targets, string root = """{ "kind": "spacer" }""")
        => $$"""{ "width": 360, "height": 640, "entry": "a", {{targets}} "screens": [ { "id": "a", "root": {{root}} } ] }""";

    [Fact]
    public void NoTargetsGivesOneDefaultFromTheDesignSize()
    {
        var t = Doc().Targets.ShouldHaveSingleItem();
        t.Name.ShouldBe(PrototypeTargets.DefaultName);
        (t.Width, t.Height, t.Scale).ShouldBe((360f, 640f, 1f));
    }

    [Fact]
    public void DeclaredTargetsAreRead()
    {
        var doc = Doc("""
            "targets": [ { "name": "phone", "width": 390, "height": 844, "scale": 3 },
                         { "name": "desktop", "width": 1440, "height": 900 } ],
            """);
        doc.Targets.Select(t => (t.Name, t.Width, t.Height, t.Scale))
            .ShouldBe([("phone", 390f, 844f, 3f), ("desktop", 1440f, 900f, 1f)]);
    }

    [Fact]
    public void ACompactNodeIsThereAt390AndAbsentAt1440()
    {
        var doc = Doc();
        Has(Tree(doc, 390, 844), "phone").ShouldBeTrue();
        Has(Tree(doc, 390, 844), "desktop").ShouldBeFalse();
        Has(Tree(doc, 1440, 900), "phone").ShouldBeFalse();
        Has(Tree(doc, 1440, 900), "desktop").ShouldBeTrue();
    }

    [Fact]
    public void WithNoTargetTheDocumentSizeDecides()
    {
        var player = new PrototypePlayer<RgbaImage>(new RgbaImageRenderer(10, 10), Doc(), PrototypeKit.Theme, "font.ttf");
        Has(player.BuildTree(), "phone").ShouldBeTrue();
        player.State.Get(PrototypeTargets.StateName).ShouldBe(PrototypeTargets.Compact);
    }

    [Theory]
    [InlineData(599.9f, "compact")]
    [InlineData(600f, "regular")]
    public void TheBoundaryIs600(float width, string expected)
        => PrototypeTargets.ForWidth(width).ShouldBe(expected);

    [Fact]
    public void ABuilderOnItsOwnSetsTargetOnlyOnceItHasADesignSize()
    {
        var state = new PrototypeState();
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, state);
        var screen = Doc().Screens[0];

        var bare = builder.Build(screen);
        state.Has(PrototypeTargets.StateName).ShouldBeFalse("no design size, so nothing says which target this is");
        Has(bare, "always").ShouldBeTrue();
        Has(bare, "phone").ShouldBeFalse();
        Has(bare, "desktop").ShouldBeFalse();

        builder.DesignSize = (1440f, 900f);
        Has(builder.Build(screen), "desktop").ShouldBeTrue();
        state.Get(PrototypeTargets.StateName).ShouldBe(PrototypeTargets.Regular);
    }

    [Fact]
    public void ThePlayersTargetFollowsItsOwnDesignSize_NotOneWrittenOnItsBuilder()
    {
        var player = new PrototypePlayer<RgbaImage>(new RgbaImageRenderer(10, 10), Doc(), PrototypeKit.Theme, "font.ttf");
        player.TreeBuilder.DesignSize = (1440f, 900f);

        Has(player.BuildTree(), "phone").ShouldBeTrue("the player draws at 360 wide, so the screen is the compact one");
        player.State.Get(PrototypeTargets.StateName).ShouldBe(PrototypeTargets.Compact);
        player.TreeBuilder.DesignSize.ShouldBe(player.DesignSize);
    }

    [Fact]
    public void BuildingATreeNeverRaisesChanged_EvenWhenTheTargetMoves()
    {
        var player = new PrototypePlayer<RgbaImage>(new RgbaImageRenderer(10, 10), Doc(), PrototypeKit.Theme, "font.ttf");
        var raised = 0;
        player.State.Changed += () => raised++;

        player.BuildTree();
        player.Target = new PrototypeTarget { Name = "desktop", Width = 1440, Height = 900 };
        player.BuildTree();
        player.State.Get(PrototypeTargets.StateName).ShouldBe(PrototypeTargets.Regular, "the target did move");
        raised.ShouldBe(0, "target is written while building, and a build that raised Changed would ask for another build");
    }

    [Fact]
    public void ADefaultFindingPrintsWithoutThrowing()
    {
        // default skips the Target initializer, so Target is null there and ToString must not assume a string.
        default(PrototypeFinding).Target.ShouldBeNull();
        default(PrototypeFinding).ToString().ShouldBe(" [] 0x0: ");
    }

    [Fact]
    public void TargetsMustBeAnArray()
        => Paths(Wrap("\"targets\": 3,")).ShouldBe(["$.targets"]);

    [Fact]
    public void ATargetMissingFieldsIsReportedPerField()
        => Paths(Wrap("\"targets\": [ {} ],")).ShouldBe(["$.targets[0].name", "$.targets[0].width", "$.targets[0].height"]);

    [Fact]
    public void ANonPositiveNumberIsReported()
        => Paths(Wrap("""
            "targets": [ { "name": "a", "width": 0, "height": -1, "scale": 0 } ],
            """)).ShouldBe(["$.targets[0].width", "$.targets[0].height", "$.targets[0].scale"]);

    [Fact]
    public void AWrongTypeIsReported()
        => Paths(Wrap("""
            "targets": [ { "name": "a", "width": "wide", "height": 10 } ],
            """)).ShouldBe(["$.targets[0].width"]);

    [Fact]
    public void AnUnknownPropertyIsReported()
        => Paths(Wrap("""
            "targets": [ { "name": "a", "width": 1, "height": 1, "dpi": 2 } ],
            """)).ShouldBe(["$.targets[0].dpi"]);

    [Fact]
    public void ADuplicateNameIsReported()
        => Paths(Wrap("""
            "targets": [ { "name": "a", "width": 1, "height": 1 }, { "name": "a", "width": 2, "height": 2 } ],
            """)).ShouldBe(["$.targets[1].name"]);

    [Fact]
    public void ATargetThatIsNotAnObjectIsReported()
        => Paths(Wrap("\"targets\": [ 1 ],")).ShouldBe(["$.targets[0]"]);

    [Fact]
    public void AWhenOnTargetNamingAnotherValueIsReported()
        => Paths(Wrap("", """{ "kind": "vstack", "children": [ { "kind": "spacer", "when": "target=tablet" } ] }"""))
            .ShouldBe(["$.screens[0].root.children[0].when"]);

    [Fact]
    public void SetAndToggleAndBindOnTargetAreReported()
        => Paths(Wrap("", """
            { "kind": "vstack", "children": [
              { "kind": "text", "text": "x", "action": "set:target=compact" },
              { "kind": "text", "text": "y", "action": "toggle:target" },
              { "kind": "checkbox", "label": "z", "bind": "target" } ] }
            """)).ShouldBe([
                "$.screens[0].root.children[0].action",
                "$.screens[0].root.children[1].action",
                "$.screens[0].root.children[2].bind"]);
}
