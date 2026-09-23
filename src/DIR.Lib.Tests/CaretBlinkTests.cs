using System;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The caret blinks on the clock, not by frame count. What is pinned: the phase is a function of time
/// alone, so any frame rate blinks at the same speed; the two ways a host reads it agree; and the phase
/// is what decides whether the caret's pixels are painted.
/// </summary>
public class CaretBlinkTests
{
    /// <summary>Answers MeasureText itself, so no font file is needed.</summary>
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

    [Theory]
    [InlineData(0L, 0L, true)]
    [InlineData(529L, 0L, true)]
    [InlineData(530L, 1L, false)]
    [InlineData(1059L, 1L, false)]
    [InlineData(1060L, 2L, true)]
    public void ThePhaseAndVisibilityFollowTheClockInHalfBlinks(long milliseconds, long phase, bool visible)
    {
        CaretBlink.PhaseAt(milliseconds).ShouldBe(phase);
        CaretBlink.IsVisible(CaretBlink.PhaseAt(milliseconds)).ShouldBe(visible);
    }

    // A host whose clock is not a TimeProvider reads the raw timestamp overload; both must land in the
    // same phase for the same instant, or a caret painted from one and a redraw asked from the other
    // disagree about when it flips.
    [Theory]
    [InlineData(10_000_000L)]   // 10 MHz, a Windows QPC
    [InlineData(1_000_000_000L)] // 1 GHz, a Linux monotonic clock in ns
    public void TheRawTimestampOverloadAgreesWithMilliseconds(long ticksPerSecond)
    {
        foreach (var ms in new long[] { 0, 529, 530, 1_000, 86_400_000 })
        {
            CaretBlink.PhaseAt(ms * (ticksPerSecond / 1000), ticksPerSecond).ShouldBe(CaretBlink.PhaseAt(ms), $"{ms} ms");
        }
    }

    [Fact]
    public void TheCaretIsPaintedOnlyInAnOnPhase()
    {
        var state = new TextInputState { Text = "abc", IsActive = true, CursorPos = 1 };

        var on = new MetricsRenderer(120, 30);
        var caret = TextInputRenderer.Render(on, state, 0, 0, 100, 24, "font.ttf", 14f,
            caretVisible: CaretBlink.IsVisible(0));
        var off = new MetricsRenderer(120, 30);
        TextInputRenderer.Render(off, state, 0, 0, 100, 24, "font.ttf", 14f,
            caretVisible: CaretBlink.IsVisible(1));

        var x = caret.UpperLeft.X;
        var y = (caret.UpperLeft.Y + caret.LowerRight.Y) / 2;
        Pixel(on, x, y).ShouldBe(TextInputRenderer.Colors.Cursor, "an ON phase paints the caret");
        Pixel(off, x, y).ShouldNotBe(TextInputRenderer.Colors.Cursor, "an OFF phase does not");
    }

    private static RGBAColor32 Pixel(RgbaImageRenderer r, int x, int y)
    {
        var i = ((y * r.Surface.Width) + x) * 4;
        var p = r.Surface.Pixels;
        return new RGBAColor32(p[i], p[i + 1], p[i + 2], p[i + 3]);
    }
}
