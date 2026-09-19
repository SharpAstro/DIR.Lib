using System.Collections.Generic;

namespace DIR.Lib;

/// <summary>
/// A set of popovers of which at most one is open: opening a member closes the others. A menu bar, a
/// row of chips each dropping a card, a toolbar of dropdowns.
/// </summary>
/// <remarks>
/// <para>
/// Exclusivity is a property of the GROUP and not of a popover, which is why it is not a flag on
/// <see cref="PopoverState"/>: a submenu raised from inside a card is a popover too, and "close every
/// other popover when one opens" would close the card it came from. A member is exclusive with its
/// siblings and with nothing else, so nesting keeps working by simply not joining.
/// </para>
/// <para>
/// Declared where the states are made -- <c>new PopoverState { Group = bar }</c> -- rather than enforced
/// by whichever code opens them. Every consumer with a bar of cards wrote the closing by hand, one
/// <c>Close()</c> per sibling in every open path, and the path that forgot one was the keyboard's. The
/// group is the one place the rule is stated, and it holds for a press, a shortcut and a consumer's own
/// <see cref="PopoverState.Open"/> alike.
/// </para>
/// </remarks>
public sealed class PopoverGroup
{
    private readonly List<PopoverState> _members = [];

    /// <summary>The popovers in this group, in the order they joined.</summary>
    public IReadOnlyList<PopoverState> Members => _members;

    /// <summary>The member that is open, or null when none is. Never more than one, by construction.</summary>
    public PopoverState? Open
    {
        get
        {
            foreach (var member in _members)
            {
                if (member.IsOpen)
                {
                    return member;
                }
            }

            return null;
        }
    }

    /// <summary>Closes whichever member is open. Idempotent.</summary>
    public void CloseAll()
    {
        foreach (var member in _members)
        {
            member.Close();
        }
    }

    internal void Add(PopoverState member) => _members.Add(member);

    /// <summary>
    /// Called by a member as it opens: the siblings close FIRST, so their <see cref="PopoverState.Closed"/>
    /// fires before the newcomer's <see cref="PopoverState.Opened"/>, and a consumer committing on close
    /// has done so before the next card reads the state it committed.
    /// </summary>
    internal void Opening(PopoverState member)
    {
        foreach (var other in _members)
        {
            if (!ReferenceEquals(other, member))
            {
                other.Close();
            }
        }
    }
}
