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
/// <b>Lines are logical until a layout is set.</b> <see cref="LineCount"/>, <see cref="Line"/> and
/// <see cref="Column"/> always count the runs between <c>'\n'</c>. Home, End, Up, Down, PageUp and PageDown
/// follow them too, until the paint hands over a <see cref="TextEditorLayout"/> (<see cref="SetLayout"/>);
/// from then on they follow its visual lines, with an x that Up and Down keep, and <see cref="ScrollOffsetY"/>
/// is kept so the caret's line is in view.
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
    private int _caretAfterLastEdit = -1;

    // The column Up and Down keep while they cross shorter lines; -1 when no vertical run is in progress.
    private int _preferredColumn = -1;

    // The same, as an x in the visual layout, which is what wrapped lines keep.
    private float _preferredX = -1f;

    private TextEditorLayout? _layout;

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
    /// How many lines PageUp and PageDown move while the box is not bounded (<see cref="MaxLines"/>) or has
    /// no layout yet; once it has both, they move the box's <see cref="VisibleLines"/>.
    /// </summary>
    public int PageLines { get; set; } = 10;

    /// <summary>The fewest lines the box shows, however little text there is. At least 1.</summary>
    public int MinLines { get; set; } = 1;

    /// <summary>
    /// The most lines the box shows. The box grows with its content up to this and then scrolls
    /// (<see cref="ScrollOffsetY"/>); unbounded by default, so the box never scrolls.
    /// </summary>
    public int MaxLines { get; set; } = int.MaxValue;

    /// <summary>
    /// How far the content is slid up in the box, in layout units, so the caret's line stays in view. Kept by
    /// the state after every edit and motion; zero until a layout is set and while the text fits.
    /// </summary>
    public float ScrollOffsetY { get; internal set; }

    /// <summary>
    /// The wrapped layout the paint last built, or null before there is one; every vertical motion and the
    /// scroll follow it, and fall back to logical lines without it. Always of the current text: one built for
    /// older text is re-wrapped with the same width and measure on the way out.
    /// </summary>
    public TextEditorLayout? VisualLayout
    {
        get
        {
            if (_layout is not null && !string.Equals(_layout.Text, Text, StringComparison.Ordinal))
            {
                _layout = _layout.WithText(Text);
            }

            return _layout;
        }
    }

    /// <summary>
    /// The height of the text area the paint last drew into, or zero before one has. The paint is the authority
    /// on how tall the box is -- a parent may arrange it shorter than its content asked for -- and the scroll that
    /// keeps the caret in view has to be of the box that is on screen, not of the one the content would like.
    /// </summary>
    internal float PaintedViewportHeight { get; set; }

    /// <summary>What the paint last laid the text out with (face, size, fallback chain), so a frame that changed none of it reuses the wrap.</summary>
    internal (string Font, float Size, object? Fallback)? PaintKey { get; set; }

    /// <summary>
    /// The lines the box shows now: the painted box's once there is one, otherwise its content's, within
    /// <see cref="MinLines"/> and <see cref="MaxLines"/>.
    /// </summary>
    public int VisibleLines => PaintedViewportHeight > 0f && VisualLayout is { } painted
        ? Math.Max(1, (int)Math.Floor(PaintedViewportHeight / painted.LineHeight + 1e-3f))
        : VisualLayout?.VisibleLines(MinLines, MaxLines) ?? Math.Max(1, Math.Min(LineCount, MaxLines));

    /// <summary>The box's height in layout units, or zero before a layout is set.</summary>
    public float ViewportHeight => PaintedViewportHeight > 0f
        ? PaintedViewportHeight
        : VisualLayout?.ViewportHeight(MinLines, MaxLines) ?? 0f;

    private ListScrollController? _wheel;
    private bool _syncingWheel;

    /// <summary>
    /// The controller the wheel is delivered to while the text overflows its box, bound by the paint and null
    /// before one. Counts in pixels, so <see cref="ScrollOffsetY"/> and its offset are the same number; a wheel
    /// notch moves three lines.
    /// </summary>
    internal ListScrollController? WheelScroll => _wheel;

    /// <summary>States where the box is this frame and how tall its text is, and keeps the controller's offset and <see cref="ScrollOffsetY"/> one number.</summary>
    internal ListScrollController BindWheel(RectF32 viewport, float contentHeight, float lineHeight, DesignScale scale)
    {
        if (_wheel is null)
        {
            _wheel = new ListScrollController { Mode = ScrollBarMode.None, WheelStepAtoms = 3f };
            _wheel.Changed += () =>
            {
                // A wheel notch moved the controller; the state follows. The paint's own write to it is not a notch.
                if (!_syncingWheel)
                {
                    ScrollOffsetY = _wheel.Offset;
                }
            };
        }

        _wheel.LineExtentPx = lineHeight;
        _wheel.SetExtent(viewport, 1f, (int)Math.Ceiling(contentHeight), scale);
        var want = (int)MathF.Round(ScrollOffsetY);
        if (_wheel.AtomOffset != want)
        {
            _syncingWheel = true;
            try
            {
                _wheel.AtomOffset = want;
            }
            finally
            {
                _syncingWheel = false;
            }
        }

        return _wheel;
    }

    /// <summary>
    /// Hands the state the layout the paint just built. A new width or text brings the caret back into view;
    /// the same layout again only holds the scroll inside its range, so a scroll the host moved is not undone
    /// by the next frame.
    /// </summary>
    public void SetLayout(TextEditorLayout? layout)
    {
        var changed = layout is null || _layout is null || layout.Width != _layout.Width
            || layout.LineHeight != _layout.LineHeight
            || !string.Equals(layout.Text, _layout.Text, StringComparison.Ordinal);
        _layout = layout;
        if (layout is null)
        {
            ScrollOffsetY = 0f;
        }
        else if (changed)
        {
            KeepCaretInView();
        }
        else
        {
            ScrollOffsetY = Math.Clamp(ScrollOffsetY, 0f, layout.MaxScroll(ViewportHeight));
        }
    }

    /// <summary>
    /// The caret index under a point in the box (its top-left is the origin), the scroll added. Null before a
    /// layout is set, when the host has only the single-line hit test to ask.
    /// </summary>
    public int? CaretIndexAt(float x, float y) => VisualLayout?.CaretIndexAt(x, y + ScrollOffsetY);

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

    /// <summary>
    /// Selects the whole logical line <paramref name="index"/> is on, without its line break: what a triple
    /// click means in a box of lines, where selecting everything would be a Ctrl+A with extra steps.
    /// </summary>
    public void SelectLineAt(int index)
    {
        ClampToText();
        var start = LineStartIndex(LineOf(Math.Clamp(index, 0, Text.Length)));
        SelectionAnchor = start;
        CursorPos = LineEndIndex(start);
        _preferredColumn = -1;
        _preferredX = -1f;
        EndRun();
        KeepCaretInView();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// What a double click means in a box of lines. The word is found in whole grapheme clusters, so an emoji
    /// is selected whole and a letter keeps its combining mark, and a line break is never part of it: a click
    /// past the end of a line selects the last cluster on it, and a click on an empty line selects nothing and
    /// only places the caret, so typing over the selection cannot join two lines.
    /// </remarks>
    public override void SelectWordAt(int position)
    {
        ClampToText();
        if (Text.Length == 0)
        {
            return;
        }

        if (IsMasked)
        {
            SelectAll();
            return;
        }

        position = Math.Clamp(position, 0, Text.Length);
        var atLineEnd = position == Text.Length || IsBreak(Text[position]);
        if (atLineEnd && (position == 0 || IsBreak(Text[position - 1])))
        {
            // An empty line: there is no word to take, and the break must not be selected.
            MoveCaretTo(position);
            return;
        }

        var start = atLineEnd ? PreviousBoundary(position) : SnapToBoundary(position, -1);
        var end = NextBoundary(start);
        if (IsWordAt(start))
        {
            while (start > 0 && !IsBreak(Text[start - 1]))
            {
                var before = PreviousBoundary(start);
                if (!IsWordAt(before))
                {
                    break;
                }

                start = before;
            }

            while (end < Text.Length && IsWordAt(end))
            {
                end = NextBoundary(end);
            }
        }

        SelectionAnchor = start;
        CursorPos = end;
        _preferredColumn = -1;
        _preferredX = -1f;
        EndRun();
        KeepCaretInView();
    }

    private static bool IsBreak(char c) => c is '\n' or '\r';

    /// <summary>Whether the cluster starting at <paramref name="index"/> is a word's: its first code point is a letter, digit, <c>_</c>, <c>-</c> or a mark.</summary>
    private bool IsWordAt(int index)
        => char.IsLetterOrDigit(Text, index) || Text[index] is '_' or '-'
            || CharUnicodeInfo.GetUnicodeCategory(Text, index)
                is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

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
        _preferredX = -1f;
        EndRun();
        KeepCaretInView();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Normalises CRLF and a lone CR to <c>'\n'</c> and keeps every line break, which is what a paste needs.
    /// One typed grapheme coalesces with the typing around it into a single undo step; anything longer, a
    /// replaced selection or a line break is a step of its own.
    /// </remarks>
    public override void InsertText(string input) => Insert(input, mayCoalesce: true);

    /// <inheritdoc/>
    /// <remarks>A paste is a step of its own even when it is one grapheme long: it never joins the typing around it.</remarks>
    internal override void InsertPastedText(string input) => Insert(input, mayCoalesce: false);

    private void Insert(string input, bool mayCoalesce)
    {
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        input = NormaliseLineBreaks(input);
        ClampToText();

        var typed = mayCoalesce && !HasSelection && input != "\n" && input.Length <= 2 && IsOneGrapheme(input);
        BeginEdit(typed ? EditKind.Typing : EditKind.Other);
        ReplaceSelection(input);
        EndEdit(typed ? EditKind.Typing : EditKind.Other);
        IsCommitted = false;
        IsCancelled = false;
        _preferredColumn = -1;
        _preferredX = -1f;
        KeepCaretInView();
    }

    /// <summary>
    /// What a chord means to this editor: <see cref="TextInputKey.Commit"/> for <see cref="CommitChord"/>
    /// exactly; the keys only a box of lines has (Up, Down, the Page keys, Ctrl+Home/End, undo and redo); and
    /// otherwise the single-line mapping, in which Ctrl+Enter that is not the chord is only an Enter. Kept
    /// here rather than in <see cref="InputKeyExtensions.ToTextInputKey"/> so a single-line field's mapping
    /// does not change under hosts that call that directly.
    /// </summary>
    internal TextInputKey? KeyFor(InputKey key, InputModifier modifiers)
    {
        if (key == CommitChord.Key && modifiers == CommitChord.Modifiers)
        {
            return TextInputKey.Commit;
        }

        var ctrl = (modifiers & InputModifier.Ctrl) != 0;
        var shift = (modifiers & InputModifier.Shift) != 0;
        return key switch
        {
            InputKey.Z when ctrl && shift => TextInputKey.Redo,
            InputKey.Z when ctrl => TextInputKey.Undo,
            InputKey.Y when ctrl => TextInputKey.Redo,
            InputKey.Home when ctrl => TextInputKey.DocumentStart,
            InputKey.End when ctrl => TextInputKey.DocumentEnd,
            InputKey.Up => TextInputKey.Up,
            InputKey.Down => TextInputKey.Down,
            InputKey.PageUp => TextInputKey.PageUp,
            InputKey.PageDown => TextInputKey.PageDown,
            _ => key.ToTextInputKey(modifiers)
        };
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
                Move(VisualLayout?.Home(CursorPos) ?? LineStartIndex(Line), extend);
                break;

            case TextInputKey.End:
                Move(VisualLayout?.End(CursorPos) ?? LineEndIndex(LineStartIndex(Line)), extend);
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
                MoveLines(-PageStep, extend);
                break;

            case TextInputKey.PageDown:
                vertical = true;
                MoveLines(PageStep, extend);
                break;

            case TextInputKey.Undo:
                Undo();
                break;

            case TextInputKey.Redo:
                Redo();
                break;

            case TextInputKey.Commit:
                EndRun();
                SelectionAnchor = -1;
                IsCommitted = true;
                break;

            case TextInputKey.Escape:
                EndRun();
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
            _preferredX = -1f;
        }

        KeepCaretInView();
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
        KeepCaretInView();
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
        KeepCaretInView();
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
        ScrollOffsetY = 0f;
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

    /// <summary>How far PageUp and PageDown move: the box's lines once it is bounded, else <see cref="PageLines"/>.</summary>
    private int PageStep => Math.Max(1, MaxLines < int.MaxValue && VisualLayout is not null ? VisibleLines : PageLines);

    /// <summary>Scrolls the least that shows the caret's visual line, and holds the offset inside the content.</summary>
    private void KeepCaretInView()
    {
        if (VisualLayout is not { } layout)
        {
            return;
        }

        var caret = Math.Clamp(CursorPos, 0, Text.Length);
        ScrollOffsetY = layout.RevealLine(layout.LineOf(caret), ScrollOffsetY, ViewportHeight);
    }

    private void MoveLines(int delta, bool extend)
    {
        if (VisualLayout is { } layout)
        {
            // Wrapped lines keep an x, not a column: the characters differ in width, so the same column on the
            // next row is not the same place under the caret.
            if (_preferredX < 0f)
            {
                _preferredX = layout.XOf(CursorPos);
            }

            Move(layout.MoveVertically(CursorPos, delta, _preferredX), extend);
            return;
        }

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
            && ReferenceEquals(Text, _textAfterLastEdit) && CursorPos == _caretAfterLastEdit && !HasSelection;
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
        _caretAfterLastEdit = CursorPos;
    }

    /// <summary>A motion, a click or an undo ends the run of typing, so the next edit is a step of its own.</summary>
    private void EndRun() => _lastEdit = EditKind.None;
}
