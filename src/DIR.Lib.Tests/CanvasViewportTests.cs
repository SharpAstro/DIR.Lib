using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>The canvas viewport: fitted and centred at first, kept where the person moves it, wheel to pan.</summary>
public sealed class CanvasViewportTests
{
    private static CanvasViewport Fitted(float viewW, float viewH, float contentW, float contentH, string key = "k")
    {
        var vp = new CanvasViewport();
        vp.SetView(viewW, viewH);
        vp.Update(contentW, contentH, key);
        return vp;
    }

    [Fact]
    public void EnsureVisiblePansJustFarEnoughAndNotAtAllWhenInView()
    {
        var vp = new CanvasViewport();
        vp.Update(1000f, 2000f, "k");
        vp.SetView(400f, 300f);
        vp.SetScale(1f);

        vp.EnsureVisible(100f, 1900f, 200f, 72f).ShouldBeTrue();
        vp.ViewY(1900f + 72f).ShouldBe(300f - 8f, 0.01f);
        var pan = (vp.PanX, vp.PanY);
        vp.EnsureVisible(100f, 1900f, 200f, 72f).ShouldBeFalse();
        (vp.PanX, vp.PanY).ShouldBe(pan);
        vp.EnsureVisible(100f, 0f, 200f, 72f).ShouldBeTrue();
        vp.ViewY(0f).ShouldBe(8f, 0.01f);
    }

    [Fact]
    public void AWideCanvasIsFittedToItsBindingSideAndCentred()
    {
        var vp = Fitted(1900f, 700f, 1200f, 600f);

        vp.Scale.ShouldBeGreaterThanOrEqualTo(vp.MinFitScale);
        vp.Scale.ShouldBeLessThanOrEqualTo(vp.MaxFitScale);
        var (w, h) = (1200f * vp.Scale, 600f * vp.Scale);
        var filled = MathF.Abs(w - (1900f - 2f * vp.Margin)) < 1f || MathF.Abs(h - (700f - 2f * vp.Margin)) < 1f;
        filled.ShouldBeTrue();
        vp.ViewX(600f).ShouldBe(950f, 0.5f);
        vp.ViewY(300f).ShouldBe(350f, 0.5f);
        vp.Moved.ShouldBeFalse();
    }

    [Theory]
    [InlineData(3000, 2000, 400, 300)]
    [InlineData(1280, 800, 2000, 1500)]
    [InlineData(1900, 700, 6000, 3000)]
    public void TheFitScaleStaysBetweenTheFloorAndTheCap(int viewW, int viewH, int contentW, int contentH)
    {
        var vp = Fitted(viewW, viewH, contentW, contentH);

        vp.Scale.ShouldBeLessThanOrEqualTo(vp.MaxFitScale);
        vp.Scale.ShouldBeGreaterThanOrEqualTo(vp.MinFitScale);
        if (contentW == 400)
        {
            vp.Scale.ShouldBe(1.5f, 0.001f);
        }
        if (contentW == 6000)
        {
            vp.Scale.ShouldBe(0.5f, 0.001f);
        }
    }

    [Fact]
    public void ACanvasLargerThanTheViewAtTheFloorIsReachedByPanning()
    {
        var vp = Fitted(900f, 800f, 4000f, 1000f);
        vp.Scale.ShouldBe(0.5f, 0.001f);
        vp.ViewX(4000f).ShouldBeGreaterThan(900f);

        for (var i = 0; i < 100 && vp.ViewX(4000f) > 900f - 8f; i++)
        {
            vp.Wheel(-1f, sideways: true);
        }
        vp.ViewX(4000f).ShouldBeLessThanOrEqualTo(900f);
        vp.Scale.ShouldBe(0.5f, 0.001f);
    }

    [Fact]
    public void AChangeOfSizeAfterAPanOrZoomLeavesTheViewWhereItWas()
    {
        var vp = Fitted(1280f, 800f, 1000f, 800f);
        vp.PanBy(0f, -40f);
        vp.ZoomAt(1.3f, 300f, 300f).ShouldBeTrue();
        vp.Moved.ShouldBeTrue();
        var scale = vp.Scale;
        var (cornerX, cornerY) = (vp.ViewX(0f), vp.ViewY(0f));

        vp.Update(1000f, 1000f, "k");
        vp.Update(1200f, 1000f, "k");
        vp.Update(1200f, 900f, "k");

        vp.Moved.ShouldBeTrue();
        vp.Scale.ShouldBe(scale);
        vp.ViewX(0f).ShouldBe(cornerX, 0.01f);
        vp.ViewY(0f).ShouldBe(cornerY, 0.01f);
    }

