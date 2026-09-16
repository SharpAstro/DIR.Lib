using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <c>.Disabled(reason)</c> on any node: the rule the dropdown row has always had, generalised.
///
/// <para>
/// Everywhere else it was written by hand as "grey the text", and the two halves that are not the
/// colour went missing every time: the press has to be SWALLOWED (a disabled row that lets the press
/// through to the backdrop behind it dismisses the panel, which is to say it behaves exactly like a
/// working row), and the keyboard cursor has to step over it (a cursor parked where Enter will refuse
/// is a card that looks drawn and stops responding).
/// </para>
/// </summary>
public class LayoutDisabledTests
{
    private static readonly RGBAColor32 Panel = new(0x20, 0x20, 0x20, 0xff);
    private static readonly RGBAColor32 Ink = new(0xf0, 0xf0, 0xf0, 0xff);

    private static (DeclarationWidget Widget, DeclarationStubRenderer Renderer) Fixture()
    {
        var renderer = new DeclarationStubRenderer(100, 100);
        return (new DeclarationWidget(renderer), renderer);
    }

    [Fact]
    public void ADisabledNodeRegistersItsRegionWithoutItsHandlersSoThePressIsSwallowed()
    {
        var (widget, _) = Fixture();
        var clicked = 0;
        var tree = Layout.Builder.Spacer().Stretch()
            .Clickable(new HitResult.ButtonHit("start"), _ => clicked++)
            .Disabled("No camera is connected");

        widget.Render(tree, new RectF32(0, 0, 100, 100));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.ButtonHit);
        region.IsDisabled.ShouldBeTrue();
        region.OnClick.ShouldBeNull();
        region.Cursor.ShouldBe(CursorKind.NotAllowed);

