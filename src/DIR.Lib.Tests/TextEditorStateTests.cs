using System.Collections.Generic;
using System.Threading.Tasks;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="TextEditorState"/> -- the multi-line editing model, driven both directly and through
/// <see cref="TextInputInteraction"/>, which is the way a host reaches it.
/// </summary>
public class TextEditorStateTests
{
    // A family emoji: man + ZWJ + woman + ZWJ + girl, one grapheme of eight UTF-16 units.
    private const string Family = "\U0001F468‍\U0001F469‍\U0001F467";

    // "e" with a combining acute accent: one grapheme of two units.
    private const string EAcute = "é";

    private sealed class Harness
    {
        public TextInputFocus Focus { get; } = new();
        public BackgroundTaskTracker Tracker { get; } = new();
        public string? Clipboard { get; set; }

        public TextInputInteraction.KeyContext Context => new(
            Tracker, Focus, () => { },
            GetClipboardText: () => Clipboard,
            SetClipboardText: t => Clipboard = t);

        public bool Key(InputKey key, InputModifier modifiers = InputModifier.None)
            => TextInputInteraction.HandleKey(key, modifiers, Context);
    }

    private static TextEditorState Editor(string text, int caret = -1) => new()
    {
        Text = text,
        CursorPos = caret < 0 ? text.Length : caret,
    };

    // ---- Newline versus commit and cancel ----

    [Fact]
    public void Enter_InsertsANewline_AndDoesNotCommit()
    {
        var editor = Editor("ab", 1);

        editor.HandleKey(TextInputKey.Enter).ShouldBeTrue();

        editor.Text.ShouldBe("a\nb");
        editor.CursorPos.ShouldBe(2);
        editor.IsCommitted.ShouldBeFalse();
        (editor.Line, editor.Column).ShouldBe((1, 0));
    }

    [Fact]
    public void Enter_ReplacesTheSelection()
    {
        var editor = Editor("abcd", 3);
        editor.SelectionAnchor = 1;

        editor.HandleKey(TextInputKey.Enter);

        editor.Text.ShouldBe("a\nd");
    }

    [Fact]
    public void CommitKey_Commits_AndEscapeCancels()
    {
        var editor = Editor("x");

        editor.HandleKey(TextInputKey.Commit).ShouldBeTrue();
        editor.IsCommitted.ShouldBeTrue();

        editor.IsCommitted = false;
        editor.HandleKey(TextInputKey.Escape).ShouldBeTrue();
        editor.IsCancelled.ShouldBeTrue();
        editor.Text.ShouldBe("x");
    }

    [Fact]
    public async Task Interaction_EnterInsertsANewline_CtrlEnterCommits_EscapeCancels()
    {
        string? committed = null;
        var cancelled = false;
        var editor = new TextEditorState
        {
            OnCommit = t => { committed = t; return Task.CompletedTask; },
            OnCancel = () => cancelled = true,
        };
        var harness = new Harness();
        harness.Focus.Focus(editor);
        TextInputInteraction.HandleText(editor, "one");

        harness.Key(InputKey.Enter);
        TextInputInteraction.HandleText(editor, "two");
        committed.ShouldBeNull("plain Enter is a line break");
        editor.Text.ShouldBe("one\ntwo");

        harness.Key(InputKey.Enter, InputModifier.Ctrl);
        await harness.Tracker.DrainAsync();
        committed.ShouldBe("one\ntwo");
        harness.Focus.Current.ShouldBeSameAs(editor);

        harness.Key(InputKey.Escape);
        cancelled.ShouldBeTrue();
        harness.Focus.Current.ShouldBeNull();
    }

    [Fact]
    public async Task Interaction_EnterOnASingleLineField_StillCommits()
    {
        string? committed = null;
        var field = new TextInputState { OnCommit = t => { committed = t; return Task.CompletedTask; } };
        var harness = new Harness();
        harness.Focus.Focus(field);
        TextInputInteraction.HandleText(field, "42");

        harness.Key(InputKey.Enter);
        await harness.Tracker.DrainAsync();

        committed.ShouldBe("42");
        field.Text.ShouldBe("42");
    }

