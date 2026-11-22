using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Node.PressedBackground"/> and <see cref="Content.Text.UnderlineOnHover"/>: the two states a themed
/// button and link need. The pressed fill is recorded by <see cref="InputRouter"/> from real MouseDown and MouseUp events
/// and painted by <see cref="PixelWidgetBase{TSurface}"/>, read back as pixels. In memory, no clock, no real font.
/// </summary>
public class PressedBackgroundTests
{
    private static readonly RGBAColor32 Rest = new(0x20, 0x40, 0x60, 0xff);
    private static readonly RGBAColor32 Hot = new(0xc0, 0x30, 0x10, 0xff);
    private static readonly RGBAColor32 Down = new(0x10, 0xd0, 0x40, 0xff);
    private static readonly RGBAColor32 Ink = new(0xf0, 0xf0, 0x20, 0xff);

    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        // No glyphs: any pixel of the ink colour in a text test is the underline.
        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    private sealed class TestWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "stub.ttf", scale: DesignScale.One);
        }
    }

    private sealed class Harness
    {
        public StubRenderer Renderer { get; } = new(100, 100);
        public TestWidget Widget { get; }
        public InputRouter Router { get; }
        public int Redraws;
        public Layout.Node Root = Layout.Builder.Spacer();

        public Harness()
        {
            Widget = new TestWidget(Renderer);
            Router = new InputRouter(Widget.Ui, new BackgroundTaskTracker(), () => Redraws++)
            {
                Widgets = () => [Widget],
            };
        }

        public void Paint()
        {
            Widget.Render(Root, new RectF32(10, 10, 40, 40));
            Router.AfterPaint();
        }

        public void Move(float x, float y) => Router.Handle(new InputEvent.MouseMove(x, y));
        public void Press(float x, float y) => Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
        public void Release(float x, float y) => Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left));

        public RGBAColor32 Pixel(int x, int y)
        {
            var img = Renderer.Surface;
            var i = (y * img.Width + x) * 4;
            return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
        }

        public int Count(RGBAColor32 c)
        {
            var n = 0;
            for (var y = 0; y < 100; y++)
            {
                for (var x = 0; x < 100; x++)
                {
                    if (Pixel(x, y) == c) n++;
                }
            }

            return n;
        }
    }

    private static Layout.Node Button() => Layout.Builder.Spacer().Stretch()
        .Bg(Rest).BgHover(Hot).BgPressed(Down).Clickable(new HitResult.ButtonHit("b"));

    private static Harness WithButton(Layout.Node? node = null)
    {
        var h = new Harness { Root = node ?? Button() };
        h.Paint();
        return h;
    }

    [Fact]
    public void HeldOverTheNode_PaintsThePressedFill()
    {
        var h = WithButton();
        h.Move(30, 30);
        h.Press(30, 30);
        h.Paint();

        h.Pixel(30, 30).ShouldBe(Down);
    }

    [Fact]
    public void BothEdgesAskForARedraw()
    {
        var h = WithButton();
        h.Move(30, 30);
        h.Redraws = 0;

        h.Press(30, 30);
        h.Redraws.ShouldBeGreaterThan(0);

        h.Redraws = 0;
        h.Release(30, 30);
        h.Redraws.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void AfterRelease_FallsBackToHover()
    {
        var h = WithButton();
        h.Move(30, 30);
        h.Press(30, 30);
        h.Paint();
        h.Release(30, 30);
        h.Paint();

        h.Pixel(30, 30).ShouldBe(Hot);
    }

    [Fact]
    public void DraggedOffWhileHeld_PaintsNoPressedFill()
    {
        var h = WithButton();
        h.Move(30, 30);
        h.Press(30, 30);
        h.Move(80, 80);
        h.Paint();

        h.Pixel(30, 30).ShouldBe(Rest);
    }

    [Fact]
    public void PressedElsewhere_ThenDraggedOver_PaintsHoverNotPressed()
    {
        var h = WithButton();
        h.Press(80, 80);
        h.Move(30, 30);
        h.Paint();

        h.Pixel(30, 30).ShouldBe(Hot);
    }

    [Fact]
    public void ADisabledNode_ShowsNeitherPressedNorHover()
    {
        var h = WithButton(Button().Disabled("not now"));
        h.Move(30, 30);
        h.Paint();
        h.Pixel(30, 30).ShouldBe(Rest);

        h.Press(30, 30);
        h.Paint();
        h.Pixel(30, 30).ShouldBe(Rest);
    }

    [Fact]
    public void ANodeWithNoPressedBackground_StillShowsHoverWhileHeld()
    {
        var h = WithButton(Layout.Builder.Spacer().Stretch().Bg(Rest).BgHover(Hot).Clickable(new HitResult.ButtonHit("b")));
        h.Move(30, 30);
        h.Press(30, 30);
        h.Paint();

        h.Pixel(30, 30).ShouldBe(Hot);
    }

    [Fact]
    public void ThePressedFillWinsOverHover()
    {
        // Pointer is within the node (hover would paint Hot) and the button is held: Down wins.
        var h = WithButton();
        h.Move(30, 30);
        h.Paint();
        h.Pixel(30, 30).ShouldBe(Hot);

        h.Press(30, 30);
        h.Paint();
        h.Pixel(30, 30).ShouldBe(Down);
    }

    [Fact]
    public void PressedBackground_IsInitOnly_AndSetByBgPressed()
    {
        Layout.Builder.Spacer().BgPressed(Down).PressedBackground.ShouldBe(Down);
        Layout.Builder.Spacer().PressedBackground.ShouldBeNull();
    }

    // ---- presses a layer over the button takes ----

    [Fact]
    public void APressASurfaceTakes_LightsNothingBeneath()
    {
        // A transparent surface over the whole button: the button shows through it, and a press on it is the surface's.
        var h = WithButton(Layout.Builder.Overlay(Button(), Layout.Builder.Spacer().Stretch().AsSurface()));
        h.Paint();
        h.Move(30, 30);
        h.Press(30, 30);
        h.Paint();

        h.Widget.Ui.PressOrigin.ShouldBeNull();
        h.Pixel(30, 30).ShouldNotBe(Down);
    }

    [Fact]
    public void APressAPopoverBackdropTakes_LightsNothingBeneath()
    {
        var state = new PopoverState();
        state.Open();
        var content = Layout.Builder.Box(4, 4, Ink);
        var h = WithButton(Layout.Builder.Overlay(Button(), Layout.Builder.Popover(new RectF32(10, 10, 4, 4), content, state)));
        h.Paint();
        h.Move(40, 40);
        h.Press(40, 40);
        state.IsOpen.ShouldBeFalse("the backdrop's press closes the popover");
        h.Paint();

        // Still held over the button, now uncovered: hover, never the pressed fill of a press it did not take.
        h.Widget.Ui.PressOrigin.ShouldBeNull();
        h.Pixel(40, 40).ShouldBe(Hot);
    }

    // A modal card at the widget's top left (10,10)-(26,26), over the button; outside it the press is the modal's.
    private static Layout.Node ModalOverButton() => Layout.Builder.Modal(Button(),
        Layout.Builder.Anchored(Layout.Builder.Box(16, 16).Bg(Rest).BgHover(Hot).BgPressed(Down)
            .Clickable(new HitResult.ButtonHit("card"))));

    [Fact]
    public void APressOutsideAModalCard_LightsNothingBeneath()
    {
        var h = WithButton(ModalOverButton());
        h.Paint();
        h.Move(40, 40);
        h.Press(40, 40);
        h.Paint();

        h.Widget.Ui.PressOrigin.ShouldBeNull();
        h.Pixel(40, 40).ShouldNotBe(Down);
    }

    [Fact]
    public void APressInsideAModalCard_LightsTheCardsButton()
    {
        var h = WithButton(ModalOverButton());
        h.Paint();
        h.Move(15, 15);
        h.Press(15, 15);
        h.Paint();

        h.Pixel(15, 15).ShouldBe(Down);
        h.Release(15, 15);
        h.Paint();
        h.Pixel(15, 15).ShouldBe(Hot);
    }

    // ---- widgets that share no settings ----

    [Fact]
    public void WidgetsWithUnsharedSettings_EachPaintTheirOwnPressedFill()
    {
        // Two widgets, each with its own WindowUiSettings, and a router on a third: the press reaches the widget that
        // paints the button only through the router writing it to every listed widget.
        var lowerRenderer = new StubRenderer(100, 100);
        var upperRenderer = new StubRenderer(100, 100);
        var lower = new TestWidget(lowerRenderer);
        var upper = new TestWidget(upperRenderer);
        var router = new InputRouter(new WindowUiSettings(), new BackgroundTaskTracker(), static () => { })
        {
            Widgets = () => [lower, upper],
        };
        lower.Ui.ShouldNotBeSameAs(upper.Ui);

        void Paint()
        {
            lower.Render(Button(), new RectF32(0, 0, 40, 40));
            upper.Render(Button(), new RectF32(50, 50, 40, 40));
            router.AfterPaint();
        }

        static RGBAColor32 PixelOf(StubRenderer r, int x, int y)
        {
            var img = r.Surface;
            var i = (y * img.Width + x) * 4;
            return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
        }

        Paint();

        // The upper widget's button.
        router.Handle(new InputEvent.MouseMove(70, 70));
        router.Handle(new InputEvent.MouseDown(70, 70, MouseButton.Left, InputModifier.None, 1));
        Paint();
        PixelOf(upperRenderer, 70, 70).ShouldBe(Down);
        PixelOf(lowerRenderer, 20, 20).ShouldBe(Rest);
        router.Handle(new InputEvent.MouseUp(70, 70, MouseButton.Left));
        Paint();
        PixelOf(upperRenderer, 70, 70).ShouldBe(Hot);

        // The lower widget's button.
        router.Handle(new InputEvent.MouseMove(20, 20));
        router.Handle(new InputEvent.MouseDown(20, 20, MouseButton.Left, InputModifier.None, 1));
        Paint();
        PixelOf(lowerRenderer, 20, 20).ShouldBe(Down);
        PixelOf(upperRenderer, 70, 70).ShouldBe(Rest);
        router.Handle(new InputEvent.MouseUp(20, 20, MouseButton.Left));
        Paint();
        PixelOf(lowerRenderer, 20, 20).ShouldBe(Hot);
    }

    private static Layout.Node Link() => Layout.Builder.Text("link", 14f, Ink).Stretch().Bg(Rest)
        .UnderlineOnHover().Clickable(new HitResult.ButtonHit("l"));

    [Fact]
    public void Underline_PaintsUnderHoveredTextOnly()
    {
        var h = WithButton(Link());
        h.Count(Ink).ShouldBe(0);

        h.Move(30, 30);
        h.Paint();
        var hovered = h.Count(Ink);
        hovered.ShouldBeGreaterThan(0);

        // The rule is one unit tall: its pixels are one row, as wide as the measured run (4 chars * 7 = 28).
        hovered.ShouldBe(28);

        h.Move(90, 90);
        h.Paint();
        h.Count(Ink).ShouldBe(0);
    }
}
