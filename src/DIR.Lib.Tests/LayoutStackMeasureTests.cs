using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A stack measures each child's cross extent at the main extent the arrange gives it, not at the stack's whole main. A row
/// of a fixed label, a wrapping text and a fixed time was measured as if the text had the label's and the time's width too:
/// it measured fewer lines than it took in its column, and in pilot's chat each long reply's last line was drawn over the
/// row below.
/// </summary>
public class LayoutStackMeasureTests
{
    // Eighteen 10-wide items, beside a 20-wide label and a 10-wide time with gaps of 5: in a row of 100 the items get 60,
    // three lines of six; measured at the whole 100 they made two lines of ten.
    private static Layout.Node Row() =>
        Layout.Builder.HStack(
                Layout.Builder.Spacer().WFixed(20f).HFixed(10f),
                Layout.Builder.WrapH([.. Enumerable.Range(0, 18).Select(_ => Layout.Builder.Spacer().WFixed(10f).HFixed(10f))]).WStar(),
                Layout.Builder.Spacer().WFixed(10f).HFixed(10f))
            .WithGap(5f)
            .WStar()
            .HAuto();

    [Fact]
    public void AStarChildsHeightIsMeasuredAtTheWidthItIsGiven()
    {
        var size = Layout.Engine.Measure(Row(), new Layout.Size<float>(100f, 1000f), new UnitContext());

        size.Height.ShouldBe(30f, 0.01f);
    }

    [Fact]
    public void ARowInAListTakesTheHeightItsWrappedTextIsDrawnIn()
    {
        // Two rows in a list: the second starts under the first's last line, not on it.
        var list = Layout.Builder.VStack(Row(), Row()).WStar().HAuto();
        var arranged = Layout.Engine.Arrange(list, new Rect<float>(0, 0, 100f, 1000f), new UnitContext());

        var items = arranged.Where(a => a.Node is Layout.Node.Leaf && a.Bounds.Width == 10f && a.Bounds.X >= 25f && a.Bounds.X < 85f).ToList();
        items.Count.ShouldBe(36);
        var first = items.Take(18).Max(a => a.Bounds.Y + a.Bounds.Height);
        var second = items.Skip(18).Min(a => a.Bounds.Y);
        second.ShouldBeGreaterThanOrEqualTo(first);
    }

    [Fact]
    public void AnAutoHeightChildOfARowIsArrangedAtTheWidthItIsGiven()
    {
        // The wrap's height is Auto here, so the arrange measures it: at its 60, three lines, not two at the row's 100.
        var row = Layout.Builder.HStack(
                Layout.Builder.Spacer().WFixed(20f).HFixed(10f),
                Layout.Builder.WrapH([.. Enumerable.Range(0, 18).Select(_ => Layout.Builder.Spacer().WFixed(10f).HFixed(10f))]).WStar().HAuto(),
                Layout.Builder.Spacer().WFixed(10f).HFixed(10f))
            .WithGap(5f);
        var arranged = Layout.Engine.Arrange(row, new Rect<float>(0, 0, 100f, 1000f), new UnitContext());

        arranged.Single(a => a.Node is Layout.Node.Wrap).Bounds.Height.ShouldBe(30f, 0.01f);
    }

    // The nesting the reviewer counted: HStack(Text, inner.WStar(), Text) and VStack(Text, inner), alternating.
    private static Layout.Node Nested(int depth)
    {
        Layout.Node node = Layout.Builder.Text("x", 10f);
        for (var i = 0; i < depth; i++)
        {
            node = i % 2 == 0
                ? Layout.Builder.HStack(Layout.Builder.Text("a", 10f), node.WStar(), Layout.Builder.Text("b", 10f))
                : Layout.Builder.VStack(Layout.Builder.Text("c", 10f), node);
        }

        return node;
    }

    [Fact]
    public void MeasureCostGrowsLinearlyWithStackNesting()
    {
        var calls = Enumerable.Range(1, 8).Select(depth =>
        {
            var ctx = new CountingContext();
            Layout.Engine.Measure(Nested(depth), new Layout.Size<float>(1000f, 1000f), ctx);
            return ctx.TextCalls;
        }).ToArray();

        // Every text in the tree is measured once: 3, 4, 6, 7, 9, 10, 12, 13 at depths 1 to 8, as before the fix. Measuring
        // each child a second and third time was 8, 27, 60, ... 6735, doubling and more with every level.
        calls.ShouldBe([3, 4, 6, 7, 9, 10, 12, 13], string.Join(",", calls));
    }

    [Fact]
    public void ArrangeCostGrowsLinearlyWithStackNesting()
    {
        var calls = Enumerable.Range(1, 8).Select(depth =>
        {
            var ctx = new CountingContext();
            Layout.Engine.Arrange(Nested(depth), new Rect<float>(0, 0, 1000f, 1000f), ctx);
            return ctx.TextCalls;
        }).ToArray();

        // An arrange measures the subtree under each Auto child once per level, so it is polynomial in the depth, not
        // exponential: 76 at depth 8 before the fix to stack measures, 4038 with the first version of it.
        calls[7].ShouldBeLessThanOrEqualTo(76, string.Join(",", calls));
    }

    private sealed class CountingContext : Layout.IMeasureContext<float>
    {
        public int TextCalls;

        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize)
        {
            TextCalls++;
            return new(text.Length * fontSize, fontSize);
        }

        public float ToSurface(float designUnits) => designUnits;
    }

    private sealed class UnitContext : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * fontSize, fontSize);

        public float ToSurface(float designUnits) => designUnits;
    }
}
