using System.Collections.Immutable;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Builder.Checkbox"/>: the tick is a drawn mark rather than "[x]" in the label, the whole
/// row toggles, and it follows <see cref="Layout.Builder.ButtonGroup{T}"/>'s rules for hover, disabled and
/// display.
/// </summary>
public class LayoutCheckboxTests
{
    private static readonly RGBAColor32 Well = new(0x20, 0x20, 0x28, 0xff);
    private static readonly RGBAColor32 Tick = new(0x60, 0xd0, 0x60, 0xff);
    private static readonly RGBAColor32 Label = new(0xc0, 0xc0, 0xc0, 0xff);
    private static readonly RGBAColor32 Hover = new(0x50, 0x50, 0x58, 0xff);

    private static readonly Layout.CheckboxStyle Style = new(Well, Tick, Label, Hover);

    private static ImmutableArray<ClickableRegion> Regions(Layout.Node row)
    {
        var widget = new DeclarationWidget(new DeclarationStubRenderer(300, 30));
        widget.Render(row.RowH(30f), new RectF32(0, 0, 300, 30));
        return [.. widget.GetRegisteredRegions()];
    }

    private static Layout.Node Box(Layout.Node row) => ((Layout.Node.Stack)row).Children[0];

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void APressAnywhereOnTheRowHandsOverTheOtherState(bool isChecked, bool expected)
    {
        bool? toggled = null;
        var regions = Regions(Layout.Builder.Checkbox("Show ROI", isChecked, v => toggled = v, Style));

        var row = regions.ShouldHaveSingleItem();
        row.Width.ShouldBe(300f, "the label is part of the target, not only the box");
        row.OnClick.ShouldNotBeNull()(default);

        toggled.ShouldBe(expected);
    }

    // The mark is a declared icon, never a character in the label.
    [Fact]
    public void ACheckedBoxCarriesADrawnTickAndTheLabelStaysPlain()
    {
        var row = Layout.Builder.Checkbox("Show ROI", true, _ => { }, Style);

        var box = Box(row).ShouldBeOfType<Layout.Node.Leaf>();
        box.Content.ShouldBeOfType<Layout.Content.Icon>().Kind.ShouldBe(Layout.IconKind.Check);
        box.Background.ShouldBe(Well);

        var label = ((Layout.Node.Stack)row).Children[1].ShouldBeOfType<Layout.Node.Leaf>();
        label.Content.ShouldBeOfType<Layout.Content.Text>().Value.ShouldBe("Show ROI");
    }

    [Fact]
    public void AnUncheckedBoxIsAnEmptyWell()
    {
        var box = Box(Layout.Builder.Checkbox("Show ROI", false, _ => { }, Style));

        box.ShouldBeOfType<Layout.Node.Leaf>().Content.ShouldNotBeOfType<Layout.Content.Icon>();
        box.Background.ShouldBe(Well);
    }

    [Fact]
    public void ACheckedRowTakesItsOwnFillAndLabelColour()
    {
        var on = new RGBAColor32(0x30, 0x60, 0x30, 0xff);
        var bright = new RGBAColor32(0xff, 0xff, 0xff, 0xff);
        var style = Style with { CheckedRowFill = on, CheckedLabelColor = bright };

        var row = Layout.Builder.Checkbox("Mount jog", true, _ => { }, style);

        row.Background.ShouldBe(on);
        ((Layout.Node.Stack)row).Children[1].ShouldBeOfType<Layout.Node.Leaf>()
            .Content.ShouldBeOfType<Layout.Content.Text>().Color.ShouldBe(bright);
        Layout.Builder.Checkbox("Mount jog", false, _ => { }, style).Background.ShouldBeNull();
    }

    [Fact]
    public void ADisabledRowSwallowsThePressSaysWhyAndDoesNotLight()
    {
        var row = Layout.Builder.Checkbox("Mount jog", false, _ => { }, Style, disabledReason: "Not while capturing");

        row.HoverBackground.ShouldBeNull();
        var region = Regions(row).ShouldHaveSingleItem();
        region.IsDisabled.ShouldBeTrue();
        region.OnClick.ShouldBeNull();
        region.Tooltip.ShouldBe("Not while capturing");
    }

    [Fact]
    public void ARowWithNoToggleHandlerIsADisplay()
    {
        var row = Layout.Builder.Checkbox("Aligned", true, null, Style);

        row.HoverBackground.ShouldBeNull();
        Regions(row).ShouldBeEmpty();
    }

    [Fact]
    public void TheHitIsNamedAfterTheLabelUnlessOneIsGiven()
    {
        Regions(Layout.Builder.Checkbox("Show ROI", false, _ => { }, Style)).Single().Result
            .ShouldBe(new HitResult.ButtonHit("Show ROI"));
        Regions(Layout.Builder.Checkbox("Show ROI", false, _ => { }, Style, hit: new HitResult.ButtonHit("RoiOverlayToggle")))
            .Single().Result.ShouldBe(new HitResult.ButtonHit("RoiOverlayToggle"));
    }
}
