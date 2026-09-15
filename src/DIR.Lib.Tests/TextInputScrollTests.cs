using System;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A value longer than its field. <see cref="TextInputRenderer"/> drew every field from its first
/// character and let the rest run off the right-hand edge, so typing past the width of the box put the
/// caret somewhere outside it and the characters over whatever was painted next door -- which is the one
/// state a text box is in most often once it holds a path, a URL or a long name.
/// <para>
/// The three things worth pinning are: the caret stays inside the box wherever it is put; the paint and
/// <see cref="TextInputRenderer.CaretIndexAt"/> agree about which character is where, because the click
/// mapping has to undo exactly the shift the paint applied; and a value that FITS is not touched at all,
/// so nothing that rendered correctly before renders differently now.
/// </para>
/// </summary>
public class TextInputScrollTests
{
    /// <summary>Answers MeasureText itself, so no font file is needed: one char is half the font size wide.</summary>
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
    private const int FieldX = 10;
    private const int FieldY = 0;
    private const int FieldW = 100;      // about 12 characters of room at 7px each
    private const int FieldH = 24;

    /// <summary>Three times the field's worth of value, which is the case the field could not show.</summary>
    private const string LongValue = "the quick brown fox jumps over a lazy dog";

    private static TextInputState Field(string text, int caret) =>
        new() { Text = text, IsActive = true, CursorPos = caret };

    private static (RectInt Caret, MetricsRenderer Renderer) Paint(TextInputState state)
    {
        var renderer = new MetricsRenderer(400, 40);
        var caret = TextInputRenderer.Render(
            renderer, state, FieldX, FieldY, FieldW, FieldH, "font.ttf", FontSize);
        return (caret, renderer);
    }

    // ---- The caret stays in the box ----

    [Fact]
    public void AfterEnd_TheCaretIsStillInsideTheField()
    {
        var state = Field(LongValue, LongValue.Length);

        var (caret, _) = Paint(state);

        caret.UpperLeft.X.ShouldBeGreaterThanOrEqualTo(FieldX);
        caret.LowerRight.X.ShouldBeLessThanOrEqualTo(FieldX + FieldW,
            "a caret drawn past the field's right edge is a caret the user cannot see");
    }

    [Fact]
    public void AfterHome_TheCaretComesBackToTheStartOfTheField()
    {
        var state = Field(LongValue, LongValue.Length);
        Paint(state);                                     // scrolled to the end

        state.CursorPos = 0;
        var (caret, _) = Paint(state);

        caret.UpperLeft.X.ShouldBe(TextInputRenderer.TextOriginX(FieldX, FontSize, 0f),
            "Home must bring the value back with the caret, not leave the window at the far end");
        state.ScrollOffsetPx.ShouldBe(0f);
    }

    [Fact]
    public void TheCaretIsInsideTheFieldAtEveryPositionAlongALongValue()
    {
        var state = Field(LongValue, 0);

        for (var i = 0; i <= LongValue.Length; i++)
        {
            state.CursorPos = i;
            var (caret, _) = Paint(state);

            caret.UpperLeft.X.ShouldBeGreaterThanOrEqualTo(FieldX, $"caret at {i} fell off the left");
            caret.LowerRight.X.ShouldBeLessThanOrEqualTo(FieldX + FieldW, $"caret at {i} fell off the right");
        }
    }

    /// <summary>
    /// Walking a caret one character at a time is the real gesture, and it is where a scroll that only
    /// recentres on a jump would leave the caret pinned to an edge and then let it slip off.
    /// </summary>
    [Fact]
    public void WalkingTheCaretForwardsAndBackwardsKeepsItVisibleThroughout()
    {
        var state = Field(LongValue, 0);
        Paint(state);

        for (var i = 0; i <= LongValue.Length; i++)
        {
            state.CursorPos = i;
            var (caret, _) = Paint(state);
            caret.UpperLeft.X.ShouldBeGreaterThanOrEqualTo(FieldX);
            caret.LowerRight.X.ShouldBeLessThanOrEqualTo(FieldX + FieldW);
        }

        for (var i = LongValue.Length; i >= 0; i--)
        {
            state.CursorPos = i;
            var (caret, _) = Paint(state);
            caret.UpperLeft.X.ShouldBeGreaterThanOrEqualTo(FieldX);
            caret.LowerRight.X.ShouldBeLessThanOrEqualTo(FieldX + FieldW);
        }
    }

