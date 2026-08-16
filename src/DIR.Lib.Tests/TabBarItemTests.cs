using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Covers the item model: <see cref="TabItem{T}"/> and the <see cref="TabBar{TSurface}.Render{T}"/> /
/// <see cref="TabBar{TSurface}.HandleMouseDown{T}(float, float, IReadOnlyList{TabItem{T}})"/> pair that
/// hands a press back as the value it selects rather than as an index the host has to map.
/// </summary>
/// <remarks>
/// Geometry is read the way <see cref="TabBarRegionTests"/> and <see cref="TabBarHoverTests"/> read it —
/// a stub measure oracle of half the font size per character, so the coordinates are arithmetic rather
/// than a baseline that moves with a font update, and the plates are distinct flat colours so a tab's
/// state can be identified by the pixel over it.
/// </remarks>
public class TabBarItemTests
{
    private const float Height = 30f;   // TabBar's BaseHeight at scale 1
    private const float MinTabW = 92f;  // the short titles here all measure under the minimum
    private const float Pad = 10f;
    private const float CloseBox = 16f;
    private const float IconBox = 18f;  // TabBar's BaseIconBox at scale 1

    private static readonly RGBAColor32 Idle = new(0x11, 0x22, 0x33, 0xff);
    private static readonly RGBAColor32 Lifted = new(0x44, 0x55, 0x66, 0xff);
    private static readonly RGBAColor32 Rule = new(0x99, 0x00, 0x99, 0xff);
    private static readonly RGBAColor32 Accent = new(0xff, 0x00, 0x00, 0xff);

