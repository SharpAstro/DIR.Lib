using System;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Per-column sizing on a <c>Grid</c>: <c>Auto</c> takes its own widest cell, <c>Fixed</c> is fixed,
/// <c>Star</c> shares the rest.
///
/// <para>
/// That is what a TABLE is, and it is the one container a consumer kept writing out: a helper that
/// measures its own column stops and places each cell by hand, with a comment saying "the stops are
/// measured, not assumed" -- true, and the measuring was the engine's to do. A grid could only split
/// evenly, so a label column got the same width as its value column whatever either contained.
/// </para>
/// </summary>
public class LayoutGridColumnsTests
{
    /// <summary>Half the font size per character, so a column's Auto width is arithmetic a test states.</summary>
    private sealed class StubCtx : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize)
            => new(text.Length * fontSize * 0.5f, fontSize);

        public float ToSurface(float designUnits) => designUnits;
    }

    private static readonly StubCtx Ctx = new();

    /// <summary>The first row's cells, which are the column stops; pre-order puts the grid itself first.</summary>
    private static Layout.ArrangedNode<float>[] FirstRow(Layout.Node grid, RectF32 bounds, int columns)
        => [.. Layout.Engine
            .Arrange(grid, new Rect<float>(bounds.X, bounds.Y, bounds.Width, bounds.Height), Ctx)
            .Skip(1)
            .Take(columns)];

    private static float[] ColumnWidths(Layout.Node grid, RectF32 bounds, int columns)
        => [.. FirstRow(grid, bounds, columns).Select(a => a.Bounds.Width)];

    private static float[] ColumnXs(Layout.Node grid, RectF32 bounds, int columns)
        => [.. FirstRow(grid, bounds, columns).Select(a => a.Bounds.X)];

    /// <summary>A two-column table: a label column and a value column.</summary>
    private static Layout.Node Table(params ReadOnlySpan<Layout.Sizing> columns)
        => Layout.Builder.Grid(2,
                Layout.Builder.Text("Exposure", 10f),      // 8 chars -> 40 units
                Layout.Builder.Text("120 s", 10f),
                Layout.Builder.Text("Gain", 10f),          // 4 chars -> 20 units
                Layout.Builder.Text("100", 10f))
            .WithColumns(columns);

    [Fact]
    public void AnAutoColumnIsAsWideAsItsOwnWidestCell()
    {
        // "Exposure" is the widest cell in column 0, at 8 characters x half of a 10-unit font.
        var widths = ColumnWidths(Table(Layout.Sizing.Auto, Layout.Sizing.Star()), new RectF32(0, 0, 200, 40), 2);

        widths[0].ShouldBe(40f);
        widths[1].ShouldBe(160f, "the star column takes what is left");
    }

    [Fact]
    public void AndItIsThatColumnsOwnCellsThatDecideItNotTheGridsWidest()
    {
        // An Auto column must not size to a cell in some OTHER column, which is exactly what "the widest
        // cell in the grid, N times" gives you.
        var grid = Layout.Builder.Grid(2,
                Layout.Builder.Text("Gain", 10f),                      // 20
                Layout.Builder.Text("a much longer value", 10f))       // 95
            .WithColumns(Layout.Sizing.Auto, Layout.Sizing.Star());

        ColumnWidths(grid, new RectF32(0, 0, 200, 20), 2)[0].ShouldBe(20f);
    }

    [Fact]
    public void AFixedColumnTakesItsStatedExtentAndTheStarsShareTheRest()
    {
        var widths = ColumnWidths(Table(Layout.Sizing.Fixed(60f), Layout.Sizing.Star()), new RectF32(0, 0, 200, 40), 2);

        widths[0].ShouldBe(60f);
        widths[1].ShouldBe(140f);
    }

    [Fact]
    public void StarsShareByWeight()
    {
        var grid = Layout.Builder.Grid(3,
                Layout.Builder.Spacer(), Layout.Builder.Spacer(), Layout.Builder.Spacer())
            .WithColumns(Layout.Sizing.Star(1f), Layout.Sizing.Star(3f), Layout.Sizing.Star(4f));

        ColumnWidths(grid, new RectF32(0, 0, 200, 20), 3).ShouldBe([25f, 75f, 100f]);
    }

    [Fact]
    public void ColumnsPastTheStatedOnesAreStarSoATableStatesOnlyItsLabelColumn()
    {
        var widths = ColumnWidths(Table(Layout.Sizing.Fixed(50f)), new RectF32(0, 0, 200, 40), 2);

        widths[0].ShouldBe(50f);
        widths[1].ShouldBe(150f);
    }

    [Fact]
    public void TheColumnGapIsStillHonouredAndTheStopsFollowTheWidths()
    {
        var grid = Table(Layout.Sizing.Fixed(60f), Layout.Sizing.Star()).WithGaps(0f, 10f);
        var bounds = new RectF32(0, 0, 200, 40);

        ColumnWidths(grid, bounds, 2).ShouldBe([60f, 130f]);
        ColumnXs(grid, bounds, 2).ShouldBe([0f, 70f]);
    }

    [Fact]
    public void AGridThatStatesNoColumnSizingSplitsEvenlyExactlyAsBefore()
    {
        // Every grid written before this existed is in this state and must arrange byte-identically.
        var grid = Layout.Builder.Grid(2,
            Layout.Builder.Text("Exposure", 10f), Layout.Builder.Text("120 s", 10f),
            Layout.Builder.Text("Gain", 10f), Layout.Builder.Text("100", 10f));

        grid.ShouldBeOfType<Layout.Node.Grid>().ColumnSizing.ShouldBeEmpty();
        ColumnWidths(grid, new RectF32(0, 0, 200, 40), 2).ShouldBe([100f, 100f]);
    }

    [Fact]
    public void AnOverrunningAutoColumnKeepsItsContentsWidthAndStarvesTheStars()
    {
        // The same rule an Auto child of a Stack follows, and the one the Sizing clamps describe: hold
        // the floor and overflow VISIBLY rather than truncate to a width the content does not fit. What
        // must not happen is a negative width, which is what "the remainder" gives you unfloored -- and
        // a caller who does want a cap states Max.
        var grid = Layout.Builder.Grid(3,
                Layout.Builder.Spacer().WFixed(30f),
                Layout.Builder.Text("wwwwwwwwwwwwwwwwwwww", 10f),   // 100 units of run
                Layout.Builder.Spacer())
            .WithColumns(Layout.Sizing.Fixed(60f), Layout.Sizing.Auto, Layout.Sizing.Star());

        var widths = ColumnWidths(grid, new RectF32(0, 0, 100, 20), 3);
        widths[0].ShouldBe(60f);
        widths[1].ShouldBe(100f);
        widths[2].ShouldBe(0f, "starved, not negative");
    }

    [Fact]
    public void AnAutoColumnWithAMaxStopsThere()
    {
        var grid = Layout.Builder.Grid(2,
                Layout.Builder.Text("wwwwwwwwwwwwwwwwwwww", 10f),   // 100 units of run
                Layout.Builder.Spacer())
            .WithColumns(Layout.Sizing.Auto with { Max = 45f }, Layout.Sizing.Star());

        ColumnWidths(grid, new RectF32(0, 0, 200, 20), 2).ShouldBe([45f, 155f]);
    }

    [Fact]
    public void MeasureAndArrangeAgreeOnTheColumnStops()
    {
        // The two used to share one even-split expression; they now share one helper. A grid whose
        // intrinsic width disagrees with its arranged one is how a table ends up clipped by the stack
        // that sized it.
        var grid = Table(Layout.Sizing.Auto, Layout.Sizing.Fixed(70f));

        Layout.Engine.Measure(grid, new Layout.Size<float>(200f, 40f), Ctx).Width
            .ShouldBe(110f, "40 for the auto column plus 70 for the fixed one");
    }

    [Fact]
    public void WithColumnsIsANoOpOnAnythingThatIsNotAGrid()
    {
        // Like every other container-specific modifier here.
        Layout.Builder.VStack(Layout.Builder.Spacer())
            .WithColumns(Layout.Sizing.Auto).ShouldBeOfType<Layout.Node.Stack>();
    }

    [Fact]
    public void AutoRowsStillSizesEachRowToItsOwnContentOverSizedColumns()
    {
        var grid = Layout.Builder.Grid(2,
                Layout.Builder.Text("Exposure", 10f).HAuto(),
                Layout.Builder.Text("120 s", 10f).HAuto())
            .WithColumns(Layout.Sizing.Auto, Layout.Sizing.Star())
            .WithAutoRows();

        Layout.Engine.Measure(grid, new Layout.Size<float>(200f, 100f), Ctx).Height.ShouldBe(10f);
    }
}
