using System;
using System.Threading.Tasks;

namespace DIR.Lib;

/// <summary>
/// State for a single-line text input field. Renderer-agnostic — works with both
/// VkRenderer (GPU) and RgbaImageRenderer (TUI). The SDL3 StartTextInput/StopTextInput
/// lifecycle is managed by the host application's event loop.
/// </summary>
public class TextInputState
{
    /// <summary>Whether this field is currently focused and accepting text input.</summary>
    public bool IsActive { get; set; }

    /// <summary>The current text content.</summary>
    public string Text { get; set; } = "";

    /// <summary>Cursor position (character index, 0 = before first char).</summary>
    public int CursorPos { get; set; }

    /// <summary>
    /// Selection anchor position, or -1 if no selection.
    /// Selection range is between <see cref="SelectionStart"/> and <see cref="CursorPos"/>.
    /// </summary>
    public int SelectionAnchor { get; set; } = -1;

    /// <summary>Start of the selection range (min of anchor and cursor).</summary>
    public int SelectionStart => HasSelection ? Math.Min(SelectionAnchor, CursorPos) : CursorPos;

    /// <summary>End of the selection range (max of anchor and cursor).</summary>
    public int SelectionEnd => HasSelection ? Math.Max(SelectionAnchor, CursorPos) : CursorPos;

    /// <summary>Whether there is an active text selection.</summary>
    public bool HasSelection => SelectionAnchor >= 0 && SelectionAnchor != CursorPos;

    /// <summary>Optional placeholder text shown when empty and not active.</summary>
    public string Placeholder { get; set; } = "";

    /// <summary>Set to true when the user pressed Enter to commit the value.</summary>
    public bool IsCommitted { get; set; }

    /// <summary>Set to true when the user pressed Escape to cancel.</summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Called when Enter is pressed to commit the value. Set by the owning tab
    /// so the central event handler doesn't need tab-specific commit logic.
    /// Async — the returned Task is tracked by <see cref="BackgroundTaskTracker"/>.
    /// </summary>
    public Func<string, Task>? OnCommit { get; set; }

    /// <summary>
    /// Called when Escape is pressed to cancel editing. Set by the owning tab.
    /// </summary>
    public Action? OnCancel { get; set; }

    /// <summary>
    /// Called on every text change (insert, backspace, delete). Set by the owning tab
    /// for live-search / autocomplete scenarios.
    /// </summary>
    public Action<string>? OnTextChanged { get; set; }

    /// <summary>
    /// Optional key override handler. Gets first crack at keys when this input is active.
    /// Return true to consume the key (e.g. for autocomplete navigation).
    /// </summary>
    public Func<TextInputKey, bool>? OnKeyOverride { get; set; }

    /// <summary>
    /// Handles a text input event (from SDL3 TextInput or Console.Lib TryReadInput).
    /// Replaces selection (if any) with the input, then inserts at cursor.
    /// </summary>
    public void InsertText(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        DeleteSelection();
        Text = Text.Insert(CursorPos, input);
        CursorPos += input.Length;
        IsCommitted = false;
        IsCancelled = false;
    }

