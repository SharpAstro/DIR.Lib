using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The strip reports presses from the regions it registered while painting, rather than from a private
/// copy of the layout it happened to keep — <see cref="TabBar{TSurface}.HandleMouseDown"/>,
/// <see cref="TabBar{TSurface}.HitNewTabButton"/> and <see cref="TabBar{TSurface}.SlotAt"/>, all reading
/// the same rects the tabs were drawn in.
/// </summary>
/// <remarks>
/// Two properties come with that and are worth pinning, because the hand-rolled version had neither. The
/// ✕ is registered after the tab it sits in, so it wins the hit the way an inner control should; and the
/// whole strip goes quiet on a frame the host did not draw it in (<see cref="WindowUiSettings.FrameId"/>)
/// — which for a tab bar is not academic, since a window carrying a torn-out tab paints itself as a chip
/// and no strip at all.
/// </remarks>
public class TabBarRegionTests
{
    private const float Height = 30f;   // TabBar's BaseHeight at scale 1
    private const float MinTabW = 92f;  // every title here measures under the minimum
    private const float CloseBox = 16f;
    private const float Pad = 10f;

    /// <summary>Half the font size per char, and no glyph rasterization — this is a geometry test.</summary>
    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    private static TabBar<RgbaImage> NewBar(Renderer<RgbaImage> renderer)
    {
        const string path = "stub.ttf";
        return new TabBar<RgbaImage>(renderer)
        {
            FontPath = path,
            FontFallback = new FontFallbackResolver(path, []),
        };
    }

    private static TabBar<RgbaImage> ThreeTabs()
    {
        var bar = NewBar(new StubRenderer(600, 40));
        bar.Render(contentLeft: 0f, viewportW: 600f, ["a", "b", "c"], activeIndex: 0);
        return bar;
    }

    /// <summary>Centre of the ✕ inside tab <paramref name="index"/>: its right edge sits Pad*0.4 in from
    /// the tab's, and the box is CloseBox wide.</summary>
    private static float CloseCentre(int index) => (index + 1) * MinTabW - Pad * 0.4f - CloseBox * 0.5f;

    [Fact]
    public void APressOnATabReportsItsIndex()
    {
        var bar = ThreeTabs();

        bar.HandleMouseDown(MinTabW * 0.5f, Height * 0.5f).ShouldBe(new TabBar<RgbaImage>.TabClick(0, Close: false));
        bar.HandleMouseDown(MinTabW * 1.5f, Height * 0.5f).ShouldBe(new TabBar<RgbaImage>.TabClick(1, Close: false));
    }

    [Fact]
    public void TheCloseButtonBeatsTheTabItSitsIn()
    {
        // Both regions cover this point; the ✕ is registered second, and the hit test resolves
        // last-registered-first. Get the order backwards and every press on a ✕ activates the tab.
        var bar = ThreeTabs();

        bar.HandleMouseDown(CloseCentre(1), Height * 0.5f).ShouldBe(new TabBar<RgbaImage>.TabClick(1, Close: true));
    }

    [Fact]
    public void APressBelowTheStripIsNotATab()
    {
        // The regions are the bar's own height, so the page underneath is not the bar's to claim.
        ThreeTabs().HandleMouseDown(MinTabW * 0.5f, Height + 4f).ShouldBeNull();
    }

    [Fact]
    public void EmptyBarSpaceIsNotATab()
    {
        ThreeTabs().HandleMouseDown(MinTabW * 3 + 20f, Height * 0.5f).ShouldBeNull();
    }

    [Fact]
    public void SlotAtCrossesAtTheTabMidpoints()
    {
        var bar = ThreeTabs();

        bar.SlotAt(MinTabW * 0.25f).ShouldBe(0);    // left half of tab 0
        bar.SlotAt(MinTabW * 0.75f).ShouldBe(1);    // right half — the drop lands after it
        bar.SlotAt(MinTabW * 2.75f).ShouldBe(2);
        bar.SlotAt(999f).ShouldBe(2);               // past every tab clamps to the last slot
    }

    [Fact]
    public void SlotAtIsMinusOneWithNoTabs()
    {
        var bar = NewBar(new StubRenderer(600, 40));
        bar.Render(contentLeft: 0f, viewportW: 600f, [], activeIndex: -1);

        bar.SlotAt(10f).ShouldBe(-1);
    }

    [Fact]
    public void TheNewTabButtonIsNotCountedAsASlot()
    {
        // It registers a region in the same strip, so a slot walk that counted every region would let a
        // drag drop a tab "into" the + and index one past the end of the session list.
        var bar = NewBar(new StubRenderer(600, 40));
        bar.ShowNewTabButton = true;
        bar.Render(contentLeft: 0f, viewportW: 600f, ["a", "b"], activeIndex: 0);

        bar.SlotAt(999f).ShouldBe(1);
    }

    [Fact]
    public void ATabStatesThePointerCursor()
    {
        ThreeTabs().HitTestCursor(MinTabW * 0.5f, Height * 0.5f).ShouldBe(CursorKind.Pointer);
    }

    [Fact]
    public void AStripTheHostStoppedDrawingReportsNothing()
    {
        // The real case: this window is now carrying a torn-out tab, so it paints itself as a chip and
        // draws no strip. Every answer below would otherwise come from the layout of a bar that is no
        // longer on screen — and HandleMouseDown's would name a tab by an index into a list that has
        // since lost one.
        var bar = NewBar(new StubRenderer(600, 40));
        bar.ShowNewTabButton = true;
        bar.Ui.FrameId = 1;
        bar.Render(contentLeft: 0f, viewportW: 600f, ["a", "b"], activeIndex: 0);

        bar.Ui.FrameId = 2;

        bar.HandleMouseDown(MinTabW * 0.5f, Height * 0.5f).ShouldBeNull();
        bar.HitNewTabButton(MinTabW * 2 + Height * 0.5f, Height * 0.5f).ShouldBeFalse();
        bar.SlotAt(MinTabW * 0.25f).ShouldBe(-1);
        bar.HitTestCursor(MinTabW * 0.5f, Height * 0.5f).ShouldBeNull();
    }
}
