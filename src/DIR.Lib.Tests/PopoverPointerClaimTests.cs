using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// An open popover's claim on the pointer confines the hover of what is painted BEFORE it as well as after
/// (<see cref="WindowUiSettings.PointerClaim"/>). A popover paints last, over the chrome it covers, and its claim is
/// made by being painted; read from <see cref="WindowUiSettings.PointerOwner"/> alone, a declared button painted
/// earlier in the frame lit under the menu's backdrop, which is why consumers kept a hand-written "does an overlay
/// own the pointer" prediction beside their chrome.
/// </summary>
public class PopoverPointerClaimTests
{
    private static readonly RGBAColor32 Rest = new(0x20, 0x40, 0x60, 0xff);
    private static readonly RGBAColor32 Hot = new(0xc0, 0x30, 0x10, 0xff);
    private static readonly RectF32 ButtonRect = new(0f, 0f, 40f, 20f);

    /// <summary>A bar painted first and a popover painted after it, in two calls, as a host paints chrome and then
    /// its overlays.</summary>
    private sealed class TwoPassWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Paint(PopoverState popover, (float X, float Y) pointer)
        {
            Pointer = pointer;
            BeginFrame();
            RenderLayout(Layout.Builder.Spacer().Stretch().Bg(Rest).BgHover(Hot), ButtonRect,
                fontPath: "stub.ttf", scale: DesignScale.One);
            RenderLayout(Layout.Builder.Popover(new RectF32(60f, 40f, 20f, 10f), Layout.Builder.Box(30f, 30f), popover),
                new RectF32(0f, 0f, 200f, 200f), fontPath: "stub.ttf", scale: DesignScale.One);
        }

        /// <summary>The query hand-painted chrome asks, by the same rule.</summary>
        public bool Hovers(RectF32 rect) => PointerHovers(rect);
    }

    private static bool ButtonIsLit(DeclarationStubRenderer renderer)
    {
        var img = renderer.Surface;
        var i = ((5 * img.Width) + 5) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]) == Hot;
    }

    [Fact]
    public void AButtonPaintedBeforeAnOpenPopoverDoesNotLightUnderItsBackdrop()
    {
        var renderer = new DeclarationStubRenderer(200, 200);
        var widget = new TwoPassWidget(renderer);
        var redraws = 0;
        var router = new InputRouter(widget.Ui, new BackgroundTaskTracker(), () => redraws++) { Widgets = () => [widget] };
        var popover = new PopoverState();
        popover.Open();
        var onTheButton = (10f, 10f);

        // The frame the popover opens in has no claim to carry: the button, painted first, lights, and the router
        // asks for the next frame, since the claim the frame ended with is not the one it began with.
        widget.Paint(popover, onTheButton);
        router.AfterPaint();
        widget.Ui.PointerClaimSettled.ShouldBeFalse("the popover claimed the pointer during this frame");
        redraws.ShouldBeGreaterThan(0, "so the router asks for a frame that carries the claim");

        // That frame carries it: the button, painted before the popover again, is confined to the popover's content.
        redraws = 0;
        widget.Paint(popover, onTheButton);
        router.AfterPaint();
        ButtonIsLit(renderer).ShouldBeFalse("the open popover's backdrop covers the button");
        widget.Ui.PointerClaimSettled.ShouldBeTrue();
        redraws.ShouldBe(0, "a settled claim asks for nothing more");
    }

    [Fact]
    public void AClosedPopoverLiftsItsClaimAtTheNextFrame()
    {
        var renderer = new DeclarationStubRenderer(200, 200);
        var widget = new TwoPassWidget(renderer);
        var popover = new PopoverState();
        popover.Open();
        widget.Paint(popover, (10f, 10f));
        widget.Paint(popover, (10f, 10f));
        ButtonIsLit(renderer).ShouldBeFalse();

        popover.Close();
        widget.Paint(popover, (10f, 10f));

        ButtonIsLit(renderer).ShouldBeTrue("a popover that closed claims nothing, carried or not");
        widget.Ui.PointerClaimSettled.ShouldBeTrue();
    }

    /// <summary>
    /// Chrome that still decides its own highlight asks <c>PointerHovers</c> and gets the painter's answer: no under
    /// an open popover's backdrop, yes once it has closed. Before, such chrome kept a list of its popovers beside it.
    /// </summary>
    [Fact]
    public void HandPaintedChromeAsksTheSameRuleAndGetsTheSameAnswer()
    {
        var widget = new TwoPassWidget(new DeclarationStubRenderer(200, 200));
        var popover = new PopoverState();
        popover.Open();
        widget.Paint(popover, (10f, 10f));
        widget.Paint(popover, (10f, 10f));

        widget.Hovers(ButtonRect).ShouldBeFalse("the open popover's backdrop covers it");

        popover.Close();
        widget.Paint(popover, (10f, 10f));
        widget.Hovers(ButtonRect).ShouldBeTrue();
        widget.Hovers(new RectF32(100f, 100f, 10f, 10f)).ShouldBeFalse("the pointer is not there");
    }

    [Fact]
    public void WithNoPopoverTheButtonLightsAsEver()
    {
        var renderer = new DeclarationStubRenderer(200, 200);
        var widget = new TwoPassWidget(renderer);
        var popover = new PopoverState();

        widget.Paint(popover, (10f, 10f));
        widget.Paint(popover, (10f, 10f));

        ButtonIsLit(renderer).ShouldBeTrue();
        widget.Ui.PointerClaim.ShouldBeNull();
    }
}
