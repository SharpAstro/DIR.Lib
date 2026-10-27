using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A padded node measures its children in the room inside its padding, as arrange lays them out. They were measured
/// in the whole of the available width, so wrapped content inside a padded box measured with twice the padding more
/// width than it was given, came out a line short, and ran out of its box: pilot's flight page cut off the end of
/// its last leg's report.
/// </summary>
public class LayoutPaddedMeasureTests
{
    // Eighteen 10-wide items: two lines of ten in a width of 100, three lines of eight in the 80 inside a padding of 10.
    private static Layout.Node Padded() =>
        Layout.Builder.VStack(
                Layout.Builder.WrapH([.. Enumerable.Range(0, 18).Select(_ => Layout.Builder.Spacer().WFixed(10f).HFixed(10f))]).WStar())
            .Pad(10f, 5f)
            .WStar()
            .HAuto();

    [Fact]
    public void WrappedContentIsMeasuredInTheWidthInsideThePadding()
    {
        var size = Layout.Engine.Measure(Padded(), new Layout.Size<float>(100f, 1000f), new UnitContext());

        // Three lines of 10, and the padding above and below.
        size.Height.ShouldBe(30f + 5f + 5f, 0.01f);
    }

    [Fact]
    public void TheMeasuredHeightHoldsEveryArrangedChild()
    {
        var box = Padded();
        var size = Layout.Engine.Measure(box, new Layout.Size<float>(100f, 1000f), new UnitContext());
        var arranged = Layout.Engine.Arrange(box, new Rect<float>(0, 0, 100f, 1000f), new UnitContext());

        arranged.Where(a => a.Depth > 0).Max(a => a.Bounds.Y + a.Bounds.Height).ShouldBeLessThanOrEqualTo(size.Height - 5f + 0.01f);
    }

    private sealed class UnitContext : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * fontSize, fontSize);

        public float ToSurface(float designUnits) => designUnits;
    }
}