    [Fact]
    public void ANewKeyOrFitRefitsAMovedViewButTheSameKeyDoesNot()
    {
        var vp = Fitted(1280f, 800f, 1000f, 800f);

        vp.ZoomAt(1.4f, 300f, 300f);
        vp.PanBy(30f, -20f);
        vp.Update(1000f, 800f, "k");
        vp.Moved.ShouldBeTrue();

        vp.Update(1000f, 800f, "other");
        vp.Moved.ShouldBeFalse();
        vp.PanX.ShouldBe(0f);
        vp.PanY.ShouldBe(0f);

        vp.PanBy(30f, -20f);
        vp.Fit();
        vp.Moved.ShouldBeFalse();
        vp.PanX.ShouldBe(0f);

        // An unmoved view is refitted by a size change even with the same key.
        vp.Update(500f, 400f, "other");
        vp.ViewX(250f).ShouldBe(640f, 0.5f);
    }

    [Fact]
    public void TheWheelPansUpAndDownOrLeftAndRight()
    {
        var vp = Fitted(1280f, 800f, 4000f, 4000f);
        var (x0, y0) = (vp.PanX, vp.PanY);

        vp.Wheel(-1f, sideways: false);
        vp.PanY.ShouldBeLessThan(y0);
        vp.PanX.ShouldBe(x0);
        var y1 = vp.PanY;
        vp.Wheel(1f, sideways: false);
        vp.PanY.ShouldBeGreaterThan(y1);

        vp.Wheel(-1f, sideways: true);
        vp.PanX.ShouldBeLessThan(x0);
        var x1 = vp.PanX;
        vp.Wheel(1f, sideways: true);
        vp.PanX.ShouldBeGreaterThan(x1);
        vp.Moved.ShouldBeTrue();
    }

    [Fact]
    public void ADragMovesTheCanvasByThePointerMotion()
    {
        var vp = Fitted(1000f, 600f, 2000f, 2000f);
        vp.UpdatePan(10f, 10f).ShouldBeFalse();

        vp.BeginPan(100f, 100f);
        vp.IsPanning.ShouldBeTrue();
        vp.UpdatePan(130f, 90f).ShouldBeTrue();
        vp.PanX.ShouldBe(30f);
        vp.PanY.ShouldBe(-10f);
        vp.UpdatePan(135f, 90f).ShouldBeTrue();
        vp.PanX.ShouldBe(35f);
        vp.EndPan();
        vp.IsPanning.ShouldBeFalse();
        vp.Moved.ShouldBeTrue();
    }

    [Fact]
    public void TheViewportMapsAPointToTheViewAndBack()
    {
        var vp = new CanvasViewport();
        vp.SetView(1000f, 600f);
        vp.Update(400f, 300f, "a");
        vp.PanBy(25f, -10f);
        vp.ZoomAt(1.5f, 700f, 200f);

        vp.DesignX(vp.ViewX(123f)).ShouldBe(123f, 0.01f);
        vp.DesignY(vp.ViewY(77f)).ShouldBe(77f, 0.01f);
    }

    [Fact]
    public void AZoomKeepsThePointUnderTheCursorAndStaysWithinTheFloorAndTheCeiling()
    {
        var vp = Fitted(1000f, 600f, 400f, 300f, "a");
        var (cx, cy) = (vp.DesignX(700f), vp.DesignY(200f));

        vp.ZoomAt(1.2f, 700f, 200f).ShouldBeTrue();
        vp.ViewX(cx).ShouldBe(700f, 0.05f);
        vp.ViewY(cy).ShouldBe(200f, 0.05f);
        for (var i = 0; i < 40; i++)
        {
            vp.ZoomAt(0.5f, 700f, 200f);
        }
        vp.Scale.ShouldBe(0.25f);
        for (var i = 0; i < 40; i++)
        {
            vp.ZoomAt(2f, 700f, 200f);
        }
        vp.Scale.ShouldBe(4f);
    }