    /// <summary>What a tab means here — the point being that it is not an int.</summary>
    private enum Page
    {
        Home,
        Equipment,
        Planner,
        Nowhere,
    }

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
            Colors = new TabBarColors
            {
                InactiveBackground = Idle,
                ActiveBackground = Lifted,
                Separator = Rule,
                ActiveAccent = Accent,
            },
        };
    }

    private static TabItem<Page>[] ThreePages() =>
    [
        new("Home", Page.Home),
        new("Equipment", Page.Equipment),
        new("Planner", Page.Planner),
    ];

    /// <summary>Centre of tab <paramref name="index"/>, which is exactly MinTabW wide here.</summary>
    private static float TabCentre(int index) => (index + 0.5f) * MinTabW;

    /// <summary>Centre of the ✕ inside tab <paramref name="index"/>.</summary>
    private static float CloseCentre(int index) => (index + 1) * MinTabW - Pad * 0.4f - CloseBox * 0.5f;

    [Fact]
    public void APressComesBackAsTheValueItSelects()
    {
        // The whole point of the item overload: no index-to-meaning switch on the host's side, so
        // reordering the strip cannot silently select the wrong page.
        var items = ThreePages();
        var bar = NewBar(new StubRenderer(600, 40));
        bar.Render(contentLeft: 0f, viewportW: 600f, items, Page.Home);

        bar.HandleMouseDown(TabCentre(1), Height * 0.5f, items)
            .ShouldBe(new TabClick<Page>(1, Page.Equipment, Close: false));
    }

    [Fact]
    public void TheCloseButtonReportsTheValueToo()
    {
        var items = ThreePages();
        var bar = NewBar(new StubRenderer(600, 40));
        bar.Render(contentLeft: 0f, viewportW: 600f, items, Page.Home);

        bar.HandleMouseDown(CloseCentre(2), Height * 0.5f, items)
            .ShouldBe(new TabClick<Page>(2, Page.Planner, Close: true));
    }

    [Fact]
    public void TheActiveTabIsMatchedByValueNotByPosition()
    {
        // Resolved through EqualityComparer<T>.Default while rendering. Read from the accent, which is
        // the only thing that distinguishes the active tab from a merely lifted one.
        var renderer = new StubRenderer(600, 40);
        var bar = NewBar(renderer);
        bar.Render(contentLeft: 0f, viewportW: 600f, ThreePages(), Page.Planner);

        AccentedTabs(renderer.Surface).ShouldBe([2]);
    }

    [Fact]
    public void AValueNoItemCarriesLeavesNoTabActive()
    {
        // What a host showing something other than a tab needs — a new-tab page owning the window.
        var renderer = new StubRenderer(600, 40);
        var bar = NewBar(renderer);
        bar.Render(contentLeft: 0f, viewportW: 600f, ThreePages(), Page.Nowhere);

        AccentedTabs(renderer.Surface).ShouldBeEmpty();
    }

    [Fact]
    public void ADisabledTabReportsNoPress()
    {
        var items = new TabItem<Page>[]
        {
            new("Home", Page.Home),
            TabItem<Page>.Disabled("Equipment", Page.Equipment, "connect a profile first"),
            new("Planner", Page.Planner),
        };
        var bar = NewBar(new StubRenderer(600, 40));
        bar.Render(contentLeft: 0f, viewportW: 600f, items, Page.Home);

        bar.HandleMouseDown(TabCentre(1), Height * 0.5f, items).ShouldBeNull();
        bar.HandleMouseDown(TabCentre(2), Height * 0.5f, items)
            .ShouldBe(new TabClick<Page>(2, Page.Planner, Close: false));
    }

    [Fact]
    public void ADisabledTabStatesNoCursorAndOffersNoCloseButton()
    {
        // Nothing on a tab drawn as inert may answer: not the pointer shape that promises a press, and
        // not a ✕ that would be the one live control on it.
        var items = new TabItem<Page>[] { TabItem<Page>.Disabled("Equipment", Page.Equipment) };
        var bar = NewBar(new StubRenderer(600, 40));
        bar.Render(contentLeft: 0f, viewportW: 600f, items, Page.Home);

        bar.HitTestCursor(TabCentre(0), Height * 0.5f).ShouldBeNull();
        bar.HandleMouseDown(CloseCentre(0), Height * 0.5f, items).ShouldBeNull();
    }

    [Fact]
    public void ADisabledTabStillHoldsItsSlot()
    {
        // The reason it registers a region at all. Drop it and the slot walk closes the gap, so every
        // tab after a disabled one answers a drag with the slot of its neighbour.
        var items = new TabItem<Page>[]
        {
            TabItem<Page>.Disabled("Home", Page.Home),
            new("Equipment", Page.Equipment),
            new("Planner", Page.Planner),
        };
        var bar = NewBar(new StubRenderer(600, 40));
        bar.Render(contentLeft: 0f, viewportW: 600f, items, Page.Equipment);

        bar.SlotAt(MinTabW * 0.25f).ShouldBe(0);
        bar.SlotAt(MinTabW * 1.75f).ShouldBe(2);
        bar.SlotAt(999f).ShouldBe(2);
    }

    [Fact]
    public void TheHoveredTabIsReportedSoTheHostCanTooltipIt()
    {
        // The bar resolves it while laying the tabs out; drawing the tooltip is the host's, because it
        // lands outside the strip and the strip clips to itself.
        var bar = NewBar(new StubRenderer(600, 40));
        bar.Pointer = (TabCentre(1), Height * 0.5f);
        bar.Render(contentLeft: 0f, viewportW: 600f, ThreePages(), Page.Home);

        bar.HoveredIndex.ShouldBe(1);
    }

    [Fact]
    public void ADisabledTabIsNeverReportedAsHoveredOrLifted()
    {
        // Or the host tooltips it as though it were live, and its plate lights under a pointer that
        // cannot press it.
        var items = new TabItem<Page>[]
        {
            new("Home", Page.Home),
            TabItem<Page>.Disabled("Equipment", Page.Equipment, "connect a profile first"),
        };
        var renderer = new StubRenderer(600, 40);
        var bar = NewBar(renderer);
        bar.Pointer = (TabCentre(1), Height * 0.5f);
        bar.Render(contentLeft: 0f, viewportW: 600f, items, Page.Home);

        bar.HoveredIndex.ShouldBe(-1);
        TabPlate(renderer.Surface, 1).ShouldBe(Idle);
    }

    [Fact]
    public void AnIconWidensTheTabByAFixedBox()
    {
        // Fixed rather than measured: a pictograph's advance varies by face, so measuring it would make
        // tab width depend on which fallback resolved. The title here is long enough to clear the
        // minimum in both bars, so the difference is the icon's box and nothing else.
        const string longTitle = "Equipment and focusers";

        var plainRenderer = new StubRenderer(600, 40);
        var plain = NewBar(plainRenderer);
        plain.Render(contentLeft: 0f, viewportW: 600f,
            new TabItem<Page>[] { new(longTitle, Page.Equipment) }, Page.Equipment);

        var iconRenderer = new StubRenderer(600, 40);
        var withIcon = NewBar(iconRenderer);
        withIcon.Render(contentLeft: 0f, viewportW: 600f,
            new TabItem<Page>[] { new(longTitle, Page.Equipment) { Icon = "\U0001F52D" } }, Page.Equipment);

        double grew = FirstTabWidth(iconRenderer.Surface) - FirstTabWidth(plainRenderer.Surface);
        grew.ShouldBe(IconBox + Pad * 0.5f, tolerance: 1.5);
    }

    [Fact]
    public void TheTitleOverloadLaysOutExactlyAsItDidBeforeItems()
    {
        // The additive half of the phase: an existing consumer passing titles gets the same strip, since
        // a source with no icons and nothing disabled takes every branch the old code took. Compared as
        // PIXELS — with text stubbed out, what is left on the surface is precisely the geometry.
        var titleRenderer = new StubRenderer(600, 40);
        NewBar(titleRenderer).Render(contentLeft: 0f, viewportW: 600f, ["a", "b", "c"], activeIndex: 1);

        var itemRenderer = new StubRenderer(600, 40);
        NewBar(itemRenderer).Render(contentLeft: 0f, viewportW: 600f,
            new TabItem<Page>[] { new("a", Page.Home), new("b", Page.Equipment), new("c", Page.Planner) },
            Page.Equipment);

        itemRenderer.Surface.Pixels.AsSpan().SequenceEqual(titleRenderer.Surface.Pixels).ShouldBeTrue();
    }

    [Fact]
    public void APressIsNullWhenTheListNoLongerCoversTheIndex()
    {
        // The strip can outlive its model by a frame when the host closes a tab between painting and
        // dispatching. Reporting nothing beats throwing out of an input handler.
        var bar = NewBar(new StubRenderer(600, 40));
        bar.Render(contentLeft: 0f, viewportW: 600f, ThreePages(), Page.Home);

        bar.HandleMouseDown(TabCentre(2), Height * 0.5f,
            new TabItem<Page>[] { new("Home", Page.Home) }).ShouldBeNull();
    }

    [Fact]
    public void ADefaultConstructedItemIsEnabled()
    {
        // A record struct ignores a primary-constructor property initialiser on `new()`, so an
        // `IsEnabled { get; init; } = true` would read correctly and produce a silently unselectable tab
        // for anyone reaching the parameterless form. IsEnabled is stored inverted for exactly this.
        new TabItem<Page>().IsEnabled.ShouldBeTrue();
        default(TabItem<Page>).IsEnabled.ShouldBeTrue();
        new TabItem<Page>("Home", Page.Home).IsEnabled.ShouldBeTrue();
        (new TabItem<Page>("Home", Page.Home) with { IsEnabled = false }).IsEnabled.ShouldBeFalse();
    }

    // RgbaImage exposes its buffer rather than a pixel accessor (see DrawLineTests.IsLit).
    private static RGBAColor32 PixelAt(RgbaImage image, int x, int y)
    {
        var o = (y * image.Width + x) * 4;
        return new RGBAColor32(image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]);
    }

    // Mid-height, and left of the close box, so it reads the tab's own plate rather than the ✕'s.
    private static RGBAColor32 TabPlate(RgbaImage image, int tab) =>
        PixelAt(image, (int)(tab * MinTabW + MinTabW * 0.5f) - (int)CloseBox, (int)(Height * 0.5f));

    /// <summary>Indices of the tabs wearing the active accent, read from the painted strip along its top.</summary>
    private static List<int> AccentedTabs(RgbaImage image)
    {
        var accented = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            if (PixelAt(image, (int)TabCentre(i), 0) == Accent)
            {
                accented.Add(i);
            }
        }

        return accented;
    }

    /// <summary>
    /// Width of the first tab, taken from the rule the bar draws down its trailing edge — the one pixel
    /// on the row that is neither plate colour.
    /// </summary>
    private static int FirstTabWidth(RgbaImage image)
    {
        var y = (int)(Height * 0.5f);
        for (var x = 0; x < image.Width; x++)
        {
            if (PixelAt(image, x, y) == Rule)
            {
                return x + 1;   // the rule occupies the tab's last pixel column
            }
        }

        return 0;
    }
}