    [Fact]
    public async Task Interaction_CtrlEnterAndCtrlHomeOnASingleLineField_ReadAsBefore()
    {
        string? committed = null;
        var field = new TextInputState
        {
            Text = "abc",
            CursorPos = 2,
            OnCommit = t => { committed = t; return Task.CompletedTask; },
        };
        var harness = new Harness();
        harness.Focus.Focus(field);

        harness.Key(InputKey.Home, InputModifier.Ctrl);
        field.CursorPos.ShouldBe(0);
        harness.Key(InputKey.End, InputModifier.Ctrl);
        field.CursorPos.ShouldBe(3);

        harness.Key(InputKey.Enter, InputModifier.Ctrl);
        await harness.Tracker.DrainAsync();
        committed.ShouldBe("abc");
    }

    [Fact]
    public void SingleLineField_DoesNotHandleTheEditorKeys()
    {
        var field = new TextInputState { Text = "abc" };

        foreach (var key in new[]
        {
            TextInputKey.Up, TextInputKey.Down, TextInputKey.PageUp, TextInputKey.PageDown,
            TextInputKey.DocumentStart, TextInputKey.DocumentEnd, TextInputKey.Undo, TextInputKey.Redo,
            TextInputKey.Commit,
        })
        {
            field.HandleKey(key).ShouldBeFalse($"{key}");
        }
    }

    [Fact]
    public void SingleLineField_IsNotOfferedTheEditorKeysAsOverrides()
    {
        var seen = new List<TextInputKey>();
        var field = new TextInputState { OnKeyOverride = k => { seen.Add(k); return false; } };
        var harness = new Harness();
        harness.Focus.Focus(field);

        harness.Key(InputKey.Up);
        harness.Key(InputKey.PageDown);
        harness.Key(InputKey.Z, InputModifier.Ctrl);

        seen.ShouldBeEmpty();
    }

    [Fact]
    public void ACustomCommitChord_CommitsOnThatChord_AndEnterWithOtherModifiersIsANewline()
    {
        var editor = new TextEditorState { CommitChord = new KeyChord(InputKey.Enter) };
        var harness = new Harness();
        harness.Focus.Focus(editor);
        TextInputInteraction.HandleText(editor, "a");

        harness.Key(InputKey.Enter, InputModifier.Shift);
        editor.Text.ShouldBe("a\n");

        harness.Key(InputKey.Enter, InputModifier.Ctrl);
        editor.Text.ShouldBe("a\n\n", "Ctrl+Enter is only a newline once it is not the chord");

        var committed = false;
        editor.OnCommit = _ => { committed = true; return Task.CompletedTask; };
        harness.Key(InputKey.Enter);
        harness.Tracker.DrainAsync().GetAwaiter().GetResult();
        committed.ShouldBeTrue();
    }

    [Fact]
    public void Interaction_OnTextChanged_FiresForEnterAndUndo()
    {
        var changes = new List<string>();
        var editor = new TextEditorState { OnTextChanged = changes.Add };
        var harness = new Harness();
        harness.Focus.Focus(editor);

        TextInputInteraction.HandleText(editor, "a");
        harness.Key(InputKey.Enter);
        harness.Key(InputKey.Z, InputModifier.Ctrl);

        changes.ShouldBe(["a", "a\n", "a"]);
    }

    // ---- Chord mapping ----

    [Theory]
    [InlineData(InputKey.Z, InputModifier.Ctrl, TextInputKey.Undo)]
    [InlineData(InputKey.Y, InputModifier.Ctrl, TextInputKey.Redo)]
    [InlineData(InputKey.Z, InputModifier.Ctrl | InputModifier.Shift, TextInputKey.Redo)]
    [InlineData(InputKey.Home, InputModifier.Ctrl, TextInputKey.DocumentStart)]
    [InlineData(InputKey.End, InputModifier.Ctrl | InputModifier.Shift, TextInputKey.DocumentEnd)]
    [InlineData(InputKey.Enter, InputModifier.Ctrl, TextInputKey.Commit)]
    [InlineData(InputKey.Enter, InputModifier.None, TextInputKey.Enter)]
    [InlineData(InputKey.Home, InputModifier.None, TextInputKey.Home)]
    [InlineData(InputKey.Up, InputModifier.None, TextInputKey.Up)]
    [InlineData(InputKey.Down, InputModifier.Shift, TextInputKey.Down)]
    [InlineData(InputKey.PageUp, InputModifier.None, TextInputKey.PageUp)]
    [InlineData(InputKey.PageDown, InputModifier.None, TextInputKey.PageDown)]
    public void TheChordMapsToOneMeaning(InputKey key, InputModifier modifiers, TextInputKey expected)
        => key.ToTextInputKey(modifiers).ShouldBe(expected);

