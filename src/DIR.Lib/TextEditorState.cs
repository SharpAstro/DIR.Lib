using System;
using System.Collections.Generic;
using System.Globalization;

namespace DIR.Lib;

/// <summary>
/// State for a multi-line text editor: <see cref="TextInputState"/> with line breaks, so Enter inserts one
/// and a separate chord commits. Renderer-agnostic, like the field it extends.
/// </summary>
/// <remarks>
/// <para>
/// <b>A subclass, not a sibling.</b> Everything that already handles a <see cref="TextInputState"/> --
/// <see cref="TextInputFocus"/> and its owner rules (<see cref="TextInputState.IsActive"/> is written only
/// there), the router's field list, Tab cycling, <see cref="TextInputState.OnCommit"/> /
/// <see cref="TextInputState.OnCancel"/> / <see cref="TextInputState.OnTextChanged"/>, the clipboard path in
/// <see cref="TextInputInteraction"/> -- takes an editor unchanged, and a consumer that holds a
/// <c>TextInputState</c> need not know which kind it has. A parallel type would have needed all of that
/// repeated, and a second focus owner is exactly the bug <see cref="TextInputFocus"/> exists to prevent.
/// What differs is overridden: <see cref="HandleKey"/>, <see cref="InsertText"/>, <see cref="MoveCaretTo"/>
/// and <see cref="Clear"/>.
/// </para>
/// <para>
/// <b>Indices are UTF-16, steps are graphemes.</b> <see cref="TextInputState.CursorPos"/> and
/// <see cref="TextInputState.SelectionAnchor"/> index <see cref="TextInputState.Text"/> exactly as the
/// single-line field's do, so the renderer and the clipboard need no second coordinate system. Every motion
/// and deletion here steps a whole extended grapheme cluster, so an emoji ZWJ sequence or a letter with a
/// combining mark is one caret step and one Backspace, and the caret is never left inside one. The text
/// holds <c>'\n'</c> only: <see cref="InsertText"/> turns CRLF and a lone CR into it.
/// </para>
/// <para>
/// <b>Lines here are logical lines</b>, the runs between <c>'\n'</c>. Home, End, Up and Down follow them;
/// the layout that wraps a line into visual lines is the renderer's, and moves the caret by those.
/// </para>
/// </remarks>
public class TextEditorState : TextInputState
{
    /// <summary>How many undo steps are kept; the oldest is dropped past it.</summary>
    public const int HistoryLimit = 200;

    private enum EditKind { None, Typing, Backspacing, Deleting, Other }

