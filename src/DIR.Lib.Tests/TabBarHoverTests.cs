using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Covers <see cref="TabBar.Pointer"/>: that the tab under it lifts, that the tabs either side of it
/// do not, that the ✕ inside it gets a plate of its own, and that a pointer outside the strip's band
/// leaves every tab idle.
/// </summary>
/// <remarks>
/// Read as pixels rather than as a reported index, because there is no index to report — hover is
/// resolved inside <see cref="TabBar.Render"/> and only ever leaves the bar as a colour. Widths come
/// from a stub oracle (half the font size per character), so the coordinates are arithmetic rather
/// than a baseline that moves with a font update, as in <see cref="TabBarNewTabButtonTests"/>.
/// </remarks>
public class TabBarHoverTests
{
    private const float Height = 30f;   // TabBar.BaseHeight at scale 1
    private const float MinTabW = 92f;  // every title here measures under the minimum, so tabs are exactly this
    private const float CloseBox = 16f; // TabBar.BaseCloseBox at scale 1

    private static readonly RGBAColor32 Idle = new(0x11, 0x22, 0x33, 0xff);
    private static readonly RGBAColor32 Lifted = new(0x44, 0x55, 0x66, 0xff);
    private static readonly RGBAColor32 Plate = new(0x99, 0x00, 0x99, 0xff);

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

    private static TabBar NewBar()
    {
        const string path = "stub.ttf";
        return new TabBar(path, new FontFallbackResolver(path, []))
        {
            // Distinct flat colours so a plate can be identified by the pixel under it. Separator is
            // the ✕ plate, and is deliberately not a tone of either tab surface here.
            Colors = new TabBarColors
            {
                InactiveBackground = Idle,
                ActiveBackground = Lifted,
                Separator = Plate,
            },
        };
    }

    // Mid-height, and left of the close box, so it reads the tab's own plate rather than the ✕'s.
    private static RGBAColor32 TabPlate(RgbaImage image, int tab) =>
        PixelAt(image, (int)(tab * MinTabW + MinTabW * 0.5f) - (int)CloseBox, (int)(Height * 0.5f));

    [Fact]
    public void LiftsTheTabUnderThePointerAndNoOther()
    {
        var bar = NewBar();
        var renderer = new StubRenderer(600, 40);
        // Tab 1 hovered, tab 0 active (so its own lift proves nothing), tab 2 idle.
        bar.Pointer = (MinTabW * 1.5f, Height * 0.5f);
        bar.Render(renderer, contentLeft: 0f, viewportW: 600f, ["a", "b", "c"], activeIndex: 0);

        TabPlate(renderer.Surface, 1).ShouldBe(Lifted);
        TabPlate(renderer.Surface, 2).ShouldBe(Idle);
    }

    [Fact]
    public void LeavesEveryTabIdleWithNoPointer()
    {
        var bar = NewBar();
        var renderer = new StubRenderer(600, 40);
        bar.Pointer = null;
        bar.Render(renderer, contentLeft: 0f, viewportW: 600f, ["a", "b"], activeIndex: 0);

        TabPlate(renderer.Surface, 1).ShouldBe(Idle);
    }

    [Fact]
    public void IgnoresAPointerBelowTheStrip()
    {
        // The host hands over the window-wide pointer, so most of the time it is over the page. A bar
        // that only compared x would keep a tab lit for the whole session.
        var bar = NewBar();
        var renderer = new StubRenderer(600, 40);
        bar.Pointer = (MinTabW * 1.5f, Height + 12f);
        bar.Render(renderer, contentLeft: 0f, viewportW: 600f, ["a", "b"], activeIndex: 0);

        TabPlate(renderer.Surface, 1).ShouldBe(Idle);
    }

    [Fact]
    public void HoverFollowsTheTabsWhenOneIsClosed()
    {
        // The point of taking a position rather than an index: after a close, the tab under an
        // unmoved pointer is a different one, and the bar must re-resolve it in the same frame it
        // relays the strip out. A host-supplied index would still name the tab that has gone.
        var bar = NewBar();
        var renderer = new StubRenderer(600, 40);
        bar.Pointer = (MinTabW * 2.5f, Height * 0.5f);   // over tab 2 of three

        bar.Render(renderer, contentLeft: 0f, viewportW: 600f, ["a", "b", "c"], activeIndex: 0);
        TabPlate(renderer.Surface, 2).ShouldBe(Lifted);

        // Tab 2 closes. Nothing is under the pointer now, and tab 1 must NOT have inherited the lift.
        renderer = new StubRenderer(600, 40);
        bar.Render(renderer, contentLeft: 0f, viewportW: 600f, ["a", "b"], activeIndex: 0);
        TabPlate(renderer.Surface, 1).ShouldBe(Idle);
    }

    [Fact]
    public void PlatesTheCloseButtonUnderThePointer()
    {
        var bar = NewBar();
        var renderer = new StubRenderer(600, 40);
        // Centre of tab 0's ✕: its right edge sits Pad*0.4 in from the tab's, and the box is CloseBox wide.
        var closeCentre = MinTabW - 10f * 0.4f - CloseBox * 0.5f;
        bar.Pointer = (closeCentre, Height * 0.5f);
        bar.Render(renderer, contentLeft: 0f, viewportW: 600f, ["a", "b"], activeIndex: 1);

        PixelAt(renderer.Surface, (int)closeCentre, (int)(Height * 0.5f)).ShouldBe(Plate);
        // The tab itself is lifted, not plated — the ✕'s mark is a separate, smaller target.
        TabPlate(renderer.Surface, 0).ShouldBe(Lifted);
    }

    [Fact]
    public void DoesNotPlateTheCloseButtonOfAnUnhoveredTab()
    {
        var bar = NewBar();
        var renderer = new StubRenderer(600, 40);
        bar.Pointer = (MinTabW * 0.5f, Height * 0.5f);   // over tab 0's body, not its ✕
        bar.Render(renderer, contentLeft: 0f, viewportW: 600f, ["a", "b"], activeIndex: 1);

        var otherClose = MinTabW * 2 - 10f * 0.4f - CloseBox * 0.5f;
        PixelAt(renderer.Surface, (int)otherClose, (int)(Height * 0.5f)).ShouldNotBe(Plate);
    }

    [Fact]
    public void HoversTheNewTabButtonFromThePointerToo()
    {
        // NewTabHovered predates Pointer and still works; a host setting neither used to get no hover
        // on the + at all.
        var bar = NewBar();
        bar.ShowNewTabButton = true;
        var renderer = new StubRenderer(600, 40);
        bar.Pointer = (MinTabW + Height * 0.5f, Height * 0.5f);
        bar.Render(renderer, contentLeft: 0f, viewportW: 600f, ["a"], activeIndex: 0);

        // Just inside the button's left edge, clear of the + mark's arms at its centre.
        PixelAt(renderer.Surface, (int)MinTabW + 3, (int)(Height * 0.5f)).ShouldBe(Lifted);
    }

    // RgbaImage exposes its buffer rather than a pixel accessor (see DrawLineTests.IsLit).
    private static RGBAColor32 PixelAt(RgbaImage image, int x, int y)
    {
        var o = (y * image.Width + x) * 4;
        return new RGBAColor32(image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]);
    }
}
