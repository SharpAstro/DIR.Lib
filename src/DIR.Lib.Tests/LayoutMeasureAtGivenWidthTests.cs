using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A node is measured at the width it will be arranged in. A fixed-width or max-capped node used to measure its
/// children at the whole available width and only then override its own width, so a wrapping child counted the lines
/// it would take at window width and the box came out short of what arrange filled (DIR.Lib #157).
/// </summary>
public class LayoutMeasureAtGivenWidthTests
{
    // Eighteen 10-wide words: one line of 180 at a wide window, two lines of ten at 100, three of eight at 80.
    private static Layout.Node Paragraph() =>
        Layout.Builder.WrapH([.. Enumerable.Range(0, 18).Select(_ => Layout.Builder.Spacer().WFixed(10f).HFixed(10f))]).WStar();

    private static float Bottom(Layout.Node box, float width)
        => Layout.Engine.Arrange(box, new Rect<float>(0, 0, width, 1000f), new UnitContext())
            .Where(a => a.Depth > 0 && a.Node is Layout.Node.Leaf)
            .Max(a => a.Bounds.Y + a.Bounds.Height);

    [Fact]
    public void AFixedWidthStackMeasuresItsWrappedHeight()
    {
        var box = Layout.Builder.VStack(Paragraph()).WFixed(100f).HAuto();

        var size = Layout.Engine.Measure(box, new Layout.Size<float>(1000f, 1000f), new UnitContext());

        size.Width.ShouldBe(100f);
        size.Height.ShouldBe(20f, 0.01f);
        Bottom(box, 100f).ShouldBeLessThanOrEqualTo(size.Height + 0.01f);
    }

    [Fact]
    public void AMaxCappedStackMeasuresItsWrappedHeight()
    {
        var capped = (Layout.Node)Layout.Builder.VStack(Paragraph()).HAuto() with { Width = Layout.Sizing.Auto with { Max = 100f } };

        var size = Layout.Engine.Measure(capped, new Layout.Size<float>(1000f, 1000f), new UnitContext());

        size.Width.ShouldBeLessThanOrEqualTo(100f);
        size.Height.ShouldBe(20f, 0.01f);
        Bottom(capped, size.Width).ShouldBeLessThanOrEqualTo(size.Height + 0.01f);
    }

    private sealed class UnitContext : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * fontSize, fontSize);

        public float ToSurface(float designUnits) => designUnits;
    }
}
