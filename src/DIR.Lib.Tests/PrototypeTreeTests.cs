using System.Collections.Immutable;
using DIR.Lib.Layout;
using DIR.Lib.Prototyping;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>What the prototype tests share: a theme, the sample document and a one-screen loader.</summary>
internal static class PrototypeKit
{
    public static readonly UiTheme Theme = new()
    {
        Palette = new UiPalette
        {
            ContentBg = new(10, 10, 10, 255),
            PanelBg = new(20, 21, 22, 255),
            HeaderBg = new(30, 31, 32, 255),
            Separator = new(40, 41, 42, 255),
            BodyText = new(200, 201, 202, 255),
            DimText = new(100, 101, 102, 255),
            Accent = new(1, 2, 3, 255),
            Selection = new(50, 60, 70, 255),
            Info = new(0, 0, 255, 255),
            Warn = new(255, 255, 0, 255),
            Error = new(255, 0, 0, 255),
        },
        Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f),
    };

    public static PrototypeDocument Sample()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Prototype", "sample.json");
        var result = Prototype.Load(File.ReadAllText(path));
        result.Problems.ShouldBeEmpty();
        return result.Document!;
    }

    public static PrototypeScreen Screen(string rootJson)
    {
        var json = $$"""{ "width": 360, "height": 640, "entry": "a", "screens": [ { "id": "a", "root": {{rootJson}} } ] }""";
        var result = Prototype.Load(json);
        result.Problems.ShouldBeEmpty();
        return result.Document!.Screens[0];
    }

    public static Node Build(string rootJson, PrototypeState? state = null)
        => new PrototypeTreeBuilder(Theme, state ?? new PrototypeState()).Build(Screen(rootJson));

    /// <summary>Every node of a tree, depth first.</summary>
    public static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        IEnumerable<Node> kids = node switch
        {
            Node.Stack s => s.Children,
            Node.Grid g => g.Cells,
            Node.Wrap w => w.Children,
            Node.Overlay o => [o.Base, o.Top],
            Node.Split sp => [sp.First, sp.Second],
            Node.Dock d => [.. d.Docked.Select(c => c.Child), d.Fill],
            _ => [],
        };
        foreach (var k in kids)
        {
            foreach (var n in Walk(k)) yield return n;
        }
    }

    public static IEnumerable<string> Texts(Node root)
        => Walk(root).OfType<Node.Leaf>().Select(l => l.Content).OfType<Content.Text>().Select(t => t.Value);
}

public sealed class PrototypeTreeTests
{
    [Theory]
    [InlineData("""{ "kind": "vstack" }""", typeof(Node.Stack))]
    [InlineData("""{ "kind": "hstack" }""", typeof(Node.Stack))]
    [InlineData("""{ "kind": "grid", "columns": 2 }""", typeof(Node.Grid))]
    [InlineData("""{ "kind": "wrap" }""", typeof(Node.Wrap))]
    [InlineData("""{ "kind": "dock", "children": [ { "kind": "spacer" } ] }""", typeof(Node.Dock))]
    [InlineData("""{ "kind": "split", "children": [ { "kind": "spacer" }, { "kind": "spacer" } ] }""", typeof(Node.Split))]
    [InlineData("""{ "kind": "overlay", "children": [ { "kind": "spacer" }, { "kind": "spacer" } ] }""", typeof(Node.Overlay))]
    [InlineData("""{ "kind": "text", "text": "x" }""", typeof(Node.Leaf))]
    [InlineData("""{ "kind": "icon", "icon": "Plus" }""", typeof(Node.Leaf))]
    [InlineData("""{ "kind": "box", "width": 4, "height": 5 }""", typeof(Node.Leaf))]
    [InlineData("""{ "kind": "fill" }""", typeof(Node.Leaf))]
    [InlineData("""{ "kind": "spacer" }""", typeof(Node.Leaf))]
    [InlineData("""{ "kind": "textInput", "bind": "n" }""", typeof(Node.Leaf))]
    [InlineData("""{ "kind": "slider", "bind": "v" }""", typeof(Node.Leaf))]
    [InlineData("""{ "kind": "checkbox", "label": "c", "bind": "b" }""", typeof(Node.Stack))]
    [InlineData("""{ "kind": "buttonGroup", "options": ["a", "b"], "bind": "g" }""", typeof(Node.Stack))]
    [InlineData("""{ "kind": "dropdown", "options": ["a", "b"], "bind": "d" }""", typeof(Node.Stack))]
    [InlineData("""{ "kind": "markdown", "source": "hi" }""", typeof(Node.Stack))]
    [InlineData("""{ "kind": "progress", "value": 0.5 }""", typeof(Node.Overlay))]
    public void EachKindBuildsItsBuilderNode(string json, Type expected)
        => PrototypeKit.Build(json).ShouldBeOfType(expected);

