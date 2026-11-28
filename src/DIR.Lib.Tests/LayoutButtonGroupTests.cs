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

    // ---- EqualToWidest ----

    private sealed class EqFloatCtx : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 8f, 14f);
        public float ToSurface(float designUnits) => designUnits;
        public float ToSurfaceX(float designUnits) => designUnits;
        public float ToSurfaceY(float designUnits) => designUnits;
    }

    private sealed class EqCellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
    }

    private static Layout.ButtonGroupStyle Equal(float padding = 0f) => Style with
    {
        EqualToWidest = true,
        SegmentPadding = padding,
        Gap = 4f,
    };

    private static Layout.Node InRow(Layout.Node group)
        => Layout.Builder.HStack(group, Layout.Builder.Spacer().WStar()).HFixed(30f);

    private static Layout.ArrangedNode<float>[] Segs(Layout.Node group, float width = 400f)
        => [.. Layout.Engine.Arrange(InRow(group), new Rect<float>(0, 0, width, 30f), new EqFloatCtx())
            .Where(n => n.Node.Hit is HitResult.ButtonHit)
            .OrderBy(n => n.Bounds.X)];

    [Fact]
    public void EqualToWidestMeasuresNTimesWidestPlusGaps()
    {
        // "Neither"/"Profile" are 7 chars = 56; 3 x (56 + 2 x 5) + 2 x 4 = 206.
        var group = Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, _ => { }, Equal(5f));
        group.ShouldBeOfType<Layout.Node.Grid>();
        Layout.Engine.Measure(group, new Layout.Size<float>(1000f, 30f), new EqFloatCtx()).Width.ShouldBe(206f);
    }

    [Fact]
    public void EveryEqualSegmentHasOneWidthAndTheyAreAdjacentWithTheGap()
    {
        var segs = Segs(Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, _ => { }, Equal(5f)));
        segs.Length.ShouldBe(3);
        segs.Select(s => s.Bounds.Width).Distinct().ShouldBe([66f]);
        segs[1].Bounds.X.ShouldBe(segs[0].Bounds.X + 66f + 4f);
        segs[2].Bounds.X.ShouldBe(segs[1].Bounds.X + 66f + 4f);
    }

    [Fact]
    public void AWidthSampleWiderThanEveryLabelSetsTheWidthAndTheLabelIsStillDrawn()
    {
        Layout.ButtonGroupOption<Side>[] options =
        [
            new(Side.Mount, "On") { WidthSample = "Stopping now" },
            new(Side.Profile, "Off"),
        ];
        var group = Layout.Builder.ButtonGroup<Side>(options, Side.Mount, _ => { }, Equal());
        Layout.Engine.Measure(group, new Layout.Size<float>(1000f, 30f), new EqFloatCtx()).Width.ShouldBe(2 * 96f + 4f);

        var arranged = Layout.Engine.Arrange(InRow(group), new Rect<float>(0, 0, 400f, 30f), new EqFloatCtx());
        var words = arranged.Select(n => n.Node).OfType<Layout.Node.Leaf>()
            .Select(l => l.Content).OfType<Layout.Content.Text>().Select(t => t.Value).ToArray();
        words.ShouldContain("On");
        words.ShouldNotContain("Stopping now");
    }

    [Theory]
    [InlineData(0f, 14f)]
    [InlineData(5f, 24f)]
    public void AnIconOptionCountsTowardTheWidest(float padding, float segment)
    {
        // The icon (14 wide) is wider than the one-character label (8 wide), so it alone sets the width.
        Layout.ButtonGroupOption<Side>[] options =
        [
            new(Side.Mount, Icon: Layout.IconKind.Spinner),
            new(Side.Profile, "a"),
        ];
        var group = Layout.Builder.ButtonGroup<Side>(options, Side.Mount, _ => { }, Equal(padding));
        Layout.Engine.Measure(group, new Layout.Size<float>(1000f, 30f), new EqFloatCtx()).Width.ShouldBe(2 * segment + 4f);
        Segs(group).Select(s => s.Bounds.Width).Distinct().ShouldBe([segment]);
    }

    [Fact]
    public void EqualSegmentsOnACellSurfaceAreExactlyTheWidestWordAndMeasureAsAWhole()
    {
        // The 4-unit gap is 0 cells here: ToSurfaceX(4f) = round(4 / 8) = round(0.5) = 0 (to even). The widths
        // below (7 x 3 = 21, and segment N + 1 starting 7 cells on) rely on that, so it is asserted first.
        new EqCellCtx().ToSurfaceX(4f).ShouldBe(0);
        var group = Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, _ => { }, Equal());
        Layout.Engine.Measure(group, new Layout.Size<int>(80, 3), new EqCellCtx()).Width.ShouldBe(21);
        var segs = Layout.Engine.Arrange(InRow(group), new Rect<int>(0, 0, 80, 3), new EqCellCtx())
            .Where(n => n.Node.Hit is HitResult.ButtonHit).OrderBy(n => n.Bounds.X).ToArray();
        segs.Select(s => s.Bounds.Width).ShouldBe([7, 7, 7]);
        segs[1].Bounds.X.ShouldBe(segs[0].Bounds.X + 7);
    }

    [Fact]
    public void PaddingOnACellSurfaceRoundsToWholeCells()
    {
        // 5 design units are 1 cell a side (5 / 8 rounds up), so 7 + 2 = 9.
        var group = Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, _ => { }, Equal(5f));
        var segs = Layout.Engine.Arrange(InRow(group), new Rect<int>(0, 0, 80, 3), new EqCellCtx())
            .Where(n => n.Node.Hit is HitResult.ButtonHit).OrderBy(n => n.Bounds.X).ToArray();
        segs.Select(s => s.Bounds.Width).ShouldBe([9, 9, 9]);
    }

    [Fact]
    public void AnInsetLookKeepsEqualWidthsAndTheHitCoversTheWholeCellHeight()
    {
        // The width comes from the face, not from the spacers: 3 x 56 + 2 x 4 = 176.
        var style = Equal() with { InsetFraction = 0.7f, CornerRadius = 6f };
        var group = Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, _ => { }, style);
        Layout.Engine.Measure(group, new Layout.Size<float>(1000f, 30f), new EqFloatCtx()).Width.ShouldBe(176f);
        var segs = Segs(group);
        segs.Select(s => s.Bounds.Width).ShouldBe([56f, 56f, 56f]);
        segs.ShouldAllBe(s => s.Bounds.Height == 30f);
    }

    [Fact]
    public void ASetSegmentWidthWinsOverEqualToWidest()
    {
        var group = Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, _ => { }, Equal() with { SegmentWidth = 20f });
        group.ShouldBeOfType<Layout.Node.Stack>();
        Segs(group).Select(s => s.Bounds.Width).Distinct().ShouldBe([20f]);
    }

    [Fact]
    public void SegmentPaddingOutsideEqualToWidestStillBuildsStarSegments()
    {
        var group = Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, _ => { }, Style with { SegmentPadding = 6f });
        group.ShouldBeOfType<Layout.Node.Stack>();
        Segments(group).Length.ShouldBe(3);
        Segments(group).ShouldAllBe(c => c.Width.Kind == Layout.SizeKind.Star);
    }

    [Fact]
    public void SegmentPaddingOutsideEqualToWidestLeavesTheLabelThatMuchLessRoomEachSide()
    {
        static (float[] Segments, float[] Labels) Widths(Layout.Node group)
        {
            var arranged = Layout.Engine.Arrange(InRow(group), new Rect<float>(0, 0, 400f, 30f), new EqFloatCtx());
            var segs = arranged.Where(n => n.Node.Hit is HitResult.ButtonHit).OrderBy(n => n.Bounds.X)
                .Select(n => n.Bounds.Width).ToArray();
            var labels = arranged.Where(n => n.Node is Layout.Node.Leaf { Content: Layout.Content.Text })
                .OrderBy(n => n.Bounds.X).Select(n => n.Bounds.Width).ToArray();
            return (segs, labels);
        }

        var plain = Widths(Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, _ => { }, Style));
        var padded = Widths(Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, _ => { }, Style with { SegmentPadding = 6f }));

        padded.Segments.ShouldBe(plain.Segments);
        padded.Labels.Length.ShouldBe(plain.Labels.Length);
        for (var i = 0; i < plain.Labels.Length; i++)
        {
            padded.Labels[i].ShouldBe(plain.Labels[i] - 12f);
        }
    }

    [Fact]
    public void TheDefaultStyleStillBuildsAStackOfStarSegments()
    {
        var group = Layout.Builder.ButtonGroup<Side>(Options(), Side.Mount, _ => { }, Style);
        Segments(group).Length.ShouldBe(3);
        Segments(group).ShouldAllBe(c => c.Width.Kind == Layout.SizeKind.Star);
    }
}