    [Fact]
    public void TheDefaultsAreTheDocumentedLimits()
    {
        var vp = new CanvasViewport();

        (vp.MinFitScale, vp.MaxFitScale, vp.MinZoom, vp.MaxZoom, vp.Margin, vp.KeepInView).ShouldBe((0.5f, 1.5f, 0.25f, 4f, 8f, 80f));
    }

    [Fact]
    public void CustomLimitsAreHonoured()
    {
        var vp = new CanvasViewport { MinFitScale = 0.1f, MaxFitScale = 3f, MinZoom = 0.05f, MaxZoom = 10f, Margin = 0f };
        vp.SetView(1000f, 1000f);

        vp.Update(100f, 100f, "a");
        vp.Scale.ShouldBe(3f, 0.001f);
        vp.Update(100000f, 100000f, "b");
        vp.Scale.ShouldBe(0.1f, 0.001f);
        vp.Update(500f, 500f, "c");
        vp.Scale.ShouldBe(2f, 0.001f);

        for (var i = 0; i < 40; i++)
        {
            vp.ZoomCentre(0.5f);
        }
        vp.Scale.ShouldBe(0.05f);
        for (var i = 0; i < 40; i++)
        {
            vp.ZoomCentre(2f);
        }
        vp.Scale.ShouldBe(10f);
        vp.SetScale(100f);
        vp.Scale.ShouldBe(10f);
    }

    [Fact]
    public void KeepInViewBoundsHowFarTheCanvasCanBePushedAway()
    {
        var near = Fitted(1000f, 600f, 1000f, 600f);
        var far = Fitted(1000f, 600f, 1000f, 600f);
        near.KeepInView = 20f;
        far.KeepInView = 300f;

        near.PanBy(100000f, 0f);
        far.PanBy(100000f, 0f);

        near.PanX.ShouldBeGreaterThan(far.PanX);
        // At scale 1 the canvas spans the view: with 20 kept, its left edge may sit 20 short of the view's right edge.
        near.ViewX(0f).ShouldBe(980f, 0.01f);
    }

    [Fact]
    public void FitScaleWithNoRoomOrANonFiniteRoomIsTheFloor()
    {
        CanvasViewport.FitScale(0f, 0f, 100f, 100f).ShouldBe(0.5f);
        CanvasViewport.FitScale(10f, 10f, 100f, 100f, margin: 8f).ShouldBe(0.5f);
        CanvasViewport.FitScale(float.NaN, 600f, 100f, 100f).ShouldBe(0.5f);
        CanvasViewport.FitScale(float.PositiveInfinity, float.PositiveInfinity, 100f, 100f).ShouldBe(0.5f);
        CanvasViewport.FitScale(0f, 0f, 100f, 100f, minScale: 0.2f, maxScale: 2f).ShouldBe(0.2f);
        CanvasViewport.FitScale(1000f, 1000f, 0f, 100f).ShouldBe(1f);
    }

    [Fact]
    public void ChangedIsRaisedOnEveryChange()
    {
        var vp = new CanvasViewport();
        var n = 0;
        vp.Changed += () => n++;

        vp.SetView(1000f, 600f);
        vp.Update(2000f, 2000f, "a");
        vp.Fit();
        vp.PanBy(5f, 5f);
        vp.ZoomAt(1.2f, 100f, 100f);
        vp.BeginPan(0f, 0f);
        vp.UpdatePan(5f, 5f);
        vp.EndPan();

        n.ShouldBe(6);
    }

    // A 800x600 view shown at (100, 50) on the surface, a 400x300 canvas at scale 1.
    private static readonly RectF32 Bounds = new(100f, 50f, 800f, 600f);

    private static CanvasViewport Live()
    {
        var vp = Fitted(800f, 600f, 400f, 300f);
        vp.SetScale(1f);
        return vp;
    }

