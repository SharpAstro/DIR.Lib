using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A <see cref="Layout.Node.Split"/> whose divider drags itself (<see cref="Layout.Node.Split.OnResize"/>, 11.18).
/// <para>
/// Before this a divider carried a hit and no handler, which a router consumes and runs nothing for, so every consumer
/// registered a second region over it to arm the drag and turned the pointer back into an extent by hand, against a
/// pane origin it assumed and a scale it read off the window. What is pinned here is that the engine does that once:
/// the drag is relative to the press, in design units, clamped to the split, and the band that answers a press can be
/// wider than the line that is drawn.
/// </para>
/// </summary>
public class LayoutSplitResizeTests
{
    private sealed class ScaledCtx(float scale) : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 7f, 16f);
        public float ToSurface(float designUnits) => designUnits * scale;
    }

    private sealed record DividerHit(string Id) : HitResult;

    private static Layout.Node Pane() => Layout.Builder.Spacer();

    private static PointerPress Press(float x, float y) => new(x, y, MouseButton.Left, InputModifier.None, 1);

    private static PointerMove Move(float x, float y) => new(x, y, MouseButton.Left, InputModifier.None);

    private static Layout.ArrangedNode<float> Pressable(ImmutableArray<Layout.ArrangedNode<float>> arranged)
        => arranged.Single(a => a.Node.OnPress is not null);

    [Fact]
    public void ADividerWithNoResizeHandlerStaysTheHostsAsBefore()
    {
        var hit = new DividerHit("list");
        var split = Layout.Builder.Split(Pane(), Pane(), firstExtent: 30f, dividerHit: hit);

        var arranged = Layout.Engine.Arrange(split, new Rect<float>(0, 0, 100, 40), new ScaledCtx(1f));

        var divider = arranged.Single(a => ReferenceEquals(a.Node.Hit, hit));
        divider.Node.OnPress.ShouldBeNull();
        divider.Node.Cursor.ShouldBeNull();
    }

    [Fact]
    public void TheDragIsRelativeToThePressSoAPressInTheBandMovesNothing()
    {
        // The divider is at 30..36; a press at 34 that has not moved yet must report the extent it started from,
        // never the pointer's position (which would jump the pane by four units).
        var steps = new List<Layout.SplitResize>();
        var split = Layout.Builder.Split(Pane(), Pane(), steps.Add, firstExtent: 30f, dividerThickness: 6f);
        var arranged = Layout.Engine.Arrange(split, new Rect<float>(0, 0, 100, 40), new ScaledCtx(1f));

        var capture = Pressable(arranged).Node.OnPress!(Press(34f, 20f));

        capture.ShouldNotBeNull();
        steps.ShouldBe([new Layout.SplitResize(30f, Ended: false)]);

        capture.Move(Move(44f, 25f));
        capture.Release(Move(50f, 25f));

        steps.ShouldBe([
            new Layout.SplitResize(30f, Ended: false),
            new Layout.SplitResize(40f, Ended: false),
            new Layout.SplitResize(46f, Ended: true),
        ]);
    }

    [Fact]
    public void TheExtentIsInDesignUnitsWhateverTheSurfaceScale()
    {
        // At 1.5x the split starts at 15 px and its first pane is 30 design units, 45 px. A 30 px move is 20 units.
        var steps = new List<Layout.SplitResize>();
        var split = Layout.Builder.Split(Pane(), Pane(), steps.Add, firstExtent: 30f, dividerThickness: 4f);
        var arranged = Layout.Engine.Arrange(split, new Rect<float>(15, 0, 300, 40), new ScaledCtx(1.5f));

        var capture = Pressable(arranged).Node.OnPress!(Press(15f + 45f + 3f, 20f))!;
        capture.Move(Move(15f + 45f + 3f + 30f, 20f));

        steps[^1].FirstExtent.ShouldBe(50f, 1e-4f);
    }

    [Fact]
    public void TheExtentReportedIsClampedToWhatTheSplitCanLayOut()
    {
        var steps = new List<Layout.SplitResize>();
        var split = Layout.Builder.Split(Pane(), Pane(), steps.Add, firstExtent: 30f, dividerThickness: 6f);
        var arranged = Layout.Engine.Arrange(split, new Rect<float>(0, 0, 100, 40), new ScaledCtx(1f));

        var capture = Pressable(arranged).Node.OnPress!(Press(33f, 20f))!;

        capture.Move(Move(-500f, 20f));
        steps[^1].FirstExtent.ShouldBe(0f);

        capture.Move(Move(500f, 20f));
        steps[^1].FirstExtent.ShouldBe(94f); // 100 - the 6-unit divider
    }

    [Fact]
    public void AVerticalSplitDragsAlongY()
    {
        var steps = new List<Layout.SplitResize>();
        var split = Layout.Builder.Split(Pane(), Pane(), steps.Add, Layout.Axis.Vertical, firstExtent: 20f,
            dividerThickness: 4f);
        var arranged = Layout.Engine.Arrange(split, new Rect<float>(0, 0, 50, 100), new ScaledCtx(1f));

        var divider = Pressable(arranged);
        divider.Node.Cursor.ShouldBe(CursorKind.ResizeNS);
        var capture = divider.Node.OnPress!(Press(10f, 22f))!;
        capture.Move(Move(40f, 32f));

        steps[^1].FirstExtent.ShouldBe(30f);
    }

    [Fact]
    public void AResizableDividerStatesTheResizeCursorAndAChromeHitWhenGivenNoHit()
    {
        var split = Layout.Builder.Split(Pane(), Pane(), _ => { }, firstExtent: 30f, dividerThickness: 6f);

        var arranged = Layout.Engine.Arrange(split, new Rect<float>(0, 0, 100, 40), new ScaledCtx(1f));

        var divider = Pressable(arranged);
        divider.Bounds.ShouldBe(new Rect<float>(30, 0, 6, 40));
        divider.Node.Cursor.ShouldBe(CursorKind.ResizeEW);
        divider.Node.Hit.ShouldBe(new HitResult.ChromeHit());
    }

    [Fact]
    public void AWideGrabBandIsCentredOnTheLineAndEmittedAfterBothPanes()
    {
        // A 2-unit line at 40..42 with a 12-unit band: 35..47, over both panes' edges, so it is the LAST node, where
        // it wins the hit over whatever the panes registered there. The line itself is drawn but answers nothing.
        var colour = new RGBAColor32(0x40, 0x40, 0x48, 0xff);
        var hit = new DividerHit("compare");
        var second = Layout.Builder.Spacer().Clickable(new HitResult.ButtonHit("image"), _ => { });
        var split = Layout.Builder.Split(Pane(), second, _ => { }, firstExtent: 40f, dividerThickness: 2f,
            grabThickness: 12f, dividerHit: hit, dividerColor: colour);

        var arranged = Layout.Engine.Arrange(split, new Rect<float>(0, 0, 100, 40), new ScaledCtx(1f));

        var line = arranged.Single(a => a.Node.Background == colour);
        line.Bounds.ShouldBe(new Rect<float>(40, 0, 2, 40));
        line.Node.Hit.ShouldBeNull();
        line.Node.OnPress.ShouldBeNull();

        var band = arranged[^1];
        band.Node.Hit.ShouldBe(hit);
        band.Bounds.ShouldBe(new Rect<float>(35, 0, 12, 40));
        band.Node.OnPress.ShouldNotBeNull();
        band.Node.Cursor.ShouldBe(CursorKind.ResizeEW);
    }

    [Fact]
    public void AGrabBandAtTheEdgeIsKeptInsideTheSplit()
    {
        var split = Layout.Builder.Split(Pane(), Pane(), _ => { }, firstExtent: 0f, dividerThickness: 2f,
            grabThickness: 12f);

        var arranged = Layout.Engine.Arrange(split, new Rect<float>(10, 0, 100, 40), new ScaledCtx(1f));

        // Centred on the line at 11, the band would start at 5, left of the split; it starts at the split's own edge.
        arranged[^1].Bounds.ShouldBe(new Rect<float>(10, 0, 7, 40));
    }

    [Fact]
    public void PaintingTheSplitRegistersTheBandWithItsPressAndCursor()
    {
        var renderer = new DeclarationStubRenderer(100, 100);
        var widget = new DeclarationWidget(renderer);
        var hit = new DividerHit("list");
        var steps = new List<Layout.SplitResize>();

        widget.Render(
            Layout.Builder.Split(Pane(), Pane(), steps.Add, firstExtent: 30f, dividerThickness: 4f, grabThickness: 10f,
                dividerHit: hit).Stretch(),
            new RectF32(0, 0, 100, 40));

        var region = widget.GetRegisteredRegions().Single(r => r.Result == hit);
        region.X.ShouldBe(27f);
        region.Width.ShouldBe(10f);
        region.Cursor.ShouldBe(CursorKind.ResizeEW);
        region.OnPress!(Press(30f, 5f))!.Release(Move(20f, 5f));
        steps[^1].ShouldBe(new Layout.SplitResize(20f, Ended: true));
    }
}
