using System;

namespace DIR.Lib;

/// <summary>Which end of the list its resting position is anchored to.</summary>
public enum ScrollAnchor
{
    /// <summary>Offset 0 shows the first atom at the top (the default for selection lists).</summary>
    Top,

    /// <summary>
    /// Offset rests at the tail: the newest atoms are pinned to the bottom of the viewport, and the
    /// list auto-follows as new atoms are appended — until the user scrolls up into history, which
    /// releases the pin (re-established by scrolling back to the bottom). Models a "tail -f" log
    /// (the live-session exposure log).
    /// </summary>
    Bottom,
}

/// <summary>How (and whether) the scrollbar layer participates.</summary>
public enum ScrollBarMode
{
    /// <summary>No scrollbar drawn and no thumb hit-testing — wheel + drag scrolling only.</summary>
    None,

    /// <summary>Scrollbar is drawn as a passive indicator but does not accept thumb drags / track paging.</summary>
    Decorative,

    /// <summary>Scrollbar is drawn and accepts thumb drags + track paging.</summary>
    Interactive,
}

/// <summary>The scroll axis. Only <see cref="Vertical"/> has shipping consumers; the parameter leaves room for a horizontal variant.</summary>
public enum ScrollAxis
{
    /// <summary>Atoms stack top-to-bottom; the main axis is Y and the scrollbar is a right-edge column.</summary>
    Vertical,

    /// <summary>Atoms flow left-to-right; the main axis is X and the scrollbar is a bottom-edge row.</summary>
    Horizontal,
}

/// <summary>
/// One list-scroll model, host- and DPI-independent, expressed in <b>atoms</b> — the smallest logical
/// scrollable unit of the surface (a list row, config line, or log entry; one cell row in a terminal).
/// The entire scroll position is a single <see cref="Offset"/> float in fractional atoms, which
/// subsumes the three mechanisms that used to be hand-rolled (and inconsistent) per list:
///
/// <list type="bullet">
///   <item><b>Wheel notch</b>: <c>Offset -= delta * <see cref="WheelStepAtoms"/></c>.</item>
///   <item><b>Trackpad / web wheel bridge</b>: the same expression — the fractional part of
///   <see cref="Offset"/> <i>is</i> the carry, so a stream of sub-1.0 deltas accumulates into a scroll
///   instead of truncating to zero (the bug the per-list <c>ref float accum</c> hack worked around).</item>
///   <item><b>Touch / mouse drag</b>: <c>Offset -= dragDeltaPx / atomExtentPx</c> — pixel-smooth by
///   construction.</item>
/// </list>
///
/// <para>
/// Because the unit is atoms (not pixels and not row indices), the same offset value is meaningful
/// across a GUI (where an atom is <c>rowHeight * dpiScale</c> px) and a TUI (where an atom is one
/// cell row) — which is what dissolves the pixels-vs-rows mismatch that made a shared scroll-offset
/// field a latent cross-host bug. <see cref="AtomOffset"/> is the snapped integer accessor for
/// persistence and legacy state fields.
/// </para>
///
/// <para>
/// <b>Redraw:</b> the controller raises <see cref="Changed"/> only on an input-driven position change,
/// never from <see cref="SetExtent"/> geometry updates; the owning widget wires it to its redraw path.
/// </para>
///
/// <para>
/// <b>Degenerate / clip-only mode is free:</b> when the content fits (<see cref="TotalAtoms"/> ≤
/// <see cref="VisibleAtoms"/>) <see cref="MaxOffset"/> is 0, the scrollbar draws nothing, the thumb is
/// unhittable, and drags clamp to 0 — so a search-result or dropdown list that just wants to clip at
/// the visible count needs no special configuration.
/// </para>
/// </summary>
public sealed class ListScrollController
{
    /// <summary>Scrollbar column thickness in DPI-independent pixels.</summary>
    public const float ScrollBarBaseWidthPx = 6f;

    /// <summary>Minimum thumb extent in DPI-independent pixels, so a huge list still leaves a grabbable thumb.</summary>
    public const float MinThumbPx = 20f;