    [Fact]
    public void TheNewKeysAreAppended_SoThePublishedValuesDoNotMove()
    {
        ((int)TextInputKey.Cut).ShouldBe(14);
        ((int)TextInputKey.Up).ShouldBe(15);
        ((int)TextInputKey.Commit).ShouldBe(23);
    }

    // ---- Caret and selection ----

    [Fact]
    public void LeftAndRight_CrossLineBreaks()
    {
        var editor = Editor("ab\ncd", 3);

        editor.HandleKey(TextInputKey.Left);
        editor.CursorPos.ShouldBe(2);
        (editor.Line, editor.Column).ShouldBe((0, 2));

        editor.HandleKey(TextInputKey.Right);
        editor.HandleKey(TextInputKey.Right);
        (editor.Line, editor.Column).ShouldBe((1, 1));
    }

    [Fact]
    public void HomeAndEnd_GoToTheEdgesOfTheLogicalLine()
    {
        var editor = Editor("one\ntwo three\nfour", 8);

        editor.HandleKey(TextInputKey.Home);
        editor.CursorPos.ShouldBe(4);

        editor.HandleKey(TextInputKey.End);
        editor.CursorPos.ShouldBe(13);

        editor.CursorPos = 14;
        editor.HandleKey(TextInputKey.End);
        editor.CursorPos.ShouldBe(18);
    }

    [Fact]
    public void CtrlHomeAndEnd_GoToTheEdgesOfTheText()
    {
        var editor = Editor("one\ntwo\nthree", 5);

        editor.HandleKey(TextInputKey.DocumentStart);
        editor.CursorPos.ShouldBe(0);

        editor.HandleKey(TextInputKey.DocumentEnd);
        editor.CursorPos.ShouldBe(13);
    }

    [Fact]
    public void UpAndDown_KeepAStickyColumn_AcrossAShorterLine()
    {
        var editor = Editor("long line\nab\nanother long", 7);   // column 7 of line 0

        editor.HandleKey(TextInputKey.Down);
        (editor.Line, editor.Column).ShouldBe((1, 2), "clamped to the short line's end");

        editor.HandleKey(TextInputKey.Down);
        (editor.Line, editor.Column).ShouldBe((2, 7), "and back to the column it had");

        editor.HandleKey(TextInputKey.Up);
        editor.HandleKey(TextInputKey.Up);
        (editor.Line, editor.Column).ShouldBe((0, 7));
    }

    [Fact]
    public void AHorizontalMove_ForgetsTheStickyColumn()
    {
        var editor = Editor("long line\nab\nanother long", 7);
        editor.HandleKey(TextInputKey.Down);
        editor.HandleKey(TextInputKey.Left);             // column 1 on line 1

        editor.HandleKey(TextInputKey.Down);

        (editor.Line, editor.Column).ShouldBe((2, 1));
    }

    [Fact]
    public void UpOnTheFirstLine_GoesToTheStart_AndDownOnTheLast_ToTheEnd()
    {
        var editor = Editor("ab\ncd", 4);

        editor.HandleKey(TextInputKey.Up);
        editor.HandleKey(TextInputKey.Up);
        editor.CursorPos.ShouldBe(0);

        editor.HandleKey(TextInputKey.Down);
        editor.HandleKey(TextInputKey.Down);
        editor.CursorPos.ShouldBe(5);
    }

    [Fact]
    public void PageDownAndUp_MoveByPageLines()
    {
        var editor = Editor("0\n1\n2\n3\n4\n5\n6", 0);
        editor.PageLines = 3;

        editor.HandleKey(TextInputKey.PageDown);
        editor.Line.ShouldBe(3);

        editor.HandleKey(TextInputKey.PageDown);
        editor.Line.ShouldBe(6);

        editor.HandleKey(TextInputKey.PageUp);
        editor.Line.ShouldBe(3);
    }

