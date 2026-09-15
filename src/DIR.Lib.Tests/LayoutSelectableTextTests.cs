using System;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Content.Text.Selectable"/> and its <c>.Selectable()</c> modifier: a read-only run
/// saying that its characters are content the reader may want to take away, not chrome.
/// <para>
/// A raster host draws glyphs into a texture, so there is nothing to select unless the run is put into the
/// host's selection layer -- the same layer a <see cref="HitResult.LinkHit"/> already routes text through
/// (<see cref="PixelWidgetBase{TSurface}.DrawSelectableText"/>). Until now only a LINK could reach it, so a
/// coordinate, an error message or a file path drawn in a panel was untakeable by construction and the two
/// hosts that could have offered selection (a DOM one natively, a terminal one by drag) were never told
/// which runs to offer.
/// </para>
/// <para>
/// No interaction in this wave: the run is registered, and what a raster host does with the registration
/// is the router's. What is pinned here is that the DECLARATION reaches the region, that it composes with
/// a link rather than colliding with it, and that an unmarked run still takes the plain path -- otherwise
/// every label in a window starts landing in the selection layer.
/// </para>
/// </summary>
public class LayoutSelectableTextTests
{
    /// <summary>Answers MeasureText itself so no real font is loaded, and records what was rastered.</summary>
    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public List<string> Rastered { get; } = [];

        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
            => Rastered.Add(text.ToString());
    }

    private sealed class TextWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public SelectableTextRegion[] Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "stub.ttf", scale: DesignScale.One);
            return SelectableTextRegions.ToArray();
        }
    }

    private static (TextWidget Widget, StubRenderer Renderer) Fixture()
    {
        var renderer = new StubRenderer(200, 50);
        return (new TextWidget(renderer), renderer);
    }

    private static Layout.Node Run(string value) => Layout.Builder.Text(value, 10f).WStar().HStar();

    [Fact]
    public void AMarkedRunIsRegisteredForSelection()
    {
        var (widget, _) = Fixture();

        var regions = widget.Render(Run("12h 31m 49s").Selectable(), new RectF32(0f, 0f, 200f, 20f));

        var region = regions.ShouldHaveSingleItem();
        region.Text.ShouldBe("12h 31m 49s");
        region.Href.ShouldBeNull("a selectable run is a span, not an anchor -- it has nowhere to navigate to");
    }

    [Fact]
    public void AnUnmarkedRunIsNot()
    {
        var (widget, _) = Fixture();

        widget.Render(Run("Declination"), new RectF32(0f, 0f, 200f, 20f))
            .ShouldBeEmpty("every label in a window would otherwise land in the host's selection layer");
    }

    [Fact]
    public void AMarkedRunStillDrawsItsGlyphsExactlyOnce()
    {
        var (widget, renderer) = Fixture();

        widget.Render(Run("12h 31m 49s").Selectable(), new RectF32(0f, 0f, 200f, 20f));

        renderer.Rastered.ShouldBe(["12h 31m 49s"],
            "the selection layer is an addition to the paint, not a replacement for it");
    }

    [Fact]
    public void TheRegionIsTheArrangedRect_SoTheSelectionCoversWhatWasDrawn()
    {
        var (widget, _) = Fixture();

        var region = widget.Render(Run("value").Selectable(), new RectF32(12f, 8f, 140f, 18f))
            .ShouldHaveSingleItem();

        region.X.ShouldBe(12f);
        region.Y.ShouldBe(8f);
        region.Width.ShouldBe(140f);
        region.Height.ShouldBe(18f);
    }

    /// <summary>
    /// The two declarations compose. A link was already selectable by virtue of taking this path, and
    /// marking it as well must not turn the anchor into a plain span -- which is what dropping the Href on
    /// the "selectable" branch would silently do.
    /// </summary>
    [Fact]
    public void ALinkThatIsAlsoMarkedKeepsItsHref()
    {
        var (widget, _) = Fixture();

        var region = widget.Render(
            Run("the report").Selectable().Clickable(new HitResult.LinkHit("https://example.com/r")),
            new RectF32(0f, 0f, 200f, 20f)).ShouldHaveSingleItem();

        region.Href.ShouldBe("https://example.com/r");
    }

    [Fact]
    public void ALinkWithNoMarkIsUnchanged()
    {
        var (widget, _) = Fixture();

        var region = widget.Render(
            Run("the report").Clickable(new HitResult.LinkHit("https://example.com/r")),
            new RectF32(0f, 0f, 200f, 20f)).ShouldHaveSingleItem();

        region.Href.ShouldBe("https://example.com/r");
    }

    [Fact]
    public void TheModifierIsANoOpOnANodeThatIsNotATextRun()
    {
        // .Selectable() is chainable on any node like every other modifier, so it has to be harmless on the
        // ones it cannot mean anything for rather than throwing at tree-build time.
        var stack = Layout.Builder.VStack(Run("a"), Run("b"));

        stack.Selectable().ShouldBe(stack);
        Layout.Builder.Spacer().Selectable().ShouldBe(Layout.Builder.Spacer());
    }

    [Fact]
    public void TheMarkCanBeTakenOffAgain()
    {
        var (widget, _) = Fixture();

        widget.Render(Run("value").Selectable().Selectable(false), new RectF32(0f, 0f, 200f, 20f))
            .ShouldBeEmpty();
    }
}
