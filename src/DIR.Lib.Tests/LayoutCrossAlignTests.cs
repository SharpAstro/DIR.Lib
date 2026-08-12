using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Pins <see cref="Layout.CrossAlign"/>: where a stack puts a child ACROSS its axis.
/// <para>
/// Added because its absence is a trap that looks like a styling bug. A Fixed-height control in a taller
/// row hugged the row's top, so a header of buttons sat visibly high, and the workaround at every call site
/// was to pad the container or wrap each child in a spacer sandwich -- both re-deriving a position from the
/// parent's inner size, which is precisely the arithmetic a layout pass exists to remove.
/// </para>
/// </summary>
public class LayoutCrossAlignTests
{
    private static Layout.Node Row(Layout.CrossAlign align) =>
        Layout.Builder.HStack(
                Layout.Builder.Spacer().WFixed(10f).HFixed(20f),
                Layout.Builder.Spacer().WFixed(10f).HFixed(10f))
            .Align(align)
            .RowH(40f);

    private static Rect<float>[] Arrange(Layout.Node root) =>
        [.. Layout.Engine.Arrange(root, new Rect<float>(0, 0, 100f, 40f), new UnitContext())
            .Where(a => a.Depth > 0)
            .Select(a => a.Bounds)];

    [Fact]
    public void StartKeepsTheLongStandingBehaviour()
    {
        var kids = Arrange(Row(Layout.CrossAlign.Start));

        kids.Length.ShouldBe(2);
        kids[0].Y.ShouldBe(0f, 0.01f);
        kids[1].Y.ShouldBe(0f, 0.01f);
    }

    [Fact]
    public void CenterSplitsTheSlackEvenly_PerChild()
    {
        var kids = Arrange(Row(Layout.CrossAlign.Center));

        // Each child is centred on ITS OWN slack, so two differently-sized children share a centre line
        // rather than a top edge: (40-20)/2 and (40-10)/2.
        kids[0].Y.ShouldBe(10f, 0.01f);
        kids[1].Y.ShouldBe(15f, 0.01f);

        // The property that matters to a caller: one centre line.
        (kids[0].Y + kids[0].Height / 2f).ShouldBe(kids[1].Y + kids[1].Height / 2f, 0.01f);
    }

    [Fact]
    public void EndPushesChildrenToTheFarEdge()
    {
        var kids = Arrange(Row(Layout.CrossAlign.End));

        (kids[0].Y + kids[0].Height).ShouldBe(40f, 0.01f);
        (kids[1].Y + kids[1].Height).ShouldBe(40f, 0.01f);
    }

    [Fact]
    public void AStarChildIsUnaffected_BecauseItAlreadyFillsTheAxis()
    {
        var root = Layout.Builder.HStack(
                Layout.Builder.Spacer().WFixed(10f).HStar(),
                Layout.Builder.Spacer().WFixed(10f).HFixed(10f))
            .Align(Layout.CrossAlign.Center)
            .RowH(40f);

        var kids = Arrange(root);

        kids[0].Y.ShouldBe(0f, 0.01f);
        kids[0].Height.ShouldBe(40f, 0.01f);
        kids[1].Y.ShouldBe(15f, 0.01f);
    }

    [Fact]
    public void AVerticalStackAlignsAcrossTheOtherAxis()
    {
        var root = Layout.Builder.VStack(
                Layout.Builder.Spacer().WFixed(20f).HFixed(10f),
                Layout.Builder.Spacer().WFixed(10f).HFixed(10f))
            .Align(Layout.CrossAlign.Center)
            .WFixed(100f)
            .HFixed(40f);

        var kids = Arrange(root);

        // A column centres horizontally: the cross axis is whichever one the stack does not run along.
        kids[0].X.ShouldBe(40f, 0.01f);
        kids[1].X.ShouldBe(45f, 0.01f);
    }

    /// <summary>Design units map 1:1 to surface units, so an assertion reads as the number it was authored as.</summary>
    private sealed class UnitContext : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) =>
            new(text.Length * fontSize, fontSize);

        public float ToSurface(float designUnits) => designUnits;
    }
}
