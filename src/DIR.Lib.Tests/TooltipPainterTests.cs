using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Builder.Tooltip"/>, the one tooltip painter, and the tab strip's tooltips, which now reach it
/// through the region model rather than through a host reading <see cref="TabBar{TSurface}.HoveredIndex"/> and
/// placing a box of its own.
/// </summary>
/// <remarks>
/// Geometry through <see cref="DeclarationStubRenderer"/>, which measures half the font size a character and the
/// font size tall, so every rect below is arithmetic: "Cool" at 10 is 20 x 10, padded 6 and 3 to 32 x 16, and a
/// one-unit border makes the box 34 x 18.
/// </remarks>
public class TooltipPainterTests
{
    private const float Area = 200f;

    private static Layout.TooltipStyle Style => new(
        Background: new RGBAColor32(0x20, 0x20, 0x20, 0xff),
        Text: new RGBAColor32(0xee, 0xee, 0xee, 0xff),
        Border: new RGBAColor32(0x80, 0x80, 0x80, 0xff),
        FontSize: 10f);

    /// <summary>Where the box lands for <paramref name="request"/> in a 200 x 200 window.</summary>
    private static Rect<float> Box(TooltipRequest request)
    {
        var widget = new DeclarationWidget(new DeclarationStubRenderer((uint)Area, (uint)Area));
        // Depth 1 is the box: Arrange places the Anchored root itself at the bounds it was given.
        return Layout.Engine.Arrange(Layout.Builder.Tooltip(request, Style), new Rect<float>(0, 0, Area, Area), widget.Context())
            .First(a => a.Depth == 1)
            .Bounds;
    }

    [Fact]
    public void TheBoxIsItsTextPaddedAndBorderedAndSitsCentredBelowTheRegionByDefault()
    {
        var box = Box(new TooltipRequest("Cool", new RectF32(50f, 20f, 30f, 10f)));

        box.Width.ShouldBe(34f);
        box.Height.ShouldBe(18f);
        box.X.ShouldBe(48f, "centred under the region: 50 + (30 - 34) / 2");
        box.Y.ShouldBe(34f, "below the region, with the margin between");
    }

    /// <summary>The old rail tooltip's placement, centred on the tab, now the engine's rather than the host's arithmetic.</summary>
    [Fact]
    public void ARequestForTheRightOpensBesideTheRegionLevelWithItsMiddle()
    {
        var box = Box(new TooltipRequest("Cool", new RectF32(0f, 50f, 40f, 40f)) { Side = Layout.DockSide.Right });

        box.X.ShouldBe(44f, "past the region's right edge, with the margin between");
        box.Y.ShouldBe(61f, "centred on the region: 50 + (40 - 18) / 2");
    }

    [Fact]
    public void ABoxThatWouldRunOffTheWindowIsKeptOnIt()
    {
        var box = Box(new TooltipRequest("Cool", new RectF32(180f, 20f, 15f, 10f)));

        box.X.ShouldBe(Area - 4f - 34f, "clamped inside the window, the margin from its edge");
    }

    /// <summary>
    /// Below a region at the bottom of the window the box has no room, and the clamp alone would slide it back up
    /// over the region the pointer rests on; it opens above instead.
    /// </summary>
    [Fact]
    public void ABoxWithNoRoomBelowItsRegionOpensAbove()
    {
        var box = Box(new TooltipRequest("Cool", new RectF32(50f, 185f, 30f, 10f)));

        box.Y.ShouldBe(185f - 4f - 18f, "above the region, with the margin between");
    }

    [Fact]
    public void ABoxOfContentIsFramedAndPlacedByTheSameRule()
    {
        var widget = new DeclarationWidget(new DeclarationStubRenderer((uint)Area, (uint)Area));
        var lines = Layout.Builder.VStack(
            Layout.Builder.Text("21:00", 10f),
            Layout.Builder.Text("Cloud 40%", 10f));

        var box = Layout.Engine.Arrange(Layout.Builder.Tooltip(new RectF32(50f, 20f, 30f, 10f), lines, Style),
                new Rect<float>(0, 0, Area, Area), widget.Context())
            .First(a => a.Depth == 1)
            .Bounds;

        box.Width.ShouldBe(45f + 12f + 2f, "the widest line, padded and bordered");
        box.Height.ShouldBe(20f + 6f + 2f, "both lines, padded and bordered");
        box.Y.ShouldBe(34f);
    }

    /// <summary>
    /// A tooltip that answered presses, lit on hover or registered a region would take the pointer from the
    /// region that asked for it, and the router would then resolve no tooltip at all.
    /// </summary>
    [Fact]
    public void PaintingATooltipRegistersNothing()
    {
        var widget = new DeclarationWidget(new DeclarationStubRenderer((uint)Area, (uint)Area));

        widget.Render(Layout.Builder.Tooltip(new TooltipRequest("Cool", new RectF32(50f, 20f, 30f, 10f)), Style),
            new RectF32(0, 0, Area, Area));

        widget.GetRegisteredRegions().ShouldBeEmpty();
    }

    /// <summary>
    /// A tab's tooltip is declared on its own region and opens away from the strip: right of a strip down the
    /// left edge, which is where a host used to place it with arithmetic of its own from the hovered index.
    /// </summary>
    [Theory]
    [InlineData(TabStripSide.Left, Layout.DockSide.Right)]
    [InlineData(TabStripSide.Top, Layout.DockSide.Bottom)]
    [InlineData(TabStripSide.Right, Layout.DockSide.Left)]
    [InlineData(TabStripSide.Bottom, Layout.DockSide.Top)]
    public void ATabsTooltipRidesOnItsRegionAndOpensAwayFromTheStrip(TabStripSide side, Layout.DockSide opens)
    {
        var bar = new TabBar<RgbaImage>(new DeclarationStubRenderer(400, 400))
        {
            FontPath = "stub.ttf",
            FontFallback = new FontFallbackResolver("stub.ttf", []),
            Side = side,
        };
        TabItem<int>[] items =
        [
            new("Home", 0) { Tooltip = "Home (Ctrl+H)" },
            new("Equipment", 1),
            TabItem<int>.Disabled("Guider", 2, "Connect a guider first"),
        ];
        var vertical = side is TabStripSide.Left or TabStripSide.Right;

        bar.Render(vertical ? new RectF32(0f, 0f, 40f, 400f) : new RectF32(0f, 0f, 400f, 30f), items, 0);

        var tabs = bar.GetRegisteredRegions()
            .Where(r => r.Result is HitResult.ListItemHit { ListId: TabBarRegions.Tabs or TabBarRegions.DisabledTabs })
            .ToDictionary(r => ((HitResult.ListItemHit)r.Result).Index);
        tabs[0].Tooltip.ShouldBe("Home (Ctrl+H)");
        tabs[0].TooltipSide.ShouldBe(opens);
        tabs[1].Tooltip.ShouldBeNull("a tab that states nothing says nothing");
        tabs[2].Tooltip.ShouldBe("Connect a guider first", "a disabled tab says why");
        tabs[2].TooltipSide.ShouldBe(opens);
    }
}
