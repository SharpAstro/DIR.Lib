using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Covers <see cref="TabBar{TSurface}.Side"/> and <see cref="TabBar{TSurface}.Sizing"/>: that a strip
/// laid down an edge advances its tabs on the other axis, that the accent follows the OUTER edge while
/// the bar's own rule takes the opposite one, and that a uniform strip is a square nav rail rather than
/// a row of label-width cells stood on end.
/// </summary>
/// <remarks>
/// Read from the painted pixels with distinct flat plate colours, as <see cref="TabBarHoverTests"/>
/// does — the point of these is WHERE each piece landed, and an arranged rect would only report what
/// the code computed rather than what it drew.
/// </remarks>
public class TabBarSideTests
{
    private const float Thickness = 30f;   // TabBar's BaseHeight at scale 1
    private const float MinTabW = 92f;     // the one-char titles here measure well under the minimum
    private const int Border = 1;          // TabBar.Border at scale 1
    private const int AccentThickness = Border * 2;

    private static readonly RGBAColor32 Idle = new(0x11, 0x22, 0x33, 0xff);
    private static readonly RGBAColor32 Lifted = new(0x44, 0x55, 0x66, 0xff);
    private static readonly RGBAColor32 Rule = new(0x99, 0x00, 0x99, 0xff);
    private static readonly RGBAColor32 Accent = new(0xff, 0x00, 0x00, 0xff);

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