    [Fact]
    public void StacksTakeTheirAxisAndGap()
    {
        var v = PrototypeKit.Build("""{ "kind": "vstack", "gap": 5, "children": [ { "kind": "spacer" }, { "kind": "spacer" } ] }""").ShouldBeOfType<Node.Stack>();
        v.Axis.ShouldBe(Axis.Vertical);
        v.Gap.ShouldBe(5f);
        v.Children.Length.ShouldBe(2);
        PrototypeKit.Build("""{ "kind": "hstack" }""").ShouldBeOfType<Node.Stack>().Axis.ShouldBe(Axis.Horizontal);
    }

    [Fact]
    public void TextUsesTheBuilderDefaults()
    {
        var leaf = PrototypeKit.Build("""{ "kind": "text", "text": "hello" }""").ShouldBeOfType<Node.Leaf>();
        var run = leaf.Content.ShouldBeOfType<Content.Text>();
        var reference = Builder.Text("hello").ShouldBeOfType<Node.Leaf>().Content.ShouldBeOfType<Content.Text>();
        run.Value.ShouldBe("hello");
        run.FontSize.ShouldBe(reference.FontSize);
        run.HAlign.ShouldBe(reference.HAlign);
        run.VAlign.ShouldBe(reference.VAlign);
        run.Trim.ShouldBe(reference.Trim);
        run.Color.ShouldBe(PrototypeKit.Theme.Palette.BodyText, "the one default that follows the theme: BodyText, not white");
    }

    [Fact]
    public void ANodeWithAWhenIsAbsentUnlessTheStateMatches()
    {
        const string root = """
            { "kind": "vstack", "children": [
              { "kind": "text", "text": "always" },
              { "kind": "text", "text": "when-on", "when": "flag=true" },
              { "kind": "text", "text": "when-off", "when": "flag=false" } ] }
            """;
        var state = new PrototypeState();

        PrototypeKit.Texts(PrototypeKit.Build(root, state)).ShouldBe(["always", "when-off"], "an unset flag is off");

        state.Set("flag", true);
        PrototypeKit.Texts(PrototypeKit.Build(root, state)).ShouldBe(["always", "when-on"]);

        state.Set("flag", "maybe");
        PrototypeKit.Texts(PrototypeKit.Build(root, state)).ShouldBe(["always"]);
    }

    [Fact]
    public void ARoleNameResolvesThroughThePalette()
    {
        var node = PrototypeKit.Build("""{ "kind": "vstack", "background": "Accent", "border": "Warn", "borderWidth": 2, "borderRadius": 4 }""");
        node.Background.ShouldBe(PrototypeKit.Theme.Palette.Accent);
        node.BorderStyle.ShouldNotBeNull().Color.ShouldBe(PrototypeKit.Theme.Palette.Warn);
        node.BorderStyle!.Width.ShouldBe(2f);
        node.BorderStyle.Radius.ShouldBe(4f);
    }

