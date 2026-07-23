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
    private static ImmutableArray<string> Items(int n) =>
        Enumerable.Range(0, n).Select(i => $"item{i}").ToImmutableArray();

    private static DropdownMenuState OpenWith(int itemCount, float viewportH = 100f, float atomPx = 10f)
    {
        var d = new DropdownMenuState();
        d.Open(0f, 0f, 100f, Items(itemCount), (_, _) => { });
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
        d.Open(0f, 0f, 100f, Items(30), (_, _) => { }); // reopen resets offset even before the next render
        d.Scroll.FirstVisibleAtom.ShouldBe(0);
    }

    [Fact]
    public void CustomEntry_IsTheLastScrollableAtom()
    {
        var d = new DropdownMenuState();
        d.Open(0f, 0f, 100f, Items(12), (_, _) => { }, hasCustomEntry: true, onCustom: () => { });
        d.Scroll.SetExtent(new RectF32(0f, 0f, 100f, 100f), 10f, 13, 1f); // 12 items + custom = 13 atoms

        // Arrow down to the custom entry (index 12) -> it scrolls into view.
        for (var i = 0; i < 13; i++) d.HandleKeyDown(InputKey.Down);
        d.HighlightIndex.ShouldBe(12);
        d.Scroll.FirstVisibleAtom.ShouldBe(3); // 12+1-10
    }
}