    private readonly record struct Snapshot(string Text, int Caret, int Anchor);

    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];

    // What the last edit was and the text it left, so a run of the same kind coalesces into one undo step
    // only while nothing else -- a motion, a click, a host assigning Text -- has happened in between.
    private EditKind _lastEdit = EditKind.None;
    private string _textAfterLastEdit = "";

    // The column Up and Down keep while they cross shorter lines; -1 when no vertical run is in progress.
    private int _preferredColumn = -1;

    /// <inheritdoc/>
    public override bool IsMultiLine => true;

    /// <summary>
    /// The chord that commits, since Enter does not. Ctrl+Enter by default. A chat box that sends on Enter
    /// and breaks the line on Shift+Enter sets <c>new KeyChord(InputKey.Enter)</c>. Matched exactly,
    /// modifiers included, by <see cref="TextInputInteraction.HandleKey"/>; any other Enter inserts a line
    /// break, Ctrl+Enter among them when it is not the chord.
    /// </summary>
    public KeyChord CommitChord { get; set; } = new(InputKey.Enter, InputModifier.Ctrl);

    /// <summary>
    /// How many lines PageUp and PageDown move. The editor has no viewport of its own; the layout that does
    /// sets this to the visible line count.
    /// </summary>
    public int PageLines { get; set; } = 10;

    /// <summary>Whether <see cref="Undo"/> has a step to take.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether <see cref="Redo"/> has a step to take.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The number of logical lines; an empty text is one empty line.</summary>
    public int LineCount
    {
        get
        {
            var count = 1;
            foreach (var c in Text)
            {
                if (c == '\n')
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>The caret's logical line, zero-based.</summary>
    public int Line => LineOf(Math.Clamp(CursorPos, 0, Text.Length));

    /// <summary>The caret's column in its logical line, zero-based, counted in grapheme clusters.</summary>
    public int Column => ColumnOf(Math.Clamp(CursorPos, 0, Text.Length));

    /// <summary>
    /// The text index of (<paramref name="line"/>, <paramref name="column"/>), both clamped into the text:
    /// a line past the last is the last, a column past the end of its line is the end of it.
    /// </summary>
    public int IndexOf(int line, int column)
    {
        var start = LineStartIndex(Math.Clamp(line, 0, LineCount - 1));
        var end = LineEndIndex(start);
        var index = start;
        for (var col = 0; col < column && index < end; col++)
        {
            index = NextBoundary(index);
        }

        return Math.Min(index, end);
    }

    /// <summary>
    /// Puts the caret at (<paramref name="line"/>, <paramref name="column"/>), clamped as
    /// <see cref="IndexOf"/> clamps it. Like <see cref="MoveCaretTo"/>, <paramref name="extend"/> grows the
    /// selection from where the caret was.
    /// </summary>
    public void SetCaret(int line, int column, bool extend = false) => MoveCaretTo(IndexOf(line, column), extend);

    /// <summary>The text of the logical line <paramref name="line"/>, without its line break.</summary>
    public string GetLine(int line)
    {
        var start = LineStartIndex(Math.Clamp(line, 0, LineCount - 1));
        return Text[start..LineEndIndex(start)];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The index is moved to a grapheme boundary (down, when it falls inside a cluster), so a click or a
    /// host's own index cannot leave the caret inside one.
    /// </remarks>
    public override void MoveCaretTo(int index, bool extend = false)
    {
        ClampToText();
        base.MoveCaretTo(index, extend);
        CursorPos = SnapToBoundary(CursorPos, -1);
        _preferredColumn = -1;
        EndRun();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Normalises CRLF and a lone CR to <c>'\n'</c> and keeps every line break, which is what a paste needs.
    /// One typed grapheme coalesces with the typing around it into a single undo step; anything longer, a
    /// replaced selection or a line break is a step of its own.
    /// </remarks>
    public override void InsertText(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        input = NormaliseLineBreaks(input);
        ClampToText();

        var typed = !HasSelection && input != "\n" && input.Length <= 2 && IsOneGrapheme(input);
        BeginEdit(typed ? EditKind.Typing : EditKind.Other);
        ReplaceSelection(input);
        EndEdit(typed ? EditKind.Typing : EditKind.Other);
        IsCommitted = false;
        IsCancelled = false;
    }

    /// <summary>
    /// What a chord means to this editor: <see cref="TextInputKey.Commit"/> for <see cref="CommitChord"/>
    /// exactly, otherwise the shared mapping, in which Ctrl+Enter that is not the chord is only an Enter.
    /// </summary>
    internal TextInputKey? KeyFor(InputKey key, InputModifier modifiers)
    {
        if (key == CommitChord.Key && modifiers == CommitChord.Modifiers)
        {
            return TextInputKey.Commit;
        }

        return key.ToTextInputKey(modifiers) is TextInputKey.Commit ? TextInputKey.Enter : key.ToTextInputKey(modifiers);
    }

    /// <inheritdoc/>
    public override bool HandleKey(TextInputKey key, bool extend = false)
    {
        ClampToText();
        var vertical = false;
        var handled = true;

        switch (key)
        {
            case TextInputKey.Backspace:
                Delete(-1, EditKind.Backspacing, wordwise: false);
                break;

            case TextInputKey.Delete:
                Delete(1, EditKind.Deleting, wordwise: false);
                break;

            case TextInputKey.WordBackspace:
                Delete(-1, EditKind.Other, wordwise: true);
                break;

            case TextInputKey.Enter:
                BeginEdit(EditKind.Other);
                ReplaceSelection("\n");
                EndEdit(EditKind.Other);
                break;

            case TextInputKey.Left:
                if (HasSelection && !extend)
                {
                    Move(SelectionStart, extend: false);
                }
                else
                {
                    Move(PreviousBoundary(CursorPos), extend);
                }
                break;

            case TextInputKey.Right:
                if (HasSelection && !extend)
                {
                    Move(SelectionEnd, extend: false);
                }
                else
                {
                    Move(NextBoundary(CursorPos), extend);
                }
                break;

            case TextInputKey.WordLeft:
                Move(WordBoundary(CursorPos, -1), extend);
                break;

            case TextInputKey.WordRight:
                Move(WordBoundary(CursorPos, 1), extend);
                break;

            case TextInputKey.Home:
                Move(LineStartIndex(Line), extend);
                break;

            case TextInputKey.End:
                Move(LineEndIndex(LineStartIndex(Line)), extend);
                break;

            case TextInputKey.DocumentStart:
                Move(0, extend);
                break;

            case TextInputKey.DocumentEnd:
                Move(Text.Length, extend);
                break;

            case TextInputKey.Up:
                vertical = true;
                MoveLines(-1, extend);
                break;

            case TextInputKey.Down:
                vertical = true;
                MoveLines(1, extend);
                break;

            case TextInputKey.PageUp:
                vertical = true;
                MoveLines(-Math.Max(1, PageLines), extend);
                break;

            case TextInputKey.PageDown:
                vertical = true;
                MoveLines(Math.Max(1, PageLines), extend);
                break;

            case TextInputKey.Undo:
                Undo();
                break;

            case TextInputKey.Redo:
                Redo();
                break;

            case TextInputKey.Commit:
                SelectionAnchor = -1;
                IsCommitted = true;
                break;

            case TextInputKey.Escape:
                SelectionAnchor = -1;
                IsCancelled = true;
                break;

            case TextInputKey.SelectAll:
                SelectAll();
                EndRun();
                break;

            case TextInputKey.Paste:
            case TextInputKey.Copy:
            case TextInputKey.Cut:
                // The clipboard is the host's; consumed here, as the single-line field does.
                break;

            default:
                handled = false;
                break;
        }

        if (!vertical)
        {
            _preferredColumn = -1;
        }

        return handled;
    }

    /// <summary>
    /// Undoes the last edit step, restoring its text, caret and selection. Returns false when there is none.
    /// </summary>
    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Add(Capture());
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        return true;
    }

    /// <summary>Redoes the step <see cref="Undo"/> took back. Returns false when there is none.</summary>
    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Add(Capture());
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>Also forgets the undo history: a cleared editor is a new document.</remarks>
    public override void Clear()
    {
        base.Clear();
        _undo.Clear();
        _redo.Clear();
        EndRun();
    }

    /// <summary>
    /// <see cref="TextInputState.Text"/> is a plain settable property, so a host can shorten it under a
    /// caret; every operation starts by pulling caret and anchor back inside what is there.
    /// </summary>
    private void ClampToText()
    {
        CursorPos = Math.Clamp(CursorPos, 0, Text.Length);
        if (SelectionAnchor > Text.Length)
        {
            SelectionAnchor = Text.Length;
        }
    }

    // ---- Lines and graphemes ---------------------------------------------------------------------

    private int LineOf(int index)
    {
        var line = 0;
        for (var i = 0; i < index; i++)
        {
            if (Text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private int ColumnOf(int index)
    {
        var start = StartOfLineContaining(index);
        var column = 0;
        for (var i = start; i < index; i = NextBoundary(i))
        {
            column++;
        }

        return column;
    }

    private int StartOfLineContaining(int index) => index <= 0 ? 0 : Text.LastIndexOf('\n', index - 1) + 1;

    private int LineStartIndex(int line)
    {
        var index = 0;
        for (var l = 0; l < line; l++)
        {
            var nl = Text.IndexOf('\n', index);
            if (nl < 0)
            {
                return Text.Length;
            }

            index = nl + 1;
        }

        return index;
    }

    /// <summary>The index of the line break ending the line that starts at <paramref name="lineStart"/>, or the text's end.</summary>
    private int LineEndIndex(int lineStart)
    {
        var nl = Text.IndexOf('\n', lineStart);
        return nl < 0 ? Text.Length : nl;
    }

    /// <summary>The grapheme boundary after <paramref name="index"/>; a CRLF in an externally set text is one step.</summary>
    private int NextBoundary(int index)
    {
        if (index >= Text.Length)
        {
            return Text.Length;
        }

        return index + Math.Max(1, StringInfo.GetNextTextElementLength(Text.AsSpan(index)));
    }

    /// <summary>The grapheme boundary before <paramref name="index"/>.</summary>
    private int PreviousBoundary(int index)
    {
        if (index <= 0)
        {
            return 0;
        }

        var lineStart = StartOfLineContaining(index);
        if (lineStart == index)
        {
            // Just after a line break: the break is the one cluster before, with its CR if the text has one.
            return index >= 2 && Text[index - 2] == '\r' ? index - 2 : index - 1;
        }

        return SnapToBoundary(index - 1, -1, lineStart);
    }

    /// <summary>
    /// <paramref name="index"/> itself when it is a grapheme boundary, otherwise the boundary of its cluster
    /// in <paramref name="direction"/> (negative: its start, else its end). Scans from the line start, since
    /// a boundary cannot depend on what lies before a line break.
    /// </summary>
    private int SnapToBoundary(int index, int direction, int lineStart = -1)
    {
        index = Math.Clamp(index, 0, Text.Length);
        if (index == 0 || index == Text.Length)
        {
            return index;
        }

        var pos = lineStart >= 0 ? lineStart : StartOfLineContaining(index);
        while (pos < index)
        {
            var next = NextBoundary(pos);
            if (next > index)
            {
                return direction < 0 ? pos : next;
            }

            pos = next;
        }

        return index;
    }

    private static bool IsOneGrapheme(string s) => StringInfo.GetNextTextElementLength(s) == s.Length;

    private static string NormaliseLineBreaks(string s)
        => s.IndexOf('\r') < 0 ? s : s.Replace("\r\n", "\n").Replace('\r', '\n');

    // ---- Words (the single-line field's, over lines) ---------------------------------------------

    private int WordBoundary(int from, int direction)
    {
        var index = Math.Clamp(from, 0, Text.Length);

        if (direction < 0)
        {
            while (index > 0 && !IsWordChar(Text[index - 1]))
            {
                index--;
            }

            while (index > 0 && IsWordChar(Text[index - 1]))
            {
                index--;
            }
        }
        else
        {
            while (index < Text.Length && IsWordChar(Text[index]))
            {
                index++;
            }

            while (index < Text.Length && !IsWordChar(Text[index]))
            {
                index++;
            }
        }

        return SnapToBoundary(index, direction);
    }

    // The single-line field's word, plus the marks that belong to a letter so a combining accent is not a gap.
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-'
        || char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    // ---- Motion and edits ------------------------------------------------------------------------

    /// <summary>Moves the caret to <paramref name="index"/>, growing the selection from where it was when extending.</summary>
    private void Move(int index, bool extend)
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
            SelectionAnchor = -1;
        }

        CursorPos = Math.Clamp(index, 0, Text.Length);
        EndRun();
    }

    private void MoveLines(int delta, bool extend)
    {
        var line = Line;
        if (_preferredColumn < 0)
        {
            _preferredColumn = Column;
        }

        var target = line + delta;
        if (target < 0)
        {
            Move(0, extend);
        }
        else if (target >= LineCount)
        {
            Move(Text.Length, extend);
        }
        else
        {
            Move(IndexOf(target, _preferredColumn), extend);
        }
    }

    /// <summary>
    /// Backspace, Delete and the word backspace: the selection if there is one, otherwise a cluster (or a
    /// word) on <paramref name="direction"/>'s side of the caret.
    /// </summary>
    private void Delete(int direction, EditKind kind, bool wordwise)
    {
        if (!HasSelection)
        {
            SelectionAnchor = -1;               // an anchor collapsed onto the caret
            var cursor = Math.Clamp(CursorPos, 0, Text.Length);
            var edge = wordwise
                ? WordBoundary(cursor, direction)
                : direction < 0 ? PreviousBoundary(cursor) : NextBoundary(cursor);
            if (edge == cursor)
            {
                return;
            }

            BeginEdit(kind);
            Text = Text.Remove(Math.Min(edge, cursor), Math.Abs(edge - cursor));
            CursorPos = Math.Min(edge, cursor);
            EndEdit(kind);
            return;
        }

        BeginEdit(EditKind.Other);
        RemoveSelection();
        EndEdit(EditKind.Other);
    }

    private void RemoveSelection()
    {
        if (!HasSelection)
        {
            SelectionAnchor = -1;
            return;
        }

        var start = SelectionStart;
        var end = SelectionEnd;
        Text = Text.Remove(start, end - start);
        CursorPos = start;
        SelectionAnchor = -1;
    }

    private void ReplaceSelection(string input)
    {
        RemoveSelection();
        CursorPos = Math.Clamp(CursorPos, 0, Text.Length);
        Text = Text.Insert(CursorPos, input);
        CursorPos += input.Length;
    }

    // ---- Undo history ----------------------------------------------------------------------------

    private Snapshot Capture() => new(Text, CursorPos, SelectionAnchor);

    private void Restore(Snapshot snapshot)
    {
        Text = snapshot.Text;
        CursorPos = Math.Clamp(snapshot.Caret, 0, Text.Length);
        SelectionAnchor = snapshot.Anchor > Text.Length ? -1 : snapshot.Anchor;
        EndRun();
    }

    /// <summary>Records the state before an edit as an undo step, unless it continues the run before it.</summary>
    private void BeginEdit(EditKind kind)
    {
        var continues = kind != EditKind.Other && kind == _lastEdit
            && ReferenceEquals(Text, _textAfterLastEdit) && !HasSelection;
        if (continues)
        {
            return;
        }

        _undo.Add(Capture());
        if (_undo.Count > HistoryLimit)
        {
            _undo.RemoveAt(0);
        }
    }

    /// <summary>Any edit makes the redo history stale.</summary>
    private void EndEdit(EditKind kind)
    {
        _redo.Clear();
        _lastEdit = kind;
        _textAfterLastEdit = Text;
    }

    /// <summary>A motion, a click or an undo ends the run of typing, so the next edit is a step of its own.</summary>
    private void EndRun() => _lastEdit = EditKind.None;
}
