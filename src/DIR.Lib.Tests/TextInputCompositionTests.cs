using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="TextInputState"/>'s IME composition ("preedit") state.
/// <para>
/// The invariant these serve: <b>the composition is NOT part of <see cref="TextInputState.Text"/></b>. It
/// belongs to the input method until it commits, at which point the platform delivers the committed
/// characters as ordinary text input. Merging it early is what would let a cancelled composition survive in
/// the field.
/// </para>
/// <para>
/// Why this exists at all: a field that handles only committed text can accept Latin input and nothing
/// else, because with a CJK IME every keystroke before the commit is composition. That gap shipped once
/// precisely because the test for it injected committed text directly, which exercises none of this path.
/// </para>
/// </summary>
public class TextInputCompositionTests
{
    private static TextInputState Field(string text = "", int cursor = 0)
        => new TextInputState { Text = text, CursorPos = cursor, IsActive = true };

    [Fact]
    public void AFreshFieldIsNotComposing()
    {
        var field = Field();

        field.IsComposing.ShouldBeFalse();
        field.Composition.ShouldBe("");
    }

    [Fact]
    public void SettingACompositionDoesNotTouchTheCommittedText()
    {
        var field = Field("abc", cursor: 3);

        field.SetComposition("ni", cursor: 2);

        field.Composition.ShouldBe("ni");
        field.CompositionCursor.ShouldBe(2);
        field.IsComposing.ShouldBeTrue();
        // The whole point: the preedit is the IME's, not the field's.
        field.Text.ShouldBe("abc");
        field.CursorPos.ShouldBe(3);
    }

    [Fact]
    public void AnEmptyCompositionEndsComposing()
    {
        var field = Field();
        field.SetComposition("nihao", cursor: 5);

        // Every IME signals both a commit and a cancel by clearing the preedit; the committed characters
        // arrive separately as ordinary text input.
        field.SetComposition("");

        field.IsComposing.ShouldBeFalse();
        field.Composition.ShouldBe("");
    }

    [Fact]
    public void ANullCompositionIsTreatedAsEmptyRatherThanThrowing()
    {
        var field = Field();
        field.SetComposition("ni");

        field.SetComposition(null);

        field.IsComposing.ShouldBeFalse();
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(99, 2)]
    public void TheCompositionCursorIsClampedIntoTheComposition(int given, int expected)
    {
        var field = Field();

        // These cross a P/Invoke boundary as raw ints. An out-of-range value would index past the string
        // when the renderer measures where to put the caret, so it is clamped rather than trusted.
        field.SetComposition("ab", cursor: given);

        field.CompositionCursor.ShouldBe(expected);
    }

    [Fact]
    public void TheCompositionLengthCannotReachPastTheEnd()
    {
        var field = Field();

        field.SetComposition("abcd", cursor: 3, length: 99);

        field.CompositionCursor.ShouldBe(3);
        field.CompositionLength.ShouldBe(1);
    }

    [Fact]
    public void DeactivatingDropsTheComposition()
    {
        var field = Field("abc");
        field.SetComposition("ni");

        field.Deactivate();

        // A preedit belongs to the input method, and blurring abandons it. Left behind, it would paint
        // composition text into a field nobody is typing in, and still be there on the next focus.
        field.IsComposing.ShouldBeFalse();
        field.Text.ShouldBe("abc");
    }

    [Fact]
    public void ClearingTheFieldDropsTheComposition()
    {
        var field = Field("abc");
        field.SetComposition("ni");

        field.Clear();

        field.IsComposing.ShouldBeFalse();
        field.Text.ShouldBe("");
    }

    [Fact]
    public void CommittedTextArrivingDuringCompositionIsInsertedAtTheFieldsOwnCursor()
    {
        var field = Field("ab", cursor: 1);
        field.SetComposition("ni");

        // What the platform actually does on commit: the preedit ends and the characters arrive as
        // ordinary text input. The insert must land at the FIELD's cursor, not the composition's.
        field.SetComposition("");
        field.InsertText("你");

        field.Text.ShouldBe("a你b");
        field.IsComposing.ShouldBeFalse();
    }
}