    [Theory]
    [InlineData("#f80", 255, 136, 0, 255)]
    [InlineData("#102030", 16, 32, 48, 255)]
    [InlineData("#10203040", 16, 32, 48, 64)]
    public void AHexColourResolves(string hex, int r, int g, int b, int a)
    {
        var node = PrototypeKit.Build($$"""{ "kind": "vstack", "background": "{{hex}}" }""");
        node.Background.ShouldBe(new RGBAColor32((byte)r, (byte)g, (byte)b, (byte)a));
    }

    [Fact]
    public void SizingPaddingAndTooltipApply()
    {
        var node = PrototypeKit.Build("""{ "kind": "vstack", "width": 120, "height": "star:2", "padding": 7, "tooltip": "tip" }""");
        node.Width.ShouldBe(Sizing.Fixed(120f));
        node.Height.ShouldBe(Sizing.Star(2f));
        node.Padding.ShouldBe(7f);
        node.Tooltip.ShouldBe("tip");
    }

    [Fact]
    public void AnUnboundInputKeepsItsStateAcrossBuilds()
    {
        var screen = PrototypeKit.Screen("""{ "kind": "vstack", "children": [ { "kind": "textInput" }, { "kind": "textInput" } ] }""");
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, new PrototypeState());

        TextInputState[] Fields() => PrototypeKit.Walk(builder.Build(screen))
            .OfType<Node.Leaf>().Select(l => l.Content).OfType<Content.TextInput>().Select(t => t.State).ToArray();

