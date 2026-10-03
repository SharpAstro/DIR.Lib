using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A widget's own <see cref="PixelWidgetBase{TSurface}.InterfaceScale"/> over the window's DPI: the dial for a host whose
/// chrome should be a tad larger than an image viewer it embeds, whose toolbar keeps the size it has in its own
/// application. The widget's declared nodes and its own arithmetic must agree on the scale, and a widget sharing the
/// window keeps the window's DPI.
/// </summary>
public class InterfaceScaleTests
{
    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public Layout.ArrangedNode<float> Arrange(Layout.Node root)
            => ArrangeLayout(root, new RectF32(0, 0, 400, 400), fontPath: "stub.ttf").Single(n => n.Node.Hit is not null);
    }

    private sealed class Chrome(Renderer<RgbaImage> renderer, Widget child) : CompositeWidget<RgbaImage>(renderer)
    {
        protected override IReadOnlyList<PixelWidgetBase<RgbaImage>> Children => [child];

        public void Share() => ShareUiContext(child);

        public Layout.ArrangedNode<float> Arrange(Layout.Node root)
            => ArrangeLayout(root, new RectF32(0, 0, 400, 400), fontPath: "stub.ttf").Single(n => n.Node.Hit is not null);
    }

    [Fact]
    public void A_widgets_own_scale_is_in_its_dpi_and_in_its_layout_and_not_in_the_window_it_shares()
    {
        var renderer = new RgbaImageRenderer(400, 400);
        var viewer = new Widget(renderer);
        var chrome = new Chrome(renderer, viewer) { DpiScale = 2f };
        chrome.Share();

        chrome.InterfaceScale = 1.25f;

        chrome.DpiScale.ShouldBe(2.5f, "the window's DPI times the chrome's own scale");
        chrome.Scale.X.ShouldBe(chrome.DpiScale, "the layout's scale is the one the widget's own arithmetic reads");
        chrome.Ui.DpiScale.ShouldBe(2f, "the window's DPI is untouched");
        viewer.DpiScale.ShouldBe(2f, "an embedded widget keeps the window's DPI unless it is set on its own");

        // In a row: the arrange root fills its bounds, so a box declared alone would be the whole rect.
        var box = Layout.Builder.HStack(
            Layout.Builder.Spacer().WFixed(10f).HStar().Clickable(new HitResult.ButtonHit("box")),
            Layout.Builder.Spacer().WStar());
        chrome.Arrange(box).Bounds.Width.ShouldBe(25f, "a declared node is laid out at the widget's scale");
        viewer.Arrange(box).Bounds.Width.ShouldBe(20f);

        // Setting DpiScale sets the WINDOW's DPI, whichever widget it is set through; each keeps its own scale over it.
        viewer.DpiScale = 1f;
        (chrome.DpiScale, viewer.DpiScale).ShouldBe((1.25f, 1f));
    }
}