    [Fact]
    public void ZoomByWheelUsesZoomStepToTheDelta()
    {
        var vp = Live();
        vp.ZoomByWheel(1f, 400f, 300f).ShouldBeTrue();
        vp.Scale.ShouldBe(1.15f, 1e-4f);

        vp = Live();
        vp.ZoomByWheel(0.1f, 400f, 300f);
        vp.Scale.ShouldBe(MathF.Pow(1.15f, 0.1f), 1e-4f);

        vp = Live();
        vp.ZoomByWheel(-1f, 400f, 300f);
        vp.Scale.ShouldBe(1f / 1.15f, 1e-4f);

        vp = Live();
        vp.ZoomStep = 2f;
        vp.ZoomByWheel(2f, 400f, 300f);
        vp.Scale.ShouldBe(4f, 1e-4f);
    }

    [Fact]
    public void TouchpadSeriesOfSmallCtrlWheelDeltasZoomsNoMoreThanOneNotchPerUnitOfDelta()
    {
        var vp = Live();
        for (var i = 0; i < 10; i++)
        {
            vp.HandleInput(new InputEvent.Scroll(0.1f, 500f, 350f, InputModifier.Ctrl), Bounds).ShouldBeTrue();
        }
        vp.Scale.ShouldBeLessThanOrEqualTo(1.15f + 1e-3f);
        vp.Scale.ShouldBe(1.15f, 1e-3f);
    }

    [Fact]
    public void CtrlScrollZoomsAboutThePointerAcrossBoundsOffsetAndDesignScale()
    {
        var vp = Live();
        var (sx, sy) = (300f, 200f);
        var (beforeX, beforeY) = (vp.DesignX((sx - Bounds.X) / 2f), vp.DesignY((sy - Bounds.Y) / 2f));
        vp.HandleInput(new InputEvent.Scroll(1f, sx, sy, InputModifier.Ctrl), Bounds, null, 2f, 2f).ShouldBeTrue();
        vp.Scale.ShouldBe(1.15f, 1e-4f);
        vp.DesignX((sx - Bounds.X) / 2f).ShouldBe(beforeX, 0.01f);
        vp.DesignY((sy - Bounds.Y) / 2f).ShouldBe(beforeY, 0.01f);
    }

    [Fact]
    public void PlainScrollPansVerticallyAndShiftScrollSideways()
    {
        var vp = Live();
        vp.HandleInput(new InputEvent.Scroll(1f, 500f, 350f), Bounds).ShouldBeTrue();
        (vp.PanX, vp.PanY).ShouldBe((0f, 60f));
        vp.HandleInput(new InputEvent.Scroll(-1f, 500f, 350f, InputModifier.Shift), Bounds).ShouldBeTrue();
        (vp.PanX, vp.PanY).ShouldBe((-60f, 60f));
    }

    [Fact]
    public void EventsOutsideBoundsAreNotTaken()
    {
        var vp = Live();
        vp.HandleInput(new InputEvent.Scroll(1f, 10f, 10f, InputModifier.Ctrl), Bounds).ShouldBeFalse();
        vp.HandleInput(new InputEvent.Scroll(1f, 10f, 10f), Bounds).ShouldBeFalse();
        vp.HandleInput(new InputEvent.Pinch(1.5f, 10f, 10f), Bounds).ShouldBeFalse();
        vp.HandleInput(new InputEvent.MouseDown(10f, 10f, MouseButton.Middle), Bounds).ShouldBeFalse();
        vp.IsPanning.ShouldBeFalse();
        vp.Scale.ShouldBe(1f);
        vp.Moved.ShouldBeFalse();
    }

    [Fact]
    public void PinchZoomsAboutItsPointByItsScale()
    {
        var vp = Live();
        vp.HandleInput(new InputEvent.Pinch(1.5f, 500f, 350f), Bounds).ShouldBeTrue();
        vp.Scale.ShouldBe(1.5f, 1e-4f);
    }

    [Fact]
    public void MiddleDragPansAndOnlyMiddleUpEndsIt()
    {
        var vp = Live();
        vp.HandleInput(new InputEvent.MouseDown(500f, 350f, MouseButton.Middle), Bounds).ShouldBeTrue();
        vp.IsPanning.ShouldBeTrue();
        vp.HandleInput(new InputEvent.MouseMove(530f, 360f), Bounds).ShouldBeTrue();
        (vp.PanX, vp.PanY).ShouldBe((30f, 10f));

        vp.HandleInput(new InputEvent.MouseUp(530f, 360f, MouseButton.Left), Bounds).ShouldBeFalse();
        vp.IsPanning.ShouldBeTrue();

        // A move that has left the bounds still pans, and the release ends it.
        vp.HandleInput(new InputEvent.MouseMove(20f, 360f), Bounds).ShouldBeTrue();
        vp.HandleInput(new InputEvent.MouseUp(20f, 360f, MouseButton.Middle), Bounds).ShouldBeTrue();
        vp.IsPanning.ShouldBeFalse();
        vp.HandleInput(new InputEvent.MouseMove(30f, 360f), Bounds).ShouldBeFalse();
    }

