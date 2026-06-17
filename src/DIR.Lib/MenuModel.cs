using System;
using System.Collections.Immutable;

namespace DIR.Lib;

/// <summary>
/// Surface-neutral state model for a vertical "wizard" menu (title + prompt + selectable items).
/// Holds selection state and handles all keyboard navigation logic. Surface-neutral: no renderer
/// dependency. The GPU (<see cref="PixelMenuWidget{TSurface}"/>) and TUI layers consume the same
/// model instance.
/// </summary>
public sealed class MenuModel
{
    private string _title = string.Empty;
    private string _prompt = string.Empty;
    private ImmutableArray<string> _items = [];
    private int _selectedIndex;

    /// <summary>Title displayed at the top of the menu.</summary>
    public string Title => _title;

    /// <summary>Prompt displayed below the title.</summary>
    public string Prompt => _prompt;

    /// <summary>Selectable menu items.</summary>
    public ImmutableArray<string> Items => _items;

    /// <summary>Zero-based index of the currently highlighted item.</summary>
    public int SelectedIndex => _selectedIndex;

    /// <summary>
    /// True after the user has confirmed a selection via Enter, a digit key, or a mouse click.
    /// Reset to false by <see cref="Reset"/>.
    /// </summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>
    /// Resets the menu with new content and clears the confirmed state.
    /// <paramref name="selected"/> is clamped to the valid item range.
    /// </summary>
    public void Reset(string title, string prompt, ImmutableArray<string> items, int selected = 0)
    {
        _title = title ?? throw new ArgumentNullException(nameof(title));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _items = items;
        _selectedIndex = items.Length == 0 ? 0 : Math.Clamp(selected, 0, items.Length - 1);
        IsConfirmed = false;
    }


    /// <summary>
    /// Moves the selection up by one, wrapping from the first item to the last.
    /// No-op when there are no items.
    /// </summary>
    public void MoveUp()
    {
        if (_items.Length == 0)
        {
            return;
        }

        _selectedIndex = (_selectedIndex - 1 + _items.Length) % _items.Length;
    }

    /// <summary>
    /// Moves the selection down by one, wrapping from the last item to the first.
    /// No-op when there are no items.
    /// </summary>
    public void MoveDown()
    {
        if (_items.Length == 0)
        {
            return;
        }

        _selectedIndex = (_selectedIndex + 1) % _items.Length;
    }

    /// <summary>
    /// Sets <see cref="SelectedIndex"/> to <paramref name="index"/> and marks the menu as confirmed.
    /// <paramref name="index"/> must be a valid item index.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is outside [0, Items.Length).</exception>
    public void ConfirmAt(int index)
    {
        if ((uint)index >= (uint)_items.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index,
                $"Index must be in [0, {_items.Length}).");
        }

        _selectedIndex = index;
        IsConfirmed = true;
    }

    /// <summary>
    /// Handles a keyboard key for menu navigation. Encapsulates Up/Down wrap, Enter confirm,
    /// and D1..D9 direct-select-and-confirm. Returns true when the key was consumed.
    /// Returns false immediately when already confirmed or when there are no items.
    /// </summary>
    public bool HandleKey(InputKey key)
    {
        if (IsConfirmed || _items.Length == 0)
        {
            return false;
        }

        switch (key)
        {
            case InputKey.Up:
                MoveUp();
                return true;
            case InputKey.Down:
                MoveDown();
                return true;
            case InputKey.Enter:
                IsConfirmed = true;
                return true;
            default:
                var digit = key switch
                {
                    InputKey.D1 => 0,
                    InputKey.D2 => 1,
                    InputKey.D3 => 2,
                    InputKey.D4 => 3,
                    InputKey.D5 => 4,
                    InputKey.D6 => 5,
                    InputKey.D7 => 6,
                    InputKey.D8 => 7,
                    InputKey.D9 => 8,
                    _ => -1,
                };
                if (digit >= 0 && digit < _items.Length)
                {
                    _selectedIndex = digit;
                    IsConfirmed = true;
                    return true;
                }

                return false;
        }
    }
}
