using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary><see cref="Layout.Node.Wrap.Ellipsis"/> and <see cref="Layout.ArrangedNode{T}.Truncated"/>.</summary>
public class LayoutWrapMaxLinesEllipsisTests(ITestOutputHelper output)
{
    private const string Ellipsis = "…";

    private sealed class PixelCtx : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 10f, 14f);
        public float ToSurface(float designUnits) => designUnits;
    }

    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
    }

    private static Layout.Node Words(params string[] words)
        => Layout.Builder.WrapH([.. words.Select(w => Layout.Builder.Text(w))]);

    private static string TextOf<T>(Layout.ArrangedNode<T> a) where T : System.Numerics.INumber<T>
        => ((Layout.Content.Text)((Layout.Node.Leaf)a.Node).Content).Value;

    private static bool IsText<T>(Layout.ArrangedNode<T> a) where T : System.Numerics.INumber<T>
        => a.Node is Layout.Node.Leaf { Content: Layout.Content.Text };

    [Fact]
    public void PixelCapEndsLastLeafWithEllipsisInsideWidth()
    {
        var wrap = Words("aaaa", "bbbb", "cccc", "dddd").WithMaxLines(1, ellipsis: true);
        var arranged = Layout.Engine.Arrange(wrap, new Rect<float>(0, 0, 85, 100), new PixelCtx());
        var leaves = arranged.Where(IsText).ToArray();
        output.WriteLine(string.Join(",", leaves.Select(TextOf)));

        leaves.Length.ShouldBe(2);
        TextOf(leaves[1]).ShouldBe("bbb" + Ellipsis);
        (leaves[1].Bounds.X + leaves[1].Bounds.Width).ShouldBeLessThanOrEqualTo(85f);
        arranged[0].Truncated.ShouldBeTrue();
    }

    [Fact]
    public void CellCapEndsLastLeafWithEllipsisInsideWidth()
    {
        var wrap = Words("aaaa", "bbbb", "cccc").WithMaxLines(1, ellipsis: true);
        var arranged = Layout.Engine.Arrange(wrap, new Rect<int>(0, 0, 8, 5), new CellCtx());
        var leaves = arranged.Where(IsText).ToArray();

        leaves.Length.ShouldBe(2);
        TextOf(leaves[1]).ShouldBe("bbb" + Ellipsis);
        (leaves[1].Bounds.X + leaves[1].Bounds.Width).ShouldBeLessThanOrEqualTo(8);
        arranged[0].Truncated.ShouldBeTrue();
    }

    [Fact]
    public void FittingWrapIsUntouchedAndNotTruncated()
    {
        var wrap = Words("aaaa", "bbbb").WithMaxLines(2, ellipsis: true);
        var arranged = Layout.Engine.Arrange(wrap, new Rect<float>(0, 0, 200, 100), new PixelCtx());

        arranged[0].Truncated.ShouldBeFalse();
        arranged.Where(IsText).Select(TextOf).ToArray().ShouldBe(["aaaa", "bbbb"]);
    }

    [Fact]
    public void CapWithoutEllipsisKeepsWordsButReportsTruncated()
    {
        var wrap = Words("aaaa", "bbbb", "cccc").WithMaxLines(1);
        var arranged = Layout.Engine.Arrange(wrap, new Rect<float>(0, 0, 85, 100), new PixelCtx());

        arranged[0].Truncated.ShouldBeTrue();
        arranged.Where(IsText).Select(TextOf).ToArray().ShouldBe(["aaaa", "bbbb"]);
    }

    [Fact]
    public void LastKeptChildThatIsNotTextGetsNoEllipsis()
    {
        var wrap = Layout.Builder.WrapH(Layout.Builder.Text("aaaa"), Layout.Builder.Box(40, 10), Layout.Builder.Text("cccc"))
            .WithMaxLines(1, ellipsis: true);
        var arranged = Layout.Engine.Arrange(wrap, new Rect<float>(0, 0, 85, 100), new PixelCtx());

        arranged[0].Truncated.ShouldBeTrue();
        arranged.Where(IsText).Select(TextOf).ToArray().ShouldBe(["aaaa"]);
    }

    [Fact]
    public void FirstLineReserveShortensTheRoomTheEllipsisFits()
    {
        // 85 wide, 40 reserved: the first line is 45. "aaaa" (40) fits and "bbbb" does not.
        var wrap = Words("aaaa", "bbbb").WithFirstLineReserve(40f).WithMaxLines(1, ellipsis: true);
        var arranged = Layout.Engine.Arrange(wrap, new Rect<float>(0, 0, 85, 100), new PixelCtx());
        var leaf = arranged.Single(IsText);

        TextOf(leaf).ShouldBe("aaa" + Ellipsis);
        (leaf.Bounds.X + leaf.Bounds.Width).ShouldBeLessThanOrEqualTo(45f);
        arranged[0].Truncated.ShouldBeTrue();
    }

    [Fact]
    public void BareEllipsisWhenNothingElseFits()
    {
        var wrap = Words("aaaaaaaa", "bbbb").WithMaxLines(1, ellipsis: true);
        var arranged = Layout.Engine.Arrange(wrap, new Rect<float>(0, 0, 5, 100), new PixelCtx());

        TextOf(arranged.Single(IsText)).ShouldBe(Ellipsis);
    }

    [Fact]
    public void MeasureAndArrangeAgreeOnCappedHeight()
    {
        var wrap = Words("aaaa", "bbbb", "cccc", "dddd").WithMaxLines(1, ellipsis: true);
        var measured = Layout.Engine.Measure(wrap, new Layout.Size<float>(85, 1000), new PixelCtx());
        var arranged = Layout.Engine.Arrange(wrap, new Rect<float>(0, 0, 85, measured.Height), new PixelCtx());

        arranged.Where(IsText).Max(a => a.Bounds.Y + a.Bounds.Height).ShouldBe(measured.Height);
    }
}
