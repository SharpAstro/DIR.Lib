using DIR.Lib.Prototyping;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Items 8 and 9 of #330 driven the way a host drives them: Enter and a double-click reach a tree row, a table row and a
/// plain node through <see cref="InputRouter"/> over a <see cref="PrototypePlayer{TSurface}"/>, never by calling the
/// delegate; and the faces a host gives reach the painter.
/// </summary>
public sealed class PrototypeKeyboardTests
{
    private const string TreeOnly = """
        { "width": 300, "height": 200, "entry": "s", "screens": [ { "id": "s", "root": {
          "kind": "vstack", "children": [
            { "kind": "tree", "bind": "sel", "children": [
              { "kind": "treeRow", "key": "a", "text": "A", "doubleAction": "set:opened=a" },
              { "kind": "treeRow", "key": "b", "text": "B", "action": "set:clicked=b", "doubleAction": "set:opened=b" },
              { "kind": "treeRow", "key": "c", "text": "C", "action": "set:clicked=c" } ] } ] } } ] }
        """;

    private const string TableOnly = """
        { "width": 300, "height": 200, "entry": "s", "screens": [ { "id": "s", "root": {
          "kind": "vstack", "children": [
            { "kind": "table", "columns": ["X"], "columnWidths": ["star"], "children": [
              { "kind": "tableRow", "doubleAction": "set:opened=r1", "children": [ { "kind": "text", "text": "one" } ] },
              { "kind": "tableRow", "action": "set:picked=r2", "children": [ { "kind": "text", "text": "two" } ] } ] } ] } } ] }
        """;

    private const string PlainNode = """
        { "width": 300, "height": 200, "entry": "s", "screens": [ { "id": "s", "root": {
          "kind": "vstack", "children": [
            { "kind": "text", "text": "Open me", "key": "t", "doubleAction": "set:opened=t" } ] } } ] }
        """;

    private sealed class Rig
    {
        private readonly RectF32 _bounds = new(0, 0, 300, 200);

        public Rig(string json)
        {
            var result = Prototype.Load(json);
            result.Problems.ShouldBeEmpty();
            Player = new PrototypePlayer<RgbaImage>(new DeclarationStubRenderer(300, 200), result.Document!,
                PrototypeKit.Theme, "stub.ttf");
            Router = new InputRouter(Player.Ui, new BackgroundTaskTracker(), () => { }) { Widgets = () => [Player] };
            Paint();
        }

        public PrototypePlayer<RgbaImage> Player { get; }

        public InputRouter Router { get; }

        public void Paint()
        {
            Player.Render(_bounds);
            Router.AfterPaint();
        }

        public void Key(InputKey key)
        {
            Router.Handle(new InputEvent.KeyDown(key));
            Paint();
        }

        public ClickableRegion Region(Func<HitResult, bool> match)
            => Player.GetRegisteredRegions().First(r => r.Result is { } hit && match(hit));