    // ---- A value that fits is untouched ----

    [Fact]
    public void AValueThatFitsIsNeverScrolled()
    {
        var state = Field("short", 5);

        var (caret, _) = Paint(state);

        state.ScrollOffsetPx.ShouldBe(0f);
        caret.UpperLeft.X.ShouldBe(TextInputRenderer.TextOriginX(FieldX, FontSize, 0f) + 5 * (int)(FontSize * 0.5f));
    }

    [Fact]
    public void AValueThatShrinksBackToFittingReleasesTheScroll()
    {
        var state = Field(LongValue, LongValue.Length);
        Paint(state);
        state.ScrollOffsetPx.ShouldBeGreaterThan(0f);

        state.Text = "short";
        state.CursorPos = 5;
        Paint(state);

        state.ScrollOffsetPx.ShouldBe(0f,
            "a field left scrolled past its own end shows blank where the deleted characters were");
    }

    [Fact]
    public void AnUnfocusedFieldShowsTheStartOfItsValue()
    {
        var state = Field(LongValue, LongValue.Length);
        Paint(state);

        state.IsActive = false;
        Paint(state);

        state.ScrollOffsetPx.ShouldBe(0f,
            "there is no caret to chase in a field nobody is editing, and the start is the readable end");
    }

    [Fact]
    public void DeactivatingReleasesTheScrollBeforeAnythingIsRepainted()
    {
        // A click that focuses another field asks CaretIndexAt before a frame has been drawn with the new
        // offset, so the release cannot wait for the next paint.
        var state = Field(LongValue, LongValue.Length);
        Paint(state);

        state.Deactivate();

        state.ScrollOffsetPx.ShouldBe(0f);
    }

    // ---- The click mapping undoes exactly what the paint did ----

    [Fact]
    public void EveryBoundaryTheCaretIsDrawnAtStillMapsBackToItsOwnIndex_EvenScrolled()
    {
        var state = Field(LongValue, 0);

        for (var i = 0; i <= LongValue.Length; i++)
        {
            state.CursorPos = i;
            var (caret, renderer) = Paint(state);

            TextInputRenderer.CaretIndexAt(
                    renderer, state, FieldX, "font.ttf", FontSize, caret.UpperLeft.X)
                .ShouldBe(i, $"the caret painted for index {i} must map back to {i} while the value is scrolled");
        }
    }

    [Fact]
    public void AClickOnTheLeftEdgeOfAScrolledFieldIsNotTheFirstCharacter()
    {
        // The whole point: the first character is off-screen, so the leftmost visible column is somewhere
        // in the middle of the value. Reading it as index 0 is what an unscrolled mapping would do.
        var state = Field(LongValue, LongValue.Length);
        var (_, renderer) = Paint(state);

        var atLeftEdge = TextInputRenderer.CaretIndexAt(
            renderer, state, FieldX, "font.ttf", FontSize,
            TextInputRenderer.TextOriginX(FieldX, FontSize, 0f) + 1f);

        atLeftEdge.ShouldBeGreaterThan(0);
    }

    // ---- The renderer leaves the clip stack as it found it ----

    [Fact]
    public void TheClipIsPoppedOnEveryPath()
    {
        var renderer = new MetricsRenderer(400, 40);

        foreach (var state in new[]
                 {
                     Field(LongValue, LongValue.Length),
                     Field(LongValue, 0),
                     Field("short", 5),
                     new TextInputState { Text = LongValue },            // inactive
                 })
        {
            TextInputRenderer.Render(renderer, state, FieldX, FieldY, FieldW, FieldH, "font.ttf", FontSize);
            renderer.ClipDepth.ShouldBe(0, "a field that leaves a clip pushed clips everything painted after it");
        }
    }
}