    /// <summary>Default track fill.</summary>
    public static readonly RGBAColor32 DefaultTrackColor = new(0x22, 0x22, 0x2a, 0xff);

    /// <summary>Default thumb fill.</summary>
    public static readonly RGBAColor32 DefaultThumbColor = new(0x44, 0x44, 0x55, 0xff);

    // Geometry — refreshed every frame via SetExtent.
    private RectF32 _viewport;
    private float _atomExtentPx = 1f;
    private int _totalAtoms;
    private float _dpiScale = 1f;
    private bool _positioned;

    // Position — a single fractional-atom offset, always clamped to [0, MaxOffset].
    private float _offset;
    private bool _pinnedToEnd; // Bottom-anchor tail-follow latch.

    // Surface tap-or-drag.
    private TapOrDragGesture _gesture;
    private float _offsetAtDragStart;

    // Interactive-scrollbar thumb drag.
    private bool _thumbDragging;
    private float _thumbGripPx; // where within the thumb the grab landed

    // Tap-on-release row select.
    private int? _pendingTap;

    /// <summary>Which end the list rests against. Set before the first <see cref="SetExtent"/>.</summary>
    public ScrollAnchor Anchor { get; set; } = ScrollAnchor.Top;

    /// <summary>Scrollbar behaviour. Defaults to <see cref="ScrollBarMode.Interactive"/>.</summary>
    public ScrollBarMode Mode { get; set; } = ScrollBarMode.Interactive;

    /// <summary>Scroll axis. Defaults to <see cref="ScrollAxis.Vertical"/>.</summary>
    public ScrollAxis Axis { get; set; } = ScrollAxis.Vertical;

    /// <summary>
    /// When true, the offset is rounded to a whole atom at the end of a drag (row-snapped lists).
    /// Wheel scrolling always keeps its fractional carry regardless — that carry is the accumulator
    /// that fixes precision-trackpad truncation — so a snapped list that does not render
    /// <see cref="SubAtomPx"/> simply reads whole rows off <see cref="FirstVisibleAtom"/>.
    /// </summary>
    public bool SnapToAtom { get; set; }

    /// <summary>Atoms scrolled per unit wheel delta. Default 3, matching desktop convention.</summary>
    public float WheelStepAtoms { get; set; } = 3f;

    /// <summary>Raised whenever an input event changes the scroll position. Wire to the widget's redraw.</summary>
    public event Action? Changed;

    /// <summary>Total atom count from the most recent <see cref="SetExtent"/>.</summary>
    public int TotalAtoms => _totalAtoms;

    /// <summary>The viewport rect from the most recent <see cref="SetExtent"/>.</summary>
    public RectF32 Viewport => _viewport;

    /// <summary>
    /// A hair below one atom, added inside the <see cref="VisibleAtoms"/> floor so a viewport sized to an
    /// exact integer multiple of the atom extent (a menu whose height is computed as <c>N * rowH</c>) does
    /// not lose its last row to <c>N * h / h</c> landing at <c>N - 1e-7</c> in float. It is far under a
    /// row, so a genuine partial atom (3.5 rows) still floors down as intended -- the atom-model successor
    /// to the per-list "+0.5px fit epsilon" the hand-rolled overflow loops needed.
    /// </summary>
    private const float AtomFitEpsilon = 1e-3f;

    /// <summary>
    /// Number of whole atoms that fit in the viewport along the main axis (at least 1, so a single
    /// atom taller than the viewport still scrolls one-at-a-time rather than off the end).
    /// </summary>
    public int VisibleAtoms => Math.Max(1, (int)MathF.Floor(ViewportExtentPx / _atomExtentPx + AtomFitEpsilon));

    /// <summary>Largest legal <see cref="Offset"/>: <c>max(0, TotalAtoms - VisibleAtoms)</c> — the bound that keeps the last atom fully visible.</summary>
    public float MaxOffset => MathF.Max(0f, _totalAtoms - VisibleAtoms);

