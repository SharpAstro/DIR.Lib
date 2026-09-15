namespace DIR.Lib;

/// <summary>
/// Platform-agnostic key codes for keyboard input handling.
/// Mapped from platform-specific types (SDL3 Scancode, ConsoleKey, etc.) by the host.
/// </summary>
public enum InputKey
{
    None,

    // Navigation
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,

    // Actions
    Enter,
    Escape,
    Tab,
    Space,
    Backspace,
    Delete,

    // Letters
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    // Digits
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    // Function keys
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    // Symbols
    Plus,
    Minus,
    Period,
    Comma,
    Slash,
    Backslash,
    Semicolon,
    Quote,
    BracketLeft,
    BracketRight,
    Grave,
}

/// <summary>
/// Modifier key flags, platform-agnostic.
/// </summary>
[System.Flags]
public enum InputModifier
{
    None  = 0,
    Shift = 1,
    Ctrl  = 2,
    Alt   = 4,
}

/// <summary>
/// Extension methods for mapping <see cref="InputKey"/> to <see cref="TextInputKey"/>.
/// </summary>
public static class InputKeyExtensions
{
    extension(InputKey key)
    {
        /// <summary>
        /// Maps an <see cref="InputKey"/> and <see cref="InputModifier"/> to a <see cref="TextInputKey"/>,
        /// or null if not applicable. Handles Ctrl+A → SelectAll.
        /// </summary>
        /// <remarks>
        /// <b>Shift is not part of the mapping, and that is deliberate.</b> Shift+Ctrl+Left is the SAME
        /// meaning as Ctrl+Left -- go a word left -- differing only in whether the selection follows, which
        /// travels beside the key as <c>extend</c> (see <see cref="TextInputState.HandleKey"/>). Folding it
        /// in would double the word half of this enum to say one thing twice.
        /// <para>
        /// The Ctrl'd arrows are matched BEFORE their plain forms, since a <c>switch</c> arm without a
        /// guard would otherwise claim Ctrl+Left as an ordinary Left and step one character.
        /// </para>
        /// </remarks>
        public TextInputKey? ToTextInputKey(InputModifier modifiers = InputModifier.None) => key switch
        {
            InputKey.Backspace when (modifiers & InputModifier.Ctrl) != 0 => TextInputKey.WordBackspace,
            InputKey.Left when (modifiers & InputModifier.Ctrl) != 0 => TextInputKey.WordLeft,
            InputKey.Right when (modifiers & InputModifier.Ctrl) != 0 => TextInputKey.WordRight,
            InputKey.Backspace => TextInputKey.Backspace,
            InputKey.Delete => TextInputKey.Delete,
            InputKey.Left => TextInputKey.Left,
            InputKey.Right => TextInputKey.Right,
            InputKey.Home => TextInputKey.Home,
            InputKey.End => TextInputKey.End,
            InputKey.Enter => TextInputKey.Enter,
            InputKey.Escape => TextInputKey.Escape,
            InputKey.A when (modifiers & InputModifier.Ctrl) != 0 => TextInputKey.SelectAll,
            InputKey.V when (modifiers & InputModifier.Ctrl) != 0 => TextInputKey.Paste,
            InputKey.C when (modifiers & InputModifier.Ctrl) != 0 => TextInputKey.Copy,
            InputKey.X when (modifiers & InputModifier.Ctrl) != 0 => TextInputKey.Cut,
            _ => null
        };
    }
}
