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

    private sealed class UnitContext : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * fontSize, fontSize);

        public float ToSurface(float designUnits) => designUnits;
    }
}
