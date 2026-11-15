using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.IMeasureContext{T}.MapExtent"/>: the engine hands a measure context every Fixed extent and
/// padding together with what the node holds, so a cell surface can keep a one-line row one row tall, round
/// padding down, drop an empty spacer and keep a thin filled bar.
/// </summary>
public class LayoutExtentHookTests
{
    /// <summary>8x16 cells that implement the hook the way Console.Lib's pixel-authored context wants to.</summary>
    private sealed class HookCellCtx : Layout.IMeasureContext<int>
    {
        public List<Layout.ExtentRequest> Requests { get; } = [];

        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
        public object? MeasureKey => "hook-cell";

        public int MapExtent(in Layout.ExtentRequest r)
        {
            Requests.Add(r);
            var unit = r.Axis == Layout.Axis.Horizontal ? 8f : 16f;
            if (r.Kind == Layout.ExtentKind.Padding)
            {
                return !r.HasContent && !r.HasFill ? 0 : (int)MathF.Floor(r.Value / unit);
            }

            if (r.Axis == Layout.Axis.Vertical && r.HasContent && r.ContentLines == 1)
            {
                return 1;
            }

            var rounded = (int)MathF.Round(r.Value / unit);
            if (!r.HasContent && !r.HasFill)
            {
                return 0;
            }

            return r.HasFill && r.Value > 0f ? Math.Max(1, rounded) : rounded;
        }
    }

    private static readonly RGBAColor32 Bg = new(0x40, 0x40, 0x48, 0xff);

    private static Layout.Node Text(string s) => new Layout.Node.Leaf(new Layout.Content.Text(s));
    private static Layout.Node Spacer(float h) =>
        new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Height = Layout.Sizing.Fixed(h), Width = Layout.Sizing.Star() };

    private static Layout.ArrangedNode<int>[] Arrange(Layout.Node root, HookCellCtx ctx, int w = 40, int h = 20)
        => [.. Layout.Engine.Arrange(root, new Rect<int>(0, 0, w, h), ctx)];

    [Fact]
    public void Fixed28_over_one_line_text_arranges_to_one_row()
    {
        var row = new Layout.Node.Stack([Text("hello")]) { Height = Layout.Sizing.Fixed(28), Width = Layout.Sizing.Star() };
        var root = new Layout.Node.Stack([row, Spacer(0) with { Height = Layout.Sizing.Star() }]);
        var a = Arrange(root, new HookCellCtx());

        // Default mapping would give round(28 / 16) = 2 rows.
        a[1].Bounds.Height.ShouldBe(1);
    }

    [Fact]
    public void Padding_goes_through_the_hook_and_rounds_down()
    {
        var node = new Layout.Node.Stack([Text("hello")]) { Padding = 12f };
        var a = Arrange(node, new HookCellCtx());

        // 12 / 8 = 1.5: the default rounds to 2 columns, the hook floors to 1.
        a[1].Bounds.X.ShouldBe(1);
    }

    [Fact]
    public void Empty_padded_node_adds_no_padding()
    {
        var node = new Layout.Node.Stack([Spacer(16)]) { Padding = 16f };
        var ctx = new HookCellCtx();
        var a = Arrange(node, ctx);

        a[1].Bounds.X.ShouldBe(0);
        ctx.Requests.ShouldContain(r => r.Kind == Layout.ExtentKind.Padding && !r.HasContent && !r.HasFill);
    }

    [Fact]
    public void Empty_and_filled_spacers_are_told_apart()
    {
        var root = new Layout.Node.Stack([Spacer(12), Spacer(12) with { Background = Bg }, Text("x")]);
        var a = Arrange(root, new HookCellCtx());

        a[1].Bounds.Height.ShouldBe(0);   // empty: nothing
        a[2].Bounds.Height.ShouldBe(1);   // filled, thinner than a cell: one row
    }

    [Fact]
    public void Filled_bar_thinner_than_a_cell_keeps_one_row()
    {
        var root = new Layout.Node.Stack([Spacer(4) with { Background = Bg }, Text("x")]);
        var a = Arrange(root, new HookCellCtx());

        a[1].Bounds.Height.ShouldBe(1);
    }

    [Fact]
    public void Request_carries_the_node_facts()
    {
        var two = new Layout.Node.Stack([Text("a\nb")]) { Height = Layout.Sizing.Fixed(40) };
        var filled = new Layout.Node.Stack([Spacer(0)]) { Width = Layout.Sizing.Fixed(24), Background = Bg };
        var root = new Layout.Node.Stack([two, filled]);
        var ctx = new HookCellCtx();
        Arrange(root, ctx);

        var h = ctx.Requests.First(r => r.Axis == Layout.Axis.Vertical && r.Kind == Layout.ExtentKind.Fixed && r.Value == 40f);
        h.HasContent.ShouldBeTrue();
        h.ContentLines.ShouldBe(2);
        h.HasFill.ShouldBeFalse();

        var w = ctx.Requests.First(r => r.Axis == Layout.Axis.Horizontal && r.Kind == Layout.ExtentKind.Fixed && r.Value == 24f);
        w.HasContent.ShouldBeFalse();
        w.ContentLines.ShouldBe(0);
        w.HasFill.ShouldBeTrue();
    }

    [Fact]
    public void Default_hook_is_the_axis_mapping()
    {
        Layout.IMeasureContext<int> ctx = new HookCellCtxDefault();
        ctx.MapExtent(new Layout.ExtentRequest { Value = 28f, Axis = Layout.Axis.Vertical }).ShouldBe(2);
        ctx.MapExtent(new Layout.ExtentRequest { Value = 28f, Axis = Layout.Axis.Horizontal }).ShouldBe(4);
    }

    private sealed class HookCellCtxDefault : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
    }
}