        var first = Fields();
        first.Length.ShouldBe(2);
        first[0].ShouldNotBeSameAs(first[1], "two unbound fields are two fields");
        first[0].Text = "typed";
        var second = Fields();
        second[0].ShouldBeSameAs(first[0], "a rebuild (the player rebuilds every frame) keeps what was typed");
        second[1].ShouldBeSameAs(first[1]);
    }

    [Fact]
    public void TextWeightFaceAndStyleBuildTheRun()
    {
        Content.Text Run(string json) => PrototypeKit.Build(json).ShouldBeOfType<Node.Leaf>().Content.ShouldBeOfType<Content.Text>();
        var m = PrototypeKit.Theme.Metrics;
        var bold = Run("""{ "kind": "text", "text": "a", "weight": "bold", "face": "mono" }""");
        bold.Weight.ShouldBe(TextWeight.Bold);
        bold.Face.ShouldBe(TextFace.Mono);
        Run("""{ "kind": "text", "text": "a" }""").Weight.ShouldBe(TextWeight.Regular);
        var heading = Run("""{ "kind": "text", "text": "a", "style": "heading" }""");
        heading.Weight.ShouldBe(TextWeight.Bold);
        heading.FontSize.ShouldBe(m.BaseFontSize * m.HeadingFontScale);
        var label = Run("""{ "kind": "text", "text": "Section", "style": "label" }""");
        label.Value.ShouldBe("SECTION");
        label.FontSize.ShouldBe(m.BaseFontSize * m.LabelFontScale);
        label.Color.ShouldBe(PrototypeKit.Theme.Palette.DimText);
    }

    [Fact]
    public void ButtonKindsBuildThroughBuilderAndRunTheirAction()
    {
        var state = new PrototypeState();
        var theme = PrototypeKit.Theme;
        var tree = (Node.Stack)new PrototypeTreeBuilder(theme, state).Build(PrototypeKit.Screen("""
            { "kind": "vstack", "children": [
              { "kind": "button", "text": "Go", "style": "accent", "action": "set:m=1" },
              { "kind": "button", "text": "Link", "style": "link", "action": "set:m=2" },
              { "kind": "button", "text": "Off", "disabled": "Not now", "action": "set:m=3" } ] }
            """));
        tree.Children[0].Background.ShouldBe(Builder.Button("Go", ButtonKind.Accent, () => { }, theme).Background);
        tree.Children[0].OnClick!(InputModifier.None);
        state.Get("m").ShouldBe("1");
        tree.Children[1].OnClick!(InputModifier.None);
        state.Get("m").ShouldBe("2");
        tree.Children[2].OnClick?.Invoke(InputModifier.None);
        state.Get("m").ShouldBe("2", "a disabled button runs nothing");
    }

    [Fact]
    public void ASwitchReadsAndWritesItsBind()
    {
        var state = new PrototypeState();
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, state);
        var screen = PrototypeKit.Screen("""{ "kind": "switch", "bind": "dark", "label": "Dark" }""");
        var off = builder.Build(screen);
        off.Background.ShouldBe(Builder.Switch(false, _ => { }, PrototypeKit.Theme, "Dark").Background);
        PrototypeKit.Walk(off).First(x => x.OnClick is not null).OnClick!(InputModifier.None);
        state.GetBool("dark").ShouldBeTrue();
        PrototypeKit.Walk(builder.Build(screen)).Count(x => x.OnClick is not null).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void HoverRevealSelectedWhenAndDoubleActionApply()
    {
        var state = new PrototypeState();
        var pal = PrototypeKit.Theme.Palette;
        var root = """{ "kind": "text", "text": "a", "hoverBackground": "Separator", "revealOnHover": true, "selectedWhen": "sel=a", "doubleAction": "set:opened=yes" }""";
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, state);
        var screen = PrototypeKit.Screen(root);
        var plain = builder.Build(screen);
        plain.HoverBackground.ShouldBe(pal.Separator);
        plain.RevealsOnRowHover.ShouldBeTrue();
        plain.IsSelected.ShouldBeFalse();
        plain.Background.ShouldBeNull();
        state.Set("sel", "a");
        var chosen = builder.Build(screen);
        chosen.IsSelected.ShouldBeTrue();
        chosen.Background.ShouldBe(pal.Selection);
        chosen.BorderStyle.ShouldNotBeNull().Color.ShouldBe(pal.Accent);
        chosen.OnDoubleClick!(InputModifier.None);
        state.Get("opened").ShouldBe("yes");
        state.Set("opened", "");
        chosen.OnActivate!(InputModifier.None);
        state.Get("opened").ShouldBe("yes");
    }

    [Fact]
    public void AnActionIsAClickableThatRunsAgainstTheState()
    {
        var state = new PrototypeState();
        var seen = new List<PrototypeAction>();
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, state, seen.Add);
        var tree = builder.Build(PrototypeKit.Screen("""
            { "kind": "vstack", "children": [
              { "kind": "text", "text": "a", "action": "set:mode=fast" },
              { "kind": "text", "text": "b", "action": "toggle:on" },
              { "kind": "text", "text": "c", "action": "goto:a" } ] }
            """));
        var buttons = ((Node.Stack)tree).Children;

        buttons[0].Hit.ShouldBe(new HitResult.ButtonHit("set:mode=fast"));
        buttons[0].OnClick!(InputModifier.None);
        state.Get("mode").ShouldBe("fast");
        buttons[1].OnClick!(InputModifier.None);
        state.GetBool("on").ShouldBeTrue();
        buttons[1].OnClick!(InputModifier.None);
        state.GetBool("on").ShouldBeFalse();
        buttons[2].OnClick!(InputModifier.None);

        seen.Select(a => a.Kind).ShouldBe(
        [
            PrototypeActionKind.Set, PrototypeActionKind.Toggle, PrototypeActionKind.Toggle, PrototypeActionKind.Goto,
        ]);
        seen[^1].Target.ShouldBe("a");
    }

    [Fact]
    public void AModalIsClosedWithoutItsCardAndOpenWithIt()
    {
        const string root = """
            { "kind": "modal", "action": "set:dlg=false", "children": [
              { "kind": "text", "text": "page" },
              { "kind": "text", "text": "card", "when": "dlg=true" } ] }
            """;
        var state = new PrototypeState();

        PrototypeKit.Build(root, state).ShouldBeOfType<Node.Leaf>("closed: the layer itself");

        state.Set("dlg", true);
        var open = PrototypeKit.Build(root, state).ShouldBeOfType<Node.Overlay>();
        open.Modal.ShouldBeTrue();
        open.OnDismiss.ShouldNotBeNull();
        open.OnDismiss!();
        state.GetBool("dlg").ShouldBeFalse();
    }

    [Fact]
    public void ABoundTextInputKeepsOneStateAcrossBuildsAndWritesTheState()
    {
        var state = new PrototypeState();
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, state);
        var screen = PrototypeKit.Screen("""{ "kind": "textInput", "bind": "name", "placeholder": "who" }""");

        var first = ((Content.TextInput)((Node.Leaf)builder.Build(screen)).Content).State;
        var second = ((Content.TextInput)((Node.Leaf)builder.Build(screen)).Content).State;
        second.ShouldBeSameAs(first, "focus and caret survive the rebuild");
        first.Placeholder.ShouldBe("who");

        first.OnTextChanged!("Ada");
        state.Get("name").ShouldBe("Ada");

        state.Set("name", "Grace");
        ((Content.TextInput)((Node.Leaf)builder.Build(screen)).Content).State.Text.ShouldBe("Grace", "an unfocused field follows the state");
    }

    [Fact]
    public void ABoundSliderCheckboxAndButtonGroupReadAndWriteTheState()
    {
        var state = new PrototypeState();
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, state);
        var screen = PrototypeKit.Screen("""
            { "kind": "vstack", "children": [
              { "kind": "slider", "bind": "vol", "min": 0, "max": 10 },
              { "kind": "checkbox", "label": "On", "bind": "on" },
              { "kind": "buttonGroup", "options": ["a", "b"], "bind": "pick" } ] }
            """);

        var kids = ((Node.Stack)builder.Build(screen)).Children;
        var slider = ((Content.Slider)((Node.Leaf)kids[0]).Content).State;
        slider.Max.ShouldBe(10f);
        slider.OnChanged!(4f);
        state.GetNumber("vol").ShouldBe(4d);

        kids[1].Hit.ShouldBe(new HitResult.ButtonHit("On"));
        kids[1].OnClick!(InputModifier.None);
        state.GetBool("on").ShouldBeTrue();

        var segments = ((Node.Stack)kids[2]).Children;
        segments.Length.ShouldBe(2);
        segments[1].OnClick!(InputModifier.None);
        state.Get("pick").ShouldBe("b");
    }

    [Fact]
    public void ADropdownOpensInlineAndPicksAnOption()
    {
        var state = new PrototypeState();
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, state);
        var screen = PrototypeKit.Screen("""{ "kind": "dropdown", "options": ["one", "two"], "label": "Pick", "bind": "d" }""");

        var closed = builder.Build(screen).ShouldBeOfType<Node.Stack>();
        PrototypeKit.Texts(closed).ShouldBe(["Pick"]);

        closed.OnClick!(InputModifier.None);
        var open = builder.Build(screen).ShouldBeOfType<Node.Stack>();
        PrototypeKit.Texts(open).ShouldBe(["Pick", "one", "two"]);

        open.Children[2].OnClick!(InputModifier.None);
        state.Get("d").ShouldBe("two");
        PrototypeKit.Texts(builder.Build(screen)).ShouldBe(["two"], "chosen and closed");
    }

    [Fact]
    public void TheSampleBuildsEveryScreenWithNoProblems()
    {
        var doc = PrototypeKit.Sample();
        doc.Screens.Length.ShouldBeGreaterThanOrEqualTo(3);
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, new PrototypeState());
        foreach (var screen in doc.Screens)
        {
            builder.Build(screen).ShouldNotBeNull();
        }
    }

    private const string Workflow = """
        { "kind": "tree", "bind": "sel", "children": [
          { "kind": "treeRow", "key": "group", "text": "Group" },
          { "kind": "treeRow", "key": "stars", "text": "Remove stars", "depth": 2, "mark": "Plus", "note": "12",
            "trailingIcon": "Plus", "trailingAction": "set:ran=stars", "action": "set:opened=stars" } ] }
        """;

    [Fact]
    public void ATreeRowIndentsByDepthAndSelectionFollowsTheBind()
    {
        var state = new PrototypeState();
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, state);
        var screen = PrototypeKit.Screen(Workflow);
        var pal = PrototypeKit.Theme.Palette;

        var rows = builder.Build(screen).ShouldBeOfType<Node.Stack>().Children;
        rows.ShouldAllBe(r => !r.IsSelected);

        // Depth is the first (indent) spacer of the row's stack: depth * the tree row style's indent.
        ((Node.Stack)rows[1]).Children[0].Width.Value.ShouldBe(2 * new TreeRowStyle(pal.DimText).Indent);
        ((Node.Stack)rows[0]).Children[0].Width.Value.ShouldBe(0f);

        rows[1].OnClick!(InputModifier.None);
        state.Get("sel").ShouldBe("stars");
        state.Get("opened").ShouldBe("stars", "the row's action ran too");

        var after = builder.Build(screen).ShouldBeOfType<Node.Stack>().Children;
        after[0].IsSelected.ShouldBeFalse();
        after[1].IsSelected.ShouldBeTrue();
        after[1].Background.ShouldBe(pal.Selection);

        state.Set("sel", "group");
        var moved = builder.Build(screen).ShouldBeOfType<Node.Stack>().Children;
        moved[0].IsSelected.ShouldBeTrue();
        moved[1].IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void ANoteAndMarkOnASelectedRowReadAgainstTheSelectionFill()
    {
        foreach (var palette in new[] { PrototypeKit.Theme.Palette, Prototype.DefaultTheme.Palette, NotificationsRenderTests.Light })
        {
            var theme = new UiTheme { Palette = palette, Metrics = PrototypeKit.Theme.Metrics };
            var state = new PrototypeState();
            state.Set("sel", "stars");
            var row = ((Node.Stack)new PrototypeTreeBuilder(theme, state).Build(PrototypeKit.Screen(Workflow))).Children[1];
            row.Background.ShouldBe(palette.Selection);
            var note = PrototypeKit.Walk(row).Select(n => n is Node.Leaf { Content: Content.Text { Value: "12" } t } ? t : null).Single(t => t is not null)!;
            RGBAColor32.ContrastRatio(note.Color, palette.Selection).ShouldBeGreaterThanOrEqualTo(4.5f,
                "the dim ink is too faint on the Selection fill, so a selected row's note takes the selection ink");
        }
    }

    [Fact]
    public void ATreeRowsTrailingIconIsRevealedOnHoverAndRunsItsAction()
    {
        var state = new PrototypeState();
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, state);
        var row = ((Node.Stack)builder.Build(PrototypeKit.Screen(Workflow))).Children[1];

        row.HoverBackground.ShouldBe(PrototypeKit.Theme.Palette.Separator);
        PrototypeKit.Walk(row).Count(n => n.RevealsOnRowHover).ShouldBe(1);
        var trailing = PrototypeKit.Walk(row).Single(n => n.RevealsOnRowHover);
        trailing.OnClick!(InputModifier.None);
        state.Get("ran").ShouldBe("stars");
        state.Get("sel").ShouldBe("", "the trailing press is its own, not a row selection");
        PrototypeKit.Texts(row).ShouldBe(["Remove stars", "12"]);
    }

    [Fact]
    public void ATabsSelectedTabIsUnderlinedInTheAccentAndAPressSetsTheBind()
    {
        var state = new PrototypeState();
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, state);
        var screen = PrototypeKit.Screen("""{ "kind": "tabs", "options": ["Project", "Frames", "Processes"], "bind": "tab" }""");
        var accent = PrototypeKit.Theme.Palette.Accent;

        var tree = builder.Build(screen);
        PrototypeKit.Texts(tree).ShouldBe(["Project", "Frames", "Processes"]);
        // Unset: the first tab is shown, and exactly one accent band underlines it.
        PrototypeKit.Walk(tree).Count(n => n.Background == accent).ShouldBe(1);

        var tabs = PrototypeKit.Walk(tree).Where(n => n.Hit is HitResult.ListItemHit && n.OnClick is not null).ToArray();
        tabs.Length.ShouldBe(3);
        tabs[2].OnClick!(InputModifier.None);
        state.Get("tab").ShouldBe("Processes");

        PrototypeKit.Walk(builder.Build(screen)).Count(n => n.Background == accent).ShouldBe(1);
        state.Set("tab", "nowhere");
        PrototypeKit.Walk(builder.Build(screen)).Count(n => n.Background == accent).ShouldBe(0, "a state naming no tab underlines none");
    }

    [Fact]
    public void ATabsWidthsComeFromTheMeasureDelegateOrTheDocumentedEstimate()
    {
        var screen = PrototypeKit.Screen("""{ "kind": "tabs", "options": ["Abcd"], "bind": "tab" }""");
        float Extent(PrototypeTreeBuilder b)
            => PrototypeKit.Walk(b.Build(screen)).First(n => n.Hit is HitResult.ListItemHit).Width.Value;

        var estimated = new PrototypeTreeBuilder(PrototypeKit.Theme, new PrototypeState());
        var pad = PrototypeKit.Theme.Metrics.Padding;
        Extent(estimated).ShouldBe(4 * PrototypeKit.Theme.Metrics.BaseFontSize * PrototypeTreeBuilder.TabCharEm + 2 * pad);

        var measured = new PrototypeTreeBuilder(PrototypeKit.Theme, new PrototypeState()) { MeasureLabel = (_, _) => 100f };
        // The measured label plus one padding of slack, so a label never lands exactly on its tab edge, plus the tab's own padding.
        Extent(measured).ShouldBe(100f + pad + 2 * pad);
    }

    private const string Files = """
        { "kind": "table", "columns": ["File", "Kind", "Exposure"], "columnWidths": [120, "star", 60], "children": [
          { "kind": "tableRow", "key": "r1", "selectedWhen": "pick=r1", "action": "set:pick=r1", "children": [
            { "kind": "text", "text": "a.fits" }, { "kind": "text", "text": "light" }, { "kind": "text", "text": "30s" } ] },
          { "kind": "tableRow", "key": "r2", "selectedWhen": "pick=r2", "children": [ { "kind": "text", "text": "b.fits" } ] } ] }
        """;

    [Fact]
    public void ATablesHeaderAndRowsShareColumnsGapAndPadding()
    {
        var table = PrototypeKit.Build(Files).ShouldBeOfType<Node.Stack>();
        table.Children.Length.ShouldBe(3, "a header and two rows");
        var header = PrototypeKit.Walk(table.Children[0]).OfType<Node.Stack>().First();
        PrototypeKit.Texts(header).ShouldBe(["File", "Kind", "Exposure"]);

        foreach (var row in table.Children.Skip(1).Cast<Node.Stack>())
        {
            row.Children.Length.ShouldBe(3, "a missing cell is left empty");
            row.Gap.ShouldBe(header.Gap);
            row.Padding.ShouldBe(header.Padding);
            for (var i = 0; i < 3; i++) row.Children[i].Width.ShouldBe(header.Children[i].Width, $"column {i}");
        }
        header.Children[0].Width.ShouldBe(Sizing.Fixed(120f));
        header.Children[1].Width.ShouldBe(Sizing.Star());
    }

    [Fact]
    public void ATableRowLightsOnHoverAndTakesSelectedWhen()
    {
        var state = new PrototypeState();
        var builder = new PrototypeTreeBuilder(PrototypeKit.Theme, state);
        var screen = PrototypeKit.Screen(Files);
        var pal = PrototypeKit.Theme.Palette;

        var rows = builder.Build(screen).ShouldBeOfType<Node.Stack>().Children;
        rows[1].HoverBackground.ShouldBe(pal.Separator);
        rows[1].IsSelected.ShouldBeFalse();

        rows[1].OnClick!(InputModifier.None);
        var after = builder.Build(screen).ShouldBeOfType<Node.Stack>().Children;
        after[1].IsSelected.ShouldBeTrue();
        after[1].Background.ShouldBe(pal.Selection);
        after[2].IsSelected.ShouldBeFalse();
    }
}
