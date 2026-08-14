using System.Collections.Immutable;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Headless tests for <see cref="DropdownMenuState"/> -- focused on the overflow-scroll behaviour added
/// so a menu whose items exceed its <c>maxHeight</c> scrolls its window instead of silently clipping the
/// rows past the fold. The render method (<see cref="PixelWidgetBase{TSurface}.RenderDropdownMenu"/>) sets
/// the scroll geometry each frame; here the tests drive <see cref="ListScrollController.SetExtent"/>
/// directly (a 100px-tall viewport of 10px atoms = 10 visible) to stand in for that render pass.
/// </summary>
public class DropdownMenuStateTests
{
    private static ImmutableArray<DropdownItem<string>> Items(int n) =>
        Enumerable.Range(0, n).Select(i => DropdownItem.Text($"item{i}")).ToImmutableArray();

    private static DropdownMenuState<string> OpenWith(int itemCount, float viewportH = 100f, float atomPx = 10f)
    {
        var d = new DropdownMenuState<string>();
        d.Open(0f, 0f, 100f, Items(itemCount));
        // Stand in for the per-frame RenderDropdownMenu geometry refresh.
        d.Scroll.SetExtent(new RectF32(0f, 0f, 100f, viewportH), atomPx, itemCount, 1f);
        return d;
    }

    [Fact]
    public void KeyboardDown_PastFold_ScrollsHighlightIntoView()
    {
        var d = OpenWith(20); // 20 items, 10 visible

        for (var i = 0; i < 15; i++)
        {
            d.HandleKeyDown(InputKey.Down).ShouldBeTrue();
        }

        d.HighlightIndex.ShouldBe(14);            // -1 -> 0 -> ... -> 14 after 15 presses
        d.Scroll.FirstVisibleAtom.ShouldBe(5);    // EnsureVisible(14): 14+1-10 = 5, showing 5..14
    }

    [Fact]
    public void KeyboardUp_ScrollsBackToKeepHighlightVisible()
    {
        var d = OpenWith(20);
        for (var i = 0; i < 15; i++) d.HandleKeyDown(InputKey.Down); // highlight 14, first 5

        for (var i = 0; i < 12; i++) d.HandleKeyDown(InputKey.Up);   // highlight 2 (clamped floor 0 path)
        d.HighlightIndex.ShouldBe(2);
        d.Scroll.FirstVisibleAtom.ShouldBe(2);   // EnsureVisible(2): 2 < 5 -> offset follows up to 2
    }

    [Fact]
    public void FittingMenu_NeverScrolls_OnKeyboardOrWheel()
    {
        var d = OpenWith(5); // 5 items, 10 visible -> fits

        for (var i = 0; i < 5; i++) d.HandleKeyDown(InputKey.Down);
        d.Scroll.FirstVisibleAtom.ShouldBe(0);
        d.Scroll.MaxOffset.ShouldBe(0f);

        d.HandleScrollInput(new InputEvent.Scroll(-1f, 50f, 50f)).ShouldBeFalse();
        d.Scroll.FirstVisibleAtom.ShouldBe(0);
    }

    [Fact]
    public void Wheel_ScrollsOverflowingMenu_WhenForwarded()
    {
        var d = OpenWith(30); // 30 items, 10 visible -> overflows

        d.HandleScrollInput(new InputEvent.Scroll(-1f, 50f, 50f)).ShouldBeTrue(); // one notch = 3 atoms
        d.Scroll.FirstVisibleAtom.ShouldBe(3);
    }

    [Fact]
    public void Wheel_IsNoOp_WhenClosed()
    {
        var d = OpenWith(30);
        d.Close();
        d.HandleScrollInput(new InputEvent.Scroll(-1f, 50f, 50f)).ShouldBeFalse();
    }

    [Fact]
    public void Reopen_ResetsScrollToTop()
    {
        var d = OpenWith(30);
        for (var i = 0; i < 15; i++) d.HandleKeyDown(InputKey.Down); // scrolled into the middle
        d.Scroll.FirstVisibleAtom.ShouldBeGreaterThan(0);

        d.Close();
        d.Open(0f, 0f, 100f, Items(30)); // reopen resets offset even before the next render
        d.Scroll.FirstVisibleAtom.ShouldBe(0);
    }

