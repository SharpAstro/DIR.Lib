using System;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>A node's declared border is reserved inside its extent, outside its padding, per side, on a pixel and
/// a cell surface, in measure and arrange alike.</summary>
public class LayoutBorderTests
{
    private sealed class PixelCtx : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 7f, 16f);
        public float ToSurface(float designUnits) => designUnits;
    }

    /// <summary>8x16 cells, default MapExtent.</summary>
    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
    }

    private static readonly RGBAColor32 Line = new(0xff, 0xff, 0xff, 0xff);

    private static Layout.Node Fx(float w, float h) =>
        new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Width = Layout.Sizing.Fixed(w), Height = Layout.Sizing.Fixed(h) };

    private static Layout.Node Star() =>
        new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Width = Layout.Sizing.Star(), Height = Layout.Sizing.Star() };

    private static Rect<float> ChildPx(Layout.Node root, float w = 200f, float h = 100f)
        => Layout.Engine.Arrange(root, new Rect<float>(0, 0, w, h), new PixelCtx())[1].Bounds;

    private static Rect<int> ChildCell(Layout.Node root, int w = 25, int h = 10)
        => Layout.Engine.Arrange(root, new Rect<int>(0, 0, w, h), new CellCtx())[1].Bounds;

    private static Layout.Node Bordered(Layout.Node child) => new Layout.Node.Stack([child]).Border(Line, 3f);

    [Fact]
    public void Pixel_all_sides_inset_by_width()
    {
        var b = ChildPx(Bordered(Star()));
        (b.X, b.Y, b.Width, b.Height).ShouldBe((3f, 3f, 194f, 94f));
    }

    [Fact]
    public void Cell_all_sides_inset_by_one_cell()
    {
        var b = ChildCell(Bordered(Star()));
        (b.X, b.Y, b.Width, b.Height).ShouldBe((1, 1, 23, 8));
    }

    [Fact]
    public void Pixel_single_side_reserves_only_that_side()
    {
        var b = ChildPx(new Layout.Node.Stack([Star()]).BorderBottom(Line, 3f));
        (b.X, b.Y, b.Width, b.Height).ShouldBe((0f, 0f, 200f, 97f));
        var t = ChildPx(new Layout.Node.Stack([Star()]).BorderTop(Line, 3f).BorderLeft(Line));
        (t.X, t.Y, t.Width, t.Height).ShouldBe((3f, 3f, 197f, 97f));
    }

    [Fact]
    public void Cell_single_side_reserves_only_that_side()
    {
        var b = ChildCell(new Layout.Node.Stack([Star()]).BorderRight(Line, 3f));
        (b.X, b.Y, b.Width, b.Height).ShouldBe((0, 0, 24, 10));
    }

    [Fact]
    public void Border_sits_outside_padding()
    {
        var b = ChildPx(new Layout.Node.Stack([Star()]) { Padding = 10f }.Border(Line, 3f));
        (b.X, b.Y, b.Width, b.Height).ShouldBe((13f, 13f, 174f, 74f));
    }

    [Fact]
    public void Auto_measure_is_child_plus_border()
    {
        var plain = Layout.Engine.Measure(new Layout.Node.Stack([Fx(40, 20)]), new Layout.Size<float>(200, 100), new PixelCtx());
        var node = new Layout.Node.Stack([Fx(40, 20)]).Border(Line, 3f);
        var m = Layout.Engine.Measure(node, new Layout.Size<float>(200, 100), new PixelCtx());
        (m.Width, m.Height).ShouldBe((plain.Width + 6f, plain.Height + 6f));

        var c = Layout.Engine.Measure(node, new Layout.Size<int>(25, 10), new CellCtx());
        var cPlain = Layout.Engine.Measure(new Layout.Node.Stack([Fx(40, 20)]), new Layout.Size<int>(25, 10), new CellCtx());
        (c.Width, c.Height).ShouldBe((cPlain.Width + 2, cPlain.Height + 2));
    }

    [Fact]
    public void Root_border_is_honoured_like_its_padding()
    {
        var arranged = Layout.Engine.Arrange(Bordered(Fx(10, 10)), new Rect<float>(5, 6, 100, 50), new PixelCtx());
        arranged[0].Bounds.ShouldBe(new Rect<float>(5, 6, 100, 50));
        (arranged[1].Bounds.X, arranged[1].Bounds.Y).ShouldBe((8f, 9f));
    }

    [Fact]
    public void Bordered_dock_insets_its_fill()
    {
        var dock = new Layout.Node.Dock([], Star()).Border(Line, 3f);
        var b = ChildPx(dock);
        (b.X, b.Y, b.Width, b.Height).ShouldBe((3f, 3f, 194f, 94f));
    }

    [Fact]
    public void Bordered_split_insets_its_panes()
    {
        var split = new Layout.Node.Split(Star(), Star(), Layout.Axis.Horizontal, FirstExtent: 50f, DividerThickness: 0f).Border(Line, 3f);
        var first = ChildPx(split);
        (first.X, first.Y, first.Height).ShouldBe((3f, 3f, 94f));
        first.Width.ShouldBe(50f);
    }

    [Fact]
    public void Tree_without_border_is_unchanged()
    {
        var b = ChildPx(new Layout.Node.Stack([Star()]) { Padding = 4f });
        (b.X, b.Width).ShouldBe((4f, 192f));
    }

    [Fact]
    public void Transparent_border_still_reserves_but_empty_box_is_not_a_fill_without_colour()
    {
        var b = ChildPx(new Layout.Node.Stack([Star()]).Border(default, 3f));
        b.X.ShouldBe(3f);
    }
}
