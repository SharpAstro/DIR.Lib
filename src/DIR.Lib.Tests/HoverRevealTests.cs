using System;
using System.Collections.Generic;
using System.IO;
using DIR.Lib.Layout;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Node.RevealsOnRowHover"/>: a node shown only while the pointer is in its row, or always while the row is
/// selected, in memory through <see cref="RgbaImageRenderer"/> (DejaVu Sans). Three rows of a tree: at rest, hovered, selected.
/// Baseline <c>hover_reveal_rows.png</c>; <c>DIR_LIB_UPDATE_BASELINES=1</c> with a filter on this class writes it.
/// </summary>
public class HoverRevealTests
{
    private const float W = 240f;
    private const float RowHeight = 28f;
    private const float H = RowHeight * 3;

    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static readonly RGBAColor32 Paper = new(0xff, 0xff, 0xff, 0xff);
    private static readonly RGBAColor32 Ink = new(0x20, 0x20, 0x20, 0xff);
    private static readonly RGBAColor32 Hover = new(0xe4, 0xec, 0xf7, 0xff);
    private static readonly RGBAColor32 Selected = new(0xc9, 0xdc, 0xf5, 0xff);
    private static readonly RGBAColor32 Accent = new(0x0b, 0x5c, 0xc9, 0xff);

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Node root) { BeginFrame(); RenderLayout(root, new RectF32(0, 0, W, H), FontPath, DesignScale.One); }
    }

    private static Node Row(int index, string name, bool selected, Action<int> onRun)
    {
        var run = Builder.Text("Run", 12f, Accent, TextAlign.Center).WFixed(40f)
            .Clickable(new HitResult.ButtonHit($"run{index}"), _ => onRun(index))
            .RevealOnRowHover();
        var row = Builder.HStack(Builder.Text(name, 14f, Ink).WStar(), run)
            .RowH(RowHeight).Pad(8f, 0f).CrossCenter()
            .Clickable(new HitResult.ListItemHit("tree", index), _ => { })
            .BgHover(Hover);
        return selected ? row.Bg(Selected).Selected() : row;
    }

    private static Node Tree(Action<int> onRun) => Builder.VStack(
            Row(0, "Rest", false, onRun),
            Row(1, "Hovered", false, onRun),
            Row(2, "Selected", true, onRun))
        .Bg(Paper);

    /// <summary>One cycle of what a host does: paint, let the router settle, move the pointer, paint again.</summary>
    private sealed class Rig
    {
        public readonly RgbaImageRenderer Renderer = new((uint)W, (uint)H);
        public readonly Widget Widget;
        public readonly InputRouter Router;
        public readonly List<int> Runs = [];

        public Rig()
        {
            Widget = new Widget(Renderer) { FontPath = FontPath };
            Router = new InputRouter(Widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [Widget] };
        }

        public Node Build() => Tree(Runs.Add);

        public void Paint()
        {
            Renderer.Surface.Clear(Paper);
            Widget.Render(Build());
            Router.AfterPaint();
        }

        public void MoveTo(float x, float y)
        {
            Router.Handle(new InputEvent.MouseMove(x, y));
            Paint();
        }

        public void Click(float x, float y)
        {
            Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
            Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left, InputModifier.None));
        }
    }

    // The run button's centre in a row, and a spot on the row's label.
    private static float RunX => W - 8f - 20f;
    private static float RowY(int row) => row * RowHeight + RowHeight / 2f;

    [Fact]
    public void AHiddenNodeCannotBePressedAndAShownOneCan()
    {
        var rig = new Rig();
        rig.Paint();

        // The pointer is not in any row yet, so nothing is revealed and the press lands on the row, not on a button.
        rig.Click(RunX, RowY(0));
        rig.Runs.ShouldBeEmpty();

        // Into row 0: the next paint reveals its button, and now it is there to press.
        rig.MoveTo(20f, RowY(0));
        rig.Click(RunX, RowY(0));
        rig.Runs.ShouldBe([0]);
    }

    /// <summary>
    /// A list declared on a hover-revealed node that is hidden this frame is not on the frame. Were it counted, the
    /// frame would hold two lists ("shown" and "hidden"), <see cref="DeclaredList.Active"/> would pick neither, and Down
    /// would not reach the shown one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AListOnAHiddenNodeIsNotOnTheFrame(bool descendant)
    {
        var shown = new ListCursor();
        var hidden = new ListCursor();
        Node Build()
        {
            var hiddenList = Builder.VStack(
                    Builder.Text("x", 12f, Accent).Clickable(new HitResult.ListItemHit("hidden", 0), _ => { }))
                .WFixed(40f).ListOf(hidden, "hidden").RevealOnRowHover();
            if (descendant)
            {
                // The list sits below the hidden node, not on it: hiding must reach the whole subtree.
                hiddenList = Builder.VStack(
                        Builder.VStack(
                                Builder.Text("x", 12f, Accent).Clickable(new HitResult.ListItemHit("hidden", 0), _ => { }))
                            .ListOf(hidden, "hidden"))
                    .WFixed(40f).RevealOnRowHover();
            }
            Node Row(int index, Node? extra) => (extra is null
                    ? Builder.HStack(Builder.Text($"Row {index}", 14f, Ink).WStar())
                    : Builder.HStack(Builder.Text($"Row {index}", 14f, Ink).WStar(), extra))
                .RowH(RowHeight).CrossCenter()
                .Clickable(new HitResult.ListItemHit("shown", index), _ => { });
            return Builder.VStack(Row(0, hiddenList), Row(1, null), Row(2, null)).ListOf(shown, "shown").Bg(Paper);
        }

        var renderer = new RgbaImageRenderer((uint)W, (uint)H);
        var widget = new Widget(renderer) { FontPath = FontPath };
        var router = new InputRouter(widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [widget] };
        void Paint()
        {
            widget.Render(Build());
            router.AfterPaint();
        }

        // The pointer is in no row, so the node holding the "hidden" list is not drawn.
        Paint();
        router.Handle(new InputEvent.KeyDown(InputKey.Down));
        Paint();

        shown.ListId.ShouldBe("shown");
        shown.Index.ShouldBe(0);
        hidden.IsOpen.ShouldBeFalse();

        // Positive control: with the pointer in row 0 the same node is revealed, and pressing its row reaches that list.
        // Without this the assertions above cannot tell "hidden" from "never registered".
        router.Handle(new InputEvent.MouseMove(20f, RowY(0)));
        Paint();
        router.Handle(new InputEvent.MouseDown(W - 20f, RowY(0), MouseButton.Left, InputModifier.None, 1));
        router.Handle(new InputEvent.MouseUp(W - 20f, RowY(0), MouseButton.Left, InputModifier.None));
        Paint();
        // The press made "hidden" the last pressed list, so Down steps it; were it not on the frame, Down would not open it.
        router.Handle(new InputEvent.KeyDown(InputKey.Down));
        Paint();
        hidden.IsOpen.ShouldBeTrue();
        hidden.ListId.ShouldBe("hidden");
    }

    [Fact]
    public void ASelectedRowAlwaysShowsItsNode()
    {
        var rig = new Rig();
        rig.Paint();
        rig.Click(RunX, RowY(2));
        rig.Runs.ShouldBe([2]);
    }

    [Fact]
    public void LeavingTheRowHidesItAgain()
    {
        var rig = new Rig();
        rig.Paint();
        rig.MoveTo(20f, RowY(1));
        rig.MoveTo(20f, RowY(0));
        rig.Click(RunX, RowY(1));
        rig.Runs.ShouldBeEmpty("row 1 was left, and a press on its hidden button runs nothing");
    }

    [Fact]
    public void AHiddenNodeKeepsItsRoomSoNothingMoves()
    {
        var ctx = new PixelMeasureContext<RgbaImage>(new RgbaImageRenderer((uint)W, (uint)H), FontPath);
        var arranged = Engine.Arrange(Tree(_ => { }), new Rect<float>(0, 0, W, H), ctx);
        var run = arranged.Single(n => n.Node is Node.Leaf { Content: Content.Text { Value: "Run" } } && n.Bounds.Y < RowHeight);
        run.Bounds.Width.ShouldBe(40f);
    }

    [Fact]
    public void TheHoverEdgeDamagesTheRevealedNode()
    {
        var ctx = new PixelMeasureContext<RgbaImage>(new RgbaImageRenderer((uint)W, (uint)H), FontPath);
        var arranged = Engine.Arrange(Tree(_ => { }), new Rect<float>(0, 0, W, H), ctx);
        var damage = new List<Rect<float>>();
        LayoutDamage.Compute(arranged, arranged, null, (20f, RowY(0)), null, damage);
        damage.ShouldContain(r => r.X + r.Width > W - 48f && r.Y < RowHeight, "the run button of row 0 appeared");
        damage.ShouldAllBe(r => r.Y < RowHeight + 0.5f, "rows 1 and 2 did not change");
    }

    [Fact]
    public void RestHoveredAndSelectedRowsBaseline()
    {
        var rig = new Rig();
        rig.Paint();
        rig.MoveTo(20f, RowY(1));
        CompareBaseline(rig.Renderer.Surface, "hover_reveal_rows.png");
    }

    private static void CompareBaseline(RgbaImage img, string name)
    {
        var baselinePath = Path.Combine(BaselineDir, name);
        if (UpdateBaselines)
        {
            Directory.CreateDirectory(SourceBaselineDir);
            Directory.CreateDirectory(BaselineDir);
            var png = PngWriter.Encode(img.Pixels, img.Width, img.Height);
            File.WriteAllBytes(Path.Combine(SourceBaselineDir, name), png);
            File.WriteAllBytes(baselinePath, png);
            return;
        }

        File.Exists(baselinePath).ShouldBeTrue($"Baseline '{name}' is missing; generate it with DIR_LIB_UPDATE_BASELINES=1.");
        var baseline = PngReader.Decode(File.ReadAllBytes(baselinePath));
        baseline.Width.ShouldBe(img.Width);
        baseline.Height.ShouldBe(img.Height);

        var maxDiff = 0;
        var diffCount = 0;
        for (var i = 0; i < baseline.Pixels.Length; i++)
        {
            var diff = Math.Abs(img.Pixels[i] - baseline.Pixels[i]);
            if (diff > 0) diffCount++;
            if (diff > maxDiff) maxDiff = diff;
        }

        if (maxDiff > 2)
        {
            var actualPath = Path.ChangeExtension(baselinePath, ".actual.png");
            PngWriter.Save(actualPath, img.Pixels, img.Width, img.Height);
            Assert.Fail($"Baseline mismatch for '{name}': {diffCount} pixels differ, max diff={maxDiff}. Actual saved to '{actualPath}'.");
        }
    }
}
