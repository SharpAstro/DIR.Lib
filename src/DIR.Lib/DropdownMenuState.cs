using System;
using System.Collections.Immutable;

namespace DIR.Lib
{
    /// <summary>
    /// State for a generic dropdown menu overlay. Open it with <see cref="Open"/>,
    /// render with <see cref="PixelWidgetBase{TSurface}.RenderDropdownMenu"/>,
    /// and handle keyboard with <see cref="HandleKeyDown"/>.
    /// </summary>
    public class DropdownMenuState
    {
        public bool IsOpen { get; set; }
        public ImmutableArray<string> Items { get; set; } = [];
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

        /// <summary>Callback when an item is selected (receives index and item text).</summary>
        public Action<int, string>? OnSelect { get; set; }

        /// <summary>Whether to include a "Custom..." entry at the end of the list.</summary>
        public bool HasCustomEntry { get; set; }

        /// <summary>Label for the custom entry (defaults to "Custom...").</summary>
        public string CustomEntryLabel { get; set; } = "Custom...";

        /// <summary>Callback when the custom entry is selected.</summary>
        public Action? OnCustom { get; set; }

        /// <summary>
        /// Opens the dropdown anchored below the trigger at the given position.
        /// </summary>
        public void Open(float x, float y, float width,
                         ImmutableArray<string> items,
                         Action<int, string> onSelect,
                         bool hasCustomEntry = false,
                         Action? onCustom = null,
                         string? customEntryLabel = null)
        {
            IsOpen = true;
            AnchorX = x;
            AnchorY = y;
            AnchorWidth = width;
            Items = items;
            OnSelect = onSelect;
            HasCustomEntry = hasCustomEntry;
            CustomEntryLabel = customEntryLabel ?? "Custom...";
            OnCustom = onCustom;
            HighlightIndex = -1;
            // Start each open at the top; the next render's SetExtent re-clamps to the real geometry.
            Scroll.AtomOffset = 0;
        }

        /// <summary>
        /// Closes the dropdown.
        /// </summary>
        public void Close()
        {
            IsOpen = false;
            HighlightIndex = -1;
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

            var totalItems = Items.Length + (HasCustomEntry ? 1 : 0);

            switch (key)
            {
                case InputKey.Down:
                    HighlightIndex = Math.Min(HighlightIndex + 1, totalItems - 1);
                    Scroll.EnsureVisible(HighlightIndex);
                    return true;

                case InputKey.Up:
                    HighlightIndex = Math.Max(HighlightIndex - 1, 0);
                    Scroll.EnsureVisible(HighlightIndex);
                    return true;

                case InputKey.Enter:
                    if (HighlightIndex >= 0 && HighlightIndex < Items.Length)
                    {
                        OnSelect?.Invoke(HighlightIndex, Items[HighlightIndex]);
                        Close();
                    }
                    else if (HasCustomEntry && HighlightIndex == Items.Length)
                    {
                        OnCustom?.Invoke();
                        Close();
                    }
                    return true;

                case InputKey.Escape:
                    Close();
                    return true;

                default:
                    return false;
            }
        }
    }
}
