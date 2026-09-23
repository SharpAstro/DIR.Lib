using System.Collections.Immutable;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Builder.ButtonGroup{T}"/>: a segmented control declared once, so the three things a
/// hand-written group gets wrong are the builder's to get right. Found by one that went wrong: a
/// tie-breaker whose two halves were given the same fill, so it said nothing about which side won.
/// </summary>
public class LayoutButtonGroupTests
{
    private enum Side { Mount, Profile, Neither }

    private static readonly RGBAColor32 Selected = new(0x40, 0x70, 0xa0, 0xff);
    private static readonly RGBAColor32 Unselected = new(0x30, 0x30, 0x38, 0xff);
    private static readonly RGBAColor32 Hover = new(0x50, 0x50, 0x58, 0xff);
    private static readonly RGBAColor32 Bright = new(0xff, 0xff, 0xff, 0xff);
    private static readonly RGBAColor32 Dim = new(0x90, 0x90, 0x90, 0xff);

    private static readonly Layout.ButtonGroupStyle Style = new(Selected, Unselected, Bright, Dim, Hover);

    private static Layout.ButtonGroupOption<Side>[] Options(string? disabledReason = null) =>
    [
        new(Side.Mount, "Mount"),
        new(Side.Profile, "Profile"),
        new(Side.Neither, "Neither") { DisabledReason = disabledReason },
    ];

    private static (DeclarationWidget Widget, ImmutableArray<ClickableRegion> Regions) Paint(Layout.Node group)
    {
        var widget = new DeclarationWidget(new DeclarationStubRenderer(300, 30));
        widget.Render(group.RowH(30f), new RectF32(0, 0, 300, 30));
        return (widget, [.. widget.GetRegisteredRegions().OrderBy(r => r.X)]);
    }

    private static ImmutableArray<Layout.Node> Segments(Layout.Node group)
        => ((Layout.Node.Stack)group).Children;

    [Fact]
    public void APressOnAnotherSegmentSelectsItsValue()
    {
        Side? chosen = null;
        var (_, regions) = Paint(Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, v => chosen = v, Style));

        regions.Length.ShouldBe(3, "every segment declares its hit");
        regions[1].OnClick.ShouldNotBeNull()(default);

        chosen.ShouldBe(Side.Profile);
    }

    // The chosen segment still has a HIT, so a press on it is swallowed by the control instead of falling
    // through to the row or card behind it -- which would act on a press aimed at the control.
    [Fact]
    public void ThePressOnTheChosenSegmentIsSwallowedAndDoesNothing()
    {
        var calls = 0;
        var (_, regions) = Paint(Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, _ => calls++, Style));

        regions[0].Result.ShouldBeOfType<HitResult.ButtonHit>().Action.ShouldBe("Mount");
        regions[0].OnClick.ShouldBeNull("choosing what is already chosen has nothing to do");
        calls.ShouldBe(0);
    }

    [Fact]
    public void ADisabledSegmentSwallowsItsPressAndSaysWhy()
    {
        var calls = 0;
        var (_, regions) = Paint(Layout.Builder.ButtonGroup<Side>(
            Options(disabledReason: "Not while a session runs"), Side.Mount, _ => calls++, Style));

        var disabled = regions[2];
        disabled.IsDisabled.ShouldBeTrue();
        disabled.OnClick.ShouldBeNull();
        disabled.Cursor.ShouldBe(CursorKind.NotAllowed);
        disabled.Tooltip.ShouldBe("Not while a session runs");
    }

    // The regression this exists for: the chosen segment must LOOK chosen, and only a segment a press
    // would act on may light under the pointer.
    [Fact]
    public void TheChosenSegmentIsFilledDifferentlyAndOnlyActionableSegmentsLight()
    {
        var segments = Segments(Layout.Builder.ButtonGroup<Side>(
            Options(disabledReason: "no"), Side.Profile, _ => { }, Style));

        segments[1].Background.ShouldBe(Selected);
        segments[0].Background.ShouldBe(Unselected);
        segments[0].Background.ShouldNotBe(segments[1].Background);

        segments[0].HoverBackground.ShouldBe(Hover, "an unchosen, enabled segment is actionable");
        segments[1].HoverBackground.ShouldBeNull("a press on the chosen one changes nothing");
        segments[2].HoverBackground.ShouldBeNull("a disabled one cannot be pressed");
    }

    [Fact]
    public void AnOptionsOwnFillWinsOverTheStyles()
    {
        var warning = new RGBAColor32(0xa0, 0x30, 0x30, 0xff);
        Layout.ButtonGroupOption<Side>[] options =
        [
            new(Side.Mount, "On"),
            new(Side.Profile, "Off") { Fill = warning },
        ];

        var segments = Segments(Layout.Builder.ButtonGroup<Side>(options, Side.Mount, _ => { }, Style));

        segments[1].Background.ShouldBe(warning);
        segments[1].HoverBackground.ShouldBe(Hover, "with no hover of its own, it lights like the rest");
    }

    // A warning fill must stay a warning under the pointer, which is when it matters.
    [Fact]
    public void AnOptionsOwnHoverWinsOverTheStyles()
    {
        var warning = new RGBAColor32(0xa0, 0x30, 0x30, 0xff);
        var warningLit = new RGBAColor32(0xc0, 0x40, 0x40, 0xff);
        Layout.ButtonGroupOption<Side>[] options =
        [
            new(Side.Mount, "On"),
            new(Side.Profile, "Off") { Fill = warning, HoverFill = warningLit },
        ];

        var segments = Segments(Layout.Builder.ButtonGroup<Side>(options, Side.Mount, _ => { }, Style));

        segments[1].HoverBackground.ShouldBe(warningLit);
    }

    // An inset pill keeps the whole cell pressable: the hit is on the outer cell, the fill on the band.
    [Fact]
    public void AnInsetGroupStillTakesThePressOverTheWholeHeight()
    {
        var (_, regions) = Paint(Layout.Builder.ButtonGroup<Side>(
            Options(), Side.Mount, _ => { }, Style with { InsetFraction = 0.6f }));

        regions[1].Height.ShouldBe(30f, 0.5f, "the press covers the full row, not only the pill");
    }

    // A group with nowhere to send a choice is a DISPLAY: nothing registers, so a press reaches the row
    // behind it. Distinct from disabling every segment, which would swallow that press.
    [Fact]
    public void AGroupWithNoSelectHandlerIsADisplayThatTakesNoPress()
    {
        var (_, regions) = Paint(Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, null, Style));

        regions.ShouldBeEmpty();
    }

    [Fact]
    public void ADisplayGroupStillShowsWhichSegmentIsChosenButLightsNone()
    {
        var segments = Segments(Layout.Builder.ButtonGroup<Side>(Options(), Side.Profile, null, Style));

        segments[1].Background.ShouldBe(Selected);
        segments[0].Background.ShouldBe(Unselected);
        segments.ShouldAllBe(s => s.HoverBackground == null, "nothing here would act on a press");
    }

    [Fact]
    public void AnIconSegmentIsNamedAfterItsValueWhenItHasNoLabel()
    {
        Layout.ButtonGroupOption<Side>[] options =
        [
            new(Side.Mount, Icon: Layout.IconKind.Check),
            new(Side.Profile, Icon: Layout.IconKind.Grid),
        ];

        var (_, regions) = Paint(Layout.Builder.ButtonGroup<Side>(options, Side.Mount, _ => { }, Style));

        regions.Select(r => ((HitResult.ButtonHit)r.Result).Action).ShouldBe(["Mount", "Profile"]);
    }
}
