using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Word motion in a text field: Ctrl+Left, Ctrl+Right, Ctrl+Backspace and the Shift'd forms of the first
/// two, which every desktop text box has had for thirty years and this one did not.
/// <para>
/// The property worth pinning is that the two directions are INVERSES over the same text. Each is easy to
/// write plausibly on its own -- skip the word, skip the gap -- and easy to write so that walking right
/// then left lands somewhere else, which reads as the caret drifting and is almost impossible to see in a
/// screenshot. They also have to agree with <see cref="TextInputState.SelectWordAt"/> about what a word
/// IS, since a double-click and a Ctrl+Left are two ways of asking the same question.
/// </para>
/// </summary>
public class TextInputWordMotionTests
{
    private static TextInputState Field(string text, int caret) =>
        new() { Text = text, IsActive = true, CursorPos = caret };

    // ---- Where the boundaries are ----

    [Fact]
    public void RightLandsOnTheStartOfTheNextWord_NotTheEndOfThisOne()
    {
        // "hello| world" would be the naive answer: it stops at the end of the word it was in and the next
        // press has to step the space separately, so a word walk takes twice the presses it looks like.
        var state = Field("hello world", 0);

        state.MoveCaretToWordBoundary(1);

        state.CursorPos.ShouldBe(6);
    }

    [Fact]
    public void LeftLandsOnTheStartOfTheWordBehindTheCaret()
    {
        var state = Field("hello world", 11);

        state.MoveCaretToWordBoundary(-1);

        state.CursorPos.ShouldBe(6);
    }

    [Fact]
    public void LeftFromInsideAWordGoesToThatWordsStart()
    {
        var state = Field("hello world", 9);

        state.MoveCaretToWordBoundary(-1);

        state.CursorPos.ShouldBe(6);
    }

    [Fact]
    public void WalkingRightAndThenBackLandsWhereItStarted()
    {
        const string text = "one  two-part   three";
        var forwards = new List<int>();

        var state = Field(text, 0);
        while (state.CursorPos < text.Length)
        {
            state.MoveCaretToWordBoundary(1);
            forwards.Add(state.CursorPos);
        }

        var backwards = new List<int>();
        while (state.CursorPos > 0)
        {
            state.MoveCaretToWordBoundary(-1);
            backwards.Add(state.CursorPos);
        }

        // Every stop on the way out is a stop on the way back, in reverse -- with the far end (the end of
        // the text) and the near end (its start) as the two that only one direction produces.
        backwards.ShouldBe([.. forwards.AsEnumerable().Reverse().Skip(1), 0]);
    }

    [Fact]
    public void AHyphenatedWordIsOneWord_TheSameOneADoubleClickSelects()
    {
        // IsWordChar counts '-' and '_', so "two-part" is one token. The motion and SelectWordAt have to
        // read the same rule or a double-click and a Ctrl+Left disagree about the same characters.
        var state = Field("two-part word", 13);

        state.MoveCaretToWordBoundary(-1);
        state.CursorPos.ShouldBe(9);

        state.MoveCaretToWordBoundary(-1);
        state.CursorPos.ShouldBe(0);

        var selected = Field("two-part word", 0);
        selected.SelectWordAt(3);
        selected.SelectionStart.ShouldBe(0);
        selected.SelectionEnd.ShouldBe(8);
    }

    [Fact]
    public void TheEndsAreStops_NotThrows()
    {
        var state = Field("hello", 5);
        state.MoveCaretToWordBoundary(1);
        state.CursorPos.ShouldBe(5);

        state.CursorPos = 0;
        state.MoveCaretToWordBoundary(-1);
        state.CursorPos.ShouldBe(0);

        var empty = Field("", 0);
        empty.MoveCaretToWordBoundary(1);
        empty.MoveCaretToWordBoundary(-1);
        empty.CursorPos.ShouldBe(0);
    }

    [Fact]
    public void ADirectionOfZeroMovesNothing()
    {
        var state = Field("hello world", 3);

        state.MoveCaretToWordBoundary(0);

        state.CursorPos.ShouldBe(3);
    }

    // ---- Selection ----

    [Fact]
    public void ExtendingAnchorsWhereTheCaretWas()
    {
        var state = Field("hello world", 0);

        state.MoveCaretToWordBoundary(1, extend: true);

        state.SelectionStart.ShouldBe(0);
        state.SelectionEnd.ShouldBe(6);
    }

    [Fact]
    public void ExtendingTwiceKeepsTheOriginalAnchor()
    {
        var state = Field("one two three", 0);

        state.MoveCaretToWordBoundary(1, extend: true);
        state.MoveCaretToWordBoundary(1, extend: true);

        state.SelectionStart.ShouldBe(0);
        state.SelectionEnd.ShouldBe(8);
    }

