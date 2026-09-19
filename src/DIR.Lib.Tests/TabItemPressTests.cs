using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// A tab carries its own press, its own close and the bar its own new-tab handler
/// (<see cref="TabItem{T}.OnPress"/>, <see cref="TabItem{T}.OnClose"/>, <c>TabBar.OnNewTab</c>), so under
/// a router the strip is live rather than a set of hits with no handler.
/// </summary>
/// <remarks>
/// What this replaces: a host under a router could not start a drag from a tab at all -- the region
/// consumed the press and nothing reached the host -- and the ✕ and the + were consumed and did nothing.
/// Built as a tree rather than painted, for the reason <see cref="TabItemShortcutTests"/> gives.
/// </remarks>
public class TabItemPressTests
{
    private enum Doc
    {
        Spec,
        Plan,
    }

    private static readonly TabStripOptions Options = new()
    {
        CanCloseTabs = true,
        Metrics = new TabStripMetrics(
            Thickness: 30f, FontSize: 12f, Pad: 10f, Border: 1f,
            IconBox: 18f, CloseBox: 16f, MinTabExtent: 80f, MaxTabExtent: 200f),
    };

    private static float Measure(string s) => s.Length * 6f;

    private static TabStrip Build(IReadOnlyList<TabItem<Doc>> items)
        => TabStripTree.Build(items, activeIndex: 0, pointerFlow: null, pointerCross: null,
            availableFlow: 1000f, Measure, Options);

    private static Node? Cell(TabStrip strip, int index) => Find(strip,
        n => n.Hit is HitResult.ListItemHit { ListId: TabBarRegions.Tabs or TabBarRegions.DisabledTabs } h && h.Index == index);

    private static Node? CloseMark(TabStrip strip, int index) => Find(strip,
        n => n.Hit is HitResult.ListItemHit { ListId: TabBarRegions.CloseButtons } h && h.Index == index);

    private static Node? Find(TabStrip strip, Func<Node, bool> match)
    {
        Node? found = null;
        Walk(strip.Root, n =>
        {
            if (match(n))
            {
                found = n;
            }
        });
        return found;
    }

    private static void Walk(Node node, Action<Node> visit)
    {
        visit(node);
        foreach (var child in Children(node))
        {
            Walk(child, visit);
        }
    }

    private static IEnumerable<Node> Children(Node node) => node switch
    {
        Node.Stack s => s.Children,
        Node.Wrap w => w.Children,
        Node.Grid g => g.Cells,
        Node.Overlay o => [o.Base, o.Top],
        Node.Anchored a => [a.Child],
        Node.Split sp => [sp.First, sp.Second],
        _ => [],
    };

    private static PointerPress Press(MouseButton button, int clicks = 1)
        => new(5f, 5f, button, InputModifier.None, clicks);

    [Fact]
    public void ATabsPressIsBoundWithItsValueAndToldTheButton()
    {
        (Doc Value, MouseButton Button)? seen = null;
        var strip = Build([
            new TabItem<Doc>("Spec", Doc.Spec),
            new TabItem<Doc>("Plan", Doc.Plan) { OnPress = (v, p) => { seen = (v, p.Button); return null; } },
        ]);

        Cell(strip, 0)!.OnPress.ShouldBeNull("a tab without a press keeps only its click");
        Cell(strip, 1)!.OnPress!(Press(MouseButton.Middle)).ShouldBeNull();
        seen.ShouldBe((Doc.Plan, MouseButton.Middle));
    }

    [Fact]
    public void ThePressRidesBesideTheClickRatherThanReplacingIt()
    {
        var selected = new List<Doc>();
        var strip = Build([
            new TabItem<Doc>("Spec", Doc.Spec)
            {
                OnSelect = selected.Add,
                OnPress = (_, _) => null,
            },
        ]);

        var cell = Cell(strip, 0)!;
        cell.OnPress.ShouldNotBeNull();
        cell.OnClick.ShouldNotBeNull();
        cell.OnClick!(InputModifier.None);
        selected.ShouldBe([Doc.Spec]);
    }

    [Fact]
    public void ThePressReturnsTheCaptureItClaims()
    {
        var capture = new DragCapture(_ => { }, _ => { });
        var strip = Build([new TabItem<Doc>("Spec", Doc.Spec) { OnPress = (_, _) => capture }]);

        Cell(strip, 0)!.OnPress!(Press(MouseButton.Left)).ShouldBeSameAs(capture);
    }

    [Fact]
    public void TheCloseMarkIsBoundToOnClose()
    {
        var closed = new List<Doc>();
        var strip = Build([
            new TabItem<Doc>("Spec", Doc.Spec),
            new TabItem<Doc>("Plan", Doc.Plan) { OnClose = closed.Add },
        ]);

        CloseMark(strip, 0)!.OnClick.ShouldBeNull("a tab without OnClose leaves its mark to the host");
        CloseMark(strip, 1)!.OnClick!(InputModifier.None);
        closed.ShouldBe([Doc.Plan]);
    }

    /// <summary>"Still drawn, and inert": a disabled tab drops its press with the rest of its handlers,
    /// and has no ✕ to bind at all.</summary>
    [Fact]
    public void ADisabledTabDropsItsPressAndHasNoClose()
    {
        var strip = Build([
            new TabItem<Doc>("Spec", Doc.Spec)
            {
                IsEnabled = false,
                OnPress = (_, _) => null,
                OnClose = _ => { },
            },
        ]);

        var cell = Cell(strip, 0)!;
        cell.Hit.ShouldBeOfType<HitResult.ListItemHit>().ListId.ShouldBe(TabBarRegions.DisabledTabs);
        cell.OnPress.ShouldBeNull();
        CloseMark(strip, 0).ShouldBeNull();
    }

    [Fact]
    public void TheBarBindsThePlusToOnNewTab()
    {
        var opened = 0;
        var bar = new TabBar<RgbaImage>(new DeclarationStubRenderer(400, 40))
        {
            ShowNewTabButton = true,
            OnNewTab = () => opened++,
        };

        bar.Render(0f, 400f, ["Spec"], 0);

        var plus = bar.GetRegisteredRegions().Single(r => r.Result is HitResult.ButtonHit { Action: TabBarRegions.NewTab });
        plus.OnClick.ShouldNotBeNull();
        plus.OnClick!(InputModifier.None);
        opened.ShouldBe(1);
    }

    [Fact]
    public void ThePlusWithoutAHandlerStaysAHitTheHostReadsBack()
    {
        var bar = new TabBar<RgbaImage>(new DeclarationStubRenderer(400, 40)) { ShowNewTabButton = true };

        bar.Render(0f, 400f, ["Spec"], 0);

        var plus = bar.GetRegisteredRegions().Single(r => r.Result is HitResult.ButtonHit { Action: TabBarRegions.NewTab });
        plus.OnClick.ShouldBeNull();
        bar.HitNewTabButton(plus.X + 1f, plus.Y + 1f).ShouldBeTrue();
    }
}
