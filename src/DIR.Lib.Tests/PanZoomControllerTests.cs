using System.Numerics;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Headless unit tests for <see cref="PanZoomController"/> — flat pan + cursor-anchored zoom.
/// Reproduces the FITS viewer's zoom formula: the scene point under the cursor stays fixed on screen,
/// and the zoom clamps to the configurable <see cref="PanZoomController.MinZoom"/>/<see cref="PanZoomController.MaxZoom"/>.
/// </summary>
public class PanZoomControllerTests
{
    [Fact]
    public void Pan_TranslatesByIncrementalMotion()
    {
        var c = new PanZoomController();
        c.UpdatePan(15f, 13f).ShouldBeFalse(); // not panning yet
        c.BeginPan(10f, 10f);
        c.UpdatePan(15f, 13f).ShouldBeTrue();
        c.PanOffset.ShouldBe(new Vector2(5f, 3f));
        c.UpdatePan(20f, 20f);
        c.PanOffset.ShouldBe(new Vector2(10f, 10f));
        c.EndPan();
        c.IsPanning.ShouldBeFalse();
    }

    [Fact]
    public void ZoomByFactor_KeepsScenePointUnderCursorFixed()
    {
        var c = new PanZoomController();
        var viewport = new RectF32(0f, 0f, 100f, 100f);
        c.ZoomByFactor(2f, 75f, 50f, viewport).ShouldBeTrue();
        c.Zoom.ShouldBe(2f);
        // cx = 75 - 50 - 0 = 25; ratio = 2/1 - 1 = 1; PanOffset -= (25, 0)
        c.PanOffset.ShouldBe(new Vector2(-25f, 0f));
        c.ZoomToFit.ShouldBeFalse();
    }

    [Fact]
    public void ZoomAtCursor_PositiveDeltaZoomsInByStep()
    {
        var c = new PanZoomController { ZoomStep = 1.15f };
        var viewport = new RectF32(0f, 0f, 100f, 100f);
        c.ZoomAtCursor(1f, 50f, 50f, viewport); // positive = zoom in, at centre → no pan shift
        c.Zoom.ShouldBe(1.15f, 0.0001);
        c.PanOffset.ShouldBe(Vector2.Zero);
    }

    [Fact]
    public void ZoomAtCursor_NegativeDeltaZoomsOut()
    {
        var c = new PanZoomController { ZoomStep = 1.15f };
        var viewport = new RectF32(0f, 0f, 100f, 100f);
        c.ZoomAtCursor(-1f, 50f, 50f, viewport);
        c.Zoom.ShouldBe(1f / 1.15f, 0.0001);
    }

    [Fact]
    public void Zoom_ClampsToMinAndMax()
    {
        var c = new PanZoomController { MinZoom = 0.1f, MaxZoom = 2f };
        var viewport = new RectF32(0f, 0f, 100f, 100f);

        c.ZoomByFactor(100f, 50f, 50f, viewport);
        c.Zoom.ShouldBe(2f); // clamped up
        c.ZoomByFactor(100f, 50f, 50f, viewport).ShouldBeFalse(); // already at max → no change

        c.ZoomByFactor(0.0001f, 50f, 50f, viewport);
        c.Zoom.ShouldBe(0.1f); // clamped down
    }

    [Fact]
    public void Reset_And_FitToView()
    {
        var c = new PanZoomController();
        c.BeginPan(0f, 0f);
        c.UpdatePan(30f, 30f);
        c.ZoomByFactor(3f, 10f, 10f, new RectF32(0f, 0f, 100f, 100f));

        c.Reset();
        c.Zoom.ShouldBe(1f);
        c.PanOffset.ShouldBe(Vector2.Zero);
        c.ZoomToFit.ShouldBeFalse();

        c.FitToView();
        c.ZoomToFit.ShouldBeTrue();
    }

    [Fact]
    public void Changed_FiresOnPanZoomResetFit()
    {
        var c = new PanZoomController();
        var count = 0;
        c.Changed += () => count++;

        c.BeginPan(0f, 0f);
        c.UpdatePan(5f, 5f);          // 1
        c.ZoomByFactor(2f, 10f, 10f, new RectF32(0f, 0f, 100f, 100f)); // 2
        c.Reset();                    // 3
        c.FitToView();                // 4
        count.ShouldBe(4);
    }
}