    [Fact]
    public void MovingWithoutExtendingDropsTheSelection()
    {
        var state = Field("hello world", 0);
        state.SelectAll();

        state.MoveCaretToWordBoundary(1);

        state.HasSelection.ShouldBeFalse();
    }

    // ---- The keys, through HandleKey ----

    [Fact]
    public void HandleKeyRoutesTheWordMotionsAndHonoursExtend()
    {
        var state = Field("hello world", 0);

        state.HandleKey(TextInputKey.WordRight).ShouldBeTrue();
        state.CursorPos.ShouldBe(6);

        state.HandleKey(TextInputKey.WordRight, extend: true).ShouldBeTrue();
        state.SelectionStart.ShouldBe(6);
        state.SelectionEnd.ShouldBe(11);

        state.HandleKey(TextInputKey.WordLeft).ShouldBeTrue();
        state.CursorPos.ShouldBe(6);
    }

    /// <summary>
    /// Teaching the plain arrows to extend would change what Shift+Left does for every consumer already
    /// shipping against this type, so it is deliberately not part of this change. Pinned so that doing it
    /// later is a decision someone takes rather than a side effect of a passing parameter.
    /// </summary>
    [Fact]
    public void ThePlainArrowsStillCollapseASelection_EvenWhenExtendIsAsked()
    {
        var state = Field("hello world", 0);
        state.SelectAll();

        state.HandleKey(TextInputKey.Left, extend: true);

        state.HasSelection.ShouldBeFalse();
    }

    [Fact]
    public void WordBackspaceDeletesExactlyWhatWordLeftWouldHaveSteppedOver()
    {
        var walked = Field("hello brave world", 17);
        walked.MoveCaretToWordBoundary(-1);

        var deleted = Field("hello brave world", 17);
        deleted.HandleKey(TextInputKey.WordBackspace).ShouldBeTrue();

        deleted.CursorPos.ShouldBe(walked.CursorPos);
        deleted.Text.ShouldBe("hello brave ");
    }

    [Fact]
    public void WordBackspaceWithASelectionDeletesTheSelection_NotAWordBeyondIt()
    {
        var state = Field("hello brave world", 0);
        state.SelectionAnchor = 6;
        state.CursorPos = 11;

        state.HandleKey(TextInputKey.WordBackspace);

        state.Text.ShouldBe("hello  world");
        state.CursorPos.ShouldBe(6);
    }

    [Fact]
    public void WordBackspaceAtTheStartIsANoOp()
    {
        var state = Field("hello", 0);

        state.HandleKey(TextInputKey.WordBackspace).ShouldBeTrue();

        state.Text.ShouldBe("hello");
    }

    [Fact]
    public void CutIsSwallowedByTheStateAndPerformedByTheHost()
    {
        // The clipboard is platform-specific, so the state only says "consumed" -- the same contract Paste
        // and Copy have had. TextInputInteraction is where the two halves actually happen.
        var state = Field("hello", 0);
        state.SelectAll();

        state.HandleKey(TextInputKey.Cut).ShouldBeTrue();

        state.Text.ShouldBe("hello");
    }

    // ---- The chord -> meaning mapping ----

    [Theory]
    [InlineData(InputKey.Left, InputModifier.Ctrl, TextInputKey.WordLeft)]
    [InlineData(InputKey.Right, InputModifier.Ctrl, TextInputKey.WordRight)]
    [InlineData(InputKey.Backspace, InputModifier.Ctrl, TextInputKey.WordBackspace)]
    [InlineData(InputKey.X, InputModifier.Ctrl, TextInputKey.Cut)]
    [InlineData(InputKey.Left, InputModifier.Ctrl | InputModifier.Shift, TextInputKey.WordLeft)]
    [InlineData(InputKey.Right, InputModifier.Ctrl | InputModifier.Shift, TextInputKey.WordRight)]
    [InlineData(InputKey.Left, InputModifier.None, TextInputKey.Left)]
    [InlineData(InputKey.Right, InputModifier.Shift, TextInputKey.Right)]
    [InlineData(InputKey.Backspace, InputModifier.None, TextInputKey.Backspace)]
    public void TheChordMapsToOneMeaning(InputKey key, InputModifier modifiers, TextInputKey expected)
        => key.ToTextInputKey(modifiers).ShouldBe(expected);

    /// <summary>
    /// Shift does not make a different key. It is carried beside it, so the enum stays one member per
    /// MEANING instead of one per chord -- which is what keeps a cell host and a pixel host from having to
    /// agree on a second, parallel table.
    /// </summary>
    [Fact]
    public void ShiftDoesNotChangeWhichKeyAChordMeans()
    {
        InputKey.Left.ToTextInputKey(InputModifier.Ctrl | InputModifier.Shift)
            .ShouldBe(InputKey.Left.ToTextInputKey(InputModifier.Ctrl));
    }
}