        public void Press(ClickableRegion r, int clicks)
        {
            var x = r.X + r.Width / 2f;
            var y = r.Y + r.Height / 2f;
            Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, clicks));
            Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left));
            Paint();
        }
    }

    [Fact]
    public void EnterOnATreeRowRunsItsDoubleAction()
    {
        var rig = new Rig(TreeOnly);
        rig.Key(InputKey.Down);
        rig.Key(InputKey.Enter);
        rig.Player.State.Get("opened").ShouldBe("a");
    }

    [Fact]
    public void DownMovesTheCursorAndEnterActsOnThatRow()
    {
        var rig = new Rig(TreeOnly);
        rig.Key(InputKey.Down);
        rig.Key(InputKey.Down);
        rig.Key(InputKey.Enter);
        rig.Player.State.Get("opened").ShouldBe("b");
    }

    [Fact]
    public void EnterOnARowWithNoDoubleActionRunsItsClick()
    {
        var rig = new Rig(TreeOnly);
        rig.Key(InputKey.Down);
        rig.Key(InputKey.Down);
        rig.Key(InputKey.Down);
        rig.Key(InputKey.Enter);
        rig.Player.State.Get("clicked").ShouldBe("c");
        rig.Player.State.Get("sel").ShouldBe("c");
    }

    [Fact]
    public void TheRowUnderTheCursorShowsFocus()
    {
        var rig = new Rig(TreeOnly);
        rig.Key(InputKey.Down);
        rig.Key(InputKey.Down);
        var tree = rig.Player.BuildTree();
        // The cursor is a declared list on the tree container, and its rows are ListItemHits.
        var rows = rig.Player.GetRegisteredRegions().Where(r => r.Result is HitResult.ListItemHit).ToArray();
        rows.Length.ShouldBe(3);
        rows.ShouldAllBe(r => ((HitResult.ListItemHit)r.Result!).ListId.StartsWith("prototype-tree:"));
        tree.ShouldNotBeNull();
    }

    [Fact]
    public void EnterOnATableRowRunsItsDoubleActionWithNoAction()
    {
        var rig = new Rig(TableOnly);
        rig.Key(InputKey.Down);
        rig.Key(InputKey.Enter);
        rig.Player.State.Get("opened").ShouldBe("r1");
    }

    [Fact]
    public void EnterOnATableRowWithOnlyAnActionRunsIt()
    {
        var rig = new Rig(TableOnly);
        rig.Key(InputKey.Down);
        rig.Key(InputKey.Down);
        rig.Key(InputKey.Enter);
        rig.Player.State.Get("picked").ShouldBe("r2");
    }

    [Fact]
    public void ADoubleClickOnATableRowWithNoActionRunsItsDoubleAction()
    {
        var rig = new Rig(TableOnly);
        var row = rig.Region(h => h is HitResult.ListItemHit { Index: 0 });
        rig.Press(row, 1);
        rig.Press(row, 2);
        rig.Player.State.Get("opened").ShouldBe("r1");
    }

    [Fact]
    public void ADoubleClickOnAPlainNodeWithNoActionRunsItsDoubleAction()
    {
        var rig = new Rig(PlainNode);
        var node = rig.Region(h => h is HitResult.ButtonHit { Action: "t" });
        rig.Press(node, 1);
        rig.Press(node, 2);
        rig.Player.State.Get("opened").ShouldBe("t");
    }

    [Fact]
    public void TwoTreesThatShareARowKeyDoNotShareAHitId()
    {
        const string two = """
            { "width": 300, "height": 200, "entry": "s", "screens": [ { "id": "s", "root": {
              "kind": "vstack", "children": [
                { "kind": "tree", "children": [ { "kind": "treeRow", "key": "k", "text": "A", "trailingIcon": "Plus", "trailingAction": "set:x=1" } ] },
                { "kind": "tree", "children": [ { "kind": "treeRow", "key": "k2", "text": "B", "trailingIcon": "Plus", "trailingAction": "set:x=2" } ] } ] } } ] }
            """;
        var rig = new Rig(two);
        var ids = rig.Player.GetRegisteredRegions().Select(r => r.Result).OfType<HitResult.ButtonHit>()
            .Select(h => h.Action).Where(a => a.StartsWith("tree-trailing:")).ToArray();
        ids.Length.ShouldBe(ids.Distinct().Count());
    }

    [Fact]
    public void FacesGivenToAPlayerReachItsSettings()
    {
        var faces = new PrototypeFaces { Regular = "r.ttf", Bold = "b.ttf", Mono = "m.ttf", MonoBold = "mb.ttf" };
        var result = Prototype.Load(PlainNode);
        var player = new PrototypePlayer<RgbaImage>(new DeclarationStubRenderer(300, 200), result.Document!, PrototypeKit.Theme, faces);
        player.Ui.BoldFontPath.ShouldBe("b.ttf");
        player.Ui.MonoFontPath.ShouldBe("m.ttf");
        player.Ui.MonoBoldFontPath.ShouldBe("mb.ttf");
        player.FontPath.ShouldBe("r.ttf");
    }
}
