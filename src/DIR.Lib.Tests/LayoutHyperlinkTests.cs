using System;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Hyperlinks reaching the pixel surface from a layout tree.
///
/// <para>
/// A node states a link by carrying a <see cref="HitResult.LinkHit"/> — the hit it already needed for the
/// click. <see cref="PixelWidgetBase{TSurface}.PaintLayout"/> has always bound that hit to the arranged
/// rect; what it did not do was give the run a <see cref="SelectableTextRegion.Href"/>, so a DOM host — the
/// one host that CAN render a real <c>&lt;a href&gt;</c> — saw a bare clickable rectangle and had to
/// reimplement new-tab, open and copy-link itself. The <c>Href</c> mechanism existed, but only
/// <see cref="PixelWidgetBase{TSurface}.DrawSelectableText"/> could reach it, and no layout tree calls that.
/// </para>
///
/// <para>
/// This is the pixel half of the same authored concept Console.Lib's <c>CellLayout</c> paints as an OSC 8
/// wrap. One tree, one way to say "link", three surface realisations: OSC 8 on a terminal, an anchor on the
/// web, a plain clickable rect on raster (which has no navigation model and correctly ignores it).
/// </para>
/// </summary>
public class LayoutHyperlinkTests
{
    private const string Target = "https://example.com/report";

    /// <summary>Answers MeasureText itself so no real font file is loaded, and swallows the raster.</summary>
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

    private sealed class LinkWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public SelectableTextRegion[] Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "stub.ttf", dpiScale: 1f);
            return SelectableTextRegions.ToArray();
        }

        public ClickableRegion[] Clickables => GetRegisteredRegions();
    }

    private static Layout.Node Text(string value) =>
        Layout.Builder.Text(value, 10f).WStar().HStar();

    private static (LinkWidget Widget, StubRenderer Renderer) Fixture()
    {
        var renderer = new StubRenderer(200, 50);
        return (new LinkWidget(renderer), renderer);
    }

    [Fact]
    public void TextUnderALinkHit_IsRegisteredAsAnAnchor()
    {
        var (widget, _) = Fixture();

        var regions = widget.Render(Text("report").Clickable(new HitResult.LinkHit(Target)),
            new RectF32(0, 0, 200, 50));

        var run = regions.ShouldHaveSingleItem();
        run.Text.ShouldBe("report");
        run.Href.ShouldBe(Target);
    }

    /// <summary>
    /// The shape a row really has: the link on a wrapper, the text a leaf beneath it. Resolved through the
    /// nearest-enclosing walk, so stating it one level up is not a silent no-op — and resolved the SAME way
    /// the cell painter resolves it, or one authored tree would mean two different things.
    /// </summary>
    [Fact]
    public void ALinkOnAnAncestor_ReachesTheTextUnderneath()
    {
        var (widget, _) = Fixture();

        var tree = Layout.Builder.HStack(Text("report"))
            .Clickable(new HitResult.LinkHit(Target));

        widget.Render(tree, new RectF32(0, 0, 200, 50))
            .ShouldHaveSingleItem().Href.ShouldBe(Target);
    }

    [Fact]
    public void ASiblingOutsideTheLinkedSubtree_IsNotAnchored()
    {
        var (widget, _) = Fixture();

        var tree = Layout.Builder.HStack(
            Layout.Builder.HStack(Text("linked")).WStar().HStar().Clickable(new HitResult.LinkHit(Target)),
            Text("plain"));

        var regions = widget.Render(tree, new RectF32(0, 0, 200, 50));

        regions.ShouldHaveSingleItem().Text.ShouldBe("linked", "only the linked subtree becomes an anchor");
    }

    [Fact]
    public void ANestedLink_OverridesTheOneAroundIt()
    {
        var (widget, _) = Fixture();

        var tree = Layout.Builder.HStack(Text("inner").Clickable(new HitResult.LinkHit("https://inner")))
            .Clickable(new HitResult.LinkHit(Target));

        widget.Render(tree, new RectF32(0, 0, 200, 50))
            .ShouldHaveSingleItem().Href.ShouldBe("https://inner");
    }

    /// <summary>Only a LinkHit is a link. A button is clickable and is not somewhere a browser can navigate.</summary>
    [Fact]
    public void AnOrdinaryHit_ProducesNoAnchor()
    {
        var (widget, _) = Fixture();

        widget.Render(Text("delete").Clickable(new HitResult.ButtonHit("delete")), new RectF32(0, 0, 200, 50))
            .ShouldBeEmpty();
    }

    /// <summary>Unlinked layout text must not start landing in the host's selection layer.</summary>
    [Fact]
    public void UnlinkedText_StaysOffTheSelectionLayer()
    {
        var (widget, renderer) = Fixture();

        widget.Render(Text("plain"), new RectF32(0, 0, 200, 50)).ShouldBeEmpty();
        renderer.Rastered.ShouldContain("plain");
    }

    /// <summary>
    /// The click binding is unchanged and still applies on every host — the anchor is the navigation
    /// affordance layered on top of it, not a replacement. A raster host with no DOM keeps working exactly
    /// as before.
    /// </summary>
    [Fact]
    public void ALinkedNode_IsStillAnOrdinaryClickRegion()
    {
        var (widget, renderer) = Fixture();

        widget.Render(Text("report").Clickable(new HitResult.LinkHit(Target)), new RectF32(0, 0, 200, 50));

        // Any(), not ShouldContain(): Shouldly takes an expression tree there, which cannot hold an `is` pattern.
        widget.Clickables.Any(r => r.Result is HitResult.LinkHit hit && hit.Url == Target)
            .ShouldBeTrue("a linked node is still bound as a click region");
        renderer.Rastered.ShouldContain("report",
            "a host without a DOM text layer still gets the glyphs rastered");
    }

    /// <summary>
    /// When the host paints selectable text natively, the linked run must NOT also be rastered — that is
    /// the double-draw <see cref="Renderer{TSurface}.HostRendersSelectableText"/> exists to prevent, and
    /// routing layout text through the selectable path has to honour it rather than reinvent it.
    /// </summary>
    [Fact]
    public void OnAHostThatPaintsItsOwnText_TheLinkedRunIsNotRasteredTwice()
    {
        var (widget, renderer) = Fixture();
        renderer.HostRendersSelectableText = true;

        var regions = widget.Render(Text("report").Clickable(new HitResult.LinkHit(Target)),
            new RectF32(0, 0, 200, 50));

        regions.ShouldHaveSingleItem().Href.ShouldBe(Target);
        renderer.Rastered.ShouldBeEmpty("the host's own text is the only copy on screen");
    }
}
