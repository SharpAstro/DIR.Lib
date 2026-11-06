using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A subtree's measure is kept across frames, keyed by node reference, available size and the context's key, and within one
/// pass no child is measured twice at the same available size (DIR.Lib #170, part 2).
/// </summary>
public class LayoutMeasureMemoTests
{
    private static readonly Layout.Size<float> Room = new(200f, 1000f);

    private static Layout.Node Rows(params string[] labels) =>
        Layout.Builder.VStack([.. labels.Select(l => Layout.Builder.Text(l, 10f))]).WStar().HAuto();

    [Fact]
    public void MeasuringTheSameTreeAgainWithAKeyedContextMeasuresNoText()
    {
        var tree = Rows("one", "two", "three");
        var ctx = new CountingContext { Key = "k" };

        var first = Layout.Engine.Measure(tree, Room, ctx);
        var afterFirst = ctx.TextCalls;
        afterFirst.ShouldBeGreaterThan(0);

        Layout.Engine.Measure(tree, Room, ctx).ShouldBe(first);
        ctx.TextCalls.ShouldBe(afterFirst);
    }

    [Fact]
    public void ArrangingTheSameTreeAgainWithAKeyedContextMeasuresNoText()
    {
        var tree = Rows("one", "two", "three");
        var ctx = new CountingContext { Key = "k" };
        var bounds = new Rect<float>(0, 0, 200f, 1000f);

        var first = Layout.Engine.Arrange(tree, bounds, ctx);
        var afterFirst = ctx.TextCalls;

        var second = Layout.Engine.Arrange(tree, bounds, ctx);
        ctx.TextCalls.ShouldBe(afterFirst);
        second.Select(a => a.Bounds).ShouldBe(first.Select(a => a.Bounds));
    }

    [Fact]
    public void AnEqualKeyOnAFreshContextKeepsTheMeasure()
    {
        var tree = Rows("one", "two");
        var first = new CountingContext { Key = ("font", 1f) };
        Layout.Engine.Measure(tree, Room, first);

        var second = new CountingContext { Key = ("font", 1f) };
        Layout.Engine.Measure(tree, Room, second);
        second.TextCalls.ShouldBe(0);
    }

    [Fact]
    public void ReplacingOneSubtreeMeasuresOnlyThatSubtree()
    {
        var keep = Layout.Builder.Text("kept", 10f);
        var other = Layout.Builder.Text("other", 10f);
        var ctx = new CountingContext { Key = "k" };
        Layout.Engine.Measure(Layout.Builder.VStack(keep, other).WStar().HAuto(), Room, ctx);

        ctx.TextCalls = 0;
        ctx.Measured.Clear();
        var replacement = Layout.Builder.Text("replaced", 10f);
        Layout.Engine.Measure(Layout.Builder.VStack(keep, replacement).WStar().HAuto(), Room, ctx);

        ctx.Measured.ShouldBe(["replaced"]);
    }

    [Fact]
    public void AContextWithADifferentScaleOrFontMeasuresAgain()
    {
        var tree = Rows("one", "two");
        var ctx = new CountingContext { Key = "font-a", Scale = 1f };
        var small = Layout.Engine.Measure(tree, Room, ctx);

        var other = new CountingContext { Key = "font-a", Scale = 2f };
        // Same key claims the same metrics, so it is trusted: the proof a key is what decides.
        Layout.Engine.Measure(tree, Room, other).ShouldBe(small);
        other.TextCalls.ShouldBe(0);

        var rescaled = new CountingContext { Key = "font-a@2", Scale = 2f };
        var big = Layout.Engine.Measure(tree, Room, rescaled);
        rescaled.TextCalls.ShouldBeGreaterThan(0);
        big.Height.ShouldBe(small.Height * 2f, 0.01f);

        var refont = new CountingContext { Key = "font-b", Scale = 1f };
        Layout.Engine.Measure(tree, Room, refont);
        refont.TextCalls.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ATextEditorSubtreeIsMeasuredAgainAfterItsStateChanges()
    {
        var state = new TextEditorState { Text = "short" };
        var tree = Layout.Builder.VStack(new Layout.Node.Leaf(new Layout.Content.TextEditor(state, 10f)).WStar()).WStar().HAuto();
        var ctx = new CountingContext { Key = "k" };

        Layout.Engine.Measure(tree, Room, ctx);
        ctx.TextCalls = 0;
        state.Text = "something that is much longer than it was";
        Layout.Engine.Measure(tree, Room, ctx);

        ctx.Measured.ShouldContain("something that is much longer than it was");
    }

    [Fact]
    public void ATextInputSubtreeIsMeasuredAgainAfterItsStateChanges()
    {
        var state = new TextInputState { Placeholder = "ab" };
        var tree = Layout.Builder.VStack(new Layout.Node.Leaf(new Layout.Content.TextInput(state, 10f))).WStar().HAuto();
        var ctx = new CountingContext { Key = "k" };

        var before = Layout.Engine.Measure(tree, Room, ctx);
        state.Placeholder = "abcdefghij";
        var after = Layout.Engine.Measure(tree, Room, ctx);

        after.Width.ShouldBeGreaterThan(before.Width);
    }

    [Fact]
    public void AContextWithNoKeyKeepsNothingAcrossCalls()
    {
        var tree = Rows("one", "two");
        var ctx = new CountingContext();

        Layout.Engine.Measure(tree, Room, ctx);
        var afterFirst = ctx.TextCalls;
        Layout.Engine.Measure(tree, Room, ctx);

        ctx.TextCalls.ShouldBe(afterFirst * 2);
    }

    [Fact]
    public void NoChildIsMeasuredTwiceAtTheSameAvailableSizeInOnePass()
    {
        // Nested auto stacks used to re-measure each level's subtree at the same size; with no key at all, one pass
        // must still measure each leaf once.
        Layout.Node node = Layout.Builder.Text("leaf", 10f);
        for (var i = 0; i < 6; i++)
        {
            node = Layout.Builder.VStack(Layout.Builder.Text("x", 10f), node).WStar().HAuto();
        }

        var ctx = new CountingContext();
        Layout.Engine.Arrange(node, new Rect<float>(0, 0, 200f, 1000f), ctx);

        ctx.Measured.Count(t => t == "leaf").ShouldBe(1);
    }

    [Fact]
    public void WrappedTextMeasuredAtTwoWidthsGivesTwoHeights()
    {
        var row = Layout.Builder.WrapH(Layout.Builder.Text("aaaa", 10f), Layout.Builder.Text("bbbb", 10f),
            Layout.Builder.Text("cccc", 10f), Layout.Builder.Text("dddd", 10f)).WStar().HAuto();
        var ctx = new CountingContext { Key = "k" };

        var wide = Layout.Engine.Measure(row, new Layout.Size<float>(400f, 1000f), ctx);
        var narrow = Layout.Engine.Measure(row, new Layout.Size<float>(90f, 1000f), ctx);
        var wideAgain = Layout.Engine.Measure(row, new Layout.Size<float>(400f, 1000f), ctx);

        narrow.Height.ShouldBeGreaterThan(wide.Height);
        wideAgain.ShouldBe(wide);
    }

    private sealed class CountingContext : Layout.IMeasureContext<float>
    {
        public object? Key { get; init; }

        public float Scale { get; init; } = 1f;

        public int TextCalls { get; set; }

        public List<string> Measured { get; } = [];

        public object? MeasureKey => Key;

        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize)
        {
            TextCalls++;
            Measured.Add(text.ToString());
            return new(text.Length * fontSize * Scale, fontSize * Scale);
        }

        public float ToSurface(float designUnits) => designUnits * Scale;
    }
}
