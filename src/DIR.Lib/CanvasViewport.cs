using System;
using System.Numerics;

namespace DIR.Lib;

/// <summary>
/// A pan, zoom and fit viewport over a canvas of a known size in design units. It knows the room the canvas is shown in and
/// fits the canvas to it (fill the width or the height, whichever binds, capped at <see cref="MaxFitScale"/> so small content
/// is not blown up and floored at <see cref="MinFitScale"/> so it stays legible), centres it, and maps design points to the
/// view and back. Once the person has moved the view (a pan or a zoom) it stays where they left it: a fit is redone only by
/// <see cref="Fit"/> or when <see cref="Update"/> is given a new key (what is shown has changed). All lengths are design
/// units, and view coordinates are measured from the view's top-left corner.
///
/// <para>
/// <b>Relation to <see cref="PanZoomController"/>.</b> This class sits BESIDE <see cref="PanZoomController"/> and composes
/// one privately for the drag and cursor-anchored zoom primitives; it does not change that class's API or behaviour. The two
/// answer different questions. <see cref="PanZoomController.ZoomToFit"/> is a flag meaning the renderer computes the fit
/// scale itself, whereas this viewport owns the content size and computes the fit, the keep-in-view clamp and the
/// design/view mapping. Use the controller when a renderer fits its own image; use this when a canvas of design units needs
/// a complete viewport model.
/// </para>
///
/// <para>The limits are instance properties. <see cref="Changed"/> is raised on every change of the view.</para>
/// </summary>
public sealed class CanvasViewport
{
    private readonly PanZoomController _pan = new() { MinZoom = 0.25f, MaxZoom = 4f };
    private float _viewWidth;
    private float _viewHeight;
    private float _contentWidth;
    private float _contentHeight;
    private object? _key;
    private bool _keyed;
    private MouseButton _panButton;

    /// <summary>The smallest scale a fit reaches: below it small text is unreadable. Default 0.5.</summary>
    public float MinFitScale { get; set; } = 0.5f;

    /// <summary>The largest scale a fit reaches: past it a small canvas is blown up. Default 1.5.</summary>
    public float MaxFitScale { get; set; } = 1.5f;

    /// <summary>The smallest scale a zoom reaches (a fit never goes below <see cref="MinFitScale"/>), so a wide canvas can be seen whole. Default 0.25.</summary>
    public float MinZoom
    {
        get => _pan.MinZoom;
        set => _pan.MinZoom = value;
    }

    /// <summary>The largest scale a zoom reaches. Default 4.</summary>
    public float MaxZoom
    {
        get => _pan.MaxZoom;
        set => _pan.MaxZoom = value;
    }

    /// <summary>The room left round the canvas when it is fitted. Default 8.</summary>
    public float Margin { get; set; } = 8f;

    /// <summary>How much of the canvas must stay in view when it is panned away, so it can always be dragged back. Default 80.</summary>
    public float KeepInView { get; set; } = 80f;

    /// <summary>The factor of one wheel notch, key press or zoom step. A wheel delta d zooms by <c>ZoomStep^d</c>. Default 1.15.</summary>
    public float ZoomStep { get; set; } = 1.15f;

    /// <summary>The distance an arrow key pans, view units. Default 40.</summary>
    public float KeyPanStep { get; set; } = 40f;

    /// <summary>The distance Shift+arrow pans, view units. Default 160.</summary>
    public float KeyPanStepBig { get; set; } = 160f;

    /// <summary>
    /// Whether Up and Down pan too. Off by default because a host usually uses them to move a selection; Left and Right
    /// always pan.
    /// </summary>
    public bool PanWithVerticalKeys { get; set; }

    /// <summary>Whether Space is held, so a left drag pans. Set by <see cref="HandleInput"/> from Space's press and release; a host can show the move cursor.</summary>
    public bool SpaceHeld { get; private set; }

    /// <summary>Whether the person has panned or zoomed since the last fit.</summary>
    public bool Moved { get; private set; }

    /// <summary>Raised on every change of the view: a fit, a pan, a zoom, or a new view or content size.</summary>
    public event Action? Changed;

    /// <summary>View units per design unit of canvas.</summary>
    public float Scale => _pan.Zoom;

