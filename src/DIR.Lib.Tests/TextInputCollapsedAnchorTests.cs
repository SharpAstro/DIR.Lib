using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A selection anchor sitting ON the caret: set, but selecting nothing. A drag that ends where it began
/// leaves one (the first extending move takes the anchor from the caret), and so does a double-click on a
/// non-word character at the end of the value. <see cref="TextInputState.HasSelection"/> reads false, so
/// every edit took its no-selection branch -- and moved the caret away from an anchor it never cleared.
/// <para>
/// Backspace then left the anchor one past the caret AND past the end of the shortened value: a selection
/// that reached outside the text, which the next paint measured with a substring and threw on, taking the
/// whole app down. Typing left a phantom one-character selection instead, so the next keystroke replaced the
/// character before it.
/// </para>
/// </summary>
public class TextInputCollapsedAnchorTests
{
    /// <summary>A field holding <paramref name="text"/> with its anchor collapsed onto the caret at the end,
    /// the way a drag that ends where it began leaves it.</summary>
    private static TextInputState Collapsed(string text)
    {
        var state = new TextInputState { Text = text };
        state.MoveCaretTo(text.Length);
        state.MoveCaretTo(text.Length, extend: true);
        state.SelectionAnchor.ShouldBe(text.Length);
        state.HasSelection.ShouldBeFalse();
        return state;
    }

    [Theory]
    [InlineData(TextInputKey.Backspace)]
    [InlineData(TextInputKey.WordBackspace)]
    public void DeletingBackOverACollapsedAnchor_LeavesNoSelectionOutsideTheText(TextInputKey key)
    {
        var state = Collapsed("aus");

        state.HandleKey(key);

        state.HasSelection.ShouldBeFalse();
        state.SelectionEnd.ShouldBeLessThanOrEqualTo(state.Text.Length);
    }

    [Fact]
    public void TypingAtACollapsedAnchor_SelectsNothing()
    {
        var state = Collapsed("aus");

        state.InsertText("t");

        state.Text.ShouldBe("aust");
        state.HasSelection.ShouldBeFalse();
    }
}
