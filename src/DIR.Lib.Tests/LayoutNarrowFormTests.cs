using System;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Node.NarrowForm"/>: the full form when its natural width fits the room, the narrow form
/// otherwise, chosen identically by measure and arrange, on a pixel and on a cell measure context.
/// </summary>
public class LayoutNarrowFormTests
{
    private const string FullText = "Hello world"; // 11 chars
    private const string NarrowText = "Hi";

    /// <summary>Text is 10 wide per char and 10 tall.</summary>
    private sealed class PxCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 10, 10);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits);
    }

    /// <summary>A cell surface: text is one cell per char and one row tall.</summary>
    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
        public int ToSurfaceX(float designUnits) => (int)MathF.Round(designUnits / 8f);
        public int ToSurfaceY(float designUnits) => (int)MathF.Round(designUnits / 16f);
    }

    private sealed class FloatCtx : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 10f, 10f);
        public float ToSurface(float designUnits) => designUnits;
        public float ToSurfaceX(float designUnits) => designUnits;
        public float ToSurfaceY(float designUnits) => designUnits;
    }

    private static Layout.Node Switching()
        => Layout.Builder.Text(FullText)
            .Clickable(new HitResult.ButtonHit("full"))
            .OrWhenNarrow(Layout.Builder.Text(NarrowText).Clickable(new HitResult.ButtonHit("narrow")));

    private static string TextOf(Layout.ArrangedNode<int> a)
        => ((Layout.Content.Text)((Layout.Node.Leaf)a.Node).Content).Value;

    private static Layout.ArrangedNode<int>[] ArrangeInt(Layout.Node root, Layout.IMeasureContext<int> ctx, int w)
        => [.. Layout.Engine.Arrange(root, new Rect<int>(0, 0, w, 100), ctx)];

    [Fact]
    public void FullFormWhenItsNaturalWidthFits()
    {
        var a = ArrangeInt(Switching(), new PxCtx(), 110);
        a.Length.ShouldBe(1);
        TextOf(a[0]).ShouldBe(FullText);
        Layout.Engine.Measure(Switching(), new Layout.Size<int>(110, 100), new PxCtx()).Width.ShouldBe(110);
    }

    [Fact]
    public void NarrowFormOneUnitBelowTheNaturalWidth()
    {
        var a = ArrangeInt(Switching(), new PxCtx(), 109);
        a.Length.ShouldBe(1);
        TextOf(a[0]).ShouldBe(NarrowText);
        a[0].Bounds.Width.ShouldBe(109);
        Layout.Engine.Measure(Switching(), new Layout.Size<int>(109, 100), new PxCtx()).Width.ShouldBe(20);
    }

    [Fact]
    public void AnUnconstrainedWidthTakesTheFullForm()
    {
        Layout.Engine.Measure(Switching(), new Layout.Size<int>(10_000, 100), new PxCtx()).Width.ShouldBe(110);
    }

    [Fact]
    public void ChoiceInAnHStackSiblingContextFollowsTheRoomOffered()
    {
        var row = Layout.Builder.HStack(Switching(), Layout.Builder.Text("abc"));

        var wide = ArrangeInt(row, new PxCtx(), 200);
        TextOf(wide.First(n => n.Depth == 1)).ShouldBe(FullText);

        // A child's slot is its measured width, so measure and arrange pick the same form.
        var narrowRow = ArrangeInt(Layout.Builder.HStack(Switching()), new PxCtx(), 50);
        var first = narrowRow.First(n => n.Depth == 1);
        TextOf(first).ShouldBe(NarrowText);
    }

    [Fact]
    public void MeasureAndArrangeAgree()
    {
        var ctx = new PxCtx();
        foreach (var w in new[] { 50, 109, 110, 111, 300 })
        {
            var measured = Layout.Engine.Measure(Switching(), new Layout.Size<int>(w, 100), ctx);
            var arranged = ArrangeInt(Switching(), ctx, w)[0];
            var text = TextOf(arranged);
            (text == FullText).ShouldBe(w >= 110, $"width {w}");
            measured.Width.ShouldBe(text == FullText ? 110 : 20, $"width {w}");
        }
    }

    [Fact]
    public void FloatAndIntContextsAgree()
    {
        var px = new PxCtx();
        var fl = new FloatCtx();
        foreach (var w in new[] { 50, 109, 110, 111, 300 })
        {
            var i = TextOf(ArrangeInt(Switching(), px, w)[0]);
            var f = Layout.Engine.Arrange(Switching(), new Rect<float>(0, 0, w, 100), fl)[0];
            ((Layout.Content.Text)((Layout.Node.Leaf)f.Node).Content).Value.ShouldBe(i, $"width {w}");
        }
    }

    [Fact]
    public void CellContextChoosesInCells()
    {
        var ctx = new CellCtx();
        TextOf(ArrangeInt(Switching(), ctx, 11)[0]).ShouldBe(FullText);
        TextOf(ArrangeInt(Switching(), ctx, 10)[0]).ShouldBe(NarrowText);
    }

    [Fact]
    public void AClickRegionExistsOnlyForTheChosenForm()
    {
        var ctx = new PxCtx();
        var full = ArrangeInt(Switching(), ctx, 110);
        full.Select(n => n.Node.Hit).OfType<HitResult.ButtonHit>().Select(h => h.Action).ShouldBe(["full"]);

        var narrow = ArrangeInt(Switching(), ctx, 109);
        narrow.Select(n => n.Node.Hit).OfType<HitResult.ButtonHit>().Select(h => h.Action).ShouldBe(["narrow"]);
    }

    [Fact]
    public void ANarrowFormIsNotMeasuredAgainstAThirdForm()
    {
        var third = Layout.Builder.Text("x");
        var node = Layout.Builder.Text(FullText)
            .OrWhenNarrow(Layout.Builder.Text(NarrowText).OrWhenNarrow(third));
        TextOf(ArrangeInt(node, new PxCtx(), 5)[0]).ShouldBe(NarrowText);
    }

    [Fact]
    public void ANodeWithoutANarrowFormArrangesAsBefore()
    {
        var plain = Layout.Builder.Text(FullText);
        plain.NarrowForm.ShouldBeNull();
        var a = ArrangeInt(plain, new PxCtx(), 20);
        a.Length.ShouldBe(1);
        TextOf(a[0]).ShouldBe(FullText);
        ReferenceEquals(a[0].Node, plain).ShouldBeTrue();
    }
}
