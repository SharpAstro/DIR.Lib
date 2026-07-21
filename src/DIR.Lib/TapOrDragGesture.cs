namespace DIR.Lib;

/// <summary>Lifecycle state of a <see cref="TapOrDragGesture"/>.</summary>
public enum GestureState
{
    /// <summary>No press is in flight.</summary>
    Idle,

    /// <summary>A press landed and has not (yet) moved beyond the slop radius — still ambiguous.</summary>
    Armed,

    /// <summary>The press moved past the slop radius and has committed to a drag.</summary>
    Dragging,
}

/// <summary>How an armed press resolved when the button was released.</summary>
public enum GestureOutcome
{
    /// <summary>Release with no matching arm — nothing to report.</summary>
    None,

    /// <summary>Released without ever crossing the slop radius — treat as a click / tap.</summary>
    Tap,

    /// <summary>Released after committing to a drag.</summary>
    Drag,
}

/// <summary>
/// A tiny press → (tap | drag) discriminator, extracted from the sky-map's
/// click-vs-pan trio (<c>ClickDragThresholdPx = 4f</c>, squared-distance slop, and the
/// "did the matching MouseDown land on me" gate). A press is <see cref="Arm"/>ed on
/// mouse-down; each move is fed to <see cref="Update"/> (which latches
/// <see cref="GestureState.Dragging"/> the moment the pointer travels past the slop
/// radius); <see cref="Release"/> reports whether the whole interaction was a tap or a drag.
///
/// <para>
/// <b>Why capture modifiers at arm time?</b> <see cref="InputEvent.MouseUp"/> does not carry
/// modifiers — only <see cref="InputEvent.MouseDown"/> does — so a tap that needs to know whether
/// Shift/Ctrl was held (e.g. multi-select) must remember them from the press. <see cref="DownModifiers"/>
/// preserves them for the release.
/// </para>
///
/// <para>
/// <b>Why a struct with no field initializers?</b> This value is meant to be held as a single field
/// on the owning controller/widget and mutated in place. A field declared as
/// <c>private TapOrDragGesture _gesture;</c> is <c>default</c>-initialized, which bypasses any
/// property/field initializer — so the slop radius is passed through <see cref="Arm"/> (a method
/// default, always honored) rather than a struct initializer that <c>default</c> would silently zero.
/// A zero slop would classify every press as a drag, so this is load-bearing.
/// </para>
///
/// <para>
/// <see cref="Release"/> re-checks the slop distance itself, so a host that only calls
/// <see cref="Arm"/> + <see cref="Release"/> (never pumping <see cref="Update"/> on moves) still
/// classifies correctly; a host that pumps <see cref="Update"/> additionally gets the latch — a
/// drag that wanders back inside the slop radius before release still counts as a drag.
/// </para>
/// </summary>
public struct TapOrDragGesture
{
    /// <summary>Default slop radius in DPI-independent pixels (matches the sky-map's historical 4px).</summary>
    public const float DefaultSlopPx = 4f;

    private GestureState _state;
    private float _downX;
    private float _downY;
    private float _slopSq; // (slopPx * dpiScale)^2, resolved once at Arm time
    private InputModifier _downModifiers;

    /// <summary>Current lifecycle state.</summary>
    public readonly GestureState State => _state;

    /// <summary>True while a press is armed but has not committed to a drag.</summary>
    public readonly bool IsArmed => _state == GestureState.Armed;

    /// <summary>True once the press has committed to a drag.</summary>
    public readonly bool IsDragging => _state == GestureState.Dragging;

    /// <summary>Modifiers captured at press time (see the type remarks for why).</summary>
    public readonly InputModifier DownModifiers => _downModifiers;

    /// <summary>The press position, for tap-target resolution and absolute drag deltas.</summary>
    public readonly (float X, float Y) DownPosition => (_downX, _downY);

    /// <summary>
    /// Arm a press at <paramref name="x"/>/<paramref name="y"/>. The slop radius is
    /// <paramref name="slopPx"/> scaled by <paramref name="dpiScale"/>, resolved once here so a
    /// later monitor/DPI change cannot retroactively reinterpret an in-flight gesture.
    /// </summary>
    public void Arm(float x, float y, InputModifier modifiers = InputModifier.None, float dpiScale = 1f, float slopPx = DefaultSlopPx)
    {
        _state = GestureState.Armed;
        _downX = x;
        _downY = y;
        _downModifiers = modifiers;
        var slop = slopPx * dpiScale;
        _slopSq = slop * slop;
    }

    /// <summary>
    /// Feed a pointer move. Latches <see cref="GestureState.Dragging"/> the first time the pointer
    /// travels past the slop radius from the press point. Returns <c>true</c> when the gesture is
    /// dragging after this call (so a caller can start applying drag deltas), <c>false</c> while it
    /// is still an ambiguous armed press or idle.
    /// </summary>
    public bool Update(float x, float y)
    {
        if (_state == GestureState.Armed)
        {
            var dx = x - _downX;
            var dy = y - _downY;
            if (dx * dx + dy * dy > _slopSq)
            {
                _state = GestureState.Dragging;
            }
        }
        return _state == GestureState.Dragging;
    }

    /// <summary>
    /// Release the press and report the outcome, then reset to <see cref="GestureState.Idle"/>.
    /// A dragging gesture always reports <see cref="GestureOutcome.Drag"/>; an armed gesture reports
    /// <see cref="GestureOutcome.Drag"/> if the release point is past the slop radius (covers hosts
    /// that never pumped <see cref="Update"/>) and <see cref="GestureOutcome.Tap"/> otherwise; an idle
    /// gesture reports <see cref="GestureOutcome.None"/>.
    /// </summary>
    public GestureOutcome Release(float x, float y)
    {
        GestureOutcome outcome;
        if (_state == GestureState.Dragging)
        {
            outcome = GestureOutcome.Drag;
        }
        else if (_state == GestureState.Armed)
        {
            var dx = x - _downX;
            var dy = y - _downY;
            outcome = dx * dx + dy * dy > _slopSq ? GestureOutcome.Drag : GestureOutcome.Tap;
        }
        else
        {
            outcome = GestureOutcome.None;
        }

        _state = GestureState.Idle;
        return outcome;
    }

    /// <summary>Abandon any in-flight press without reporting an outcome (e.g. a pinch superseded it).</summary>
    public void Cancel() => _state = GestureState.Idle;
}
