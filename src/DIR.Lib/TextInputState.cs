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

    /// <summary>
    /// How far this field's text is scrolled to the LEFT, in surface pixels, so that the caret stays
    /// inside a box too narrow to show the whole value. Zero means the value is drawn from its own first
    /// character, which is every field that fits.
    /// </summary>
    /// <remarks>
    /// Maintained by <see cref="TextInputRenderer.Render"/> -- hence the internal setter -- and read back
    /// by <see cref="TextInputRenderer.CaretIndexAt"/>, which has to subtract exactly what the paint
    /// shifted by or a click on a scrolled field resolves to a character the pointer is nowhere near.
    /// <para>
    /// It lives on the STATE rather than in the renderer because a renderer is static and a field is not:
    /// several fields are on screen at once, each scrolled to its own caret, and the offset has to survive
    /// between frames or the value would snap back to its first character every time it was drawn. It is
    /// public to READ because a consumer measuring its own overlay over a field (a completion popup under
    /// the caret) needs the same number.
    /// </para>
    /// <para>
    /// Reset to zero whenever the field is not focused: an unfocused field has no caret to keep in view,
    /// and showing the START of a value is what a reader wants from a box they are not editing.
    /// </para>
    /// </remarks>
    public float ScrollOffsetPx { get; internal set; }

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
    /// <param name="extend">
    /// Grow the selection to wherever the caret lands instead of dropping it -- Shift held. Honoured by
    /// the word motions (<see cref="TextInputKey.WordLeft"/> / <see cref="TextInputKey.WordRight"/>) and
    /// ignored by everything else, deliberately: the plain arrows have always collapsed a selection, and
    /// teaching them to extend would change what Shift+Left does for every existing consumer at once.
    /// That is a behaviour change and belongs in its own wave, not smuggled in on a new parameter.
    /// </param>
    public bool HandleKey(TextInputKey key, bool extend = false)
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

            case TextInputKey.WordLeft:
                MoveCaretToWordBoundary(-1, extend);
                return true;

            case TextInputKey.WordRight:
                MoveCaretToWordBoundary(1, extend);
                return true;

            case TextInputKey.WordBackspace:
                if (HasSelection)
                {
                    DeleteSelection();
                }
                else if (CursorPos > 0)
                {
                    // The same boundary Ctrl+Left would have moved to, so "delete the word" and "step over
                    // the word" can never disagree about where the word began.
                    var wordStart = WordBoundary(CursorPos, -1);
                    Text = Text.Remove(wordStart, CursorPos - wordStart);
                    CursorPos = wordStart;
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
            case TextInputKey.Cut:
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
    /// Puts the caret at <paramref name="index"/>, the mouse's counterpart to the arrow keys.
    /// </summary>
    /// <param name="extend">
    /// Grow the selection to here instead of dropping it -- a shift-click, or every move of a drag after
    /// the first. The anchor is taken from where the caret already was when there is no selection yet,
    /// which is what makes shift-click select from the old caret rather than from nothing.
    /// </param>
    public void MoveCaretTo(int index, bool extend = false)
    {
        if (extend)
        {
            if (SelectionAnchor < 0)
            {
                SelectionAnchor = CursorPos;
            }
        }
        else
        {
            ClearSelection();
        }

        CursorPos = Math.Clamp(index, 0, Text.Length);
    }

    /// <summary>
    /// Steps the caret to the next word boundary in <paramref name="direction"/> -- Ctrl+Left and
    /// Ctrl+Right. Negative goes left, positive right; zero does nothing.
    /// </summary>
    /// <remarks>
    /// The boundary is the START of a word in both directions, which is what every desktop text box does
    /// and what makes the two directions inverses of each other over the same text: going right steps off
    /// the current word and over the gap after it, going left steps back over the gap and then off the
    /// word before it. Both use the same <c>IsWordChar</c> the double-click selection does, so "the word"
    /// means one thing in a field however you reach it.
    /// </remarks>
    /// <param name="extend">
    /// Grow the selection to the new position instead of dropping it -- Shift+Ctrl+Left / Right. Routed
    /// through <see cref="MoveCaretTo"/>, so the anchor rule is stated once: it is taken from where the
    /// caret already was when there is no selection yet.
    /// </param>
    public void MoveCaretToWordBoundary(int direction, bool extend = false)
    {
        if (direction == 0)
        {
            return;
        }

        MoveCaretTo(WordBoundary(CursorPos, direction), extend);
    }

    /// <summary>
    /// The word boundary <paramref name="direction"/> of <paramref name="from"/>, clamped to the text.
    /// Shared by the word motions and by <see cref="TextInputKey.WordBackspace"/>, which must delete
    /// exactly the span Ctrl+Left would have stepped over.
    /// </summary>
    private int WordBoundary(int from, int direction)
    {
        var index = Math.Clamp(from, 0, Text.Length);

        if (direction < 0)
        {
            // Over the gap first, then off the word: a caret sitting just after "hello " lands on the "h".
            while (index > 0 && !IsWordChar(Text[index - 1]))
            {
                index--;
            }

            while (index > 0 && IsWordChar(Text[index - 1]))
            {
                index--;
            }

            return index;
        }

        while (index < Text.Length && IsWordChar(Text[index]))
        {
            index++;
        }

        while (index < Text.Length && !IsWordChar(Text[index]))
        {
            index++;
        }

        return index;
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
        ScrollOffsetPx = 0f;
        ClearComposition();
    }

    /// <summary>
    /// Activates the field with optional initial text. <b>Internal since 10.0</b>: only
    /// <see cref="TextInputFocus"/> may move focus.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The owner existed from 8.x and was a CONVENTION, because this was public -- so tianwen had three
    /// spellings of "give this field the keyboard" (<c>input.Activate()</c>, a posted signal, and
    /// <c>TextInputFocus.Focus</c>) and nine call sites reaching around the owner. Every one of them was a
    /// second writer of the same fact: this flag says the field is active while <see cref="TextInputFocus"/>
    /// says which field is, and when the two disagree the blur has nothing to blur and the caret blinks in
    /// a field the keyboard does not reach.
    /// </para>
    /// <para>
    /// The two acts it conflates are the other half. Seeding a field's text and claiming the keyboard for it
    /// are separate things -- six of those nine sites wanted the first alone, and got the second silently,
    /// so three fields painted as focused at once and the row said nothing about where typing would go.
    /// Set <see cref="Text"/> and <see cref="CursorPos"/> to seed; call
    /// <see cref="TextInputFocus.Focus(TextInputState, string?)"/> where they are genuinely one act.
    /// </para>
    /// </remarks>
    internal void Activate(string? initialText = null)
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
    /// Deactivates the field. <b>Internal since 10.0</b>, for the reason
    /// <see cref="Activate"/> is: use <see cref="TextInputFocus.Blur"/> or
    /// <see cref="TextInputFocus.BlurIfFocused"/>, which move the owner's own record of focus with it.
    /// </summary>
    internal void Deactivate()
    {
        IsActive = false;
        ClearSelection();
        // A blurred field has no caret to keep in view, so it goes back to showing the start of its value.
        // Cleared HERE as well as in the paint because CaretIndexAt may be asked before the next frame --
        // a click that focuses a field arrives before anything has been drawn with the new offset.
        ScrollOffsetPx = 0f;
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
    Copy,

    // Appended rather than slotted in beside their plain counterparts: these are wire values for anything
    // that has already compiled against the enum, and renumbering Paste or Copy to keep the list tidy would
    // change what a published consumer's constant means.

    /// <summary>Ctrl+Left -- the caret to the start of the word before it.</summary>
    WordLeft,

    /// <summary>Ctrl+Right -- the caret to the start of the word after it.</summary>
    WordRight,

    /// <summary>
    /// Ctrl+Backspace -- delete back to the boundary <see cref="WordLeft"/> would have moved to. Its own
    /// member rather than "Backspace with Ctrl" because the state's key handling takes no modifiers: the
    /// mapping from a chord to a meaning happens once, in <see cref="InputKeyExtensions"/>, so a cell host
    /// and a pixel host cannot disagree about it.
    /// </summary>
    WordBackspace,

    /// <summary>
    /// Ctrl+X -- copy the selection and delete it. Like <see cref="Paste"/> and <see cref="Copy"/> the
    /// clipboard half is the host's, so <see cref="TextInputState.HandleKey"/> only swallows it and
    /// <see cref="TextInputInteraction.HandleKey"/> performs it.
    /// </summary>
    Cut
}
