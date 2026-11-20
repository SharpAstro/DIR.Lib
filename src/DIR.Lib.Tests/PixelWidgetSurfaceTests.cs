using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The paint half of <see cref="Layout.Node.IsSurface"/>: a node painted before a surface does not light under it. The
/// hover fill resolves while the row paints, before the surface has, so the rule rides the popover's pointer claim: the
/// surface is carried from the last cycle, the frame it appears in lights the row once, and
/// <see cref="WindowUiSettings.PointerClaimSettled"/> is false then so the router asks for the frame that does not
/// (the precedent is <see cref="PopoverPointerClaimTests"/>).
/// </summary>
public class PixelWidgetSurfaceTests
{
    private static readonly RGBAColor32 Rest = new(0x20, 0x40, 0x60, 0xff);
    private static readonly RGBAColor32 Hot = new(0xc0, 0x30, 0x10, 0xff);
    private static readonly RGBAColor32 CardHot = new(0x10, 0xc0, 0x30, 0xff);
    private static readonly RectF32 Window = new(0, 0, 200, 200);

    /// <summary>A full-width row at the top, lit under the pointer.</summary>
    private static Layout.Node Page()
        => Layout.Builder.VStack(Layout.Builder.Spacer().WStar().RowH(20f).Bg(Rest).BgHover(Hot)).Stretch();

    /// <summary>A 100x100 card at the top left with no background of its own, so the row beneath shows in the capture,
    /// holding a hover row of its own at y 60..80.</summary>
    private static Layout.Node Layer(bool surface)
    {
        var card = Layout.Builder.VStack(
                Layout.Builder.Spacer().RowH(60f),
                Layout.Builder.Spacer().WStar().RowH(20f).BgHover(CardHot))
            .WFixed(100f).HFixed(100f);
        return Layout.Builder.HStack(surface ? card.AsSurface() : card, Layout.Builder.Spacer().WStar());
    }