    /// <summary>How far the canvas's centre is from the view's centre, view units.</summary>
    public float PanX => _pan.PanOffset.X;

    /// <summary>How far the canvas's centre is from the view's centre, view units.</summary>
    public float PanY => _pan.PanOffset.Y;

    public float ViewWidth => _viewWidth;

    public float ViewHeight => _viewHeight;

    /// <summary>
    /// The scale a fit gives a canvas of <paramref name="contentWidth"/> by <paramref name="contentHeight"/> in a view of the
    /// given room, within [<paramref name="minScale"/>, <paramref name="maxScale"/>]. No room, or a room that is not finite,
    /// gives <paramref name="minScale"/>; an empty canvas gives 1.
    /// </summary>
    public static float FitScale(float viewWidth, float viewHeight, float contentWidth, float contentHeight, float margin = 0f, float minScale = 0.5f, float maxScale = 1.5f)
    {
        if (contentWidth <= 0f || contentHeight <= 0f)
        {
            return 1f;
        }
        var fit = MathF.Min((viewWidth - 2f * margin) / contentWidth, (viewHeight - 2f * margin) / contentHeight);
        return float.IsFinite(fit) ? Math.Clamp(fit, minScale, maxScale) : minScale;
    }

    /// <summary>Fits the canvas to the view and centres it, and forgets that the view was moved.</summary>
    public void Fit()
    {
        FitCore();
        Changed?.Invoke();
    }

    private void FitCore()
    {
        _pan.Zoom = FitScale(_viewWidth, _viewHeight, _contentWidth, _contentHeight, Margin, MinFitScale, MaxFitScale);
        _pan.PanOffset = Vector2.Zero;
        _pan.ZoomToFit = true;
        Moved = false;
    }

    /// <summary>
    /// States the canvas's size and what is shown. A new <paramref name="key"/> (or the first) fits; the same key refits only
    /// while the view has not been moved, so a size change leaves a view the person set alone.
    /// </summary>
    public void Update(float contentWidth, float contentHeight, object? key)
    {
        var (oldWidth, oldHeight) = (_contentWidth, _contentHeight);
        _contentWidth = contentWidth;
        _contentHeight = contentHeight;
        var changed = !_keyed || !Equals(_key, key);
        (_key, _keyed) = (key, true);
        if (changed || !Moved)
        {
            FitCore();
        }
        else
        {
            // The canvas's centre anchors the view, so growth would slide it; keep its top-left corner where it was.
            _pan.PanOffset += new Vector2((contentWidth - oldWidth) * Scale / 2f, (contentHeight - oldHeight) * Scale / 2f);
            Clamp();
        }
        Changed?.Invoke();
    }

    /// <summary>States the room the canvas is shown in; a view not yet moved is fitted to it again.</summary>
    public void SetView(float width, float height)
    {
        _viewWidth = width;
        _viewHeight = height;
        if (!Moved)
        {
            FitCore();
        }
        else
        {
            Clamp();
        }
        Changed?.Invoke();
    }

    /// <summary>The view x of a design x: the canvas's centre sits at the view's centre, moved by the pan.</summary>
    public float ViewX(float x) => _viewWidth / 2f + _pan.PanOffset.X + (x - _contentWidth / 2f) * Scale;

    /// <summary>The view y of a design y.</summary>
    public float ViewY(float y) => _viewHeight / 2f + _pan.PanOffset.Y + (y - _contentHeight / 2f) * Scale;

    /// <summary>The design x under a view x.</summary>
    public float DesignX(float viewX) => (viewX - _viewWidth / 2f - _pan.PanOffset.X) / Scale + _contentWidth / 2f;

    /// <summary>The design y under a view y.</summary>
    public float DesignY(float viewY) => (viewY - _viewHeight / 2f - _pan.PanOffset.Y) / Scale + _contentHeight / 2f;

    /// <summary>Moves the canvas by <paramref name="dx"/>, <paramref name="dy"/> view units, kept within reach.</summary>
    public void PanBy(float dx, float dy)
    {
        _pan.PanOffset += new Vector2(dx, dy);
        Moved = true;
        Clamp();
        Changed?.Invoke();
    }

