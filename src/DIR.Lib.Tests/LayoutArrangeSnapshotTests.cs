using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Pins the arranged rect of every node in a broad set of trees under a float pixel context and an int cell
/// context that rounds design units the way Console.Lib's <c>CellMeasureContext.PixelAuthored</c> does (8x16).
/// The expected text lives in <c>Snapshots/*.txt</c>. Set <c>DIR_LIB_UPDATE_SNAPSHOTS=1</c> to rewrite it, only
/// after reading why a rect moved.
/// </summary>
public class LayoutArrangeSnapshotTests
{
    private static readonly bool Update = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_SNAPSHOTS") == "1";

    private sealed record SnapSplitHit(string Id) : HitResult;

    private sealed class PixelCtx : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 7f, 16f);
        public float ToSurface(float designUnits) => designUnits;
    }

    /// <summary>Cells of 8x16 design units, one row per line of text, width = chars.</summary>
    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
    }

    private static Layout.Node Box() => new Layout.Node.Leaf(new Layout.Content.Box(0, 0));
    private static Layout.Node Text(string s) => new Layout.Node.Leaf(new Layout.Content.Text(s));
    private static Layout.Node Fx(Layout.Node n, float w, float h) =>
        n with { Width = Layout.Sizing.Fixed(w), Height = Layout.Sizing.Fixed(h) };
    private static Layout.Node Row(float h) =>
        new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Height = Layout.Sizing.Fixed(h), Width = Layout.Sizing.Star() };
    private static Layout.Node Star(Layout.Node n) =>
        n with { Width = Layout.Sizing.Star(), Height = Layout.Sizing.Star() };
    private static readonly RGBAColor32 Bg = new(0x40, 0x40, 0x48, 0xff);

    private static Layout.Node Stack(Layout.Axis axis, float gap, params Layout.Node[] c) =>
        new Layout.Node.Stack([.. c], axis, gap);

    private static IEnumerable<(string Name, Func<Layout.Node> Build)> Cases()
    {
        yield return ("rowh28_over_text", () => Stack(Layout.Axis.Vertical, 0,
            new Layout.Node.Stack([Text("hello")]) { Height = Layout.Sizing.Fixed(28), Width = Layout.Sizing.Star() },
            Star(Box())));
        yield return ("bg_bar_8", () => Stack(Layout.Axis.Vertical, 0,
            Row(8) with { Background = Bg }, Star(Box())));
        yield return ("empty_spacer_8", () => Stack(Layout.Axis.Vertical, 0,
            Row(8), Text("a"), Row(8), Star(Box())));
        yield return ("padding_4", () => new Layout.Node.Stack([Text("hello"), Text("ab")]) { Padding = 4f });
        yield return ("padding_12", () => new Layout.Node.Stack([Text("hello"), Text("ab")]) { Padding = 12f });
        yield return ("padding_split_x_y", () => new Layout.Node.Stack([Text("hello")]) { Padding = 12f, PaddingY = 4f });
        yield return ("stack_fixed_main_cross_gap", () => Stack(Layout.Axis.Vertical, 6,
            Fx(Box(), 50, 20), Fx(Box(), 90, 28), Text("x")));
        yield return ("hstack_fixed_gap", () => Stack(Layout.Axis.Horizontal, 10,
            Fx(Box(), 20, 12), Fx(Box(), 36, 40), Star(Box())));
        yield return ("stack_min_max", () => Stack(Layout.Axis.Vertical, 0,
            Box() with { Height = Layout.Sizing.Star(1f, 20f, 50f), Width = Layout.Sizing.Star() },
            Star(Box())));
        yield return ("text_and_editor", () => Stack(Layout.Axis.Vertical, 0,
            Text("some text"), new Layout.Node.Leaf(new Layout.Content.TextEditor(new TextEditorState())),
            new Layout.Node.Leaf(new Layout.Content.Fill(10f, 20f))));
        yield return ("box_content_extent", () => Stack(Layout.Axis.Vertical, 0,
            new Layout.Node.Leaf(new Layout.Content.Box(30, 30)), new Layout.Node.Leaf(new Layout.Content.Box(70, 12))));
        yield return ("grid_even", () => Star(new Layout.Node.Grid(2, [Box(), Box(), Box(), Box()], RowGap: 4f, ColumnGap: 12f)));
        yield return ("grid_fixed_columns", () => new Layout.Node.Grid(3,
            [Text("a"), Text("bb"), Text("ccc"), Text("d"), Text("e"), Text("f")], RowGap: 6f, ColumnGap: 10f, AutoRows: true)
            .WithColumns(Layout.Sizing.Fixed(40), Layout.Sizing.Auto, Layout.Sizing.Star()));
        yield return ("wrap_fixed", () => new Layout.Node.Wrap(
            [Fx(Box(), 30, 12), Fx(Box(), 30, 12), Fx(Box(), 30, 12), Fx(Box(), 30, 20)],
            Layout.Axis.Horizontal, Gap: 8f, LineGap: 6f));
        yield return ("split_h", () => Star(new Layout.Node.Split(Star(Box()), Star(Box()), Layout.Axis.Horizontal,
            FirstExtent: 30f, DividerThickness: 6f, DividerHit: new SnapSplitHit("s"))));
        yield return ("split_v", () => Star(new Layout.Node.Split(Star(Box()), Star(Box()), Layout.Axis.Vertical,
            FirstExtent: 28f, DividerThickness: 8f, DividerHit: new SnapSplitHit("s"))));
        yield return ("dock_strips", () => new Layout.Node.Dock(
            [new Layout.DockChild(Layout.DockSide.Top, Box(), Layout.Sizing.Fixed(28)),
             new Layout.DockChild(Layout.DockSide.Left, Box(), Layout.Sizing.Fixed(40)),
             new Layout.DockChild(Layout.DockSide.Bottom, Box(), Layout.Sizing.Fixed(8)),
             new Layout.DockChild(Layout.DockSide.Right, Box(), Layout.Sizing.Fixed(20))],
            Box()));
        yield return ("anchored", () => Star(new Layout.Node.Stack([Box(),
            new Layout.Node.Anchored(Fx(Box(), 40, 30), Layout.DockSide.Right, Margin: 8f)])));
        yield return ("nested_padded_fixed", () => Stack(Layout.Axis.Vertical, 4,
            new Layout.Node.Stack([Fx(Box(), 60, 20), Text("hi")])
            { Padding = 4f, Height = Layout.Sizing.Fixed(60), Width = Layout.Sizing.Fixed(100), Background = Bg },
            Star(Box())));
    }

    private static string Kind(Layout.Node n) => n switch
    {
        Layout.Node.Leaf l => "Leaf:" + l.Content.GetType().Name,
        _ => n.GetType().Name,
    };

    private static string Fmt<T>(T v) where T : INumber<T> => Convert.ToString(v, CultureInfo.InvariantCulture)!;

    private static string Serialise<T>(Layout.Node root, Layout.IMeasureContext<T> ctx, T w, T h) where T : INumber<T>
    {
        var arranged = Layout.Engine.Arrange(root, new Rect<T>(T.Zero, T.Zero, w, h), ctx);
        var sb = new StringBuilder();
        foreach (var a in arranged)
        {
            var b = a.Bounds;
            sb.Append(a.Depth).Append(' ').Append(Kind(a.Node)).Append(' ')
              .Append(Fmt(b.X)).Append(' ').Append(Fmt(b.Y)).Append(' ')
              .Append(Fmt(b.Width)).Append(' ').Append(Fmt(b.Height)).Append('\n');
        }
        return sb.ToString();
    }

    private static string Snapshot(string name)
    {
        var build = Cases().First(c => c.Name == name).Build;
        var sb = new StringBuilder();
        sb.Append("# pixel 200x160\n").Append(Serialise(build(), new PixelCtx(), 200f, 160f));
        sb.Append("# cell 25x10 (8x16)\n").Append(Serialise(build(), new CellCtx(), 25, 10));
        return sb.ToString();
    }

    private static string ThisFile([CallerFilePath] string? file = null) => file!;

    public static TheoryData<string> CaseNames()
    {
        var d = new TheoryData<string>();
        foreach (var c in Cases()) { d.Add(c.Name); }
        return d;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ArrangedRects_MatchSnapshot(string name)
    {
        var thisFile = ThisFile();
        var actual = Snapshot(name);
        var fileName = name + ".txt";
        if (Update)
        {
            var dir = Path.Combine(Path.GetDirectoryName(thisFile!)!, "Snapshots");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), actual);
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Snapshots", fileName);
        File.Exists(path).ShouldBeTrue($"Snapshot {fileName} is missing; set DIR_LIB_UPDATE_SNAPSHOTS=1 once to create it.");
        File.ReadAllText(path).ShouldBe(actual);
    }
}
