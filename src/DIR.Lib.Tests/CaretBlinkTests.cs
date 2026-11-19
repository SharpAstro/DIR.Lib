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

    private sealed class FakeClock(long milliseconds) : TimeProvider
    {
        public long Milliseconds { get; set; } = milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }

    private sealed class FieldWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void ShareWith(FieldWidget child) => ShareUiContext(child);

        public void Paint(TextInputState state)
        {
            BeginFrame();
            RenderLayout(Layout.Builder.TextInput(state, 14f).Stretch(), new RectF32(0f, 0f, 120f, 30f),
                fontPath: "font.ttf", scale: DesignScale.One);
        }
    }

    [Fact]
    public void TheWindowsPhaseDecidesTheCaret_ForEveryWidgetSharingItsUi()
    {
        var clock = new FakeClock(0);
        var state = new TextInputState { Text = "abc", IsActive = true, CursorPos = 1 };
        var parentSurface = new MetricsRenderer(120, 30);
        var parent = new FieldWidget(parentSurface);
        var childSurface = new MetricsRenderer(120, 30);
        var child = new FieldWidget(childSurface);
        parent.ShareWith(child);

        // Only the window's phase is set, never a widget's.
        parent.Ui.CaretPhase = CaretBlink.PhaseAt(clock);
        parent.Paint(state);
        var caret = parent.Ui.CaretRect;
        var x = caret.UpperLeft.X;
        var y = (caret.UpperLeft.Y + caret.LowerRight.Y) / 2;
        Pixel(parentSurface, x, y).ShouldBe(TextInputRenderer.Colors.Cursor, "an ON phase draws the caret");
        child.Paint(state);
        Pixel(childSurface, x, y).ShouldBe(TextInputRenderer.Colors.Cursor, "a widget sharing the Ui sees the same phase");

        clock.Milliseconds = CaretBlink.PhaseMilliseconds;
        parent.Ui.CaretPhase = CaretBlink.PhaseAt(clock);
        var parentOff = new MetricsRenderer(120, 30);
        var parent2 = new FieldWidget(parentOff);
        var childOff = new MetricsRenderer(120, 30);
        var child2 = new FieldWidget(childOff);
        parent2.ShareWith(child2);
        parent2.Ui.CaretPhase = CaretBlink.PhaseAt(clock);
        parent2.Paint(state);
        child2.Paint(state);
        Pixel(parentOff, x, y).ShouldNotBe(TextInputRenderer.Colors.Cursor, "the next phase draws no caret");
        Pixel(childOff, x, y).ShouldNotBe(TextInputRenderer.Colors.Cursor, "nor in the sharing widget");
        child2.CaretPhase.ShouldBe(1L, "the widget property forwards to the window");
    }

    private static RGBAColor32 Pixel(RgbaImageRenderer r, int x, int y)
    {
        var i = ((y * r.Surface.Width) + x) * 4;
        var p = r.Surface.Pixels;
        return new RGBAColor32(p[i], p[i + 1], p[i + 2], p[i + 3]);
    }
}