    /// <summary>Fractional-atom scroll position, clamped to <c>[0, <see cref="MaxOffset"/>]</c>.</summary>
    public float Offset => _offset;

    /// <summary>Index of the first (partially) visible atom — the draw loop's start.</summary>
    public int FirstVisibleAtom => (int)MathF.Floor(_offset);

    /// <summary>Sub-atom pixel shift for a smooth draw: <c>frac(Offset) * atomExtentPx</c> (0 when snapped to a whole atom).</summary>
    public float SubAtomPx => (_offset - MathF.Floor(_offset)) * _atomExtentPx;

    /// <summary>
    /// Snapped integer offset accessor for persistence and legacy state fields. Setting it clamps to
    /// the current geometry, so call <see cref="SetExtent"/> first each frame before restoring a
    /// persisted value.
    /// </summary>
    public int AtomOffset
    {
        get => (int)MathF.Round(_offset);
        set => SetOffset(value);
    }

    /// <summary>
    /// Cross-axis pixel extent available for atom content after reserving the scrollbar column — the
    /// full width/height when the list fits or the scrollbar is <see cref="ScrollBarMode.None"/>.
    /// </summary>
    public float ContentCrossExtentPx
    {
        get
        {
            var full = Axis == ScrollAxis.Vertical ? _viewport.Width : _viewport.Height;
            return HasScrollBar ? full - ScrollBarWidthPx : full;
        }
    }

    /// <summary>
    /// The content sub-rect of the viewport: the full viewport with the scrollbar column reserved (only
    /// when the list overflows and a scrollbar is shown, so a fitting list uses the full width). Lay row
    /// content out within this rect and it never underlaps the thumb. Valid after <see cref="SetExtent"/>.
    /// </summary>
    public RectF32 ContentArea => Axis == ScrollAxis.Vertical
        ? new RectF32(_viewport.X, _viewport.Y, ContentCrossExtentPx, _viewport.Height)
        : new RectF32(_viewport.X, _viewport.Y, _viewport.Width, ContentCrossExtentPx);

    /// <summary>
    /// Enumerates the visible atoms paired with their per-atom content rects along the scroll axis, so a
    /// consumer draws atom <c>Index</c> into <c>Rect</c> and hand-rolls neither the row placement, the
    /// content width (scrollbar column already reserved via <see cref="ContentArea"/>), nor the overflow
    /// cutoff. A row-snapped list yields exactly the fully-visible atoms (matching the old break-on-overflow
    /// loops); a smooth surface applies the sub-atom scroll shift and includes one extra partially-visible
    /// trailing atom (clip to <see cref="ContentArea"/> when drawing). Allocation-free struct <c>foreach</c>.
    /// </summary>
    public VisibleRowRange VisibleRows() => new(this);

    private float ViewportExtentPx => Axis == ScrollAxis.Vertical ? _viewport.Height : _viewport.Width;
    private float MainStart => Axis == ScrollAxis.Vertical ? _viewport.Y : _viewport.X;
    private float ScrollBarWidthPx => ScrollBarBaseWidthPx * _dpiScale;
    private bool HasScrollBar => Mode != ScrollBarMode.None && MaxOffset > 0f;

    private static float MainCoord(ScrollAxis axis, float x, float y) => axis == ScrollAxis.Vertical ? y : x;

    /// <summary>
    /// Refresh the per-frame geometry. Idempotent and side-effect-free w.r.t. <see cref="Changed"/>:
    /// it re-clamps <see cref="Offset"/> and, for a <see cref="ScrollAnchor.Bottom"/> list still
    /// pinned to the tail, follows the new end — but never raises the event (the caller is already
    /// rendering this frame).
    /// </summary>
    public void SetExtent(RectF32 viewport, float atomExtentPx, int totalAtoms, float dpiScale)
    {
        _viewport = viewport;
        _atomExtentPx = atomExtentPx > 0f ? atomExtentPx : 1f;
        _totalAtoms = Math.Max(0, totalAtoms);
        _dpiScale = dpiScale > 0f ? dpiScale : 1f;

        if (!_positioned)
        {
            _positioned = true;
            if (Anchor == ScrollAnchor.Bottom)
            {
                _offset = MaxOffset;
                _pinnedToEnd = true;
            }
        }
        else if (Anchor == ScrollAnchor.Bottom && _pinnedToEnd)
        {
            _offset = MaxOffset;
        }
        else if (Anchor == ScrollAnchor.Bottom && MaxOffset <= 0f)
        {
            // A fitting list has no history position to hold, so the tail pin re-establishes.
            // Concretely: clearing + refilling the list (a session restart resetting its log)
            // resumes tail-follow even if the user had scrolled into the old history.
            _pinnedToEnd = true;
        }

        _offset = Math.Clamp(_offset, 0f, MaxOffset);
    }