    /// <summary>
    /// Handles a key press. Returns true if the key was consumed.
    /// </summary>
    public bool HandleKey(TextInputKey key)
    {
        switch (key)
        {
            case TextInputKey.Backspace:
                if (HasSelection)
                {
                    DeleteSelection();
                }
                else if (CursorPos > 0)
                {
                    Text = Text.Remove(CursorPos - 1, 1);
                    CursorPos--;
                }
                return true;

            case TextInputKey.Delete:
                if (HasSelection)
                {
                    DeleteSelection();
                }
                else if (CursorPos < Text.Length)
                {
                    Text = Text.Remove(CursorPos, 1);
                }
                return true;

            case TextInputKey.Left:
                if (HasSelection)
                {
                    CursorPos = SelectionStart;
                    ClearSelection();
                }
                else if (CursorPos > 0)
                {
                    CursorPos--;
                }
                return true;

            case TextInputKey.Right:
                if (HasSelection)
                {
                    CursorPos = SelectionEnd;
                    ClearSelection();
                }
                else if (CursorPos < Text.Length)
                {
                    CursorPos++;
                }
                return true;

            case TextInputKey.Home:
                ClearSelection();
                CursorPos = 0;
                return true;

            case TextInputKey.End:
                ClearSelection();
                CursorPos = Text.Length;
                return true;

            case TextInputKey.Enter:
                ClearSelection();
                IsCommitted = true;
                return true;

            case TextInputKey.Escape:
                ClearSelection();
                IsCancelled = true;
                return true;

            case TextInputKey.SelectAll:
                SelectAll();
                return true;

            case TextInputKey.Paste:
            case TextInputKey.Copy:
                // Handled by the host (clipboard is platform-specific).
                // Returning true signals the key was consumed.
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Selects all text.
    /// </summary>
    public void SelectAll()
    {
        if (Text.Length > 0)
        {
            SelectionAnchor = 0;
            CursorPos = Text.Length;
        }
    }

    /// <summary>
    /// Selects the word at the given character position.
    /// </summary>
    public void SelectWordAt(int position)
    {
        if (Text.Length == 0)
        {
            return;
        }

        position = Math.Clamp(position, 0, Text.Length - 1);

        // Find word boundaries (alphanumeric + underscore)
        var start = position;
        while (start > 0 && IsWordChar(Text[start - 1]))
        {
            start--;
        }

        var end = position;
        while (end < Text.Length && IsWordChar(Text[end]))
        {
            end++;
        }

        // If we clicked on a non-word char, select just that char
        if (start == end && position < Text.Length)
        {
            end = position + 1;
        }

        SelectionAnchor = start;
        CursorPos = end;
    }

    /// <summary>
    /// The IME's in-progress composition ("preedit"), or empty when not composing. This is the pinyin
    /// or kana the user has typed but not yet turned into a character, and it is NOT part of
    /// <see cref="Text"/>: it belongs to the input method until the IME commits it, at which point the
    /// platform delivers it as ordinary text input.
    /// </summary>
    /// <remarks>
    /// A field that ignores this can only ever accept Latin-style input, because with a CJK IME every
    /// keystroke before the commit is composition and nothing else arrives. That is exactly how this
    /// was missed: injecting text straight at the committed-text path exercises none of it, so the
    /// field looked finished while Chinese input produced nothing at all on screen.
    /// </remarks>
    public string Composition { get; private set; } = "";

    /// <summary>
    /// Caret position WITHIN <see cref="Composition"/>, in characters, where further typing lands.
    /// Meaningless while <see cref="Composition"/> is empty.
    /// </summary>
    public int CompositionCursor { get; private set; }

    /// <summary>
    /// How many characters of <see cref="Composition"/> the next keystroke replaces (the IME's own
    /// selection inside the preedit). Zero for a plain insertion point.
    /// </summary>
    public int CompositionLength { get; private set; }

    /// <summary>True while an input method is composing, so the caller should draw the preedit.</summary>
    public bool IsComposing => Composition.Length > 0;

    /// <summary>
    /// Replaces the in-progress composition. Called from the platform's composition event; a
    /// <paramref name="text"/> of empty ends composition (which is how every IME signals both a commit
    /// and a cancel -- the committed characters arrive separately as ordinary text input).
    /// </summary>
    public void SetComposition(string? text, int cursor = 0, int length = 0)
    {
        Composition = text ?? "";
        // Clamp rather than trust: the values cross a P/Invoke boundary as raw ints, and an out-of-range
        // cursor would otherwise index past the string when the renderer measures the preedit caret.
        CompositionCursor = Math.Clamp(cursor, 0, Composition.Length);
        CompositionLength = Math.Clamp(length, 0, Composition.Length - CompositionCursor);
    }

    /// <summary>Drops any in-progress composition without touching <see cref="Text"/>.</summary>
    public void ClearComposition() => SetComposition("");

    /// <summary>
    /// Resets the field to empty, uncommitted state.
    /// </summary>
    public void Clear()
    {
        Text = "";
        CursorPos = 0;
        SelectionAnchor = -1;
        IsCommitted = false;
        IsCancelled = false;
        ClearComposition();
    }

    /// <summary>
    /// Activates the field with optional initial text.
    /// </summary>
    public void Activate(string? initialText = null)
    {
        IsActive = true;
        IsCommitted = false;
        IsCancelled = false;
        if (initialText is not null)
        {
            Text = initialText;
            CursorPos = initialText.Length;
        }
    }

    /// <summary>
    /// Deactivates the field.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        ClearSelection();
        // A preedit belongs to the input method, and blurring the field abandons it. Leaving it behind
        // would paint composition text in a field nobody is typing into, and it would still be there
        // the next time the field is focused.
        ClearComposition();
    }

    private void DeleteSelection()
    {
        if (!HasSelection)
        {
            return;
        }

        var start = SelectionStart;
        var end = SelectionEnd;
        Text = Text.Remove(start, end - start);
        CursorPos = start;
        ClearSelection();
    }

    private void ClearSelection()
    {
        SelectionAnchor = -1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
}

/// <summary>
/// Abstract key actions for text input, mapped from platform-specific scancodes.
/// </summary>
public enum TextInputKey
{
    Backspace,
    Delete,
    Left,
    Right,
    Home,
    End,
    Enter,
    Escape,
    SelectAll,
    Paste,
    Copy
}
