using System;
using System.Numerics;

namespace DIR.Lib;

/// <summary>
/// Flat pan + cursor-anchored zoom over a fixed viewport, extracted from the FITS viewer's
/// <c>ImageRendererBase</c> mouse handling and the byte-for-byte duplicate in the live-session preview.
/// Holds the display transform (<see cref="PanOffset"/>, <see cref="Zoom"/>, <see cref="ZoomToFit"/>)
/// and exposes the pan and zoom primitives; the owning widget keeps ownership of hit-testing (pan only
/// inside the image pane, zoom only over it, etc.) and interleaves these calls with its other drags.
///
/// <para>
/// The zoom keeps the scene point under the cursor fixed on screen — the standard "zoom toward the
/// pointer" behaviour — by shifting <see cref="PanOffset"/> by the cursor's offset from the viewport
/// centre times the zoom-ratio change. <see cref="MinZoom"/>/<see cref="MaxZoom"/>/<see cref="ZoomStep"/>
/// are configurable because the two original call sites differed (the viewer clamped
/// <c>[0.01, ∞)</c>, the live preview <c>[0.1, 16]</c>).
/// </para>
///
/// <para>This is a <b>flat</b> transform: it does not apply to the sky map, whose zoom is an angular
/// field-of-view change through a spherical projection (a different, non-shared path).</para>
/// </summary>
public sealed class PanZoomController
{
    private bool _panning;
    private float _panStartX;
    private float _panStartY;

    /// <summary>Pan offset in screen pixels, relative to a centred image.</summary>
    public Vector2 PanOffset { get; set; }

    /// <summary>Current zoom multiplier (1 = 1:1 before any fit).</summary>
    public float Zoom { get; set; } = 1f;

    /// <summary>Whether the view is in fit-to-window mode (cleared by any explicit zoom).</summary>
    public bool ZoomToFit { get; set; }

    /// <summary>Lower zoom clamp. Default 0.01 (the FITS viewer's historical floor).</summary>
    public float MinZoom { get; set; } = 0.01f;

    /// <summary>Upper zoom clamp. Default unbounded; the live preview sets 16.</summary>
    public float MaxZoom { get; set; } = float.PositiveInfinity;

    /// <summary>Per-notch zoom factor for <see cref="ZoomAtCursor"/>. Default 1.15.</summary>
    public float ZoomStep { get; set; } = 1.15f;

    /// <summary>True while a pan drag is in flight.</summary>
    public bool IsPanning => _panning;

    /// <summary>Raised on any pan/zoom/reset change. Wire to the widget's redraw.</summary>
    public event Action? Changed;

    /// <summary>Begin a pan drag anchored at the given screen position.</summary>
    public void BeginPan(float x, float y)
    {
        _panning = true;
        _panStartX = x;
        _panStartY = y;
    }

    /// <summary>Continue a pan drag, translating <see cref="PanOffset"/> by the incremental cursor motion. Returns <c>true</c> when panning.</summary>
    public bool UpdatePan(float x, float y)
    {
        if (!_panning)
        {
            return false;
        }

        PanOffset = new Vector2(PanOffset.X + (x - _panStartX), PanOffset.Y + (y - _panStartY));
        _panStartX = x;
        _panStartY = y;
        Changed?.Invoke();
        return true;
    }

    /// <summary>End a pan drag.</summary>
    public void EndPan() => _panning = false;

    /// <summary>
    /// Cursor-anchored zoom from a wheel delta (positive = zoom in), stepping by <see cref="ZoomStep"/>.
    /// Returns <c>true</c> when the zoom actually changed.
    /// </summary>
    public bool ZoomAtCursor(float scrollDelta, float cursorX, float cursorY, RectF32 viewport)
        => ZoomByFactor(scrollDelta > 0f ? ZoomStep : 1f / ZoomStep, cursorX, cursorY, viewport);

    /// <summary>
    /// Cursor-anchored zoom by an explicit multiplicative <paramref name="factor"/> (for keyboard /
    /// pinch). Clamps to <see cref="MinZoom"/>/<see cref="MaxZoom"/>, keeps the scene point under the
    /// cursor fixed, and clears <see cref="ZoomToFit"/>. Returns <c>true</c> when the zoom changed.
    /// </summary>
    public bool ZoomByFactor(float factor, float cursorX, float cursorY, RectF32 viewport)
    {
        var oldZoom = Zoom;
        var newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (newZoom == oldZoom)
        {
            return false;
        }

        var cx = cursorX - viewport.X - viewport.Width * 0.5f - PanOffset.X;
        var cy = cursorY - viewport.Y - viewport.Height * 0.5f - PanOffset.Y;
        var ratio = newZoom / oldZoom - 1f;

        PanOffset = new Vector2(PanOffset.X - cx * ratio, PanOffset.Y - cy * ratio);
        ZoomToFit = false;
        Zoom = newZoom;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Reset to 1:1, centred, not fit-to-window.</summary>
    public void Reset()
    {
        Zoom = 1f;
        PanOffset = Vector2.Zero;
        ZoomToFit = false;
        Changed?.Invoke();
    }

    /// <summary>Enter fit-to-window mode (the renderer computes the actual scale from the image + pane).</summary>
    public void FitToView()
    {
        ZoomToFit = true;
        Changed?.Invoke();
    }
}