    /// <summary>
    /// Single input forward: wheel + interactive-scrollbar thumb/track + surface tap-or-drag. Returns
    /// <c>true</c> when the event was consumed. The owning widget should forward only presses that its
    /// own registered clickables (row sub-buttons, etc.) did not claim — unclaimed presses fall through
    /// to here and arm the surface gesture.
    /// </summary>
    public bool HandleInput(InputEvent evt)
    {
        switch (evt)
        {
            case InputEvent.Scroll(var delta, var sx, var sy, _):
                if (!_viewport.Contains(sx, sy) || MaxOffset <= 0f)
                {
                    return false;
                }
                ApplyScrollDelta(delta);
                return true;

            case InputEvent.MouseDown(var dx, var dy, MouseButton.Left, var mods, _):
                if (!_viewport.Contains(dx, dy))
                {
                    return false;
                }
                if (Mode == ScrollBarMode.Interactive && MaxOffset > 0f && TryBeginThumbInteraction(dx, dy))
                {
                    return true;
                }
                _gesture.Arm(dx, dy, mods, _dpiScale);
                _offsetAtDragStart = _offset;
                return true;

            case InputEvent.MouseMove(var mx, var my):
                if (_thumbDragging)
                {
                    UpdateThumbDrag(mx, my);
                    return true;
                }
                if (_gesture.State != GestureState.Idle)
                {
                    if (_gesture.Update(mx, my))
                    {
                        ApplyDragTo(mx, my);
                    }
                    return true;
                }
                return false;

            case InputEvent.MouseUp(var ux, var uy, MouseButton.Left):
                if (_thumbDragging)
                {
                    _thumbDragging = false;
                    if (SnapToAtom)
                    {
                        SnapOffset();
                    }
                    return true;
                }
                if (_gesture.State != GestureState.Idle)
                {
                    var outcome = _gesture.Release(ux, uy);
                    if (outcome == GestureOutcome.Tap)
                    {
                        _pendingTap = AtomAt(ux, uy);
                    }
                    else if (outcome == GestureOutcome.Drag && SnapToAtom)
                    {
                        SnapOffset();
                    }
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Scroll <paramref name="atom"/> into view with an optional margin, minimally (a no-op when it is
    /// already visible, so mouse scrolling and keyboard selection coexist without snapping to a center).
    /// </summary>
    public void EnsureVisible(int atom, int marginAtoms = 0)
    {
        if (atom < 0 || atom >= _totalAtoms)
        {
            return;
        }

        var visible = VisibleAtoms;
        var first = FirstVisibleAtom;
        var before = _offset;

        if (atom - marginAtoms < first)
        {
            _offset = atom - marginAtoms;
        }
        else if (atom + 1 + marginAtoms > first + visible)
        {
            _offset = atom + 1 + marginAtoms - visible;
        }

        _offset = Math.Clamp(_offset, 0f, MaxOffset);
        RaiseIfMoved(before);
    }

    /// <summary>Consume the pending tap-on-release atom index (row select), or <c>null</c> if none is queued.</summary>
    public int? TakeAtomTap()
    {
        var tap = _pendingTap;
        _pendingTap = null;
        return tap;
    }

    /// <summary>
    /// Draw the scrollbar via the widget's float <c>FillRect</c> (typically passed as a method group).
    /// No-op when the list fits or <see cref="Mode"/> is <see cref="ScrollBarMode.None"/>. The
    /// interactive-vs-decorative distinction is purely about input; the drawing is identical.
    /// </summary>
    public void DrawScrollBar(Action<float, float, float, float, RGBAColor32> fillRect, RGBAColor32? track = null, RGBAColor32? thumb = null)
    {
        if (!HasScrollBar)
        {
            return;
        }

        var width = ScrollBarWidthPx;
        var (trackStart, trackExtent, thumbStart, thumbExtent) = ThumbGeometry();

        if (Axis == ScrollAxis.Vertical)
        {
            var colX = _viewport.Right - width;
            fillRect(colX, trackStart, width, trackExtent, track ?? DefaultTrackColor);
            fillRect(colX + 1f, thumbStart, width - 2f, thumbExtent, thumb ?? DefaultThumbColor);
        }
        else
        {
            var colY = _viewport.Bottom - width;
            fillRect(trackStart, colY, trackExtent, width, track ?? DefaultTrackColor);
            fillRect(thumbStart, colY + 1f, thumbExtent, width - 2f, thumb ?? DefaultThumbColor);
        }
    }

    private void ApplyScrollDelta(float delta)
    {
        var before = _offset;
        // Positive delta = scroll up = toward the start of the list. The fractional remainder stays in
        // _offset and IS the trackpad accumulator.
        _offset = Math.Clamp(_offset - delta * WheelStepAtoms, 0f, MaxOffset);
        RaiseIfMoved(before);
    }

    private void ApplyDragTo(float x, float y)
    {
        var (downX, downY) = _gesture.DownPosition;
        var moved = MainCoord(Axis, x, y) - MainCoord(Axis, downX, downY);
        var before = _offset;
        // Absolute from the drag start (like the thumb math) so integer truncation can't drift.
        // Dragging content along +main (down/right) reveals earlier atoms → offset decreases.
        _offset = Math.Clamp(_offsetAtDragStart - moved / _atomExtentPx, 0f, MaxOffset);
        RaiseIfMoved(before);
    }

    private bool TryBeginThumbInteraction(float x, float y)
    {
        if (!PointInScrollBarColumn(x, y))
        {
            return false;
        }

        var (trackStart, _, thumbStart, thumbExtent) = ThumbGeometry();
        var m = MainCoord(Axis, x, y);

        if (m >= thumbStart && m < thumbStart + thumbExtent)
        {
            _thumbDragging = true;
            _thumbGripPx = m - thumbStart;
        }
        else
        {
            // Track click above/below the thumb → page by one viewport toward the click.
            var before = _offset;
            _offset = Math.Clamp(_offset + (m < thumbStart ? -VisibleAtoms : VisibleAtoms), 0f, MaxOffset);
            RaiseIfMoved(before);
        }

        return true;
    }

    private void UpdateThumbDrag(float x, float y)
    {
        var (trackStart, trackExtent, _, thumbExtent) = ThumbGeometry();
        var trackUsable = MathF.Max(1f, trackExtent - thumbExtent);
        var targetThumbStart = MainCoord(Axis, x, y) - _thumbGripPx;
        var normalized = Math.Clamp((targetThumbStart - trackStart) / trackUsable, 0f, 1f);
        var before = _offset;
        _offset = normalized * MaxOffset;
        RaiseIfMoved(before);
    }

    private bool PointInScrollBarColumn(float x, float y)
    {
        var width = ScrollBarWidthPx;
        return Axis == ScrollAxis.Vertical
            ? x >= _viewport.Right - width && x < _viewport.Right && y >= _viewport.Y && y < _viewport.Bottom
            : y >= _viewport.Bottom - width && y < _viewport.Bottom && x >= _viewport.X && x < _viewport.Right;
    }

    // Thumb geometry in main-axis pixels — structurally identical to Console.Lib.ScrollableList.ComputeThumb
    // and the (now-superseded) TianWen ScrollBar, but over fractional atoms.
    private (float TrackStart, float TrackExtent, float ThumbStart, float ThumbExtent) ThumbGeometry()
    {
        var trackStart = MainStart;
        var trackExtent = ViewportExtentPx;
        var visible = VisibleAtoms;
        var thumbExtent = MathF.Max(MinThumbPx * _dpiScale, trackExtent * visible / Math.Max(1, _totalAtoms));
        var trackUsable = MathF.Max(1f, trackExtent - thumbExtent);
        var maxOffset = MaxOffset;
        var thumbStart = maxOffset > 0f ? trackStart + trackUsable * (_offset / maxOffset) : trackStart;
        return (trackStart, trackExtent, thumbStart, thumbExtent);
    }

    private int? AtomAt(float x, float y)
    {
        if (!_viewport.Contains(x, y))
        {
            return null;
        }

        var relMain = MainCoord(Axis, x, y) - MainStart;
        var atom = FirstVisibleAtom + (int)MathF.Floor((relMain + SubAtomPx) / _atomExtentPx);
        return atom >= 0 && atom < _totalAtoms ? atom : null;
    }

    private void SetOffset(float value)
    {
        var before = _offset;
        _offset = Math.Clamp(value, 0f, MaxOffset);
        RaiseIfMoved(before);
    }

    private void SnapOffset()
    {
        var before = _offset;
        _offset = Math.Clamp(MathF.Round(_offset), 0f, MaxOffset);
        RaiseIfMoved(before);
    }

    private void RaiseIfMoved(float before)
    {
        if (_offset != before)
        {
            if (Anchor == ScrollAnchor.Bottom)
            {
                _pinnedToEnd = _offset >= MaxOffset - 0.001f;
            }
            Changed?.Invoke();
        }
    }

    /// <summary>Test-only view of the thumb geometry (start, extent) in main-axis pixels.</summary>
    internal (float ThumbStart, float ThumbExtent) DebugThumbGeometry()
    {
        var (_, _, thumbStart, thumbExtent) = ThumbGeometry();
        return (thumbStart, thumbExtent);
    }

    /// <summary>Test-only: is a surface tap-or-drag currently mid-drag?</summary>
    internal bool DebugIsDragging => _gesture.IsDragging;

    /// <summary>Allocation-free enumerable of the visible atoms + rects (see <see cref="VisibleRows"/>).</summary>
    public readonly struct VisibleRowRange(ListScrollController controller)
    {
        public Enumerator GetEnumerator() => new(controller);

        /// <summary>Struct enumerator over <c>(atom index, content rect)</c> pairs; no heap allocation.</summary>
        public struct Enumerator
        {
            private readonly bool _vertical;
            private readonly RectF32 _content;
            private readonly int _first;
            private readonly int _lastExclusive;
            private readonly float _atom;
            private readonly float _shift;
            private int _index;

            internal Enumerator(ListScrollController c)
            {
                _vertical = c.Axis == ScrollAxis.Vertical;
                _content = c.ContentArea;
                _first = c.FirstVisibleAtom;
                _atom = c._atomExtentPx;
                // A snapped list draws only fully-visible atoms with no sub-atom shift (the old break-on-
                // overflow behaviour); a smooth surface shifts by the sub-atom fraction and draws one extra
                // partially-visible trailing atom.
                _shift = c.SnapToAtom ? 0f : c.SubAtomPx;
                var drawCount = c.SnapToAtom ? c.VisibleAtoms : c.VisibleAtoms + 1;
                _lastExclusive = Math.Min(c._totalAtoms, _first + Math.Max(0, drawCount));
                _index = _first - 1;
                Current = default;
            }

            public (int Index, RectF32 Rect) Current { get; private set; }

            public bool MoveNext()
            {
                _index++;
                if (_index >= _lastExclusive)
                {
                    return false;
                }
                var main = (_index - _first) * _atom - _shift;
                Current = _vertical
                    ? (_index, new RectF32(_content.X, _content.Y + main, _content.Width, _atom))
                    : (_index, new RectF32(_content.X + main, _content.Y, _atom, _content.Height));
                return true;
            }
        }
    }
}