        // It is still HIT, which is what swallowing means: the press stops here rather than reaching
        // whatever is behind it. Routed, so "stops here" is the router CONSUMING it with nothing run --
        // the property a returned hit could only imply.
        widget.HitTest(50f, 50f).ShouldBeOfType<HitResult.ButtonHit>();
        Routing.Press(widget, 50f, 50f).ShouldBeTrue();
        clicked.ShouldBe(0);
    }

    [Fact]
    public void TheReasonIsTheTooltipBecauseThatIsWhereTheQuestionIsAsked()
    {
        var (widget, _) = Fixture();
        widget.Render(
            Layout.Builder.Spacer().Stretch()
                .Clickable(new HitResult.ButtonHit("start"), _ => { })
                .Disabled("No camera is connected"),
            new RectF32(0, 0, 100, 100));

        widget.GetRegisteredRegions().Single(r => r.Result is HitResult.ButtonHit)
            .Tooltip.ShouldBe("No camera is connected");
    }

    [Fact]
    public void AnExplicitTooltipStillWins()
    {
        var (widget, _) = Fixture();
        widget.Render(
            Layout.Builder.Spacer().Stretch()
                .Clickable(new HitResult.ButtonHit("start"), _ => { })
                .WithTooltip("Start the run")
                .Disabled("No camera is connected"),
            new RectF32(0, 0, 100, 100));

        widget.GetRegisteredRegions().Single(r => r.Result is HitResult.ButtonHit)
            .Tooltip.ShouldBe("Start the run");
    }

    [Fact]
    public void ADisabledRowsTextIsHalvedTowardTheBackgroundItIsDrawnOn()
    {
        // Halved toward the background rather than made translucent, because these are drawn over an
        // opaque panel and a translucent colour reads as a different shade per theme. The same rule
        // RenderDropdownMenu has always used, now in one helper both call.
        var (widget, renderer) = Fixture();
        widget.Render(
            Layout.Builder.VStack(Layout.Builder.Text("Start", 10f, Ink).Stretch())
                .Stretch().Bg(Panel).Disabled("No camera is connected"),
            new RectF32(0, 0, 100, 100));

        renderer.LastTextColor.ShouldBe(new RGBAColor32(
            (byte)((Ink.Red + Panel.Red) / 2),
            (byte)((Ink.Green + Panel.Green) / 2),
            (byte)((Ink.Blue + Panel.Blue) / 2),
            Ink.Alpha));
    }

    [Fact]
    public void DisablingAContainerReachesTheLabelInsideIt()
    {
        // Because that is what a caller means by disabling a row. A row whose label stayed bright is a
        // row that looks pressable and is not.
        var (widget, renderer) = Fixture();
        widget.Render(
            Layout.Builder.VStack(
                    Layout.Builder.Text("Enabled", 10f, Ink).RowH(10f),
                    Layout.Builder.VStack(Layout.Builder.Text("Disabled", 10f, Ink).RowH(10f))
                        .RowH(10f).Disabled("Not while a run is on"))
                .Stretch().Bg(Panel),
            new RectF32(0, 0, 100, 20));

        // The disabled row paints last, so the last colour is its.
        renderer.LastTextColor.ShouldNotBe(Ink);
    }

    [Fact]
    public void ARowOutsideTheDisabledSubtreeIsUntouched()
    {
        var (widget, renderer) = Fixture();
        widget.Render(
            Layout.Builder.VStack(
                    Layout.Builder.VStack(Layout.Builder.Text("Disabled", 10f, Ink).RowH(10f))
                        .RowH(10f).Disabled("Not while a run is on"),
                    Layout.Builder.Text("Enabled", 10f, Ink).RowH(10f))
                .Stretch().Bg(Panel),
            new RectF32(0, 0, 100, 20));

        // The enabled row paints AFTER the disabled subtree, so the reset on leaving it is what this
        // catches -- a depth walk that never pops would dim the rest of the tree.
        renderer.LastTextColor.ShouldBe(Ink);
    }

    [Fact]
    public void TheListCursorStepsOverADisabledRow()
    {
        // The headline property, and the half a hand-written "grey it out" always forgot: the arrows
        // must not stop where Enter will refuse.
        var (widget, _) = Fixture();
        widget.ListCursor.Open("views", 0);
        widget.Render(Rows(disabledIndex: 1), new RectF32(0, 0, 100, 30));

        widget.MoveListCursor(1).ShouldBeTrue();
        widget.ListCursor.Index.ShouldBe(2);
        widget.MoveListCursor(-1).ShouldBeTrue();
        widget.ListCursor.Index.ShouldBe(0);
    }

    [Fact]
    public void EnterRefusesADisabledRowTheWayAClickDoes()
    {
        var (widget, _) = Fixture();
        var chosen = new List<int>();
        widget.ListCursor.Open("views", 1);
        widget.Render(Rows(disabledIndex: 1, chosen), new RectF32(0, 0, 100, 30));

        widget.ActivateListCursor().ShouldBeFalse();
        widget.HandleListKey(InputKey.Enter).ShouldBeFalse("so a forwarding host reaches its own binding");
        chosen.ShouldBeEmpty();
    }

    [Fact]
    public void TheConditionalFormLeavesTheNodeAloneWhenTheConditionIsFalse()
    {
        Layout.Builder.Spacer().Disabled(false, "No camera").IsDisabled.ShouldBeFalse();
        Layout.Builder.Spacer().Disabled(true, "No camera").DisabledReason.ShouldBe("No camera");
    }

    [Fact]
    public void OnlyTheNodeThatDECLARESTheDisabilityStatesTheCursor()
    {
        // The enclosing NotAllowed covers the whole subtree, and a region with no opinion is transparent
        // to the cursor lookup -- so a plain label inside a disabled row needs no region of its own, and
        // registering one per node would be a region per node for an answer that was already there.
        var (widget, _) = Fixture();
        widget.Render(
            Layout.Builder.VStack(Layout.Builder.Text("Start", 10f, Ink).RowH(10f))
                .Stretch().Bg(Panel)
                .Clickable(new HitResult.ButtonHit("start"), _ => { })
                .Disabled("No camera is connected"),
            new RectF32(0, 0, 100, 100));

        widget.GetRegisteredRegions().Length.ShouldBe(1);
        widget.HitTestCursor(50f, 5f).ShouldBe(CursorKind.NotAllowed);
    }

    [Fact]
    public void AButtonInsideADisabledPanelIsInertToo()
    {
        // Or a disabled panel would be a panel you can still press things in.
        var (widget, _) = Fixture();
        var clicked = 0;
        widget.Render(
            Layout.Builder.VStack(
                    Layout.Builder.Spacer().RowH(10f)
                        .Clickable(new HitResult.ButtonHit("inner"), _ => clicked++))
                .Stretch().Bg(Panel).Disabled("Not while a run is on"),
            new RectF32(0, 0, 100, 100));

        var inner = widget.GetRegisteredRegions()
            .Single(r => r.Result is HitResult.ButtonHit { Action: "inner" });
        inner.IsDisabled.ShouldBeTrue();
        inner.OnClick.ShouldBeNull();

        Routing.Press(widget, 50f, 5f).ShouldBeTrue();
        clicked.ShouldBe(0);
    }

    [Fact]
    public void AnOrdinaryNodeIsNotDisabledAndItsRegionSaysSo()
    {
        var (widget, _) = Fixture();
        widget.Render(
            Layout.Builder.Spacer().Stretch().Clickable(new HitResult.ButtonHit("go"), _ => { }),
            new RectF32(0, 0, 100, 100));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.ButtonHit);
        region.IsDisabled.ShouldBeFalse();
        region.Cursor.ShouldBeNull();
        region.Tooltip.ShouldBeNull();
    }

    /// <summary>Three rows of ten, the given one declared unavailable.</summary>
    private static Layout.Node Rows(int disabledIndex, List<int>? chosen = null)
    {
        var rows = new Layout.Node[3];
        for (var i = 0; i < rows.Length; i++)
        {
            var index = i;
            var row = Layout.Builder.Spacer().RowH(10f).Bg(Panel)
                .Clickable(new HitResult.ListItemHit("views", index), _ => chosen?.Add(index));
            rows[i] = index == disabledIndex ? row.Disabled("No filter wheel on this OTA") : row;
        }
        return Layout.Builder.VStack(rows).Stretch();
    }
}
