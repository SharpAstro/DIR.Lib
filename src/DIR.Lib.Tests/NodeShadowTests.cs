using System;
using System.Collections.Generic;
using System.IO;
using SharpAstro.Png;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A node's drop shadow: painted through <see cref="RgbaImageRenderer"/> under a rounded card on a dark and on a light
/// page (pixel baselines), kept inside the damage rect, ignored by a cell arrange, and absent when transparent.
/// <c>DIR_LIB_UPDATE_BASELINES=1</c> with <c>--filter-class '*NodeShadowTests'</c> writes the baselines.
/// </summary>
public class NodeShadowTests
{
    private const uint W = 240, H = 240;
    private static readonly RGBAColor32 Black55 = new(0, 0, 0, 140);
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, FontPath, DesignScale.One);
        }
    }

    /// <summary>Two cards, one with the toast shadow and one at elevation 3, on a page of the given colour.</summary>
    private static Layout.Node Page(RGBAColor32 page, RGBAColor32 card)
        => new Layout.Node.Stack([
                Layout.Builder.Box(160, 60).Bg(card).Radius(8f).Shadow(0f, 1f, 2f, Black55),
                Layout.Builder.Box(160, 60).Bg(card).Radius(8f).Elevated(3),
            ], Layout.Axis.Vertical, 40f) { Padding = 40f }.Bg(page);

    private static RgbaImage Draw(Layout.Node tree, RGBAColor32 clear)
    {
        var renderer = new RgbaImageRenderer(W, H);
        renderer.Surface.Clear(clear);
        new Widget(renderer) { FontPath = FontPath }.Render(tree, new RectF32(0, 0, W, H));
        return renderer.Surface;
    }

    [Fact]
    public void RoundedCardsWithShadowsOnADarkPage()
    {
        var page = new RGBAColor32(24, 25, 28, 255);
        CompareBaseline(Draw(Page(page, new(52, 56, 64, 255)), page), "node_shadow_dark.png");
    }

    [Fact]
    public void RoundedCardsWithShadowsOnALightPage()
    {
        var page = new RGBAColor32(236, 238, 242, 255);
        CompareBaseline(Draw(Page(page, new(255, 255, 255, 255)), page), "node_shadow_light.png");
    }

    [Fact]
    public void ATransparentShadowPaintsNothing()
    {
        var page = new RGBAColor32(200, 200, 200, 255);
        var plain = Draw(new Layout.Node.Stack([Layout.Builder.Box(80, 40).Bg(page).Radius(6f)]) { Padding = 30f }, page);
        var shadowed = Draw(new Layout.Node.Stack([Layout.Builder.Box(80, 40).Bg(page).Radius(6f).Shadow(2f, 3f, 6f, default)]) { Padding = 30f }, page);
        shadowed.Pixels.ShouldBe(plain.Pixels);
    }

    [Fact]
    public void AShadowDarkensPixelsBelowTheCardButNotFarAway()
    {
        var page = new RGBAColor32(200, 200, 200, 255);
        var img = Draw(new Layout.Node.Stack([Layout.Builder.Box(80, 40).Bg(page).Radius(6f).Shadow(0f, 1f, 2f, Black55)]) { Padding = 30f }, page);
        // Card spans y 30..70, x 30..110; the shadow reaches y 71..73.
        img.Pixels[(72 * (int)W + 70) * 4].ShouldBeLessThan((byte)200);
        img.Pixels[(90 * (int)W + 70) * 4].ShouldBe((byte)200);
    }

    [Fact]
    public void AMovedShadowedNodeDamagesItsShadowToo()
    {
        var node = Layout.Builder.Box(80, 40).Bg(new(10, 10, 10, 255)).Shadow(0f, 4f, 8f, Black55);
        var before = new[] { new Layout.ArrangedNode<float>(node, new Rect<float>(50, 50, 80, 40)) };
        var after = new[] { new Layout.ArrangedNode<float>(node, new Rect<float>(70, 50, 80, 40)) };
        var damage = new List<Rect<float>>();
        LayoutDamage.Compute(before, after, null, null, null, damage);

        var extent = node.ShadowStyle!.Extent; // 12
        damage.Count.ShouldBe(2);
        foreach (var (x, w) in new[] { (50f, 80f), (70f, 80f) })
        {
            var covered = false;
            foreach (var d in damage)
            {
                covered |= d.X <= x - extent && d.Y <= 50 - extent
                    && d.X + d.Width >= x + w + extent && d.Y + d.Height >= 90 + extent;
            }

            covered.ShouldBeTrue($"the shadow of the node at x={x} lies inside a damage rect");
        }
    }

    [Fact]
    public void ChangingOnlyTheShadowIsDamage()
    {
        var plain = Layout.Builder.Box(80, 40).Bg(new(10, 10, 10, 255));
        Layout.ArrangedNode<float>[] Frame(Layout.Node n) => [new Layout.ArrangedNode<float>(n, new Rect<float>(50, 50, 80, 40))];
        var damage = new List<Rect<float>>();
        LayoutDamage.Compute(Frame(plain), Frame(plain.Elevated(2)), null, null, null, damage);
        damage.ShouldNotBeEmpty();
    }

    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
    }

    [Fact]
    public void ACellArrangeLaysOutTheSameWithAndWithoutAShadow()
    {
        Layout.Node Tree(bool shadow)
        {
            var card = Layout.Builder.Text("Toast").Bg(new(1, 2, 3, 255)).Radius(8f);
            return new Layout.Node.Stack([shadow ? card.Elevated(4) : card, Layout.Builder.Text("Next")]) { Padding = 8f };
        }

        var plain = Layout.Engine.Arrange(Tree(false), new Rect<int>(0, 0, 40, 10), new CellCtx());
        var shadowed = Layout.Engine.Arrange(Tree(true), new Rect<int>(0, 0, 40, 10), new CellCtx());
        shadowed.Length.ShouldBe(plain.Length);
        for (var i = 0; i < shadowed.Length; i++)
        {
            shadowed[i].Bounds.ShouldBe(plain[i].Bounds);
        }
    }

    [Fact]
    public void ElevatedZeroClearsAndLevelsGrow()
    {
        var n = Layout.Builder.Box(1, 1);
        n.Elevated(0).ShadowStyle.ShouldBeNull();
        n.Elevated(2).Elevated(0).ShadowStyle.ShouldBeNull();
        n.Elevated(1).ShadowStyle!.Blur.ShouldBeLessThan(n.Elevated(4).ShadowStyle!.Blur);
        n.Elevated(9).ShadowStyle.ShouldBe(n.Elevated(4).ShadowStyle);
    }

    private static void CompareBaseline(RgbaImage img, string name)
    {
        var baselinePath = Path.Combine(BaselineDir, name);
        if (UpdateBaselines)
        {
            Directory.CreateDirectory(SourceBaselineDir);
            Directory.CreateDirectory(BaselineDir);
            var png = PngWriter.Encode(img.Pixels, img.Width, img.Height);
            File.WriteAllBytes(Path.Combine(SourceBaselineDir, name), png);
            File.WriteAllBytes(baselinePath, png);
            return;
        }

        File.Exists(baselinePath).ShouldBeTrue($"Baseline '{name}' is missing; generate it with DIR_LIB_UPDATE_BASELINES=1.");
        var baseline = PngReader.Decode(File.ReadAllBytes(baselinePath));
        baseline.Width.ShouldBe(img.Width);
        baseline.Height.ShouldBe(img.Height);

        var maxDiff = 0;
        var diffCount = 0;
        for (var i = 0; i < baseline.Pixels.Length; i++)
        {
            var diff = Math.Abs(img.Pixels[i] - baseline.Pixels[i]);
            if (diff > 0) diffCount++;
            if (diff > maxDiff) maxDiff = diff;
        }

        if (maxDiff > 2)
        {
            var actualPath = Path.ChangeExtension(baselinePath, ".actual.png");
            PngWriter.Save(actualPath, img.Pixels, img.Width, img.Height);
            Assert.Fail($"Baseline mismatch for '{name}': {diffCount} pixels differ, max diff={maxDiff}. Actual saved to '{actualPath}'.");
        }
    }
}