    [Fact]
    public void SetCaret_ByLineAndColumn_ClampsIntoTheText()
    {
        var editor = Editor("ab\ncde\nf", 0);

        editor.SetCaret(1, 2);
        editor.CursorPos.ShouldBe(5);

        editor.SetCaret(1, 99);
        editor.CursorPos.ShouldBe(6, "past the end of the line is its end");

        editor.SetCaret(99, 0);
        editor.CursorPos.ShouldBe(7, "past the last line is the last");

        editor.LineCount.ShouldBe(3);
        editor.GetLine(1).ShouldBe("cde");
    }

    [Fact]
    public void SelectionExtendsAcrossLines_WithShift()
    {
        var editor = Editor("ab\ncd\nef", 1);

        editor.HandleKey(TextInputKey.Down, extend: true);
        editor.HandleKey(TextInputKey.Down, extend: true);

        editor.HasSelection.ShouldBeTrue();
        editor.SelectionStart.ShouldBe(1);
        editor.SelectionEnd.ShouldBe(7);
        editor.Text[editor.SelectionStart..editor.SelectionEnd].ShouldBe("b\ncd\ne");

        editor.HandleKey(TextInputKey.Up, extend: true);
        editor.SelectionEnd.ShouldBe(4);
    }

    [Theory]
    [InlineData(TextInputKey.Left)]
    [InlineData(TextInputKey.Right)]
    [InlineData(TextInputKey.Home)]
    [InlineData(TextInputKey.End)]
    [InlineData(TextInputKey.WordLeft)]
    [InlineData(TextInputKey.WordRight)]
    [InlineData(TextInputKey.DocumentStart)]
    [InlineData(TextInputKey.DocumentEnd)]
    [InlineData(TextInputKey.PageUp)]
    [InlineData(TextInputKey.PageDown)]
    public void ShiftExtendsTheSelection_ForEveryMotion(TextInputKey key)
    {
        var editor = Editor("one two\nthree four\nfive", 10);

        editor.HandleKey(key, extend: true);

        editor.SelectionAnchor.ShouldBe(10);
        editor.HasSelection.ShouldBe(editor.CursorPos != 10);
    }

    [Fact]
    public void PlainLeftAndRight_CollapseASelectionToItsEdges()
    {
        var editor = Editor("abcdef", 4);
        editor.SelectionAnchor = 1;

        editor.HandleKey(TextInputKey.Left);
        editor.CursorPos.ShouldBe(1);
        editor.HasSelection.ShouldBeFalse();

        editor.CursorPos = 4;
        editor.SelectionAnchor = 1;
        editor.HandleKey(TextInputKey.Right);
        editor.CursorPos.ShouldBe(4);
        editor.HasSelection.ShouldBeFalse();
    }

    [Fact]
    public void SelectAll_SelectsAcrossEveryLine()
    {
        var editor = Editor("a\nb\nc", 0);

        editor.HandleKey(TextInputKey.SelectAll);

        editor.SelectionStart.ShouldBe(0);
        editor.SelectionEnd.ShouldBe(5);
    }

    // ---- Word moves ----

    [Fact]
    public void WordMoves_StepOverWords_AndAcrossLines()
    {
        var editor = Editor("one two\nthree", 0);

        editor.HandleKey(TextInputKey.WordRight);
        editor.CursorPos.ShouldBe(4);
        editor.HandleKey(TextInputKey.WordRight);
        editor.CursorPos.ShouldBe(8, "past the line break onto the next line's word");
        editor.HandleKey(TextInputKey.WordRight);
        editor.CursorPos.ShouldBe(13);

        editor.HandleKey(TextInputKey.WordLeft);
        editor.CursorPos.ShouldBe(8);
        editor.HandleKey(TextInputKey.WordLeft);
        editor.CursorPos.ShouldBe(4);
    }

    [Fact]
    public void WordBackspace_DeletesBackToTheWordStart()
    {
        var editor = Editor("one two");

        editor.HandleKey(TextInputKey.WordBackspace);

        editor.Text.ShouldBe("one ");
    }

    // ---- Graphemes ----

    [Fact]
    public void AnEmojiZwjSequence_IsOneCaretStep()
    {
        var editor = Editor("a" + Family + "b", 1);

        editor.HandleKey(TextInputKey.Right);
        editor.CursorPos.ShouldBe(1 + Family.Length);
        editor.Column.ShouldBe(2);

        editor.HandleKey(TextInputKey.Left);
        editor.CursorPos.ShouldBe(1);
    }

