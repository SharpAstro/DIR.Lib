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
    /// The host's "go back" action. When set, <see cref="MenuLayout.BuildPanel"/> declares a Back control at the
    /// card's top-left that runs it; null (the default) means no control. It does not confirm a row.
    /// </summary>
    public Action? Back { get; set; }

    /// <summary>
    /// The host's "close" action. When set, <see cref="MenuLayout.BuildPanel"/> declares a close control at the
    /// card's top-right that runs it; null (the default) means no control. It does not confirm a row.
    /// </summary>
    public Action? Close { get; set; }

    private MenuFocus _focus;

    /// <summary>
    /// Where the keyboard is: the rows (the default), or the <see cref="Back"/> or <see cref="Close"/> control.
    /// Tab and Shift+Tab move it (see <see cref="HandleKey(InputKey, InputModifier)"/>); arrows act on the rows, as
    /// in dialog/whiptail and the Windows guidelines, so a header control is a Tab stop, never an extra row. A
    /// control the host has since removed reads as <see cref="MenuFocus.Rows"/>.
    /// </summary>
    public MenuFocus Focus => _focus switch
    {
        MenuFocus.Back when Back is null => MenuFocus.Rows,
        MenuFocus.Close when Close is null => MenuFocus.Rows,
        var f => f,
    };

    /// <summary>
    /// Resets the menu with new content and clears the confirmed state.
    /// <paramref name="selected"/> is clamped to the valid item range. Focus returns to the rows.
    /// </summary>
    public void Reset(string title, string prompt, ImmutableArray<string> items, int selected = 0)
    {
        _title = title ?? throw new ArgumentNullException(nameof(title));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _items = items;
        _selectedIndex = items.Length == 0 ? 0 : Math.Clamp(selected, 0, items.Length - 1);
        _focus = MenuFocus.Rows;
        IsConfirmed = false;
    }

    /// <summary>
    /// Moves <see cref="Focus"/> one Tab stop on (<paramref name="backwards"/> false) or back, over the stops present
    /// in reading order: the rows, then Back, then Close, wrapping. Returns false, and changes nothing, when the menu
    /// has no header control, so a host's own Tab handling still gets the key.
    /// </summary>
    public bool MoveFocus(bool backwards)
    {
        if (Back is null && Close is null)
        {
            return false;
        }

        Span<MenuFocus> stops = stackalloc MenuFocus[3];
        var n = 0;
        stops[n++] = MenuFocus.Rows;
        if (Back is not null) stops[n++] = MenuFocus.Back;
        if (Close is not null) stops[n++] = MenuFocus.Close;

        var at = stops[..n].IndexOf(Focus);
        _focus = stops[(at + (backwards ? n - 1 : 1)) % n];
        return true;
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
    public bool HandleKey(InputKey key) => HandleKey(key, InputModifier.None);

    /// <summary>
    /// <see cref="HandleKey(InputKey)"/> with the modifiers, which Tab needs: Tab moves <see cref="Focus"/> on and
    /// Shift+Tab moves it back (both unconsumed when the menu has no Back or Close control). Enter on a focused
    /// control runs it and confirms no row. Up and Down return focus to the rows and move the selection there.
    /// </summary>
    public bool HandleKey(InputKey key, InputModifier modifiers)
    {
        if (IsConfirmed)
        {
            return false;
        }

        if (key == InputKey.Tab)
        {
            return MoveFocus(backwards: (modifiers & InputModifier.Shift) != 0);
        }

        if (key == InputKey.Enter && Focus is MenuFocus.Back or MenuFocus.Close)
        {
            (Focus == MenuFocus.Back ? Back : Close)!.Invoke();
            return true;
        }

        if (_items.Length == 0)
        {
            return false;
        }

        switch (key)
        {
            case InputKey.Up:
                _focus = MenuFocus.Rows;
                MoveUp();
                return true;
            case InputKey.Down:
                _focus = MenuFocus.Rows;
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

/// <summary>Where a <see cref="MenuModel"/>'s keyboard focus is. See <see cref="MenuModel.Focus"/>.</summary>
public enum MenuFocus
{
    /// <summary>The rows: arrows move the selection, Enter confirms it.</summary>
    Rows,
    /// <summary>The Back control: Enter runs <see cref="MenuModel.Back"/>.</summary>
    Back,
    /// <summary>The close control: Enter runs <see cref="MenuModel.Close"/>.</summary>
    Close,
}