    /// <summary>
    /// An ACTION row ("Custom...") is just the last item now, not an atom past the end of the list. It
    /// scrolls, highlights and is chosen like any other -- which is the whole reason the special case,
    /// three Open parameters and three properties could go.
    /// </summary>
    [Fact]
    public void AnActionRow_IsTheLastScrollableAtom()
    {
        var chosen = false;
        var d = new DropdownMenuState<string>();
        var items = Items(12).Add(DropdownItem<string>.Action("Custom...", () => chosen = true));
        d.Open(0f, 0f, 100f, items);
        d.Scroll.SetExtent(new RectF32(0f, 0f, 100f, 100f), 10f, 13, 1f); // 12 items + the action row

        for (var i = 0; i < 13; i++) d.HandleKeyDown(InputKey.Down);
        d.HighlightIndex.ShouldBe(12);
        d.Scroll.FirstVisibleAtom.ShouldBe(3); // 12+1-10

        d.HandleKeyDown(InputKey.Enter);
        chosen.ShouldBeTrue();
        d.IsOpen.ShouldBeFalse();
    }

    // ---- Disabled entries: a menu row must never be clickable-but-inert ---------------------------

    private static DropdownMenuState<string> WithDisabledMiddle(out bool[] chosen)
    {
        var picked = new bool[3];
        chosen = picked;
        var d = new DropdownMenuState<string>();
        d.Open(0f, 0f, 100f, [
            DropdownItem.Text("first"),
            DropdownItem<string>.Disabled("middle", "middle", "needs a mount"),
            DropdownItem.Text("last"),
        ], item => picked[item.Label == "first" ? 0 : item.Label == "middle" ? 1 : 2] = true);
        return d;
    }

    /// <summary>The highlight must never park on a row Enter would refuse -- that reads as a stuck key.</summary>
    [Fact]
    public void ArrowDown_SkipsADisabledEntry()
    {
        var d = WithDisabledMiddle(out _);

        d.HandleKeyDown(InputKey.Down);
        d.HighlightIndex.ShouldBe(0);
        d.HandleKeyDown(InputKey.Down);
        d.HighlightIndex.ShouldBe(2, "index 1 is disabled and must be stepped over");
    }

    [Fact]
    public void ArrowUp_SkipsADisabledEntry()
    {
        var d = WithDisabledMiddle(out _);
        d.HighlightIndex = 2;

        d.HandleKeyDown(InputKey.Up);
        d.HighlightIndex.ShouldBe(0);
    }

    /// <summary>With nothing selectable further on, the highlight HOLDS rather than jumping to an end.</summary>
    [Fact]
    public void Arrowing_PastTheEnd_HoldsItsPlace()
    {
        var d = WithDisabledMiddle(out _);
        d.HighlightIndex = 2;

        d.HandleKeyDown(InputKey.Down);
        d.HighlightIndex.ShouldBe(2);
    }

    [Fact]
    public void TrySelect_RefusesADisabledEntry_AndLeavesTheMenuOpen()
    {
        var d = WithDisabledMiddle(out var chosen);

        d.TrySelect(1).ShouldBeFalse();
        chosen[1].ShouldBeFalse();
        // Left OPEN on purpose: closing on a click that did nothing is exactly the silent dead-end the
        // disabled state exists to remove.
        d.IsOpen.ShouldBeTrue();
    }

    /// <summary>Seeding the highlight is what makes the menu open ON the current value.</summary>
    [Fact]
    public void Open_SeedsTheHighlightWithTheCurrentSelection()
    {
        var d = new DropdownMenuState<string>();
        d.Open(0f, 0f, 100f, Items(4), highlightIndex: 2);

        d.HighlightIndex.ShouldBe(2);
    }

    /// <summary>The entry carries its meaning, so no index has to be mapped back to it.</summary>
    [Fact]
    public void Select_HandsBackTheChosenEntry()
    {
        DropdownItem<int>? got = null;
        var d = new DropdownMenuState<int>();
        d.Open(0f, 0f, 100f, [new DropdownItem<int>("ten", 10), new DropdownItem<int>("twenty", 20)],
            item => got = item);

        d.TrySelect(1).ShouldBeTrue();
        got.ShouldNotBeNull();
        got.Value.ShouldBe(20);
    }
}
