using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="WindowUiSettings.FrameId"/> — a widget answers hits only on a frame it actually drew in.
///
/// <para>Registering regions as you paint already makes a widget un-hittable WHERE it is not drawn. It
/// says nothing about WHEN. A host that stops calling a widget's render — an early return for a loading
/// screen, a modal covering the window, a panel toggled off — leaves the last frame's regions standing,
/// and a control that is no longer on screen goes on taking clicks and RUNNING their handlers. Nothing
/// inside the widget can notice: from in there, not being rendered is indistinguishable from not being
/// rendered yet.</para>
///
/// <para>So the window counts frames and the widget stamps what it registers. The stale set is then
/// recognisable as stale, and every read — hit test, dispatch, cursor, tab order, the inspector's picture
/// of the screen — declines it.</para>
/// </summary>
public class FrameScopedRegionTests
{
    /// <summary>A widget whose render is a single button, so a test can choose which frames it draws in.</summary>
    private sealed class ButtonWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public int Clicks { get; private set; }

        public void Render()
        {
            BeginFrame();
            RegisterClickable(0, 0, 100, 40, new HitResult.ButtonHit("go"), _ => Clicks++,
                cursor: CursorKind.Pointer);
        }
    }

    private static ButtonWidget Widget() => new(new RgbaImageRenderer(200, 100));

    [Fact]
    public void AWidgetDrawnThisFrameAnswersNormally()
    {
        var w = Widget();
        w.Ui.FrameId = 1;
        w.Render();

        w.HitTest(50, 20).ShouldBeOfType<HitResult.ButtonHit>();
        w.HitTestCursor(50, 20).ShouldBe(CursorKind.Pointer);
        w.HitTestAndDispatch(50, 20).ShouldNotBeNull();
        w.Clicks.ShouldBe(1);
    }

    [Fact]
    public void AWidgetTheHostStoppedDrawingStopsAnswering()
    {
        var w = Widget();
        w.Ui.FrameId = 1;
        w.Render();

        // Frame 2: the host drew something else — a loading screen — and never called the widget. Its
        // regions are last frame's, and this is the whole point: they must not answer for a control that
        // is not on screen.
        w.Ui.FrameId = 2;

        w.HitTest(50, 20).ShouldBeNull();
        w.HitTestCursor(50, 20).ShouldBeNull();
        w.HitTestAndDispatch(50, 20).ShouldBeNull();
        w.Clicks.ShouldBe(0);       // the handler is the part that would have done real damage
    }

    [Fact]
    public void ItAnswersAgainOnceItIsDrawnAgain()
    {
        var w = Widget();
        w.Ui.FrameId = 1;
        w.Render();
        w.Ui.FrameId = 2;
        w.HitTest(50, 20).ShouldBeNull();

        // Going quiet is not a latch: the widget is back the frame the host paints it again.
        w.Render();
        w.HitTest(50, 20).ShouldNotBeNull();
    }

    [Fact]
    public void AFieldOffScreenLeavesTheTabOrder()
    {
        var input = new TextInputState();
        var w = new FieldWidget(new RgbaImageRenderer(200, 100), input);
        w.Ui.FrameId = 1;
        w.Render();
        w.GetRegisteredTextInputs().ShouldHaveSingleItem();

        // Tab must not reach a field that is not being painted — the same rule
        // TextInputFocus.BlurIfUnpainted applies to the focus itself, which would otherwise disagree with
        // the tab order it is supposed to move through.
        w.Ui.FrameId = 2;
        w.GetRegisteredTextInputs().ShouldBeEmpty();
    }

    [Fact]
    public void TheInspectorSeesNoWidgetTheHostIsNotPainting()
    {
        var w = Widget();
        w.Ui.FrameId = 1;
        w.Render();
        w.GetRegisteredRegions().ShouldHaveSingleItem();

        w.Ui.FrameId = 2;
        w.GetRegisteredRegions().ShouldBeEmpty();
    }

    [Fact]
    public void AHostThatDoesNotCountFramesIsUnaffected()
    {
        // The whole feature is opt-in by arithmetic rather than by a flag: leave FrameId alone and the
        // stamp is 0, the comparison always matches, and every consumer written before this behaves
        // exactly as it did.
        var w = Widget();
        w.Render();
        w.HitTest(50, 20).ShouldNotBeNull();

        w.Render();
        w.HitTest(50, 20).ShouldNotBeNull();
        w.GetRegisteredRegions().ShouldHaveSingleItem();
    }

    [Fact]
    public void SharedSettingsPutEveryWidgetOnTheSameFrame()
    {
        // One window, one frame counter: a composite bumps it once and every widget it shares with agrees
        // about which frame is current. Two counters would be two answers to "is this on screen".
        var parent = new SharingWidget(new RgbaImageRenderer(200, 100));
        var child = Widget();
        parent.Share(child);

        parent.Ui.FrameId = 1;
        child.Render();
        child.HitTest(50, 20).ShouldNotBeNull();

        parent.Ui.FrameId = 2;      // bumped on the PARENT
        child.HitTest(50, 20).ShouldBeNull();
    }

    private sealed class FieldWidget(Renderer<RgbaImage> renderer, TextInputState input)
        : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render()
        {
            BeginFrame();
            RegisterClickable(0, 0, 100, 24, new HitResult.TextInputHit(input));
        }
    }

    private sealed class SharingWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Share(PixelWidgetBase<RgbaImage> child) => ShareUiContext(child);
    }
}
