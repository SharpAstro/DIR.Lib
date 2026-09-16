using System;
using System.Collections.Immutable;

namespace DIR.Lib
{
    /// <summary>
    /// State for a generic dropdown menu overlay. Open it with <see cref="Open"/>,
    /// render with <see cref="PixelWidgetBase{TSurface}.RenderDropdownMenu"/>,
    /// and handle keyboard with <see cref="HandleKeyDown"/>.
    /// </summary>
    /// <remarks>
    /// Typed over what an entry MEANS (see <see cref="DropdownItem{T}"/>), so the select callback receives
    /// the chosen entry rather than an index the caller has to map back. Use <c>DropdownMenuState&lt;string&gt;</c>
    /// with <see cref="DropdownItem.Text"/> for a plain list of labels.
    /// </remarks>
    public class DropdownMenuState<T> : IKeyboardClaimant
    {
        /// <summary>
        /// Whether the menu is open, and the Escape that closes it -- the same <see cref="PopoverState"/>
        /// a declared popover uses, so a dropdown IS one rather than a second thing shaped like one.
        /// </summary>
        /// <remarks>
        /// <b>This is the single writer.</b> <see cref="IsOpen"/> and <see cref="Close"/> both go through
        /// it, which is what lets <see cref="Layout.Builder.Dropdown"/> hand the very same state to
        /// <see cref="Layout.Builder.Popover"/> and have the backdrop, the Escape claim and the open flag
        /// agree by construction. Two flags kept in step by hand is the failure this removes.
        /// </remarks>
        /// <summary>
        /// The <see cref="HitResult.ListItemHit.ListId"/> every dropdown row registers under, on both the
        /// declared path (<see cref="Layout.Builder.Dropdown"/>) and the hand-rendered one
        /// (<c>PixelWidgetBase.RenderDropdownMenu</c>). Named once so the two cannot drift apart while
        /// both exist -- a host matching on the id would otherwise see rows from one path and not the other.
        /// </summary>
        public const string ListId = "Dropdown";

        public PopoverState Popover { get; } = new();

        public DropdownMenuState()
        {
            // Whatever closes the menu -- Escape, the backdrop, TrySelect, a consumer assigning IsOpen --
            // clears the highlight. Close() used to do this inline because it was the ONLY way to close;
            // a declared dropdown's backdrop press closes the PopoverState directly and never calls it,
            // so the reset has to hang off the transition rather than off one of its callers.
            Popover.Closed += () => HighlightIndex = -1;
        }

        /// <summary>Whether the menu is currently displayed. Backed by <see cref="Popover"/>.</summary>
        /// <remarks>
        /// Kept settable so existing callers still read as they did, but assigning it now runs the popover's
        /// verbs -- so <see cref="PopoverState.Closed"/> fires on the way down no matter which spelling the
        /// caller used.
        /// </remarks>
        public bool IsOpen
        {
            get => Popover.IsOpen;
            set
            {
                if (value)
                {
                    Popover.Open();
                }
                else
                {
                    Popover.Close();
                }
            }
        }
        public ImmutableArray<DropdownItem<T>> Items { get; set; } = [];
        public int HighlightIndex { get; set; } = -1;

        /// <summary>
        /// Scroll model for a menu that outgrows its <c>maxHeight</c>. A menu that fits leaves this at
        /// offset 0 with no scrollbar (<see cref="ListScrollController.MaxOffset"/> is 0), so the common
        /// case is unchanged; an overflowing menu scrolls its window instead of silently clipping the rows
        /// past the fold. Geometry is refreshed each frame by
        /// <see cref="PixelWidgetBase{TSurface}.RenderDropdownMenu"/>; keyboard navigation keeps the
        /// highlight in view via <see cref="HandleKeyDown"/>, and a host may forward a wheel event through
        /// <see cref="HandleScrollInput"/>. Row-snapped + decorative (the dropdown owns row clicks, so the
        /// bar is a pure overflow indicator, not an interactive thumb).
        /// </summary>
        public ListScrollController Scroll { get; } = new()
        {
            SnapToAtom = true,
            Mode = ScrollBarMode.Decorative,
        };

        // Anchor geometry — set by the trigger during normal layout
        public float AnchorX { get; set; }
        public float AnchorY { get; set; }
        public float AnchorWidth { get; set; }

        /// <summary>Callback when an entry is selected; receives the entry itself, so no index mapping.</summary>
        public Action<DropdownItem<T>>? OnSelect { get; set; }