    private static RGBAColor32 At(DeclarationStubRenderer renderer, int x, int y)
    {
        var img = renderer.Surface;
        var i = ((y * img.Width) + x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    /// <summary>A widget that hand-paints chrome before its tree, asking the hover rule a declared node lights by.</summary>
    private sealed class HandPaintedWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public bool HandChromeHovered { get; private set; }

        public void Render()
        {
            BeginFrame();
            HandChromeHovered = PointerHovers(new RectF32(0, 0, 200, 20));
            RenderLayout(Layer(surface: true), Window, fontPath: "stub.ttf", scale: DesignScale.One);
        }
    }

    [Fact]
    public void ARowPaintedByAnEarlierWidgetDoesNotLightUnderALaterWidgetsSurface()
    {
        var backRenderer = new DeclarationStubRenderer(200, 200);
        var back = new DeclarationWidget(backRenderer);
        var front = new DeclarationWidget(new DeclarationStubRenderer(200, 200));
        back.ShareWith(front);
        var redraws = 0;
        var router = new InputRouter(back.Ui, new BackgroundTaskTracker(), () => redraws++) { Widgets = () => [back, front] };
        back.Pointer = (10f, 10f);
        front.Pointer = (10f, 10f);

        void Frame()
        {
            back.Render(Page(), Window);
            front.Render(Layer(surface: true), Window);
            router.AfterPaint();
        }

        // The frame the surface appears in has nothing carried: the row lit, and the router asks for one more frame.
        Frame();
        back.Ui.PointerClaimSettled.ShouldBeFalse("a surface was painted that the cycle did not begin with");
        redraws.ShouldBeGreaterThan(0);

        redraws = 0;
        Frame();
        At(backRenderer, 10, 10).ShouldBe(Rest, "the card covers the row here");
        back.Ui.PointerClaimSettled.ShouldBeTrue();
        redraws.ShouldBe(0, "a settled surface asks for nothing more");

        // The row in the open lights as ever.
        back.Pointer = (150f, 10f);
        front.Pointer = (150f, 10f);
        Frame();
        At(backRenderer, 150, 10).ShouldBe(Hot);
    }

    [Fact]
    public void ARowPaintedByAnEarlierWidgetWithItsOwnSettingsDoesNotLightUnderALaterWidgetsSurface()
    {
        // The two widgets share NO settings, as a host paints its toasts in a widget it never shares with the page: the
        // router, which knows the paint order of both, carries the front's surfaces onto the back's settings.
        var backRenderer = new DeclarationStubRenderer(200, 200);
        var back = new DeclarationWidget(backRenderer);
        var front = new DeclarationWidget(new DeclarationStubRenderer(200, 200));
        back.Ui.ShouldNotBeSameAs(front.Ui);
        var redraws = 0;
        var router = new InputRouter(back.Ui, new BackgroundTaskTracker(), () => redraws++) { Widgets = () => [back, front] };
        router.Handle(new InputEvent.MouseMove(10f, 10f, MouseButton.None));

        void Frame()
        {
            back.Render(Page(), Window);
            front.Render(Layer(surface: true), Window);
            router.AfterPaint();
        }

        // The frame the surface appears in has nothing carried: the row lit, and the router asks for one more frame.
        redraws = 0;
        Frame();
        redraws.ShouldBeGreaterThan(0, "a surface appeared in a widget with settings of its own");

        redraws = 0;
        Frame();
        At(backRenderer, 10, 10).ShouldBe(Rest, "the card covers the row here, though the two widgets share no settings");
        redraws.ShouldBe(0, "a settled surface asks for nothing more");

        // A move within the covered part changes nothing the frame shows.
        router.Handle(new InputEvent.MouseMove(20f, 10f, MouseButton.None));
        redraws.ShouldBe(0, "nothing under the card lights, so a move there asks for no frame");

        Frame();
        At(backRenderer, 20, 10).ShouldBe(Rest);
        redraws.ShouldBe(0);

        // The row in the open lights as ever.
        router.Handle(new InputEvent.MouseMove(150f, 10f, MouseButton.None));
        Frame();
        At(backRenderer, 150, 10).ShouldBe(Hot);

        // The card goes: the cycle it went in carries nothing across, so the router asks for the frame that lights the row.
        router.Handle(new InputEvent.MouseMove(10f, 10f, MouseButton.None));
        redraws = 0;
        back.Render(Page(), Window);
        front.Render(Layout.Builder.Spacer(), Window);
        router.AfterPaint();
        redraws.ShouldBeGreaterThan(0, "the surface went");
        back.Render(Page(), Window);
        front.Render(Layout.Builder.Spacer(), Window);
        router.AfterPaint();
        At(backRenderer, 10, 10).ShouldBe(Hot);
    }

    [Fact]
    public void AWidgetSharingThePagesSettingsButPaintedAfterTheSurfaceStillLights()
    {
        // The page and a panel share settings; the toasts are a widget of their own between them (pilot's order: page,
        // toasts, cards panel). The toast covers the page's row but lies UNDER the panel's, so only the page's stays dark.
        var pageRenderer = new DeclarationStubRenderer(200, 200);
        var panelRenderer = new DeclarationStubRenderer(200, 200);
        var page = new DeclarationWidget(pageRenderer);
        var toasts = new DeclarationWidget(new DeclarationStubRenderer(200, 200));
        var panel = new DeclarationWidget(panelRenderer);
        page.ShareWith(panel);
        var router = new InputRouter(page.Ui, new BackgroundTaskTracker(), () => { }) { Widgets = () => [page, toasts, panel] };
        router.Handle(new InputEvent.MouseMove(10f, 10f, MouseButton.None));

        for (var i = 0; i < 3; i++)
        {
            page.Render(Page(), Window);
            toasts.Render(Layer(surface: true), Window);
            panel.Render(Page(), Window);
            router.AfterPaint();
        }

        At(pageRenderer, 10, 10).ShouldBe(Rest, "the page's row is under the toast");
        At(panelRenderer, 10, 10).ShouldBe(Hot, "the panel's row is painted over the toast, so it lights");
    }

    [Fact]
    public void ARowPaintedEarlierInTheSameTreeDoesNotLightUnderTheSurface_TheSurfacesOwnRowDoes()
    {
        var renderer = new DeclarationStubRenderer(200, 200);
        var widget = new DeclarationWidget(renderer);
        var tree = Layout.Builder.Overlay(Page(), Layer(surface: true));
        widget.Pointer = (10f, 10f);
        widget.Render(tree, Window);
        widget.Render(tree, Window);

        At(renderer, 10, 10).ShouldBe(Rest);

        widget.Pointer = (10f, 70f);
        widget.Render(tree, Window);
        At(renderer, 10, 70).ShouldBe(CardHot, "what the surface contains lights as ever");
    }

    [Fact]
    public void WithoutTheSurfaceTheRowLightsThroughTheCard_WhichIsTheBugThisPins()
    {
        var renderer = new DeclarationStubRenderer(200, 200);
        var widget = new DeclarationWidget(renderer);
        var tree = Layout.Builder.Overlay(Page(), Layer(surface: false));
        widget.Pointer = (10f, 10f);
        widget.Render(tree, Window);
        widget.Render(tree, Window);

        At(renderer, 10, 10).ShouldBe(Hot);
    }

    [Fact]
    public void HandPaintedChromeUnderASurfaceIsNotHovered()
    {
        var widget = new HandPaintedWidget(new DeclarationStubRenderer(200, 200)) { Pointer = (10f, 10f) };
        widget.Render();
        widget.Render();

        widget.HandChromeHovered.ShouldBeFalse("painted before the surface, under it");

        widget.Pointer = (150f, 10f);
        widget.Render();
        widget.HandChromeHovered.ShouldBeTrue();
    }

    [Fact]
    public void ASurfaceThatGoesAwayLiftsItsClaimAtTheNextFrame()
    {
        var renderer = new DeclarationStubRenderer(200, 200);
        var widget = new DeclarationWidget(renderer);
        var covered = Layout.Builder.Overlay(Page(), Layer(surface: true));
        widget.Pointer = (10f, 10f);
        widget.Render(covered, Window);
        widget.Render(covered, Window);
        At(renderer, 10, 10).ShouldBe(Rest);

        // The card is closed: the cycle it went in carries nothing, so the next frame lights the row.
        widget.Render(Page(), Window);
        widget.Render(Page(), Window);
        At(renderer, 10, 10).ShouldBe(Hot);
    }
}
