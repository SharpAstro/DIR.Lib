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
public sealed class PopoverState : IKeyboardClaimant
{
    /// <summary>Whether the popover is currently displayed. Moved only by <see cref="Open"/>,
    /// <see cref="Close"/> and <see cref="Toggle"/>.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Raised when the popover goes from open to closed, however it was closed: Escape, the
    /// backdrop, or the consumer. Not raised by a <see cref="Close"/> on an already-closed popover.</summary>
    public event Action? Closed;

    /// <summary>Opens the popover. Idempotent.</summary>
    public void Open() => IsOpen = true;

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
    /// The claimant for keys the popover itself does not handle -- its CONTENT. Null for a popover whose
    /// content wants no keys, which is the common case and the behaviour this type shipped with.
    /// </summary>
    /// <remarks>
    /// <b>The claim is made by the popover, so anything inside it that wants keys has to come through
    /// here.</b> Painting an open popover puts THIS object in the window's single claimant slot, which is
    /// what makes Escape work without a host dispatcher line -- and, until this property existed, also what
    /// made a declared menu's Up/Down/Enter go nowhere: the menu state implemented
    /// <see cref="IKeyboardClaimant"/> and never got asked, because the popover had taken the slot. A
    /// hand-rendered menu did not hit this, since the host registered the menu itself as the claimant.
    /// </remarks>
    public IKeyboardClaimant? ContentKeys { get; set; }

    /// <summary>
    /// Closes on Escape while open, hands anything else to <see cref="ContentKeys"/>, and otherwise declines
    /// so the rest of the key routing still sees it.
    /// </summary>
    /// <remarks>
    /// Declining while CLOSED is what makes the claimant slot safe to leave stale: the slot is set by being
    /// painted and nothing clears it when the popover goes away, so a closed popover that answered true
    /// would swallow every Escape in the window.
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
            return ContentKeys?.HandleKeyDown(key) ?? false;
        }

        Close();
        return true;
    }
}