    /// <summary>
    /// Pans just far enough to bring a rect of the canvas (design units) into view, with a small margin; a rect larger than the
    /// view shows its top-left. Nothing moves when it is already in view. Returns whether the view moved.
    /// </summary>
    public bool EnsureVisible(float x, float y, float width, float height, float margin = 8f)
    {
        var (left, top) = (ViewX(x), ViewY(y));
        var (right, bottom) = (left + width * Scale, top + height * Scale);
        static float Shift(float lo, float hi, float size, float margin)
        {
            if (lo < margin)
            {
                return margin - lo;
            }
            return hi > size - margin ? MathF.Max(margin - lo, size - margin - hi) : 0f;
        }
        var (dx, dy) = (Shift(left, right, _viewWidth, margin), Shift(top, bottom, _viewHeight, margin));
        if (dx == 0f && dy == 0f)
        {
            return false;
        }
        PanBy(dx, dy);
        return true;
    }

    /// <summary>Zooms by <paramref name="factor"/> about a view point, which stays over the same canvas point. Returns whether the scale changed.</summary>
    public bool ZoomAt(float factor, float viewX, float viewY)
    {
        if (!_pan.ZoomByFactor(factor, viewX, viewY, new RectF32(0f, 0f, _viewWidth, _viewHeight)))
        {
            return false;
        }
        Moved = true;
        Clamp();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Zooms about a view point by <see cref="ZoomStep"/> raised to the wheel <paramref name="delta"/> (positive zooms in). Returns whether the scale changed.</summary>
    public bool ZoomByWheel(float delta, float viewX, float viewY) => ZoomAt(MathF.Pow(ZoomStep, delta), viewX, viewY);

    /// <summary>Zooms by <paramref name="factor"/> about the view's centre. Returns whether the scale changed.</summary>
    public bool ZoomCentre(float factor) => ZoomAt(factor, _viewWidth / 2f, _viewHeight / 2f);

    /// <summary>Sets the scale (kept within the zoom floor and ceiling) about the view's centre; the view counts as moved.</summary>
    public void SetScale(float scale) => ZoomCentre(Math.Clamp(scale, MinZoom, MaxZoom) / Scale);

    /// <summary>Whether a drag that moves the canvas is in flight.</summary>
    public bool IsPanning => _pan.IsPanning;

    /// <summary>Starts a drag that moves the canvas, from a view point.</summary>
    public void BeginPan(float viewX, float viewY) => _pan.BeginPan(viewX, viewY);

    /// <summary>Moves the canvas by the pointer's motion since the last call. False when no drag is in flight.</summary>
    public bool UpdatePan(float viewX, float viewY)
    {
        if (!_pan.UpdatePan(viewX, viewY))
        {
            return false;
        }
        Moved = true;
        Clamp();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Ends a drag.</summary>
    public void EndPan() => _pan.EndPan();

    /// <summary>One wheel step: up and down, or left and right with <paramref name="sideways"/>. A positive delta (wheel up, or left) shows more of the top (or left).</summary>
    public void Wheel(float delta, bool sideways, float step = 60f)
    {
        var d = delta * step;
        PanBy(sideways ? d : 0f, sideways ? 0f : d);
    }

    /// <summary>
    /// The canvas gestures as one handler. <paramref name="bounds"/> is the viewport's rect on the surface the event is
    /// measured in; points become view points by subtracting its corner and dividing by <paramref name="scaleX"/>,
    /// <paramref name="scaleY"/> (the design scale, when the host draws scaled). Returns whether the event was taken.
    /// <list type="bullet">
    /// <item>Ctrl+Scroll zooms about the pointer by <see cref="ZoomByWheel"/>; Scroll pans vertically, Shift+Scroll sideways.</item>
    /// <item>Pinch zooms about its point by its Scale.</item>
    /// <item>A press of Middle, of Left while Space is held, or of Left where <paramref name="leftDragPans"/> (given surface
    /// coordinates) says yes, begins a pan that only the same button's release ends; moves update it.</item>
    /// <item>Space sets <see cref="SpaceHeld"/> and its release clears it (the release is never taken).</item>
    /// <item>Ctrl+Plus and Ctrl+Minus zoom about the centre, Ctrl+0 fits, Ctrl+1 is 100%. Left and Right pan by
    /// <see cref="KeyPanStep"/> (<see cref="KeyPanStepBig"/> with Shift), and Up and Down too when <see cref="PanWithVerticalKeys"/>.</item>
    /// </list>
    /// Pointer events outside <paramref name="bounds"/> are not taken, except the moves and release of a pan in flight. Keys
    /// are not tied to the bounds: the host decides when the canvas has the keyboard.
    /// </summary>
    public bool HandleInput(InputEvent evt, RectF32 bounds, Func<float, float, bool>? leftDragPans = null, float scaleX = 1f, float scaleY = 1f)
    {
        (float X, float Y) ToView(float x, float y) => ((x - bounds.X) / scaleX, (y - bounds.Y) / scaleY);

        switch (evt)
        {
            case InputEvent.Scroll scroll when bounds.Contains(scroll.X, scroll.Y):
                if ((scroll.Modifiers & InputModifier.Ctrl) != 0)
                {
                    var (vx, vy) = ToView(scroll.X, scroll.Y);
                    ZoomByWheel(scroll.Delta, vx, vy);
                }
                else
                {
                    Wheel(scroll.Delta, (scroll.Modifiers & InputModifier.Shift) != 0);
                }
                return true;
            case InputEvent.Pinch pinch when bounds.Contains(pinch.X, pinch.Y):
                var (px, py) = ToView(pinch.X, pinch.Y);
                ZoomAt(pinch.Scale, px, py);
                return true;
            case InputEvent.MouseDown down when bounds.Contains(down.X, down.Y)
                && (down.Button == MouseButton.Middle
                    || (down.Button == MouseButton.Left && (SpaceHeld || (leftDragPans?.Invoke(down.X, down.Y) ?? false)))):
                _panButton = down.Button;
                var (dx, dy) = ToView(down.X, down.Y);
                BeginPan(dx, dy);
                return true;
            case InputEvent.MouseMove move when IsPanning:
                var (mx, my) = ToView(move.X, move.Y);
                UpdatePan(mx, my);
                return true;
            case InputEvent.MouseUp up when IsPanning && up.Button == _panButton:
                EndPan();
                return true;
            case InputEvent.KeyUp { Key: InputKey.Space }:
                if (SpaceHeld)
                {
                    SpaceHeld = false;
                    Changed?.Invoke();
                }
                return false;
            case InputEvent.KeyDown key:
                return HandleKey(key);
            default:
                return false;
        }
    }

    private bool HandleKey(InputEvent.KeyDown key)
    {
        if ((key.Modifiers & InputModifier.Alt) != 0)
        {
            return false;
        }
        if ((key.Modifiers & InputModifier.Ctrl) != 0)
        {
            switch (key.Key)
            {
                case InputKey.Plus:
                    ZoomCentre(ZoomStep);
                    return true;
                case InputKey.Minus:
                    ZoomCentre(1f / ZoomStep);
                    return true;
                case InputKey.D0:
                    Fit();
                    return true;
                case InputKey.D1:
                    SetScale(1f);
                    return true;
                default:
                    return false;
            }
        }
        var step = (key.Modifiers & InputModifier.Shift) != 0 ? KeyPanStepBig : KeyPanStep;
        switch (key.Key)
        {
            case InputKey.Left:
                PanBy(step, 0f);
                return true;
            case InputKey.Right:
                PanBy(-step, 0f);
                return true;
            case InputKey.Up when PanWithVerticalKeys:
                PanBy(0f, step);
                return true;
            case InputKey.Down when PanWithVerticalKeys:
                PanBy(0f, -step);
                return true;
            case InputKey.Space:
                if (!SpaceHeld)
                {
                    SpaceHeld = true;
                    Changed?.Invoke();
                }
                return true;
            default:
                return false;
        }
    }

    // Keeps at least KeepInView of the canvas on screen, whichever way it was pushed.
    private void Clamp()
    {
        var reachX = MathF.Max(0f, _contentWidth * Scale / 2f + _viewWidth / 2f - KeepInView);
        var reachY = MathF.Max(0f, _contentHeight * Scale / 2f + _viewHeight / 2f - KeepInView);
        _pan.PanOffset = new Vector2(Math.Clamp(_pan.PanOffset.X, -reachX, reachX), Math.Clamp(_pan.PanOffset.Y, -reachY, reachY));
    }
}
