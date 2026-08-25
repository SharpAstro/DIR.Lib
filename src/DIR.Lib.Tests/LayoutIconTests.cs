using System.Linq;
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
    /// <summary>
    /// The caret is a solid triangle that reaches all four edges of its square, and points the way it
    /// says. Its tip is the part a stroked mark loses first at chip size, so the apex row is asserted
    /// explicitly rather than left to the extent check.
    /// </summary>
    [Fact]
    public void TheCaretIsASolidTriangleThatReachesItsEdges_AndPointsTheWayItIsNamed()
    {
        var (up, _) = Paint(Layout.IconKind.CaretUp);
        var mid = (int)Surface / 2;
        var last = (int)Surface - 1;

        up(mid, 0).ShouldBeTrue("the tip must reach the top edge");
        up(0, last).ShouldBeTrue("the base must reach the bottom-left corner");
        up(last, last).ShouldBeTrue("the base must reach the bottom-right corner");
        up(0, 0).ShouldBeFalse("the top corners are outside a triangle pointing up");
        up(last, 0).ShouldBeFalse();

        // Inverted, and only inverted: same mark, other way up.
        var (down, _) = Paint(Layout.IconKind.CaretDown);
        down(mid, last).ShouldBeTrue("the tip must reach the bottom edge");
        down(0, 0).ShouldBeTrue("the base must reach the top-left corner");
        down(last, 0).ShouldBeTrue();
        down(0, last).ShouldBeFalse("the bottom corners are outside a triangle pointing down");
    }

    /// <summary>
    /// The plus reaches all four edges, and its centre is inked -- the two things that separate it from the
    /// caret (a triangle, so its top corners are empty) and from the grid (a gutter, so its centre is).
    /// </summary>
    [Fact]
    public void ThePlusReachesAllFourEdges_AndIsSolidThroughItsCentre()
    {
        var (inked, _) = Paint(Layout.IconKind.Plus);
        var mid = (int)Surface / 2;
        var last = (int)Surface - 1;

        inked(mid, 0).ShouldBeTrue("the vertical arm must reach the top edge");
        inked(mid, last).ShouldBeTrue("...and the bottom");
        inked(0, mid).ShouldBeTrue("the horizontal arm must reach the left edge");
        inked(last, mid).ShouldBeTrue("...and the right");
        inked(mid, mid).ShouldBeTrue("the arms cross at the centre");

        // The corners are what make it a cross rather than a filled square.
        inked(0, 0).ShouldBeFalse("a corner is outside both arms");
        inked(last, last).ShouldBeFalse();
    }

    /// <summary>
    /// The minus is the plus's horizontal arm and nothing else -- same thickness, same centre line. That
    /// shared geometry is the whole reason the two are one family: a stepper sets them side by side, where
    /// a one-pixel difference in weight or baseline is the difference a reader is guaranteed to notice.
    /// It is also the one kind that cannot ink its full square, so this pins WIDTH rather than both axes.
    /// </summary>
    [Fact]
    public void TheMinusIsThePlusHorizontalArm_SameThicknessAndSameCentreLine()
    {
        var (plus, _) = Paint(Layout.IconKind.Plus);
        var (minus, _) = Paint(Layout.IconKind.Minus);
        var mid = (int)Surface / 2;
        var last = (int)Surface - 1;

        minus(0, mid).ShouldBeTrue("the bar must reach the left edge");
        minus(last, mid).ShouldBeTrue("...and the right");
        minus(mid, 0).ShouldBeFalse("a minus has no vertical arm");
        minus(mid, last).ShouldBeFalse();

        // Walk a column clear of the plus's vertical arm, so what is counted is the horizontal bar alone.
        static int BarRows(Func<int, int, bool> inked, int column)
        {
            var rows = 0;
            for (var y = 0; y < (int)Surface; y++)
            {
                if (inked(column, y))
                {
                    rows++;
                }
            }

            return rows;
        }

        BarRows(minus, 2).ShouldBe(BarRows(plus, 2));
    }

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

    /// <summary>
    /// A mark that states no size takes it from the text in the same container, so a caret in a chip is
    /// sized by the chip's own label rather than by a number written out beside it.
    /// <para>
    /// Asserted on the ARRANGED rect rather than on the node, because the size has to survive the measure
    /// pass to mean anything: it is the intrinsic extent an Auto child reports to its parent.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnsizedIconTakesItsSizeFromTheTextBesideIt()
    {
        var renderer = new RgbaImageRenderer(Surface, Surface);
        var widget = new IconWidget(renderer);

        // Beside a Star sibling, so the icon's Auto width is its intrinsic one -- the same shape as
        // AnIconIsSquareAtItsDeclaredSize, and for the same reason.
        var root = Layout.Builder.HStack(
            Layout.Builder.Text("Two-Up", 20f, Ink),
            Layout.Builder.Icon(Layout.IconKind.CaretUp, color: Ink)
                .Clickable(new HitResult.ButtonHit("caret")),
            Layout.Builder.Spacer().WStar());

        var region = widget.Render(root, new RectF32(0, 0, Surface, Surface)).ShouldHaveSingleItem();

        region.Width.ShouldBe(20f * Layout.Content.Icon.TextSizeRatio, 0.5f);
        region.Height.ShouldBe(20f * Layout.Content.Icon.TextSizeRatio, 0.5f);
    }

    /// <summary>
    /// The run may be nested: a padded label is a one-child stack, since padding insets a node's CHILDREN
    /// and so cannot go on the leaf. A search that only looked at the icon's immediate siblings would miss
    /// every row built that way and silently fall back to the default size.
    /// </summary>
    [Fact]
    public void TheTextItMatchesMayBeNestedInsideTheContainer()
    {
        var nested = Layout.Builder.HStack(
            Layout.Builder.HStack(Layout.Builder.Text("Rev C", 24f, Ink)).PadX(4f),
            Layout.Builder.Icon(Layout.IconKind.CaretDown, color: Ink));

        var icon = IconIn(nested);

        icon.Size.ShouldBe(24f * Layout.Content.Icon.TextSizeRatio, 0.001f);
        icon.MatchesText.ShouldBeTrue("the resolved size still records where it came from");
    }

    /// <summary>
    /// A stated size is never second-guessed -- an icon-only button has no run to match and every existing
    /// call site states one, so the resolution must be additive rather than an override.
    /// </summary>
    [Fact]
    public void AStatedSizeWins_AndAnIconWithNoTextInScopeKeepsTheDefault()
    {
        IconIn(Layout.Builder.HStack(
                Layout.Builder.Text("Two-Up", 20f, Ink),
                Layout.Builder.Icon(Layout.IconKind.CaretUp, 9f, Ink)))
            .Size.ShouldBe(9f);

        // Nothing text-bearing anywhere in the container: the mark falls back rather than resolving to
        // zero, which is what an icon-only row (a stepper, a toolbar button) actually is.
        var lone = IconIn(Layout.Builder.HStack(
            Layout.Builder.Icon(Layout.IconKind.Plus, color: Ink),
            Layout.Builder.Spacer().WStar()));

        lone.Size.ShouldBe(Layout.Content.Icon.DefaultSize);
        lone.MatchesText.ShouldBeTrue("it asked to match text; there was none to match");
    }

    /// <summary>
    /// A field counts as text: a caret beside an editable value is the same relationship as a caret beside
    /// a label, and the two are interchangeable in one row (the zoom chip swaps between them).
    /// </summary>
    [Fact]
    public void AnEditableFieldSizesTheMarkBesideIt()
    {
        var state = new TextInputState { Text = "175%" };
        var row = Layout.Builder.HStack(
            Layout.Builder.TextInput(state, 18f),
            Layout.Builder.Icon(Layout.IconKind.CaretUp, color: Ink));

        IconIn(row).Size.ShouldBe(18f * Layout.Content.Icon.TextSizeRatio, 0.001f);
    }

    /// <summary>The first icon of several is not the only one resolved -- a stepper is two marks in one row.</summary>
    [Fact]
    public void EveryUnsizedIconInTheContainerIsResolved()
    {
        var stepper = Layout.Builder.HStack(
            Layout.Builder.Icon(Layout.IconKind.Minus, color: Ink),
            Layout.Builder.Text("3", 16f, Ink),
            Layout.Builder.Icon(Layout.IconKind.Plus, color: Ink));

        var icons = ((Layout.Node.Stack)stepper).Children
            .OfType<Layout.Node.Leaf>()
            .Select(l => l.Content)
            .OfType<Layout.Content.Icon>()
            .ToList();

        icons.Count.ShouldBe(2);
        icons.ShouldAllBe(i => i.Size == 16f * Layout.Content.Icon.TextSizeRatio);
    }

    /// <summary>The first icon leaf in a container, for the assertions that read the tree rather than pixels.</summary>
    private static Layout.Content.Icon IconIn(Layout.Node container)
        => ((Layout.Node.Stack)container).Children
            .OfType<Layout.Node.Leaf>()
            .Select(leaf => leaf.Content)
            .OfType<Layout.Content.Icon>()
            .First();

    /// <summary>
    /// The magnifier is a RING with a handle running out of it to the opposite corner, and the ring is what
    /// separates it from every filled kind: its middle is empty. Both extremes touch the bounding box -- the
    /// ring's arc up-left and the handle's tip down-right -- which is the contract each kind owes, and the
    /// only one in the family that meets it on a diagonal.
    /// </summary>
    [Fact]
    public void TheSearchIconIsAHollowLensWithAHandleToTheOppositeCorner()
    {
        var (inked, _) = Paint(Layout.IconKind.Search);
        var last = (int)Surface - 1;

        // The lens is hollow. Its centre sits up-left of the icon's centre, which is where the ring is
        // seated so the handle has room to reach the corner.
        inked(11, 11).ShouldBeFalse("the lens must be empty inside, or it is a dot and not a lens");

        // ...and it IS a ring, so walking right along the lens's own centre row crosses ink, a gap, ink.
        var runs = 0;
        var wasInk = false;
        for (var x = 0; x < (int)Surface; x++)
        {
            var isInk = inked(x, 11);
            if (isInk && !wasInk)
            {
                runs++;
            }

            wasInk = isInk;
        }

        runs.ShouldBe(2, "left limb, hollow middle, right limb");

        // The handle reaches the bottom-right corner. One pixel in, for the reason the Auto test gives:
        // the stroke's far edge lands on the rasteriser's rounding boundary.
        inked(last - 1, last - 1).ShouldBeTrue("the handle must reach the bottom-right corner");

        // The opposite corner is empty -- nothing in this mark is square, and that is what separates it
        // from Grid and Plus.
        inked(last, 0).ShouldBeFalse("the top-right corner is outside both the lens and the handle");
        inked(0, last).ShouldBeFalse("so is the bottom-left");
    }

    /// <summary>
    /// A field's leading mark is drawn INSIDE its box, and the box is unchanged by it: background and border
    /// still span the whole rect, so the mark reads as belonging to the field rather than sitting beside it.
    /// <para>
    /// Asserted on the pixels along the field's own edge, because that is the part a sibling-icon layout
    /// would get wrong -- there the box would start after the mark and leave the leading columns bare.
    /// </para>
    /// </summary>
    [Fact]
    public void AFieldsLeadingMarkIsInsideItsBox_WhichStillSpansTheWholeRect()
    {
        var renderer = new RgbaImageRenderer(64, 32);
        var widget = new IconWidget(renderer);
        var state = new TextInputState { Text = "road" };

        widget.Render(
            Layout.Builder.TextInput(state, 16f, leadingIcon: Layout.IconKind.Search).Stretch(),
            new RectF32(0, 0, 64, 32));

        var pixels = renderer.Surface.Pixels;
        bool Painted(int x, int y) => pixels[(y * 64 + x) * 4 + 3] > 0;

        // The field's own border runs along the very first column and row of the rect. A mark placed as a
        // sibling instead of inside would leave these bare.
        Painted(0, 16).ShouldBeTrue("the field's border must still start at the rect's left edge");
        Painted(32, 0).ShouldBeTrue("...and along its top");
    }

    /// <summary>
    /// The room the mark needs is reserved by the MEASURE pass and left by the PAINT, and both read it from
    /// the same place. A field with a mark measures exactly one <c>LeadingRoom</c> wider than the same field
    /// without one -- which is the property that stops the sample text fitting the box while measuring and
    /// being clipped while painting.
    /// </summary>
    [Fact]
    public void ALeadingMarkWidensTheMeasuredFieldByExactlyTheRoomItReserves()
    {
        var renderer = new RgbaImageRenderer(8, 8);
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, fontPath: string.Empty, dpiScale: 1f);
        var state = new TextInputState { Placeholder = "Search pages and text" };
        var available = new Layout.Size<float>(1000f, 1000f);

        var plain = Layout.Engine.Measure(Layout.Builder.TextInput(state, 16f), available, ctx);
        var marked = Layout.Engine.Measure(
            Layout.Builder.TextInput(state, 16f, leadingIcon: Layout.IconKind.Search), available, ctx);

        (marked.Width - plain.Width).ShouldBe(TextInputRenderer.LeadingRoom(16f, true), 0.001f);

        // ...and a field with no mark is untouched, so this is additive rather than a new inset for everyone.
        TextInputRenderer.LeadingRoom(16f, false).ShouldBe(0f);
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
