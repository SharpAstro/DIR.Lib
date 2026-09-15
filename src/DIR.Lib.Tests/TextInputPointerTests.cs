using System;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The pointer half of a text field: <see cref="TextInputRenderer.CaretIndexAt"/>, which says which
/// character a click landed on, and <see cref="TextInputInteraction.HandlePointer"/>, which says what one,
/// two or three clicks there mean.
/// <para>
/// Before these the fields had a full selection model -- anchor, word selection, select-all -- reachable
/// only from the keyboard, because nothing anywhere mapped a pointer position to a character index. A click
/// could focus a box and nothing more, so double-clicking a word in one selected nothing and there was no
/// gesture that selected what was already there.
/// </para>
/// <para>
/// The property worth pinning hardest is the ROUND TRIP: the caret drawn for index i must map back to i.
/// The two directions are separate code -- the paint sums a prefix, this binary-searches prefixes -- and
/// nothing else would notice them disagreeing, since each looks right on its own and the symptom is only a
/// caret landing a little off the pointer.
/// </para>
/// </summary>
public class TextInputPointerTests
{
    /// <summary>
    /// Answers MeasureText itself, so no font file is needed: one char is half the font size wide. Drawing
    /// is a no-op for the same reason -- the round-trip test renders a field with real text in it, and
    /// rasterizing it would want a font on disk to say nothing these assertions read.
    /// </summary>
    private sealed class MetricsRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout,
            TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
        {
        }
    }

    private const float FontSize = 14f;
    private const float CharW = FontSize * 0.5f;          // 7px, per MetricsRenderer
    private const int FieldX = 10;

    /// <summary>The x the paint would put boundary <paramref name="chars"/> at.</summary>
    private static float BoundaryX(int chars)
        => TextInputRenderer.TextOriginX(FieldX, FontSize, 0f) + chars * CharW;

    private static int CaretAt(TextInputState state, float pointerX)
        => TextInputRenderer.CaretIndexAt(
            new MetricsRenderer(200, 40), state, FieldX, "font.ttf", FontSize, pointerX);

    private static TextInputState Field(string text) => new() { Text = text, IsActive = true };

    // ---- CaretIndexAt: which character the pointer is over ----

    [Fact]
    public void APointerLeftOfTheTextLandsOnTheFirstBoundary()
    {
        var state = Field("hello world");

        CaretAt(state, BoundaryX(0) - 50f).ShouldBe(0);
        CaretAt(state, BoundaryX(0)).ShouldBe(0);
    }

    [Fact]
    public void APointerPastTheEndLandsAfterTheLastCharacter()
    {
        var state = Field("hello world");

        CaretAt(state, BoundaryX(state.Text.Length) + 200f).ShouldBe(state.Text.Length);
    }

    [Fact]
    public void APointerOnTheLeftHalfOfACharacterLandsBeforeIt_AndOnTheRightHalfAfterIt()
    {
        var state = Field("hello world");

        // Character 3 spans boundaries 3..4. Its left third and right third read as opposite sides, which
        // is what makes click-then-type land where the caret appeared rather than one character over.
        CaretAt(state, BoundaryX(3) + CharW * 0.2f).ShouldBe(3);
        CaretAt(state, BoundaryX(3) + CharW * 0.8f).ShouldBe(4);
    }

    [Fact]
    public void EveryBoundaryTheCaretIsDrawnAtMapsBackToItsOwnIndex()
    {
        var renderer = new MetricsRenderer(400, 40);
        var state = Field("hello world");

        for (var i = 0; i <= state.Text.Length; i++)
        {
            state.CursorPos = i;
            var caret = TextInputRenderer.Render(
                renderer, state, FieldX, 0, 300, 24, "font.ttf", FontSize);

            TextInputRenderer.CaretIndexAt(
                    renderer, state, FieldX, "font.ttf", FontSize, caret.UpperLeft.X)
                .ShouldBe(i, $"the caret painted for index {i} must map back to {i}");
        }
    }

    [Fact]
    public void TheTextOriginIsTheOneTheFieldPaintsFrom_NotTheFieldEdge()
    {
        // The inset is the field's, so a pointer ON the field's left edge is still before the first
        // character rather than somewhere inside it.
        TextInputRenderer.TextOriginX(FieldX, FontSize, 0f)
            .ShouldBe(FieldX + (int)TextInputRenderer.HorizontalPadding(FontSize));

        CaretAt(Field("hello"), FieldX).ShouldBe(0);
    }

    [Fact]
    public void ALeadingMarkMovesTheTextAndTheCaretMappingTogether()
    {
        var state = Field("hello");
        var lead = TextInputRenderer.LeadingRoom(FontSize, hasLeadingIcon: true);

        var withMark = TextInputRenderer.CaretIndexAt(
            new MetricsRenderer(200, 40), state, FieldX, "font.ttf", FontSize,
            pointerX: BoundaryX(2) + lead, fallback: null, leadingRoom: lead);

        withMark.ShouldBe(2);
    }

    [Fact]
    public void AnEmptyFieldAndAFontlessFieldBothAnswerZeroRatherThanThrowing()
    {
        CaretAt(Field(""), 500f).ShouldBe(0);

        TextInputRenderer.CaretIndexAt(
                new MetricsRenderer(200, 40), Field("hello"), FieldX, "", FontSize, 500f)
            .ShouldBe(0);
    }

    [Fact]
    public void ACaretNeverLandsInsideASurrogatePair()
    {
        // One astral character: two chars, one glyph, and no boundary through the middle of it.
        var state = Field("\U0001F600x");

        for (var px = BoundaryX(0) - 5f; px <= BoundaryX(3) + 5f; px += 0.5f)
        {
            var index = CaretAt(state, px);
            (index == 1).ShouldBeFalse($"pointer {px} split the pair at index 1");
        }
    }

    // ---- MoveCaretTo: the mouse's counterpart to the arrows ----

    [Fact]
    public void MovingTheCaretWithoutExtendingDropsTheSelection()
    {
        var state = Field("hello world");
        state.SelectAll();

        state.MoveCaretTo(4);

        state.CursorPos.ShouldBe(4);
        state.HasSelection.ShouldBeFalse();
    }

    [Fact]
    public void ExtendingFromNoSelectionAnchorsWhereTheCaretAlreadyWas()
    {
        var state = Field("hello world");
        state.MoveCaretTo(2);

        state.MoveCaretTo(7, extend: true);

        state.SelectionStart.ShouldBe(2);
        state.SelectionEnd.ShouldBe(7);
    }

    [Fact]
    public void ExtendingAgainKeepsTheOriginalAnchor()
    {
        var state = Field("hello world");
        state.MoveCaretTo(2);
        state.MoveCaretTo(7, extend: true);

        state.MoveCaretTo(9, extend: true);

        state.SelectionStart.ShouldBe(2);
        state.SelectionEnd.ShouldBe(9);
    }

    [Fact]
    public void TheCaretIsClampedToTheText()
    {
        var state = Field("hello");

        state.MoveCaretTo(99);
        state.CursorPos.ShouldBe(5);

        state.MoveCaretTo(-4);
        state.CursorPos.ShouldBe(0);
    }

    // ---- HandlePointer: what one, two and three clicks mean ----

    private static (TextInputFocus Focus, TextInputInteraction.PointerContext Ctx, Func<int> Redraws) Harness()
    {
        var focus = new TextInputFocus();
        var redraws = 0;
        return (focus, new TextInputInteraction.PointerContext(focus, () => redraws++), () => redraws);
    }

    [Fact]
    public void OneClickFocusesTheFieldAndPlacesTheCaret()
    {
        var (focus, ctx, redraws) = Harness();
        var state = new TextInputState { Text = "hello world" };

        TextInputInteraction.HandlePointer(state, caretIndex: 4, clicks: 1, extend: false, ctx)
            .ShouldBeTrue();

        focus.Current.ShouldBeSameAs(state);
        state.CursorPos.ShouldBe(4);
        state.HasSelection.ShouldBeFalse();
        redraws().ShouldBe(1);
    }

    [Fact]
    public void FocusingOnTheClickDoesNotWipeTheCaretItJustSet()
    {
        // Focus seeds a field when given text, and it is idempotent otherwise. Ordering the two the other
        // way round would leave the caret wherever focus put it, which is the end of the field.
        var (_, ctx, _) = Harness();
        var state = new TextInputState { Text = "hello world" };

        TextInputInteraction.HandlePointer(state, caretIndex: 2, clicks: 1, extend: false, ctx);

        state.CursorPos.ShouldBe(2);
    }

    [Fact]
    public void TwoClicksSelectTheWordUnderThePointer()
    {
        var (_, ctx, _) = Harness();
        var state = new TextInputState { Text = "hello world" };

        TextInputInteraction.HandlePointer(state, caretIndex: 7, clicks: 2, extend: false, ctx);

        state.SelectionStart.ShouldBe(6);
        state.SelectionEnd.ShouldBe(11);
    }

    [Fact]
    public void ThreeClicksSelectTheWholeField_AndSoDoesAFourth()
    {
        var (_, ctx, _) = Harness();
        var state = new TextInputState { Text = "hello world" };

        TextInputInteraction.HandlePointer(state, caretIndex: 3, clicks: 3, extend: false, ctx);
        state.SelectionStart.ShouldBe(0);
        state.SelectionEnd.ShouldBe(11);

        TextInputInteraction.HandlePointer(state, caretIndex: 3, clicks: 4, extend: false, ctx);
        state.SelectionStart.ShouldBe(0);
        state.SelectionEnd.ShouldBe(11);
    }

    [Fact]
    public void AShiftClickExtendsFromTheCaretRatherThanMovingIt()
    {
        var (_, ctx, _) = Harness();
        var state = new TextInputState { Text = "hello world" };

        TextInputInteraction.HandlePointer(state, caretIndex: 2, clicks: 1, extend: false, ctx);
        TextInputInteraction.HandlePointer(state, caretIndex: 8, clicks: 1, extend: true, ctx);

        state.SelectionStart.ShouldBe(2);
        state.SelectionEnd.ShouldBe(8);
    }

    [Fact]
    public void APressIsSwallowedWhileAnInputMethodIsComposing()
    {
        var (focus, ctx, redraws) = Harness();
        var state = new TextInputState { Text = "hello world" };
        state.MoveCaretTo(3);
        state.SetComposition("こん", 2);

        TextInputInteraction.HandlePointer(state, caretIndex: 9, clicks: 2, extend: false, ctx)
            .ShouldBeTrue();

        // Nothing moved: the indices address Text, and what is on screen is Text plus a preedit.
        state.CursorPos.ShouldBe(3);
        state.HasSelection.ShouldBeFalse();
        focus.Current.ShouldBeNull();
        redraws().ShouldBe(0);
    }
}
