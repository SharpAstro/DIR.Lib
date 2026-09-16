using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Node.Wrap.FirstLineReserve"/>: a run that flows BENEATH a floated corner item and
/// uses the whole extent once it has.
/// <para>
/// The distinction being pinned is against <see cref="Layout.Node.Dock"/>, which is the obvious way to
/// reserve a corner and the wrong one: a dock takes its strip out of every line, so the wrapped rows
/// narrow too and children that used to fit start being dropped. That difference is invisible on a
/// one-line run, so every test here is written at a width that actually wraps.
/// </para>
/// </summary>
public class LayoutWrapFirstLineReserveTests
{
    private static readonly RGBAColor32 Ink = new(0xf0, 0xf0, 0xf0, 0xff);

    private static (DeclarationWidget Widget, DeclarationStubRenderer Renderer) Fixture(uint w = 200, uint h = 200)
    {
        var renderer = new DeclarationStubRenderer(w, h);
        return (new DeclarationWidget(renderer), renderer);
    }

    /// <summary>Eight fixed 40-unit boxes, each with its own hit so its arranged rect is readable.</summary>
    private static Layout.Node Run(int count = 8, float each = 40f)
        => Layout.Builder.WrapH(
            [.. Enumerable.Range(0, count).Select(i =>
                Layout.Builder.Box(each, 10f, Ink)
                    .Clickable(new HitResult.ListItemHit("run", i), _ => { }))]);

    private static (int Index, RectF32 Rect)[] Placed(DeclarationWidget widget)
        => [.. widget.GetRegisteredRegions()
            .Where(r => r.Result is HitResult.ListItemHit { ListId: "run" })
            .Select(r => (((HitResult.ListItemHit)r.Result!).Index, new RectF32(r.X, r.Y, r.Width, r.Height)))
            .OrderBy(t => t.Index)];

    [Fact]
    public void WithNoReserveEveryLineGetsTheWholeExtent()
    {
        var (widget, _) = Fixture();

        widget.Render(Run(), new RectF32(0, 0, 200, 200));

        // 200 / 40 = 5 per line, so the first line holds five.
        var placed = Placed(widget);
        placed.Count(p => p.Rect.Y == placed[0].Rect.Y).ShouldBe(5);
    }

    [Fact]
    public void TheReserveShortensTheFirstLineOnly()
    {
        var (widget, _) = Fixture();

        // 80 units reserved: the first line now holds three, the second still holds five.
        widget.Render(Run().WithFirstLineReserve(80f), new RectF32(0, 0, 200, 200));

        var placed = Placed(widget);
        var firstY = placed[0].Rect.Y;
        var firstLine = placed.Where(p => p.Rect.Y == firstY).ToArray();
        var secondLine = placed.Where(p => p.Rect.Y > firstY).ToArray();

        firstLine.Length.ShouldBe(3, "the first line stops short of the reserve");
        secondLine.Length.ShouldBe(5, "a wrapped line runs the FULL extent -- this is what a Dock cannot do");
    }

    /// <summary>
    /// The property the toolbar depends on, stated directly: reserving a corner must not cost the run
    /// any children. A dock would take its strip off every line and start dropping the tail.
    /// </summary>
    [Fact]
    public void ReservingACornerDropsNoChildren()
    {
        var (bare, _) = Fixture();
        bare.Render(Run(), new RectF32(0, 0, 200, 200));
        var withoutReserve = Placed(bare).Length;

        var (widget, _) = Fixture();
        widget.Render(Run().WithFirstLineReserve(80f), new RectF32(0, 0, 200, 200));

        Placed(widget).Length.ShouldBe(withoutReserve, "every child still placed, just flowed differently");
    }

    [Fact]
    public void TheFirstLineStartsAtTheContainerEdgeRatherThanBeingIndented()
    {
        var (widget, _) = Fixture();

        widget.Render(Run().WithFirstLineReserve(80f), new RectF32(0, 0, 200, 200));

        // The reserve is at the END of the line, not the start: the corner item it makes room for is
        // pinned to the far edge, so the run itself still begins flush left.
        Placed(widget)[0].Rect.X.ShouldBe(0f, 0.01f);
    }

    /// <summary>
    /// Measure has to break lines the same way arrange does. A measure that ignored the reserve would
    /// report a one-line box for a run that paints on two, and the caller would size a band too short
    /// for what goes in it -- which is the hand-summed-box bug the tree exists to remove.
    /// </summary>
    [Fact]
    public void TheMeasuredBoxAccountsForTheExtraLineTheReserveForces()
    {
        var (widget, _) = Fixture();

        var without = widget.Measure(Run(5), new Layout.Size<float>(200f, 200f));
        var with = widget.Measure(Run(5).WithFirstLineReserve(80f), new Layout.Size<float>(200f, 200f));

        with.Height.ShouldBeGreaterThan(without.Height,
            "five boxes fit one line unreserved and need two when the first line stops short");
    }

    [Fact]
    public void AReserveWiderThanTheContainerDoesNotPushEverythingOffTheFirstLine()
    {
        var (widget, _) = Fixture();

        // Clamped at zero rather than going negative. The first line can still seat one child -- a line
        // never breaks before its first item, which is the same rule an over-wide child already follows.
        widget.Render(Run().WithFirstLineReserve(500f), new RectF32(0, 0, 200, 200));

        var placed = Placed(widget);
        placed.Length.ShouldBe(8, "no child is lost");
        placed.Count(p => p.Rect.Y == placed[0].Rect.Y).ShouldBe(1);
    }

    [Fact]
    public void TheModifierIsANoOpOnANodeThatIsNotAWrap()
    {
        var box = Layout.Builder.Box(10f, 10f);

        box.WithFirstLineReserve(40f).ShouldBe(box);
    }
}