        /// <summary>Whether the entry at <paramref name="index"/> can be chosen.</summary>
        public bool IsEnabled(int index)
            => index >= 0 && index < Items.Length && Items[index].IsEnabled;

        /// <summary>
        /// Opens the dropdown anchored below the trigger at the given position.
        /// </summary>
        /// <param name="highlightIndex">
        /// Where the keyboard starts, normally the CURRENT selection. That is what makes Down/Up feel like a
        /// menu rather than a list that always restarts at the top; -1 (nothing highlighted) is the default
        /// for callers with no current value.
        /// </param>
        /// <param name="onSelect">
        /// Where a VALUE entry goes. Optional, because a menu whose rows each carry their own
        /// <see cref="DropdownItem{T}.OnChoose"/> has no single handler to name -- and building one would
        /// mean a parallel list indexed by position, which is the coupling this type exists to remove.
        /// </param>
        public void Open(float x, float y, float width,
                         ImmutableArray<DropdownItem<T>> items,
                         Action<DropdownItem<T>>? onSelect = null,
                         int highlightIndex = -1)
        {
            IsOpen = true;
            AnchorX = x;
            AnchorY = y;
            AnchorWidth = width;
            Items = items;
            OnSelect = onSelect;
            HighlightIndex = highlightIndex;
            // Start each open at the top; the next render's SetExtent re-clamps to the real geometry.
            Scroll.AtomOffset = 0;
        }

        /// <summary>
        /// Closes the dropdown.
        /// </summary>
        public void Close() => Popover.Close();

        /// <summary>
        /// Selects the entry at <paramref name="index"/> if it can be chosen, and closes. Returns whether it
        /// was acted on, so a click handler can leave a disabled row's menu OPEN -- closing on a click that
        /// did nothing is the same silent dead-end the disabled state exists to remove.
        /// </summary>
        public bool TrySelect(int index)
        {
            if (!IsEnabled(index))
            {
                return false;
            }

            var item = Items[index];
            // An action entry runs its own callback; a value entry goes to the menu's select handler.
            if (item.OnChoose is { } act)
            {
                act();
            }
            else
            {
                OnSelect?.Invoke(item);
            }

            Close();
            return true;
        }

        /// <summary>
        /// Forwards a wheel event to the scroll model, so a host that routes unclaimed input to an open
        /// dropdown gets mouse-wheel scrolling on an overflowing menu. Keyboard navigation already scrolls
        /// via <see cref="HandleKeyDown"/>, so this is the opt-in mouse counterpart; returns <c>true</c>
        /// when the event was consumed (the caller should redraw). A no-op (returns <c>false</c>) when the
        /// menu is closed or fits within its viewport (<see cref="ListScrollController.MaxOffset"/> is 0).
        /// </summary>
        public bool HandleScrollInput(InputEvent evt) => IsOpen && Scroll.HandleInput(evt);

        /// <summary>
        /// Handles arrow keys, Enter, and Escape. Returns true if consumed.
        /// </summary>
        public bool HandleKeyDown(InputKey key)
        {
            if (!IsOpen)
            {
                return false;
            }

            switch (key)
            {
                case InputKey.Down:
                    HighlightIndex = NextSelectable(HighlightIndex, +1);
                    Scroll.EnsureVisible(HighlightIndex);
                    return true;

                case InputKey.Up:
                    HighlightIndex = NextSelectable(HighlightIndex, -1);
                    Scroll.EnsureVisible(HighlightIndex);
                    return true;

                case InputKey.Enter:
                    TrySelect(HighlightIndex);
                    return true;

                case InputKey.Escape:
                    // Deliberately the popover's own rule rather than a second copy of it: one Escape
                    // behaviour, whether this menu was declared as a node or rendered by hand.
                    return Popover.HandleKeyDown(key);

                default:
                    return false;
            }
        }

        /// <summary>
        /// The next selectable entry from <paramref name="from"/> in the <paramref name="step"/> direction,
        /// skipping disabled ones and stopping at the ends (no wrap).
        /// </summary>
        /// <remarks>
        /// Skipping rather than landing-then-refusing is the point: a highlight that parks on an entry Enter
        /// will not act on reads as a stuck key. Returns <paramref name="from"/> unchanged when nothing
        /// further in that direction is selectable, so the highlight holds its place instead of jumping to an
        /// end.
        /// </remarks>
        private int NextSelectable(int from, int step)
        {
            for (var i = from + step; i >= 0 && i < Items.Length; i += step)
            {
                if (IsEnabled(i))
                {
                    return i;
                }
            }

            return from;
        }
    }
}
