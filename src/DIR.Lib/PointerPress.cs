using System;

namespace DIR.Lib;

/// <summary>
/// A press WITH where it landed, handed to <see cref="Layout.Node.OnPress"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Layout.Node.OnClick"/> is <c>Action&lt;InputModifier&gt;</c> and carries no position, so a
/// control whose gesture depends on WHERE inside itself it was pressed -- a slider, a scrub bar, a
/// divider -- cannot arm anything from the node it was painted on. Every one of them therefore opts out
/// of the region model and re-derives its own track rect beside the paint, which is the drift
/// <c>.Clickable</c> exists to make impossible: six such rect caches were counted in one consumer, each
/// written every frame and read on the next press.
/// </para>
/// <para>
/// With a position on the press, "draw == hit" becomes "draw == drag" by construction.
/// </para>
/// </remarks>
/// <param name="Clicks">Click count as the platform reported it (1 = single, 2 = double). The host counts
/// this, because the double-click interval is a system setting and a hand-timed one disagrees with every
/// other application on the machine.</param>
public readonly record struct PointerPress(float X, float Y, MouseButton Button, InputModifier Modifiers, int Clicks);

/// <summary>
/// A pointer position during a gesture already in progress, carrying the button and modifiers the
/// gesture STARTED with.
/// </summary>
/// <remarks>
/// <see cref="InputEvent.MouseMove"/> gained a button in 9.2 and still carries no modifiers, and a
/// release reports the state at RELEASE time, which is not the state the gesture was armed under -- a
/// reader who lets go of Shift mid-drag has not changed what the drag is. So the press's own button and
/// modifiers travel with the capture instead, which is what every hand-rolled drag already stored in a
/// field of its own.
/// </remarks>
public readonly record struct PointerMove(float X, float Y, MouseButton Button, InputModifier Modifiers);

/// <summary>
/// What a press handler RETURNS to say "the gesture is mine until the button comes up": the moves and
/// the release go to these two, and to nothing else.
/// </summary>
/// <remarks>
/// A capture is the answer to the question a drag flag asks badly. The flag form -- a
/// <c>bool IsDragging</c> beside a cached track rect, tested in three separate branches of a host's
/// dispatcher -- puts the arming, the tracking and the ending in three places that must agree about a
/// gesture only one of them can see; the before/after split divider was added to one dispatcher and did
/// nothing in the other for exactly that reason. Returning <see langword="null"/> from the press handler
/// declines, and the press falls through to whatever would have had it.
/// </remarks>
/// <param name="onMove">Every move until the button comes up.</param>
/// <param name="onRelease">The release, once. A capture is spent after it.</param>
public sealed class DragCapture(Action<PointerMove> onMove, Action<PointerMove> onRelease)
{
    /// <summary>Deliver a move to the gesture.</summary>
    public void Move(in PointerMove move) => onMove(move);

    /// <summary>Deliver the release that ends the gesture.</summary>
    public void Release(in PointerMove release) => onRelease(release);
}
