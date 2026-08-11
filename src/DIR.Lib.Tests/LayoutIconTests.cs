using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Pins <see cref="Layout.Content.Icon"/> on the PIXEL side: the painter constructs each kind from
/// rectangles, so these assert real pixels rather than that a method was called. The cell side is pinned
/// separately in Console.Lib, which is the point of naming icons by meaning: one node, two drawings.
/// </summary>
public class LayoutIconTests
{
    private const uint Surface = 32;

    private static readonly RGBAColor32 Ink = new(0xff, 0xff, 0xff, 0xff);

    private sealed class IconWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public ClickableRegion[] Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: string.Empty, dpiScale: 1f);
            return GetRegisteredRegions();
        }
    }

    /// <summary>Paints one icon filling the whole surface and hands back an (x,y) -> is-there-ink probe.</summary>
    private static (Func<int, int, bool> Inked, ClickableRegion[] Regions) Paint(
        Layout.IconKind kind, float size = (float)Surface)
    {
        var renderer = new RgbaImageRenderer(Surface, Surface);
        var widget = new IconWidget(renderer);
        var node = Layout.Builder.Icon(kind, size, Ink).Clickable(new HitResult.ButtonHit("icon"));

        var regions = widget.Render(node, new RectF32(0, 0, Surface, Surface));

        var pixels = renderer.Surface.Pixels;
        return ((x, y) => pixels[(y * (int)Surface + x) * 4 + 3] > 0, regions);
    }

    [Fact]
    public void AnIconIsSquareAtItsDeclaredSize_AndBindsItsClickToThatRect()
    {
        var renderer = new RgbaImageRenderer(Surface, Surface);
        var widget = new IconWidget(renderer);

        // NESTED, deliberately: Arrange places the ROOT at the full bounds, so a root leaf reports the
        // surface size and would say nothing about what the icon measured. Inside a stack beside a Star
        // sibling, its Auto width is the intrinsic one -- and the hit region is bound to that same rect.
        var icon = Layout.Builder.Icon(Layout.IconKind.Grid, 16f, Ink)
            .Clickable(new HitResult.ButtonHit("icon"));
        var root = Layout.Builder.HStack(icon, Layout.Builder.Spacer().WStar());

        var region = widget.Render(root, new RectF32(0, 0, Surface, Surface)).ShouldHaveSingleItem();

        region.Width.ShouldBe(16f, 0.5f);
        region.Height.ShouldBe(16f, 0.5f);
    }

    [Fact]
    public void TheGridIconIsFourQuadrants_SeparatedByAnEmptyGutter()
    {
        var (inked, _) = Paint(Layout.IconKind.Grid);

        // The four cell centres carry ink...
        foreach (var (x, y) in new[] { (7, 7), (24, 7), (7, 24), (24, 24) })
        {
            inked(x, y).ShouldBeTrue($"quadrant at ({x},{y}) should be inked");
        }

        // ...and the gutter crossing the middle does not. This is the assertion that separates a grid from
        // a solid Box: without the gutter both would pass a "some ink in the middle" check.
        inked(16, 16).ShouldBeFalse("the gutter centre should be empty");
    }

    [Fact]
    public void TheListIconIsThreeBars_SeparatedByEmptyRows()
    {
        var (inked, _) = Paint(Layout.IconKind.List);

        // Walk the icon's vertical centre and count ink runs: three bars means three runs.
        var runs = 0;
        var wasInk = false;
        for (var y = 0; y < (int)Surface; y++)
        {
            var isInk = inked(16, y);
            if (isInk && !wasInk)
            {
                runs++;
            }

            wasInk = isInk;
        }

        runs.ShouldBe(3);
    }

    [Fact]
    public void TheAutoIconBracketsItsCorners_AndPutsTheAInTheMiddle()
    {
        var (inked, _) = Paint(Layout.IconKind.Auto);

        // The brackets reach the extreme corners, which is what separates this from Grid: Grid's quadrants
        // are inset and leave an empty gutter cross, so probing a corner tells the two apart.
        // One pixel inside the arms rather than on them: the inset is a fraction of a unit (~2.07 at this
        // size), so the outermost row sits on the rasteriser's rounding boundary and is not a stable probe.
        foreach (var (x, y) in new[] { (3, 3), (28, 3), (3, 28), (28, 28) })
        {
            inked(x, y).ShouldBeTrue($"corner bracket at ({x},{y}) should be inked");
        }

        // ...and the A occupies the middle. Its crossbar spans the centre column, unlike Grid's gutter.
        var middleInk = 0;
        for (var y = 10; y < 22; y++)
        {
            if (inked(16, y))
            {
                middleInk++;
            }
        }

        middleInk.ShouldBeGreaterThan(0, "the A should cross the icon's centre column");
    }

    [Fact]
    public void AZeroSizedRectDrawsNothing_RatherThanDividingByIt()
    {
        var renderer = new RgbaImageRenderer(Surface, Surface);
        var widget = new IconWidget(renderer);

        // A collapsed rect is ordinary (a CollapseBelow'd child, a window dragged to a sliver), so the
        // painter has to survive one: side <= 0 short-circuits before the unit division.
        widget.Render(Layout.Builder.Icon(Layout.IconKind.Grid, 16f, Ink), new RectF32(0, 0, 0, 0));

        renderer.Surface.Pixels.ShouldAllBe(b => b == 0);
    }
}