    [Fact]
    public void ACombiningMark_IsSteppedOverWithItsLetter()
    {
        var editor = Editor(EAcute + "x", 0);

        editor.HandleKey(TextInputKey.Right);
        editor.CursorPos.ShouldBe(2);

        editor.HandleKey(TextInputKey.Left);
        editor.CursorPos.ShouldBe(0);
    }

    [Fact]
    public void BackspaceAndDelete_RemoveAWholeGrapheme()
    {
        var back = Editor("a" + Family + EAcute);
        back.HandleKey(TextInputKey.Backspace);
        back.Text.ShouldBe("a" + Family);
        back.HandleKey(TextInputKey.Backspace);
        back.Text.ShouldBe("a");

        var forward = Editor(Family + EAcute, 0);
        forward.HandleKey(TextInputKey.Delete);
        forward.Text.ShouldBe(EAcute);
        forward.HandleKey(TextInputKey.Delete);
        forward.Text.ShouldBe("");
    }

    [Fact]
    public void ShiftRight_SelectsAWholeGrapheme()
    {
        var editor = Editor(Family + "x", 0);

        editor.HandleKey(TextInputKey.Right, extend: true);

        editor.SelectionEnd.ShouldBe(Family.Length);
    }

    [Fact]
    public void MoveCaretTo_NeverLeavesTheCaretInsideACluster()
    {
        var editor = Editor(EAcute + "x", 0);

        editor.MoveCaretTo(1);

        editor.CursorPos.ShouldBe(0);
    }

    [Fact]
    public void WordMove_DoesNotStopInsideAComposedLetter()
    {
        var editor = Editor("caf" + EAcute + " bar", 0);

        editor.HandleKey(TextInputKey.WordRight);

        editor.CursorPos.ShouldBe(6);
    }

    [Fact]
    public void BackspaceAtALineStart_JoinsTheLines()
    {
        var editor = Editor("ab\ncd", 3);

        editor.HandleKey(TextInputKey.Backspace);

        editor.Text.ShouldBe("abcd");
        editor.CursorPos.ShouldBe(2);
    }

    // ---- Paste and insert ----

    [Theory]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\rb", "a\nb")]
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\r\n\r\nb\r", "a\n\nb\n")]
    public void InsertText_NormalisesLineBreaks_AndKeepsThem(string pasted, string expected)
    {
        var editor = new TextEditorState();

        editor.InsertText(pasted);

        editor.Text.ShouldBe(expected);
        editor.CursorPos.ShouldBe(expected.Length);
    }

    [Fact]
    public void Paste_ThroughTheInteraction_KeepsLineBreaks_AndReplacesTheSelection()
    {
        var editor = Editor("xxyy", 2);
        editor.SelectionAnchor = 0;
        var harness = new Harness { Clipboard = "one\r\ntwo\nthree" };
        harness.Focus.Focus(editor);

        harness.Key(InputKey.V, InputModifier.Ctrl);

        editor.Text.ShouldBe("one\ntwo\nthreeyy");
        editor.LineCount.ShouldBe(3);
    }

    [Fact]
    public void Copy_OfASelectionAcrossLines_PutsTheLineBreaksOnTheClipboard()
    {
        var editor = Editor("ab\ncd", 4);
        editor.SelectionAnchor = 1;
        var harness = new Harness();
        harness.Focus.Focus(editor);

        harness.Key(InputKey.C, InputModifier.Ctrl);

        harness.Clipboard.ShouldBe("b\nc");
    }

    [Fact]
    public void Cut_RemovesTheSelection_AndCanBeUndone()
    {
        var editor = Editor("ab\ncd", 4);
        editor.SelectionAnchor = 1;
        var harness = new Harness();
        harness.Focus.Focus(editor);

        harness.Key(InputKey.X, InputModifier.Ctrl);
        editor.Text.ShouldBe("ad");
        harness.Clipboard.ShouldBe("b\nc");

        harness.Key(InputKey.Z, InputModifier.Ctrl);
        editor.Text.ShouldBe("ab\ncd");
    }

    // ---- Undo and redo ----

    [Fact]
    public void ConsecutiveTypedCharacters_AreOneUndoStep()
    {
        var editor = new TextEditorState();
        foreach (var c in "hello")
        {
            editor.InsertText(c.ToString());
        }

        editor.Undo().ShouldBeTrue();

        editor.Text.ShouldBe("");
        editor.CanUndo.ShouldBeFalse();
        editor.Undo().ShouldBeFalse();
    }

