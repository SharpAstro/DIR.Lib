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
        // One pixel IN from the corner rather than on it: an arm is a few units thick and its far edge lands
        // on the rasteriser's rounding boundary, so probing near the corner is stable while probing at the
        // arm's edge is not. (These moved when every kind was normalised to ink its full bounding box: the
        // brackets used to sit ~2 units inside it.)
        foreach (var (x, y) in new[] { (1, 1), (30, 1), (1, 30), (30, 30) })
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

    /// <summary>
    /// The crescent is the outer disc MINUS an offset one, and the assertion that separates it from a plain
    /// disc is that the bite is EMPTY. It is drawn as scanline spans rather than by over-painting the offset
    /// disc in the button's background, so this also pins the property that made that choice: the icon needs
    /// no ground, and paints correctly over a rect nothing filled.
    /// </summary>
    [Fact]
    public void TheDarkIconIsACrescent_WithItsBiteLeftEmpty()
    {
        var (inked, _) = Paint(Layout.IconKind.ThemeDark);

        // The thick limb sits on the lower left, opposite the bite.
        inked(9, 20).ShouldBeTrue("the crescent's limb should be inked");

        // The bite is offset up and to the right of centre, and nothing may be drawn inside it.
        inked(22, 10).ShouldBeFalse("the bite should be empty, not filled");

        // ...and the disc's own outside is empty too, which is what stops "bite is empty" from passing
        // trivially for an icon that drew nothing at all.
        inked(1, 1).ShouldBeFalse("outside the disc should be empty");
    }

    /// <summary>
    /// The sun is a disc ringed by rays with a GAP between them -- the property that keeps it from reading as
    /// a fuzzy dot at 13 px, which is the size a header actually uses.
    /// </summary>
    [Fact]
    public void TheLightIconKeepsAGapBetweenItsDiscAndItsRays()
    {
        var (inked, _) = Paint(Layout.IconKind.ThemeLight);

        // Centre is the disc.
        inked(16, 16).ShouldBeTrue("the sun's disc should be inked");

        // Walking out along the horizontal from the centre must cross ink, then a gap, then ink again
        // (the disc, the gap, a ray). Two runs is what makes it a sun rather than a blob.
        var runs = 0;
        var wasInk = false;
        for (var x = 16; x < (int)Surface; x++)
        {
            var isInk = inked(x, 16);
            if (isInk && !wasInk)
            {
                runs++;
            }

            wasInk = isInk;
        }

        runs.ShouldBe(2, "disc then gap then ray");
    }

    /// <summary>
    /// Half filled, half outlined. The outlined half is the whole point: it is what stops the mark reading as
    /// a moon, which is the neighbour it sits next to in a theme control.
    /// </summary>
    [Fact]
    public void TheSystemIconFillsOneHalfAndOutlinesTheOther()
    {
        var (inked, _) = Paint(Layout.IconKind.ThemeSystem);

        // Left half: solid, so a point midway between centre and the left edge carries ink.
        inked(8, 16).ShouldBeTrue("the left half should be filled");

        // Right half: outline only, so the same point mirrored is EMPTY while the rim beyond it is inked.
        // The rim sits at the bounding box now that the disc fills it, rather than ~6 units inside.
        inked(21, 16).ShouldBeFalse("the right half should be hollow");
        inked(30, 16).ShouldBeTrue("...but its rim should be inked");
    }

    /// <summary>
    /// An icon is drawn at the size it DECLARES, centred, not stretched to whatever cell it landed in.
    /// <para>
    /// Size used to be consulted only at measure time, so it meant nothing once a node carried explicit
    /// sizing -- which every real icon does, since it lives in a button. Beside a text run that showed up as
    /// a mark standing well above the word's cap height and reading as misaligned, with both perfectly
    /// centred on the same row. Two rects, one twice the other, must ink the same number of rows.
    /// </para>
    /// </summary>
    [Fact]
    public void AnIconIsDrawnAtItsDeclaredSize_NotStretchedToItsCell()
    {
        static int InkedRows(float cellSide)
        {
            var renderer = new RgbaImageRenderer(Surface, Surface);
            var widget = new IconWidget(renderer);
            var inset = ((float)Surface - cellSide) / 2f;

            widget.Render(Layout.Builder.Icon(Layout.IconKind.ThemeDark, 12f, Ink),
                new RectF32(inset, inset, cellSide, cellSide));

            var pixels = renderer.Surface.Pixels;
            var rows = 0;
            for (var y = 0; y < (int)Surface; y++)
            {
                for (var x = 0; x < (int)Surface; x++)
                {
                    if (pixels[(y * (int)Surface + x) * 4 + 3] > 0)
                    {
                        rows++;
                        break;
                    }
                }
            }

            return rows;
        }

        // The declared 12 fits inside both cells, so the mark is the same size in each.
        InkedRows(16f).ShouldBe(InkedRows(32f));

        // ...and a cell SMALLER than the declared size still clamps, so a collapsed button shrinks the mark
        // rather than letting it overflow.
        InkedRows(8f).ShouldBeLessThan(InkedRows(16f));
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
