using System;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Tests the pixel painter on <see cref="PixelWidgetBase{TSurface}"/>: it arranges a <see cref="Layout.Node"/>
/// tree and binds each leaf's click region to its arranged rect, so draw-position and hit-region cannot drift.
/// Uses the CPU <see cref="RgbaImageRenderer"/> (no GPU, no font needed for region binding).
/// </summary>
public class LayoutPainterTests
{
    private sealed class TestWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public ClickableRegion[] Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: string.Empty, dpiScale: 1f);
            return GetRegisteredRegions();
        }

        public HitResult? DispatchAt(float x, float y) => HitTestAndDispatch(x, y);
    }

    private static Layout.Node.Leaf HitRow(string action, float height, Action<InputModifier>? onClick = null) =>
        new(new Layout.Content.Box(0, 0))
        {
            Hit = new HitResult.ButtonHit(action),
            OnClick = onClick,
            Height = Layout.Sizing.Fixed(height),
            Width = Layout.Sizing.Star(),
        };

    [Fact]
    public void PaintLayout_BindsClickRegionsToArrangedRects()
    {
        using var renderer = new RgbaImageRenderer(100, 100);
        var widget = new TestWidget(renderer);

        var a = HitRow("A", 10);
        var b = HitRow("B", 10);
        var stack = new Layout.Node.Stack([a, b]);

        var regions = widget.Render(stack, new RectF32(0, 0, 100, 100));

        regions.Length.ShouldBe(2);

        var ra = regions.First(r => r.Result is HitResult.ButtonHit { Action: "A" });
        ra.X.ShouldBe(0f);
        ra.Y.ShouldBe(0f);
        ra.Width.ShouldBe(100f);   // Star cross stretches to full width
        ra.Height.ShouldBe(10f);

        var rb = regions.First(r => r.Result is HitResult.ButtonHit { Action: "B" });
        rb.Y.ShouldBe(10f);        // second row directly below the first
        rb.Height.ShouldBe(10f);
    }

    [Fact]
    public void PaintLayout_OnClick_DispatchesInsideArrangedRect()
    {
        using var renderer = new RgbaImageRenderer(100, 100);
        var widget = new TestWidget(renderer);

        var clicks = 0;
        var leaf = HitRow("X", 20, _ => clicks++);
        var stack = new Layout.Node.Stack([leaf]);

        widget.Render(stack, new RectF32(0, 0, 100, 100));

        var hit = widget.DispatchAt(50, 10); // inside the 0..100 x 0..20 row
        hit.ShouldBeOfType<HitResult.ButtonHit>().Action.ShouldBe("X");
        clicks.ShouldBe(1);

        widget.DispatchAt(50, 50); // below the row -> no hit
        clicks.ShouldBe(1);
    }

    [Fact]
    public void PaintLayout_NonClickableLeaves_RegisterNoRegions()
    {
        using var renderer = new RgbaImageRenderer(100, 100);
        var widget = new TestWidget(renderer);

        // A panel background + a plain box, neither carrying a Hit.
        var stack = new Layout.Node.Stack([new Layout.Node.Leaf(new Layout.Content.Box(0, 0)) { Height = Layout.Sizing.Fixed(10), Width = Layout.Sizing.Star() }])
        {
            Background = new RGBAColor32(0x10, 0x10, 0x18, 0xff),
        };

        var regions = widget.Render(stack, new RectF32(0, 0, 100, 100));

        regions.Length.ShouldBe(0);
    }
}
