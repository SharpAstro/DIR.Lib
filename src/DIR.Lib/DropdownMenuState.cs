using System;
using System.Collections.Generic;

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
        public IReadOnlyList<string> Items { get; set; } = [];
        public int HighlightIndex { get; set; } = -1;

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
                         IReadOnlyList<string> items,
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
        /// Handles arrow keys, Enter, and Escape. Returns true if consumed.
        /// </summary>
        public bool HandleKeyDown(InputKey key)
        {
            if (!IsOpen)
            {
                return false;
            }

            var totalItems = Items.Count + (HasCustomEntry ? 1 : 0);

            switch (key)
            {
                case InputKey.Down:
                    HighlightIndex = Math.Min(HighlightIndex + 1, totalItems - 1);
                    return true;

                case InputKey.Up:
                    HighlightIndex = Math.Max(HighlightIndex - 1, 0);
                    return true;

                case InputKey.Enter:
                    if (HighlightIndex >= 0 && HighlightIndex < Items.Count)
                    {
                        OnSelect?.Invoke(HighlightIndex, Items[HighlightIndex]);
                        Close();
                    }
                    else if (HasCustomEntry && HighlightIndex == Items.Count)
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