    private static TabBar<RgbaImage> NewBar(Renderer<RgbaImage> renderer, TabStripSide side,
        TabSizing sizing = TabSizing.Content)
    {
        const string path = "stub.ttf";
        return new TabBar<RgbaImage>(renderer)
        {
            FontPath = path,
            FontFallback = new FontFallbackResolver(path, []),
            Side = side,
            Sizing = sizing,
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
        new("a", Page.Home),
        new("b", Page.Equipment),
        new("c", Page.Planner),
    ];

    [Fact]
    public void ALeftStripAdvancesItsTabsDownwards()
    {
        // Uniform, so each cell is the strip's own thickness: tab n occupies y in [n*T, (n+1)*T).
        var renderer = new StubRenderer(200, 300);
        var bar = NewBar(renderer, TabStripSide.Left, TabSizing.Uniform);
        bar.Render(new RectF32(0f, 0f, Thickness, 300f), ThreePages(), Page.Equipment);

        // The active tab is the second cell down, not the second across.
        bar.HandleMouseDown(Thickness * 0.5f, Thickness * 1.5f, ThreePages())
            .ShouldBe(new TabClick<Page>(1, Page.Equipment, Close: false));
        bar.HandleMouseDown(Thickness * 0.5f, Thickness * 2.5f, ThreePages())
            .ShouldBe(new TabClick<Page>(2, Page.Planner, Close: false));

        // And nothing is laid out along x past the strip's thickness.
        bar.HandleMouseDown(Thickness * 1.5f, Thickness * 0.5f, ThreePages()).ShouldBeNull();
    }

    [Theory]
    [InlineData(TabStripSide.Top)]
    [InlineData(TabStripSide.Bottom)]
    [InlineData(TabStripSide.Left)]
    [InlineData(TabStripSide.Right)]
    public void TheAccentTakesTheOuterEdgeAndTheBarRulesTheOppositeOne(TabStripSide side)
    {
        // One flag places both, so they can never end up on the same edge -- which is the failure that
        // would make a strip look like it had no accent at all, the rule sitting on top of it.
        var vertical = side is TabStripSide.Left or TabStripSide.Right;
        var bounds = vertical ? new RectF32(0f, 0f, Thickness, 300f) : new RectF32(0f, 0f, 300f, Thickness);

        var renderer = new StubRenderer(320, 320);
        var bar = NewBar(renderer, side, TabSizing.Uniform);
        bar.Render(bounds, ThreePages(), Page.Home);

        // Sample across the strip's thickness, at the middle of the ACTIVE (first) tab along the flow.
        var alongFlow = (int)(Thickness * 0.5f);
        RGBAColor32 At(int crossOffset) => vertical
            ? PixelAt(renderer.Surface, crossOffset, alongFlow)
            : PixelAt(renderer.Surface, alongFlow, crossOffset);

        var outerAtStart = side is TabStripSide.Top or TabStripSide.Left;
        var accentAt = outerAtStart ? 0 : (int)Thickness - AccentThickness;
        var ruleAt = outerAtStart ? (int)Thickness - Border : 0;

        At(accentAt).ShouldBe(Accent);
        At(ruleAt).ShouldBe(Rule);
        accentAt.ShouldNotBe(ruleAt);
    }

    [Fact]
    public void AUniformTabIsASquareOfTheStripThicknessNotTheLabelWidth()
    {
        // The reason Uniform exists rather than being a preference. Content sizing on a vertical strip
        // would set a tab's HEIGHT from the WIDTH of its label -- here MinTabW, three times the cell.
        var renderer = new StubRenderer(200, 400);
        var bar = NewBar(renderer, TabStripSide.Left, TabSizing.Uniform);
        bar.Render(new RectF32(0f, 0f, Thickness, 400f), ThreePages(), Page.Home);

        // The rule between tab 0 and tab 1 sits on tab 0's trailing (bottom) edge.
        FirstRuleRowDownColumn(renderer.Surface, x: (int)(Thickness * 0.5f)).ShouldBe((int)Thickness - Border);
    }

    [Fact]
    public void AContentSizedVerticalStripStillMeasuresItsLabels()
    {
        // Uniform is a choice, not something Left forces -- a caller that wants label-sized cells on a
        // vertical strip still gets them, and this is what the default would do.
        var renderer = new StubRenderer(200, 400);
        var bar = NewBar(renderer, TabStripSide.Left);
        bar.Render(new RectF32(0f, 0f, Thickness, 400f), ThreePages(), Page.Home);

        FirstRuleRowDownColumn(renderer.Surface, x: (int)(Thickness * 0.5f)).ShouldBe((int)MinTabW - Border);
    }

    [Fact]
    public void AUniformTabOffersNoCloseButton()
    {
        // There is no room beside a centred mark for one, and a ✕ drawn under the icon would be a
        // control the cell never reserved space for.
        var items = ThreePages();
        var renderer = new StubRenderer(200, 300);
        var bar = NewBar(renderer, TabStripSide.Left, TabSizing.Uniform);
        bar.Render(new RectF32(0f, 0f, Thickness, 300f), items, Page.Home);

        for (var y = 0; y < (int)Thickness; y++)
        {
            bar.HandleMouseDown(Thickness * 0.5f, y, items)?.Close.ShouldBeFalse();
        }
    }

    [Fact]
    public void SlotAtWalksTheFlowAxis()
    {
        // A drag down a rail reorders by the tab midpoints on Y, and reading X there would answer from
        // a coordinate every tab shares.
        var renderer = new StubRenderer(200, 300);
        var bar = NewBar(renderer, TabStripSide.Left, TabSizing.Uniform);
        bar.Render(new RectF32(0f, 0f, Thickness, 300f), ThreePages(), Page.Home);

        bar.SlotAt(Thickness * 0.25f).ShouldBe(0);   // above tab 0's midpoint
        bar.SlotAt(Thickness * 0.75f).ShouldBe(1);   // below it -- the drop lands after
        bar.SlotAt(Thickness * 2.75f).ShouldBe(2);
        bar.SlotAt(999f).ShouldBe(2);
    }

    [Fact]
    public void HoverReadsTheFlowAxisAndIsGatedByTheCrossBand()
    {
        // The pointer arrives window-wide, so the band test is what stops a rail lighting a tab while
        // the cursor is out over the content beside it.
        var renderer = new StubRenderer(200, 300);
        var bar = NewBar(renderer, TabStripSide.Left, TabSizing.Uniform);

        bar.Pointer = (Thickness * 0.5f, Thickness * 2.5f);
        bar.Render(new RectF32(0f, 0f, Thickness, 300f), ThreePages(), Page.Home);
        bar.HoveredIndex.ShouldBe(2);

        // Same flow position, but out past the strip's thickness.
        bar.Pointer = (Thickness * 3f, Thickness * 2.5f);
        bar.Render(new RectF32(0f, 0f, Thickness, 300f), ThreePages(), Page.Home);
        bar.HoveredIndex.ShouldBe(-1);
    }

    [Fact]
    public void AStripPlacedAwayFromTheOriginKeepsItsGeometry()
    {
        // The rect overload exists for Bottom and Right, which have to be told where the far edge is --
        // so a strip whose cross origin is not 0 has to land there rather than at the top left.
        const float top = 40f;
        var items = ThreePages();
        var renderer = new StubRenderer(200, 400);
        var bar = NewBar(renderer, TabStripSide.Left, TabSizing.Uniform);
        bar.Render(new RectF32(0f, top, Thickness, 300f), items, Page.Home);

        bar.HandleMouseDown(Thickness * 0.5f, top + Thickness * 0.5f, items)
            .ShouldBe(new TabClick<Page>(0, Page.Home, Close: false));
        bar.HandleMouseDown(Thickness * 0.5f, top - 10f, items).ShouldBeNull();
    }

    [Fact]
    public void TheRectOverloadAndTheTwoFloatOverloadAgreeForATopStrip()
    {
        // The convenience form is the rect form with cross origin 0 and Height for thickness; if the two
        // ever disagreed, every existing consumer would be on the one that is not tested elsewhere.
        var byFloats = new StubRenderer(600, 40);
        NewBar(byFloats, TabStripSide.Top).Render(contentStart: 0f, viewportEnd: 600f, ThreePages(), Page.Equipment);

        var byRect = new StubRenderer(600, 40);
        NewBar(byRect, TabStripSide.Top).Render(new RectF32(0f, 0f, 600f, Thickness), ThreePages(), Page.Equipment);

        byRect.Surface.Pixels.AsSpan().SequenceEqual(byFloats.Surface.Pixels).ShouldBeTrue();
    }

    // RgbaImage exposes its buffer rather than a pixel accessor (see DrawLineTests.IsLit).
    private static RGBAColor32 PixelAt(RgbaImage image, int x, int y)
    {
        var o = (y * image.Width + x) * 4;
        return new RGBAColor32(image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]);
    }

    /// <summary>Row of the first inter-tab rule going down <paramref name="x"/>, i.e. tab 0's extent.</summary>
    private static int FirstRuleRowDownColumn(RgbaImage image, int x)
    {
        for (var y = 0; y < image.Height; y++)
        {
            if (PixelAt(image, x, y) == Rule)
            {
                return y;
            }
        }

        return -1;
    }
}