    [Fact]
    public void LeftDragPansOnlyWithSpaceHeldOrWhenTheHostSaysSo()
    {
        var vp = Live();
        vp.HandleInput(new InputEvent.MouseDown(500f, 350f), Bounds).ShouldBeFalse();
        vp.IsPanning.ShouldBeFalse();

        vp.HandleInput(new InputEvent.MouseDown(500f, 350f), Bounds, (x, y) => x > 600f).ShouldBeFalse();
        vp.HandleInput(new InputEvent.MouseDown(700f, 350f), Bounds, (x, y) => x > 600f).ShouldBeTrue();
        vp.IsPanning.ShouldBeTrue();
        vp.HandleInput(new InputEvent.MouseUp(700f, 350f), Bounds).ShouldBeTrue();
        vp.IsPanning.ShouldBeFalse();

        vp.HandleInput(new InputEvent.KeyDown(InputKey.Space), Bounds).ShouldBeTrue();
        vp.SpaceHeld.ShouldBeTrue();
        vp.HandleInput(new InputEvent.MouseDown(500f, 350f), Bounds).ShouldBeTrue();
        vp.IsPanning.ShouldBeTrue();
        vp.HandleInput(new InputEvent.MouseUp(500f, 350f), Bounds);
        vp.HandleInput(new InputEvent.KeyUp(InputKey.Space), Bounds).ShouldBeFalse();
        vp.SpaceHeld.ShouldBeFalse();
    }

    [Fact]
    public void ZoomFitAndPanKeys()
    {
        var vp = Live();
        vp.HandleInput(new InputEvent.KeyDown(InputKey.Plus, InputModifier.Ctrl), Bounds).ShouldBeTrue();
        vp.Scale.ShouldBe(1.15f, 1e-4f);
        vp.HandleInput(new InputEvent.KeyDown(InputKey.Minus, InputModifier.Ctrl), Bounds).ShouldBeTrue();
        vp.Scale.ShouldBe(1f, 1e-4f);

        vp.HandleInput(new InputEvent.KeyDown(InputKey.Left), Bounds).ShouldBeTrue();
        vp.PanX.ShouldBe(40f);
        vp.HandleInput(new InputEvent.KeyDown(InputKey.Right, InputModifier.Shift), Bounds).ShouldBeTrue();
        vp.PanX.ShouldBe(-120f);

        vp.HandleInput(new InputEvent.KeyDown(InputKey.D0, InputModifier.Ctrl), Bounds).ShouldBeTrue();
        vp.Moved.ShouldBeFalse();
        vp.Scale.ShouldBe(1.5f, 1e-4f);
        vp.HandleInput(new InputEvent.KeyDown(InputKey.D1, InputModifier.Ctrl), Bounds).ShouldBeTrue();
        vp.Scale.ShouldBe(1f, 1e-4f);
    }

    [Fact]
    public void UpAndDownPanOnlyWhenTheHostOptsIn()
    {
        var vp = Live();
        vp.HandleInput(new InputEvent.KeyDown(InputKey.Up), Bounds).ShouldBeFalse();
        vp.HandleInput(new InputEvent.KeyDown(InputKey.Down), Bounds).ShouldBeFalse();
        vp.PanY.ShouldBe(0f);

        vp.PanWithVerticalKeys = true;
        vp.HandleInput(new InputEvent.KeyDown(InputKey.Up), Bounds).ShouldBeTrue();
        vp.PanY.ShouldBe(40f);
        vp.HandleInput(new InputEvent.KeyDown(InputKey.Down, InputModifier.Shift), Bounds).ShouldBeTrue();
        vp.PanY.ShouldBe(-120f);
    }
}