    [Fact]
    public void AMotion_EndsTheTypingRun()
    {
        var editor = new TextEditorState();
        editor.InsertText("a");
        editor.InsertText("b");
        editor.HandleKey(TextInputKey.Left);
        editor.InsertText("c");

        editor.Undo();
        editor.Text.ShouldBe("ab");
        editor.Undo();
        editor.Text.ShouldBe("");
    }

    [Fact]
    public void UndoRestoresTextCaretAndSelection()
    {
        var editor = Editor("hello world", 5);
        editor.SelectionAnchor = 0;

        editor.HandleKey(TextInputKey.Backspace);
        editor.Text.ShouldBe(" world");

        editor.Undo();

        editor.Text.ShouldBe("hello world");
        editor.CursorPos.ShouldBe(5);
        editor.SelectionAnchor.ShouldBe(0);
    }

    [Fact]
    public void Redo_ReappliesWhatUndoTookBack()
    {
        var editor = new TextEditorState();
        editor.InsertText("a");
        editor.HandleKey(TextInputKey.Enter);
        editor.InsertText("b");

        editor.HandleKey(TextInputKey.Undo);
        editor.Text.ShouldBe("a\n");
        editor.HandleKey(TextInputKey.Undo);
        editor.Text.ShouldBe("a");

        editor.HandleKey(TextInputKey.Redo);
        editor.Text.ShouldBe("a\n");
        editor.HandleKey(TextInputKey.Redo);
        editor.Text.ShouldBe("a\nb");
        editor.CanRedo.ShouldBeFalse();
    }

    [Fact]
    public void AnEdit_ClearsTheRedoHistory()
    {
        var editor = new TextEditorState();
        editor.InsertText("a");
        editor.HandleKey(TextInputKey.Enter);
        editor.Undo();
        editor.CanRedo.ShouldBeTrue();

        editor.InsertText("z");

        editor.CanRedo.ShouldBeFalse();
        editor.Redo().ShouldBeFalse();
    }

    [Fact]
    public void UndoRedo_ThroughTheInteraction_UseTheChords()
    {
        var editor = new TextEditorState();
        var harness = new Harness();
        harness.Focus.Focus(editor);
        TextInputInteraction.HandleText(editor, "abc");

        harness.Key(InputKey.Z, InputModifier.Ctrl);
        editor.Text.ShouldBe("");

        harness.Key(InputKey.Y, InputModifier.Ctrl);
        editor.Text.ShouldBe("abc");

        harness.Key(InputKey.Z, InputModifier.Ctrl);
        harness.Key(InputKey.Z, InputModifier.Ctrl | InputModifier.Shift);
        editor.Text.ShouldBe("abc");
    }

    [Fact]
    public void History_IsBounded()
    {
        var editor = new TextEditorState();
        for (var i = 0; i < TextEditorState.HistoryLimit + 50; i++)
        {
            editor.HandleKey(TextInputKey.Enter);
        }

        var steps = 0;
        while (editor.Undo())
        {
            steps++;
        }

        steps.ShouldBe(TextEditorState.HistoryLimit);
    }

    [Fact]
    public void Clear_ForgetsTheHistory()
    {
        var editor = new TextEditorState();
        editor.InsertText("abc");

        editor.Clear();

        editor.Text.ShouldBe("");
        editor.CanUndo.ShouldBeFalse();
    }

    // ---- Focus ----

    [Fact]
    public void AnEditor_IsFocusedThroughTheSameOwner_AsAnyField()
    {
        var focus = new TextInputFocus();
        var editor = new TextEditorState();
        var other = new TextInputState();

        focus.Focus(editor);
        editor.IsActive.ShouldBeTrue();

        focus.Focus(other);
        editor.IsActive.ShouldBeFalse();
        other.IsActive.ShouldBeTrue();

        focus.Blur();
        other.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ATextShortenedUnderTheCaret_DoesNotThrow()
    {
        var editor = Editor("hello\nworld");
        editor.Text = "hi";

        editor.HandleKey(TextInputKey.Left).ShouldBeTrue();
        editor.HandleKey(TextInputKey.Up).ShouldBeTrue();
        editor.Text.ShouldBe("hi");
    }
}
