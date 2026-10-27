using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Builder.NavigationStrip"/>: two cells sharing the strip's width, each a pointer-cursor button with a
/// tooltip naming its key. A cell with nowhere to go keeps its region and its tooltip but loses its handler, so the press is
/// swallowed rather than reaching what the strip sits on.
/// </summary>
public class LayoutNavigationStripTests
{
    private static readonly Layout.NavigationStripColors Colors = new(
        Fill: new RGBAColor32(0x16, 0x17, 0x1e, 0xff),
        HoverFill: new RGBAColor32(0x22, 0x24, 0x2e, 0xff),
        Ink: new RGBAColor32(0xe6, 0xe7, 0xee, 0xff),
        DisabledInk: new RGBAColor32(0x4a, 0x4d, 0x5a, 0xff));

    private static (DeclarationWidget Widget, ClickableRegion Back, ClickableRegion Forward) Paint(
        bool canGoBack, bool canGoForward, Action? onBack = null, Action? onForward = null)
    {
        var widget = new DeclarationWidget(new DeclarationStubRenderer(200, 40));
        widget.Render(Layout.Builder.NavigationStrip(canGoBack, canGoForward, onBack, onForward, Colors), new RectF32(0, 0, 200, 40));
        var regions = widget.GetRegisteredRegions().Where(r => r.Result is HitResult.ButtonHit).ToList();
        regions.Count.ShouldBe(2);
        return (widget,
            regions.Single(r => ((HitResult.ButtonHit)r.Result).Action == Layout.Builder.NavigationBackAction),
            regions.Single(r => ((HitResult.ButtonHit)r.Result).Action == Layout.Builder.NavigationForwardAction));
    }

    [Fact]
    public void TheTwoCellsShareTheWidthAndFillTheHeight()
    {
        var (_, back, forward) = Paint(true, true, () => { }, () => { });

        back.X.ShouldBe(0f);
        back.Width.ShouldBe(100f);
        forward.X.ShouldBe(100f);
        forward.Width.ShouldBe(100f);
        back.Y.ShouldBe(0f);
        back.Height.ShouldBe(40f);
        forward.Y.ShouldBe(back.Y);
        forward.Height.ShouldBe(back.Height);
    }

    [Fact]
    public void EachCellIsAPointerButtonWithItsKeyInItsTooltip()
    {
        var (_, back, forward) = Paint(true, true, () => { }, () => { });

        back.Tooltip.ShouldBe("Back (Alt+Left)");
        forward.Tooltip.ShouldBe("Forward (Alt+Right)");
        back.Cursor.ShouldBe(CursorKind.Pointer);
        forward.Cursor.ShouldBe(CursorKind.Pointer);
        back.IsDisabled.ShouldBeFalse();
        forward.IsDisabled.ShouldBeFalse();
        ((HitResult.ButtonHit)back.Result).Action.ShouldBe("nav:back");
        ((HitResult.ButtonHit)forward.Result).Action.ShouldBe("nav:forward");
    }

    [Fact]
    public void APressOnAnEnabledCellRunsItsOwnActionOnly()
    {
        var back = 0;
        var forward = 0;
        var (widget, _, _) = Paint(true, true, () => back++, () => forward++);

        Routing.Press(widget, 50f, 20f).ShouldBeTrue();
        (back, forward).ShouldBe((1, 0));

        Routing.Press(widget, 150f, 20f).ShouldBeTrue();
        (back, forward).ShouldBe((1, 1));
    }

    [Fact]
    public void ACellWithNowhereToGoIsDisabledKeepsItsTooltipAndSwallowsThePress()
    {
        var back = 0;
        var forward = 0;
        var (widget, backRegion, forwardRegion) = Paint(false, true, () => back++, () => forward++);

        backRegion.IsDisabled.ShouldBeTrue();
        backRegion.OnClick.ShouldBeNull();
        backRegion.Tooltip.ShouldBe("Back (Alt+Left)");
        backRegion.Cursor.ShouldBe(CursorKind.NotAllowed);
        forwardRegion.IsDisabled.ShouldBeFalse();

        // Still hit, which is what swallowing means: consumed, with nothing run.
        Routing.Press(widget, 50f, 20f).ShouldBeTrue();
        back.ShouldBe(0);
        forward.ShouldBe(0);
    }

    [Fact]
    public void ForwardIsDisabledOnItsOwnToo()
    {
        var (_, back, forward) = Paint(true, false, () => { }, () => { });

        back.IsDisabled.ShouldBeFalse();
        forward.IsDisabled.ShouldBeTrue();
        forward.OnClick.ShouldBeNull();
        forward.Tooltip.ShouldBe("Forward (Alt+Right)");
    }

    [Fact]
    public void BothCellsAreDisabledWithNoHistory()
    {
        var (widget, back, forward) = Paint(false, false, () => throw new InvalidOperationException(), () => throw new InvalidOperationException());

        back.IsDisabled.ShouldBeTrue();
        forward.IsDisabled.ShouldBeTrue();
        Routing.Press(widget, 50f, 20f).ShouldBeTrue();
        Routing.Press(widget, 150f, 20f).ShouldBeTrue();
    }

    [Fact]
    public void OnlyAnEnabledCellDeclaresAHoverFill()
    {
        var enabled = Layout.Builder.NavigationStrip(true, false, () => { }, () => { }, Colors);

        var cells = ((Layout.Node.Stack)enabled).Children;
        cells[0].HoverBackground.ShouldBe(Colors.HoverFill);
        cells[1].HoverBackground.ShouldBeNull();
    }

    [Fact]
    public void ADisabledArrowIsDrawnDimmerThanAnEnabledOne()
    {
        var enabled = Layout.Builder.NavigationStrip(true, false, null, null, Colors);

        var cells = ((Layout.Node.Stack)enabled).Children;
        IconColour(cells[0]).ShouldBe(Colors.Ink);
        IconColour(cells[1]).ShouldBe(Colors.DisabledInk);
    }

    private static RGBAColor32 IconColour(Layout.Node cell)
    {
        // Cell: a column of spacer, row (spacer, icon, spacer), spacer; the icon is the middle of the middle.
        var row = (Layout.Node.Stack)((Layout.Node.Stack)cell).Children[1];
        return ((Layout.Content.Icon)((Layout.Node.Leaf)row.Children[1]).Content).Color;
    }
}
