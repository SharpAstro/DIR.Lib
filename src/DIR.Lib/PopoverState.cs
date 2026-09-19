using System;

namespace DIR.Lib;

/// <summary>
/// Whether a popover is open, and the Escape that closes it. Consumer-owned and mutable, like
/// <see cref="TextInputState"/> and <see cref="SliderState"/>: the tree is rebuilt every frame and cannot
/// carry the state, so the state outlives the tree and the tree points at it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The point of this type is that a popover costs ONE obligation instead of five.</b> Opening an
/// overlay by hand meant a flag, a backdrop that closes it, a placement, a keyboard route for Escape, and
/// a line in the host's dispatcher; forgetting any one of them is silent, because the overlay still opens,
/// still draws and still takes clicks, and only the key that should dismiss it goes missing. Declaring the
/// popover puts all five behind <see cref="Layout.Builder.Popover"/> and this state.
/// </para>
/// <para>
/// <b><see cref="IsOpen"/> is read-only from outside on purpose.</b> The three verbs are the whole surface,
/// so "is it open" has exactly one writer and <see cref="Closed"/> cannot be bypassed by someone assigning
/// the flag. That event exists because closing is the transition a consumer actually needs to observe: a
/// popover that edited something commits on close, and a caller polling <see cref="IsOpen"/> every frame
/// would have to remember the previous value to notice.
/// </para>
/// </remarks>
public sealed class PopoverState
{
    /// <summary>Whether the popover is currently displayed. Moved only by <see cref="Open"/>,
    /// <see cref="Close"/> and <see cref="Toggle"/>.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Raised when the popover goes from open to closed, however it was closed: Escape, the
    /// backdrop, or the consumer. Not raised by a <see cref="Close"/> on an already-closed popover.</summary>
    public event Action? Closed;

    /// <summary>Raised when the popover goes from closed to open, however it was opened: a trigger's
    /// press, its shortcut, or the consumer. Not raised by an <see cref="Open"/> on one already open.</summary>
    /// <remarks>
    /// The transition a consumer builds on: a card whose rows are expensive to derive -- an outline walked
    /// lazily -- derives them here, once, rather than on every frame that finds <see cref="IsOpen"/> true.
    /// </remarks>
    public event Action? Opened;

    private PopoverGroup? _group;

    /// <summary>
    /// The group this popover is exclusive within, or null for one that coexists with every other. Stated
    /// at construction, since a popover does not change bars.
    /// </summary>
    /// <remarks>
    /// Joining is what the setter does, so the group needs no separate registration call that a consumer
    /// could forget for one member of six -- which is the shape the hand-written sibling closing took.
    /// </remarks>
    public PopoverGroup? Group
    {
        get => _group;
        init
        {
            _group = value;
            value?.Add(this);
        }
    }

    /// <summary>Opens the popover, closing the rest of its <see cref="Group"/> first, and raises
    /// <see cref="Opened"/> if it was closed. Idempotent.</summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        _group?.Opening(this);
        IsOpen = true;
        Opened?.Invoke();
    }

    /// <summary>Closes the popover and raises <see cref="Closed"/> if it was open. Idempotent.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Closed?.Invoke();
    }

    /// <summary>Opens a closed popover, closes an open one.</summary>
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>
    /// Keys the popover itself does not handle, offered to its CONTENT. Returns true when the content
    /// consumed the key. Null for a popover whose content wants no keys, which is the common case.
    /// </summary>
    /// <remarks>
    /// <b>The claim is made by the popover, so anything inside it that wants keys has to come through
    /// here.</b> Painting an open popover is what puts it on the window's
    /// <see cref="WindowUiSettings.PaintedPopovers"/> stack, and that is what makes Escape work with no
    /// host dispatcher line -- and, until this property existed, also what made a declared menu's
    /// Up/Down/Enter go nowhere: the menu was its own claimant and never got asked, because the popover
    /// held the window's one slot. A delegate rather than an interface since 10.0, the interface having
    /// existed only to fill that slot.
    /// </remarks>
    public Func<InputKey, bool>? ContentKeys { get; set; }

    /// <summary>
    /// Closes on Escape while open, hands anything else to <see cref="ContentKeys"/>, and otherwise declines
    /// so the rest of the key routing still sees it.
    /// </summary>
    /// <remarks>
    /// Kept declining while CLOSED although the stack is now cleared per paint cycle, so a closed popover
    /// is not on it to be asked. The guard costs a branch and holds for a consumer calling this directly,
    /// where there is no stack in the story at all.
    /// </remarks>
    public bool HandleKeyDown(InputKey key)
    {
        if (!IsOpen)
        {
            return false;
        }

        if (key != InputKey.Escape)
        {
            // Escape is the popover's own and never reaches the content; everything else is the content's
            // business. The two sets are disjoint, so a content claimant that routes Escape back here
            // cannot loop.
            return ContentKeys?.Invoke(key) ?? false;
        }

        Close();
        return true;
    }
}
