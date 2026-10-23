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

    /// <summary>A pane painted first, the whole surface, and a popover over it whose content lights on hover, as a file
    /// list sits under a toolbar menu.</summary>
    private sealed class PaneUnderPopoverWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public static readonly RectF32 Pane = new(0f, 0f, 200f, 200f);

        public void Paint(PopoverState popover, (float X, float Y) pointer)
        {
            Pointer = pointer;
            BeginFrame();
            RenderLayout(Layout.Builder.Spacer().Stretch().Bg(Rest).BgHover(Hot), Pane, fontPath: "stub.ttf", scale: DesignScale.One);
            RenderLayout(Layout.Builder.Popover(new RectF32(60f, 40f, 20f, 10f),
                    Layout.Builder.Box(30f, 30f).Bg(Rest).BgHover(Hot), popover),
                Pane, fontPath: "stub.ttf", scale: DesignScale.One);
        }

        public bool Hovers(RectF32 rect) => PointerHovers(rect);
    }

    private static RGBAColor32 PixelAt(DeclarationStubRenderer renderer, float x, float y)
    {
        var img = renderer.Surface;
        var i = (((int)y * img.Width) + (int)x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    /// <summary>
    /// With the pointer OVER the popover, what is painted under it does not light: it is covered there. The claim used to
    /// be read as "the pointer inside the claim", true for every node under that point, so the viewer's file row under its
    /// stretch menu lit and its revealed name covered a menu item (2026-10-23). The popover's own content still lights.
    /// </summary>
    [Fact]
    public void WithThePointerOverThePopoverOnlyItsOwnContentLights()
    {
        var renderer = new DeclarationStubRenderer(200, 200);
        var widget = new PaneUnderPopoverWidget(renderer);
        var popover = new PopoverState();
        popover.Open();
        widget.Paint(popover, (5f, 5f));
        var claim = widget.Ui.PointerClaim ?? throw new InvalidOperationException("the open popover claimed nothing");
        var overThePopover = (claim.X + (claim.Width / 2f), claim.Y + (claim.Height / 2f));

        widget.Paint(popover, overThePopover);
        widget.Paint(popover, overThePopover);

        PixelAt(renderer, 5f, 5f).ShouldBe(Rest, "the pane under the popover lit where the popover covers it");
        PixelAt(renderer, overThePopover.Item1, overThePopover.Item2).ShouldBe(Hot, "the popover's own content is what is pointed at");
        widget.Hovers(PaneUnderPopoverWidget.Pane).ShouldBeFalse("hand-painted chrome under the popover answers as the pane does");
    }

    /// <summary>A pane that states a cursor painted first, the whole surface, and a popover over it whose content holds
    /// one node stating a cursor of its own and one stating none.</summary>
    private sealed class CursorUnderPopoverWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Paint(PopoverState popover)
        {
            BeginFrame();
            RenderLayout(Layout.Builder.Spacer().Stretch().Clickable(new HitResult.ChromeHit(), cursor: CursorKind.ResizeEW),
                PaneUnderPopoverWidget.Pane, fontPath: "stub.ttf", scale: DesignScale.One);
            RenderLayout(Layout.Builder.Popover(new RectF32(60f, 40f, 20f, 10f),
                    Layout.Builder.VStack(
                        Layout.Builder.Box(30f, 15f).Clickable(new HitResult.ChromeHit(), cursor: CursorKind.Pointer),
                        Layout.Builder.Box(30f, 15f)),
                    popover),
                PaneUnderPopoverWidget.Pane, fontPath: "stub.ttf", scale: DesignScale.One);
        }
    }

    /// <summary>
    /// What is under an open popover does not show its cursor through it, over the popover's content or over its backdrop:
    /// a region with no cursor is transparent to the lookup, so a slider's cursor showed through the viewer's toolbar menu
    /// (2026-10-23). The backdrop states the arrow; the content's own cursors, registered after it, still win.
    /// </summary>
    [Fact]
    public void NothingUnderAnOpenPopoverShowsItsCursorThroughIt()
    {
        var widget = new CursorUnderPopoverWidget(new DeclarationStubRenderer(200, 200));
        var popover = new PopoverState();
        popover.Open();
        widget.Paint(popover);
        widget.Paint(popover);
        var content = widget.Ui.PointerClaim ?? throw new InvalidOperationException("the open popover claimed nothing");
        var midX = content.X + (content.Width / 2f);

        widget.HitTestCursor(midX, content.Y + 4f).ShouldBe(CursorKind.Pointer, "the content's own cursor wins");
        widget.HitTestCursor(midX, content.Y + content.Height - 4f).ShouldBe(CursorKind.Default,
            "content stating no cursor shows the arrow, not the pane's cursor beneath");
        widget.HitTestCursor(5f, 190f).ShouldBe(CursorKind.Default, "the backdrop shows the arrow, not the pane's cursor beneath");

        popover.Close();
        widget.Paint(popover);
        widget.HitTestCursor(5f, 190f).ShouldBe(CursorKind.ResizeEW, "with the popover closed the pane's cursor is back");
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
