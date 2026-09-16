using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// A tab carries its own chord and its own handler (<see cref="TabItem{T}.Shortcut"/>,
/// <see cref="TabItem{T}.OnSelect"/>), so neither has to be put back on after the paint.
/// </summary>
/// <remarks>
/// <para>
/// What this replaces: a consumer overrode <c>CollectPaintedNodes</c>, found the cells by their hit and
/// rewrote each node with a chord out of a dictionary and a handler out of a closure -- the host
/// re-stating, once per frame, what the item already knew. The strip is built here directly rather than
/// painted, because the question is what the TREE says and a paint would only be a slower way to ask it.
/// </para>
/// <para>
/// The disabled cases are the ones worth the file. "Still drawn, and inert" is what
/// <see cref="TabItem{T}.IsEnabled"/> has always promised, and the index callback quietly did not keep
/// it: a greyed tab registered a handler and fired it as readily as a live one.
/// </para>
/// </remarks>
public class TabItemShortcutTests
{
    private enum Page
    {
        Home,
        Equipment,
        Planner,
    }

    private static readonly TabStripOptions Options = new()
    {
        Metrics = new TabStripMetrics(
            Thickness: 30f, FontSize: 12f, Pad: 10f, Border: 1f,
            IconBox: 18f, CloseBox: 16f, MinTabExtent: 80f, MaxTabExtent: 200f),
    };

    /// <summary>Half the font size per character: geometry is arithmetic here, not a rasterized baseline.</summary>
    private static float Measure(string s) => s.Length * 6f;

    private static TabStrip Build(IReadOnlyList<TabItem<Page>> items, Action<int>? onSelect = null)
        => TabStripTree.Build(items, activeIndex: 0, pointerFlow: null, pointerCross: null,
            availableFlow: 1000f, Measure, Options, onSelect);

    /// <summary>Walks the built tree for the node carrying a tab's hit — enabled or disabled.</summary>
    private static Node? Cell(TabStrip strip, int index)
    {
        Node? found = null;
        Walk(strip.Root, n =>
        {
            if (n.Hit is HitResult.ListItemHit { ListId: TabBarRegions.Tabs or TabBarRegions.DisabledTabs } h
                && h.Index == index)
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

    [Fact]
    public void ATabCarriesItsChordOntoTheNodeTheRouterMatches()
    {
        var strip = Build([
            new TabItem<Page>("Home", Page.Home) { Shortcut = new KeyChord(InputKey.H, InputModifier.Ctrl) },
            new TabItem<Page>("Equipment", Page.Equipment) { Shortcut = new KeyChord(InputKey.E, InputModifier.Ctrl) },
        ]);

        Cell(strip, 0)!.Shortcut.ShouldBe(new KeyChord(InputKey.H, InputModifier.Ctrl));
        Cell(strip, 1)!.Shortcut.ShouldBe(new KeyChord(InputKey.E, InputModifier.Ctrl));
    }

    [Fact]
    public void ATabWithNoChordCarriesNone()
    {
        var strip = Build([new TabItem<Page>("Home", Page.Home)]);

        Cell(strip, 0)!.Shortcut.ShouldBeNull();
    }

    /// <summary>
    /// The chord of a LOCKED tab is inert by absence. Stated as a test because the alternative -- a guard
    /// beside the key in the host -- is what the whole declaration exists to delete, and a chord that
    /// still fired would put the guard straight back.
    /// </summary>
    [Fact]
    public void ADisabledTabCarriesNoChord()
    {
        var strip = Build([
            new TabItem<Page>("Equipment", Page.Equipment)
            {
                IsEnabled = false,
                Shortcut = new KeyChord(InputKey.E, InputModifier.Ctrl),
            },
        ]);

        var cell = Cell(strip, 0);
        cell.ShouldNotBeNull("a disabled tab is still drawn and still registers");
        cell!.Hit.ShouldBeOfType<HitResult.ListItemHit>().ListId.ShouldBe(TabBarRegions.DisabledTabs);
        cell.Shortcut.ShouldBeNull("the binding goes on only when the tab can be reached");
    }

    [Fact]
    public void SelectingATabRunsItsOwnHandlerWithItsOwnValue()
    {
        var chosen = new List<Page>();
        var strip = Build([
            new TabItem<Page>("Home", Page.Home) { OnSelect = chosen.Add },
            new TabItem<Page>("Planner", Page.Planner) { OnSelect = chosen.Add },
        ]);

        Cell(strip, 1)!.OnClick!(InputModifier.None);

        chosen.ShouldBe([Page.Planner], "the tab that was chosen IS the value, not an index to map");
    }

    [Fact]
    public void ADisabledTabRunsNothing()
    {
        var ran = false;
        var strip = Build(
            [new TabItem<Page>("Equipment", Page.Equipment) { IsEnabled = false, OnSelect = _ => ran = true }],
            onSelect: _ => ran = true);

        Cell(strip, 0)!.OnClick.ShouldBeNull("still drawn, and inert -- so there is nothing to run");
        ran.ShouldBeFalse();
    }

    /// <summary>
    /// The strip-wide index callback is unchanged for an ENABLED tab: a surface that had one keeps it, and
    /// this is what makes the addition additive rather than a replacement.
    /// </summary>
    [Fact]
    public void TheIndexCallbackStillFires()
    {
        var seen = new List<int>();
        var strip = Build([
            new TabItem<Page>("Home", Page.Home),
            new TabItem<Page>("Planner", Page.Planner),
        ], onSelect: seen.Add);

        Cell(strip, 1)!.OnClick!(InputModifier.None);

        seen.ShouldBe([1]);
    }

    /// <summary>
    /// Both handlers run when both are given. They answer different questions -- "the strip was used at
    /// index i" against "this tab was chosen" -- so a caller stating both means both, and dropping either
    /// silently would be the kind of half-wiring a tab bar takes a release to notice.
    /// </summary>
    [Fact]
    public void BothHandlersRunWhenBothAreGiven()
    {
        var byIndex = -1;
        Page? byItem = null;
        var strip = Build(
            [new TabItem<Page>("Home", Page.Home), new TabItem<Page>("Planner", Page.Planner) { OnSelect = p => byItem = p }],
            onSelect: i => byIndex = i);

        Cell(strip, 1)!.OnClick!(InputModifier.None);

        byIndex.ShouldBe(1);
        byItem.ShouldBe(Page.Planner);
    }

    /// <summary>
    /// A tab the strip DROPPED on overflow takes its chord with it, because it is not in the tree at all --
    /// which is the router's match-against-the-painted-tree rule doing the work rather than a second check.
    /// </summary>
    [Fact]
    public void ADroppedTabIsNotInTheTreeAndNeitherIsItsChord()
    {
        var items = Enumerable.Range(0, 6)
            .Select(i => new TabItem<Page>($"Tab {i}", Page.Home)
            {
                Shortcut = new KeyChord(InputKey.F1 + i),
            })
            .ToArray();

        // 80 units minimum per tab, 200 available: two fit, the rest are dropped rather than clipped.
        var strip = TabStripTree.Build(items, activeIndex: 0, pointerFlow: null, pointerCross: null,
            availableFlow: 200f, Measure,
            Options with { Overflow = TabStripOverflow.Drop });

        Cell(strip, 0).ShouldNotBeNull();
        Cell(strip, 5).ShouldBeNull("dropped, so there is no node to carry the chord");
    }
}
