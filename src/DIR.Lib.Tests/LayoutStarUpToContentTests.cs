using System;
using System.Collections.Immutable;
using System.Numerics;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Sizing.StarUpToContent"/>: a Star capped at its own measured content, served before the
/// plain Stars of its Stack. What it closes is the strip whose label was trimmed while the row still had
/// room, because a plain Star's share is tied to its weight and never to its text.
///
/// <para>Text measures 7 per character and 16 tall on the stub, so "abcdefghij" is 70 wide and every
/// expectation is arithmetic.</para>
/// </summary>
public class LayoutStarUpToContentTests
{
    private sealed class PixelCtx : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 7f, 16f);
        public float ToSurface(float designUnits) => designUnits;
    }

    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
    }

    private static Rect<T> RectOf<T>(ImmutableArray<Layout.ArrangedNode<T>> arranged, Layout.Node node)
        where T : INumber<T>
    {
        foreach (var a in arranged)
        {
            if (ReferenceEquals(a.Node, node))
            {
                return a.Bounds;
            }
        }

        throw new InvalidOperationException("Node was not arranged.");
    }

    private static Layout.Node Label(string text) => Layout.Builder.Text(text, 14f);

    private static Layout.Node FixedW(float w) =>
        new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Width = Layout.Sizing.Fixed(w) };

    private static Layout.Node PlainStar(float weight = 1f, float min = 0f) =>
        new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Width = Layout.Sizing.Star(weight, min) };

    private static Layout.Node PlainStarH() =>
        new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Height = Layout.Sizing.Star() };

    [Fact]
    public void TheFactory_IsAStarWithTheFlag_AndTheClampHelperKeepsIt()
    {
        var s = Layout.Sizing.StarUpToContent(2f, 30f);
        s.IsStar.ShouldBeTrue();
        s.UpToContent.ShouldBeTrue();
        s.Value.ShouldBe(2f);
        s.Min.ShouldBe(30f);
        Layout.Sizing.Star().UpToContent.ShouldBeFalse();
        Label("x").WStarUpToContent().WClamp(10f).Width.UpToContent.ShouldBeTrue();
    }

    [Fact]
    public void WithRoom_TheContentCappedStarGetsExactlyItsContent_AndThePlainStarTheSurplus()
    {
        var label = Label("abcdefghij").WStarUpToContent();
        var hint = PlainStar();
        var arranged = Layout.Engine.Arrange(Layout.Builder.HStack(label, hint), new Rect<float>(0, 0, 300, 20), new PixelCtx());

        RectOf(arranged, label).Width.ShouldBe(70f, "natural width while there is room, not half the row");
        var h = RectOf(arranged, hint);
        h.X.ShouldBe(70f);
        h.Width.ShouldBe(230f);
    }

    [Fact]
    public void WithRoom_AHeavierPlainStarStillCannotTakeTheLabelsContent()
    {
        // Weight only decides how a SHORTFALL is shared among content-capped Stars; a plain Star is
        // served what they leave, however heavy it is.
        var label = Label("abcdefghij").WStarUpToContent();
        var hint = PlainStar(weight: 10f);
        var arranged = Layout.Engine.Arrange(Layout.Builder.HStack(label, hint), new Rect<float>(0, 0, 100, 20), new PixelCtx());

        RectOf(arranged, label).Width.ShouldBe(70f);
        RectOf(arranged, hint).Width.ShouldBe(30f);
    }

    [Fact]
    public void AShortRow_ShrinksItBelowItsContent_AndThePlainStarGivesWayFirst()
    {
        // 150 - 100 fixed leaves 50 for a 70 label: the label takes all 50, the hint none.
        var label = Label("abcdefghij").WStarUpToContent(min: 20f);
        var hint = PlainStar();
        var arranged = Layout.Engine.Arrange(Layout.Builder.HStack(FixedW(100), label, hint),
            new Rect<float>(0, 0, 150, 20), new PixelCtx());

        RectOf(arranged, label).Width.ShouldBe(50f);
        RectOf(arranged, hint).Width.ShouldBe(0f);
    }

    [Fact]
    public void AShorterRow_StopsItAtItsMin_AndTheOverflowIsVisible()
    {
        // 110 - 100 leaves 10, under the label's floor of 20: it holds 20, as a min-clamped Star does.
        var label = Label("abcdefghij").WStarUpToContent(min: 20f);
        var hint = PlainStar();
        var arranged = Layout.Engine.Arrange(Layout.Builder.HStack(FixedW(100), label, hint),
            new Rect<float>(0, 0, 110, 20), new PixelCtx());

        RectOf(arranged, label).Width.ShouldBe(20f);
        RectOf(arranged, hint).Width.ShouldBe(0f);
    }

    [Fact]
    public void APlainStarsMin_IsReservedBeforeTheContentCappedStarsAreServed()
    {
        var label = Label("abcdefghij").WStarUpToContent();
        var hint = PlainStar(min: 15f);
        var arranged = Layout.Engine.Arrange(Layout.Builder.HStack(label, hint), new Rect<float>(0, 0, 60, 20), new PixelCtx());

        RectOf(arranged, label).Width.ShouldBe(45f);
        RectOf(arranged, hint).Width.ShouldBe(15f);
    }

    [Fact]
    public void TwoContentCappedStars_NoPlainStar_TheRestOfTheRowStaysUnused()
    {
        // The rule a Max-capped Star already follows: a capped surplus with no Star to take it is left over.
        var a = Label("abcdefghij").WStarUpToContent();
        var b = Label("abc").WStarUpToContent();
        var arranged = Layout.Engine.Arrange(Layout.Builder.HStack(a, b), new Rect<float>(0, 0, 300, 20), new PixelCtx());

        RectOf(arranged, a).Width.ShouldBe(70f);
        var rb = RectOf(arranged, b);
        rb.X.ShouldBe(70f);
        rb.Width.ShouldBe(21f);
    }

    [Fact]
    public void TwoContentCappedStars_ShortRow_TheShortOneKeepsItsContent_TheLongOneTakesTheRest()
    {
        // 61 by equal weight is 30.5 each; "abc" is capped at 21 and hands 9.5 back to the long label.
        var a = Label("abcdefghij").WStarUpToContent();
        var b = Label("abc").WStarUpToContent();
        var arranged = Layout.Engine.Arrange(Layout.Builder.HStack(a, b), new Rect<float>(0, 0, 61, 20), new PixelCtx());

        RectOf(arranged, a).Width.ShouldBe(40f);
        RectOf(arranged, b).Width.ShouldBe(21f);
    }

    [Fact]
    public void TheWidthSample_IsTheContentItIsCappedAt()
    {
        // A clock label sized to its widest reading, so the strip does not jitter as the digits change.
        var clock = Layout.Builder.Text("1:05", 14f, widthSample: "00:00:00").WStarUpToContent();
        var arranged = Layout.Engine.Arrange(Layout.Builder.HStack(clock, PlainStar()), new Rect<float>(0, 0, 300, 20), new PixelCtx());

        RectOf(arranged, clock).Width.ShouldBe(56f);
    }

    [Fact]
    public void VerticalAxis_TheContentCappedStarGetsItsContentHeight_AndThePlainStarTheRest()
    {
        var title = Label("abc").HStarUpToContent();
        var body = PlainStarH();
        var arranged = Layout.Engine.Arrange(Layout.Builder.VStack(title, body), new Rect<float>(0, 0, 50, 100), new PixelCtx());

        RectOf(arranged, title).Height.ShouldBe(16f);
        var rb = RectOf(arranged, body);
        rb.Y.ShouldBe(16f);
        rb.Height.ShouldBe(84f);
    }

    [Fact]
    public void VerticalAxis_ShortColumn_ShrinksTowardMin()
    {
        // 100 - 90 fixed leaves 10 for a 16-tall title with a floor of 4.
        var top = new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Height = Layout.Sizing.Fixed(90) };
        var title = Label("abc").HStarUpToContent(min: 4f);
        var body = PlainStarH();
        var arranged = Layout.Engine.Arrange(Layout.Builder.VStack(top, title, body), new Rect<float>(0, 0, 50, 100), new PixelCtx());

        RectOf(arranged, title).Height.ShouldBe(10f);
        RectOf(arranged, body).Height.ShouldBe(0f);
    }

    [Fact]
    public void AcrossAStack_ItTakesItsContentRatherThanStretching()
    {
        var label = Label("abc").WStarUpToContent();
        var arranged = Layout.Engine.Arrange(Layout.Builder.VStack(label), new Rect<float>(0, 0, 200, 50), new PixelCtx());

        RectOf(arranged, label).Width.ShouldBe(21f);
    }

    [Fact]
    public void ItCountsInItsStacksNaturalSize()
    {
        // A plain Star contributes nothing to an Auto Stack's size; this one has content to contribute,
        // or an Auto strip holding it would measure it away to nothing.
        var row = Layout.Builder.HStack(Label("abcdefghij").WStarUpToContent(), FixedW(10));
        Layout.Engine.Measure(row, new Layout.Size<float>(1000, 100), new PixelCtx()).Width.ShouldBe(80f);
    }

    [Fact]
    public void CellCoordinates_RoomAndShortRowAreExact()
    {
        var label = Label("abcdefghij").WStarUpToContent(min: 3f);
        var hint = PlainStar();
        var roomy = Layout.Engine.Arrange(Layout.Builder.HStack(label, hint), new Rect<int>(0, 0, 40, 1), new CellCtx());
        RectOf(roomy, label).Width.ShouldBe(10);
        RectOf(roomy, hint).Width.ShouldBe(30);

        var shortRow = Layout.Engine.Arrange(Layout.Builder.HStack(label, hint), new Rect<int>(0, 0, 7, 1), new CellCtx());
        RectOf(shortRow, label).Width.ShouldBe(7);
        RectOf(shortRow, hint).Width.ShouldBe(0);
    }
}
