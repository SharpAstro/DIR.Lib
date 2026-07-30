using System;
using System.Collections.Immutable;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The per-axis <see cref="PixelMeasureContext{TSurface}"/>: a tree authored in CELL design units
/// (<c>RowH(1)</c> = one terminal row, <c>fontSize: 1f</c> = one cell of text) arranging onto a pixel
/// surface — the mirror of Console.Lib's <c>CellMeasureContext.PixelAuthored</c>, which carries a
/// pixel-authored tree the other way. Also pins the reason the painter takes the CONTEXT rather than a
/// scale of its own: one object answers measure and paint, so the two cannot disagree.
/// </summary>
public class PixelMeasureContextTests
{
    /// <summary>Deterministic glyph metrics (half-em advance) and a record of every DrawText's font size,
    /// so the measure/paint agreement is asserted on numbers, not on a real font file.</summary>
    private sealed class MetricsSpyRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public List<float> MeasuredAt { get; } = [];
        public List<float> DrawnAt { get; } = [];

        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
        {
            MeasuredAt.Add(fontSize);
            return (text.Length * fontSize * 0.5f, fontSize);
        }

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
            => DrawnAt.Add(fontSize);
    }

    private sealed class CellTreeWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public ImmutableArray<Layout.ArrangedNode<float>> Render(Layout.Node root, RectF32 bounds,
            PixelMeasureContext<RgbaImage> ctx)
        {
            BeginFrame();
            return RenderLayout(root, bounds, ctx);
        }
    }

    // --- the mapping itself ---

    [Fact]
    public void CellAuthored_MapsEachAxisByItsOwnCellExtent()
    {
        using var renderer = new MetricsSpyRenderer(400, 100);
        var ctx = PixelMeasureContext<RgbaImage>.CellAuthored(renderer, "font", cellWidth: 8f, cellHeight: 16f);

        // The same scalar resolves to DIFFERENT pixel extents per axis — the case one scalar cannot express.
        ctx.ToSurfaceX(3f).ShouldBe(24f);
        ctx.ToSurfaceY(3f).ShouldBe(48f);

        // Axis-free resolves against the horizontal scale, mirroring CellMeasureContext's column choice.
        ctx.ToSurface(3f).ShouldBe(24f);

        // fontSize 1f = one cell of text: measured at the cell HEIGHT in pixels.
        ctx.MeasureText("abc".AsSpan(), 1f);
        renderer.MeasuredAt.ShouldBe([16f]);
    }

    [Fact]
    public void UniformConstructor_IsTheDpiScaleItAlwaysWas()
    {
        using var renderer = new MetricsSpyRenderer(400, 100);
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, "font", dpiScale: 2f);

        ctx.ToSurfaceX(5f).ShouldBe(10f);
        ctx.ToSurfaceY(5f).ShouldBe(10f);
        ctx.FontScale.ShouldBe(2f);
    }

    /// <summary>
    /// The round trip that makes the two contexts a PAIR: a cell-authored extent carried onto pixels by
    /// this context is exactly the extent CellMeasureContext.PixelAuthored's nominal 8x16 cell carries a
    /// pixel-authored one back from. (The cell side lives in Console.Lib; its nominal cell is pinned here
    /// so the mirror cannot silently skew.)
    /// </summary>
    [Fact]
    public void CellAuthored_NominalCell_MirrorsPixelAuthoredExactly()
    {
        using var renderer = new MetricsSpyRenderer(400, 100);
        var ctx = PixelMeasureContext<RgbaImage>.CellAuthored(renderer, "font");

        // 10 cells across x 2 rows down, through the nominal cell, is 80x32 px — the same numbers
        // PixelAuthored divides by to land a pixel-authored 80x32 card on 10x2 cells.
        ctx.ToSurfaceX(10f).ShouldBe(80f);
        ctx.ToSurfaceY(2f).ShouldBe(32f);
    }

    // --- arrange: a cell-authored tree lands on pixel rects ---

    [Fact]
    public void CellAuthoredTree_ArrangesToPixels()
    {
        using var renderer = new MetricsSpyRenderer(400, 100);
        var ctx = PixelMeasureContext<RgbaImage>.CellAuthored(renderer, "font");

        // A one-row list row in cells: fixed 7-column index, star ply. RowH(1) must become 16px, not 1px.
        var idx = new Layout.Node.Leaf(new Layout.Content.Text("  1.", 1f))
        {
            Width = Layout.Sizing.Fixed(7f),
            Height = Layout.Sizing.Star(),
        };
        var ply = new Layout.Node.Leaf(new Layout.Content.Text("e4", 1f))
        {
            Width = Layout.Sizing.Star(),
            Height = Layout.Sizing.Star(),
        };
        var row = new Layout.Node.Stack([idx, ply], Layout.Axis.Horizontal);

        var arranged = Layout.Engine.Arrange(row, new Rect<float>(0, 0, 184f, 16f), ctx);

        var idxRect = arranged.First(a => ReferenceEquals(a.Node, idx)).Bounds;
        var plyRect = arranged.First(a => ReferenceEquals(a.Node, ply)).Bounds;
        idxRect.ShouldBe(new Rect<float>(0, 0, 56f, 16f), "7 cells x 8px");
        plyRect.ShouldBe(new Rect<float>(56f, 0, 128f, 16f), "the star takes the remaining pixels");
    }

    // --- paint: the context is the one authority, so measure and paint agree by construction ---

    [Fact]
    public void PaintLayout_DrawsTextAtTheSizeTheContextMeasured()
    {
        using var renderer = new MetricsSpyRenderer(400, 100);
        var widget = new CellTreeWidget(renderer);
        var ctx = PixelMeasureContext<RgbaImage>.CellAuthored(renderer, "font", cellWidth: 9f, cellHeight: 18f);

        // Auto width, so the arrange MEASURES the text (a Star leaf never would — it only claims leftover).
        var text = new Layout.Node.Leaf(new Layout.Content.Text("Qxd5+", 1f))
        {
            Height = Layout.Sizing.Star(),
        };
        widget.Render(new Layout.Node.Stack([text]), new RectF32(0, 0, 200, 18), ctx);

        // Measured during arrange and drawn during paint at the SAME pixel size — 1 cell = 18px — because
        // both came from ctx.FontScale. With a painter-owned scale this is exactly what drifted.
        renderer.MeasuredAt.ShouldContain(18f);
        renderer.DrawnAt.ShouldBe([18f]);
    }
}
