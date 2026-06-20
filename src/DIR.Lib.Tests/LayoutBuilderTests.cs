using System;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Tests for the <see cref="Builder"/> DSL + the fluent <see cref="Node"/> modifiers. Two guarantees:
/// (1) every factory/modifier emits exactly the records you would hand-write (the DSL is pure sugar), and
/// (2) a DSL-built tree arranges identically to the hand-built equivalent. This file imports
/// <c>DIR.Lib.Layout</c> for bareword brevity; production consumers instead write the qualified
/// <c>Layout.Builder</c> / <c>Layout.Node</c> form (see the Builder remarks).
/// </summary>
public class LayoutBuilderTests
{
    private sealed class PixelCtx : IMeasureContext<float>
    {
        public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 7f, 16f);
        public float ToSurface(float designUnits) => designUnits;
    }

    private static readonly RGBAColor32 Bg = new(0x10, 0x20, 0x30, 0xff);

    // --- Leaf factories round-trip to the hand-written records (value equality) ---

    [Fact]
    public void Text_RoundTripsToHandBuiltLeaf()
        => Builder.Text("hi").ShouldBe(new Node.Leaf(new Content.Text("hi")));

    [Fact]
    public void Text_CarriesStyle()
    {
        var leaf = Builder.Text("hi", 18f, new RGBAColor32(1, 2, 3, 4), TextAlign.Center, TextAlign.Far)
            .ShouldBeOfType<Node.Leaf>();
        var text = leaf.Content.ShouldBeOfType<Content.Text>();
        text.Value.ShouldBe("hi");
        text.FontSize.ShouldBe(18f);
        text.Color.ShouldBe(new RGBAColor32(1, 2, 3, 4));
        text.HAlign.ShouldBe(TextAlign.Center);
        text.VAlign.ShouldBe(TextAlign.Far);
    }

    [Fact]
    public void Box_Spacer_Fill_RoundTrip()
    {
        Builder.Spacer().ShouldBe(new Node.Leaf(new Content.Box(0f, 0f)));
        Builder.Box(4f, 5f, Bg).ShouldBe(new Node.Leaf(new Content.Box(4f, 5f) { Color = Bg }));
        Builder.Fill(1f, 2f, "k").ShouldBe(new Node.Leaf(new Content.Fill(1f, 2f, "k")));
    }

    // --- Fluent modifiers set the base-declared props (and preserve the runtime node kind) ---

    [Fact]
    public void Modifiers_SetBaseProps()
    {
        var n = Builder.Spacer().WFixed(12f).HStar(2f).Pad(3f).Bg(Bg);
        n.Width.ShouldBe(Sizing.Fixed(12f));
        n.Height.ShouldBe(Sizing.Star(2f));
        n.Padding.ShouldBe(3f);
        n.Background.ShouldBe(Bg);
    }

    [Fact]
    public void RowH_ColW_Stretch_Shorthands()
    {
        var row = Builder.Spacer().RowH(28f);
        row.Width.ShouldBe(Sizing.Star());
        row.Height.ShouldBe(Sizing.Fixed(28f));

        var col = Builder.Spacer().ColW(22f);
        col.Width.ShouldBe(Sizing.Fixed(22f));
        col.Height.ShouldBe(Sizing.Star());

        var stretch = Builder.Spacer().Stretch();
        stretch.Width.ShouldBe(Sizing.Star());
        stretch.Height.ShouldBe(Sizing.Star());
    }

    [Fact]
    public void Clickable_SetsHitAndHandler()
    {
        var hit = new HitResult.ButtonHit("go");
        Action<InputModifier> handler = _ => { };
        var n = Builder.Spacer().Clickable(hit, handler);
        n.Hit.ShouldBe(hit);
        n.OnClick.ShouldBe(handler);
    }

    [Fact]
    public void Modifiers_PreserveRuntimeKind()
    {
        // The polymorphic `this with { ... }` keeps the concrete kind (Stack), not just the static Node type.
        var stack = Builder.HStack(Builder.Spacer()).RowH(10f).ShouldBeOfType<Node.Stack>();
        stack.Axis.ShouldBe(Axis.Horizontal);
        stack.Height.ShouldBe(Sizing.Fixed(10f));
    }

    // --- Containers ---

    [Fact]
    public void VStack_HStack_SetAxis()
    {
        Builder.VStack(Builder.Spacer()).ShouldBeOfType<Node.Stack>().Axis.ShouldBe(Axis.Vertical);
        Builder.HStack(Builder.Spacer()).ShouldBeOfType<Node.Stack>().Axis.ShouldBe(Axis.Horizontal);
    }

    [Fact]
    public void WithGap_AppliesToStackOnly()
    {
        Builder.VStack(Builder.Spacer()).WithGap(7f).ShouldBeOfType<Node.Stack>().Gap.ShouldBe(7f);
        // No-op on a non-stack node (returns it unchanged).
        Builder.Spacer().WithGap(7f).ShouldBeOfType<Node.Leaf>();
    }

    [Fact]
    public void Grid_WithGaps()
    {
        var grid = Builder.Grid(2, Builder.Spacer(), Builder.Spacer()).WithGaps(3f, 4f).ShouldBeOfType<Node.Grid>();
        grid.Columns.ShouldBe(2);
        grid.Cells.Length.ShouldBe(2);
        grid.RowGap.ShouldBe(3f);
        grid.ColumnGap.ShouldBe(4f);
    }

    [Fact]
    public void Split_CarriesAllArgs()
    {
        var hit = new HitResult.ButtonHit("div");
        var split = Builder.Split(Builder.Spacer(), Builder.Spacer(), Axis.Horizontal, 100f, 6f, hit, Bg)
            .ShouldBeOfType<Node.Split>();
        split.Axis.ShouldBe(Axis.Horizontal);
        split.FirstExtent.ShouldBe(100f);
        split.DividerThickness.ShouldBe(6f);
        split.DividerHit.ShouldBe(hit);
        split.DividerColor.ShouldBe(Bg);
    }

    [Fact]
    public void Dock_SideHelpers()
    {
        var dock = Builder.Dock(Builder.Spacer(), Builder.Right(Builder.Spacer(), 50f), Builder.Top(Builder.Spacer(), 20f))
            .ShouldBeOfType<Node.Dock>();
        dock.Docked.Length.ShouldBe(2);
        dock.Docked[0].Side.ShouldBe(DockSide.Right);
        dock.Docked[0].Size.ShouldBe(Sizing.Fixed(50f));
        dock.Docked[1].Side.ShouldBe(DockSide.Top);
        dock.Docked[1].Size.ShouldBe(Sizing.Fixed(20f));
    }

    // --- The behavioural guarantee: DSL tree arranges identically to the hand-built tree ---

    [Fact]
    public void DslTree_ArrangesIdenticallyToHandBuilt()
    {
        Node dsl = Builder.HStack(
                Builder.Spacer().ColW(10f),
                Builder.Text("x").WStar().HStar(),
                Builder.Fill().ColW(20f))
            .RowH(30f)
            .Bg(Bg);

        Node hand = new Node.Stack(
        [
            new Node.Leaf(new Content.Box(0f, 0f)) { Width = Sizing.Fixed(10f), Height = Sizing.Star() },
            new Node.Leaf(new Content.Text("x")) { Width = Sizing.Star(), Height = Sizing.Star() },
            new Node.Leaf(new Content.Fill()) { Width = Sizing.Fixed(20f), Height = Sizing.Star() },
        ], Axis.Horizontal)
        {
            Width = Sizing.Star(),
            Height = Sizing.Fixed(30f),
            Background = Bg,
        };

        var ctx = new PixelCtx();
        var bounds = new Rect<float>(0f, 0f, 200f, 100f);
        var arrangedDsl = Engine.Arrange(dsl, bounds, ctx);
        var arrangedHand = Engine.Arrange(hand, bounds, ctx);

        arrangedDsl.Length.ShouldBe(arrangedHand.Length);
        for (var i = 0; i < arrangedDsl.Length; i++)
        {
            arrangedDsl[i].Bounds.ShouldBe(arrangedHand[i].Bounds);
        }
    }
}
