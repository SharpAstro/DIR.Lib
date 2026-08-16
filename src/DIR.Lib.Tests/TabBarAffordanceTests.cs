using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Covers <see cref="TabBar{TSurface}.CanCloseTabs"/> and
/// <see cref="TabBar{TSurface}.CanReorderTabs"/>: switching either off removes the affordance rather
/// than leaving it drawn and inert, and both default to what the strip has always done.
/// </summary>
public class TabBarAffordanceTests
{
    private const float Height = 30f;
    private const float MinTabW = 92f;
    private const float Pad = 10f;
    private const float CloseBox = 16f;

    private enum Page
    {
        Home,
        Equipment,
        Planner,
    }

    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    private static readonly RGBAColor32 Rule = new(0x99, 0x00, 0x99, 0xff);

    private static TabBar<RgbaImage> NewBar(Renderer<RgbaImage> renderer)
    {
        const string path = "stub.ttf";
        return new TabBar<RgbaImage>(renderer)
        {
            FontPath = path,
            FontFallback = new FontFallbackResolver(path, []),
            Colors = new TabBarColors { Separator = Rule },
        };
    }

    private static TabItem<Page>[] ThreePages() =>
    [
        new("Home", Page.Home),
        new("Equipment", Page.Equipment),
        new("Planner", Page.Planner),
    ];

    [Fact]
    public void ClosingOffDrawsNoCloseButtonAndReportsNoCloseHit()
    {
        var items = ThreePages();
        var bar = NewBar(new StubRenderer(600, 40));
        bar.CanCloseTabs = false;
        bar.Render(contentStart: 0f, viewportEnd: 600f, items, Page.Home);

        // Sweep the whole first tab: nothing in it may report a close.
        for (var x = 0; x < 80; x += 2)
        {
            bar.HandleMouseDown(x, Height * 0.5f, items)?.Close.ShouldBeFalse();
        }
    }

    [Fact]
    public void ClosingOffAlsoStopsReservingTheBoxSoTabsAreNarrower()
    {
        // A strip whose tabs cannot be closed must not hold a gap where the control would have been.
        // The titles here are long enough to clear the minimum in both bars, so the difference is the
        // close box and nothing else.
        const string longTitle = "Equipment and focusers";
        var items = new TabItem<Page>[] { new(longTitle, Page.Equipment) };

        var withClose = new StubRenderer(600, 40);
        NewBar(withClose).Render(contentStart: 0f, viewportEnd: 600f, items, Page.Equipment);

        var noCloseRenderer = new StubRenderer(600, 40);
        var noClose = NewBar(noCloseRenderer);
        noClose.CanCloseTabs = false;
        noClose.Render(contentStart: 0f, viewportEnd: 600f, items, Page.Equipment);

        double shrank = FirstTabWidth(withClose.Surface) - FirstTabWidth(noCloseRenderer.Surface);
        shrank.ShouldBe(CloseBox, tolerance: 1.5);
    }

    [Fact]
    public void ReorderingOffNominatesNoSlot()
    {
        // The bar never reorders anything itself -- it nominates the slot a host would drop into, so
        // declining to nominate one is the whole mechanism.
        var bar = NewBar(new StubRenderer(600, 40));
        bar.CanReorderTabs = false;
        bar.Render(contentStart: 0f, viewportEnd: 600f, ThreePages(), Page.Home);

        bar.SlotAt(MinTabW * 0.25f).ShouldBe(-1);
        bar.SlotAt(MinTabW * 1.75f).ShouldBe(-1);
        bar.SlotAt(999f).ShouldBe(-1);
    }

    [Fact]
    public void ReorderingOffStillLetsATabBePressed()
    {
        // Pinning that the two affordances are independent: a strip you cannot rearrange is still a
        // strip you can navigate with.
        var items = ThreePages();
        var bar = NewBar(new StubRenderer(600, 40));
        bar.CanReorderTabs = false;
        bar.Render(contentStart: 0f, viewportEnd: 600f, items, Page.Home);

        bar.HandleMouseDown(MinTabW * 1.5f, Height * 0.5f, items)
            .ShouldBe(new TabClick<Page>(1, Page.Equipment, Close: false));
    }

    [Fact]
    public void BothDefaultToWhatTheStripHasAlwaysDone()
    {
        var items = ThreePages();
        var bar = NewBar(new StubRenderer(600, 40));

        bar.CanCloseTabs.ShouldBeTrue();
        bar.CanReorderTabs.ShouldBeTrue();

        bar.Render(contentStart: 0f, viewportEnd: 600f, items, Page.Home);

        var closeCentre = MinTabW - Pad * 0.4f - CloseBox * 0.5f;
        bar.HandleMouseDown(closeCentre, Height * 0.5f, items)
            .ShouldBe(new TabClick<Page>(0, Page.Home, Close: true));
        bar.SlotAt(MinTabW * 0.25f).ShouldBe(0);
    }

    // RgbaImage exposes its buffer rather than a pixel accessor (see DrawLineTests.IsLit).
    private static RGBAColor32 PixelAt(RgbaImage image, int x, int y)
    {
        var o = (y * image.Width + x) * 4;
        return new RGBAColor32(image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]);
    }

    /// <summary>Width of the first tab, from the rule the bar draws down its trailing edge.</summary>
    private static int FirstTabWidth(RgbaImage image)
    {
        var y = (int)(Height * 0.5f);
        for (var x = 0; x < image.Width; x++)
        {
            if (PixelAt(image, x, y) == Rule)
            {
                return x + 1;
            }
        }

        return 0;
    }
}
