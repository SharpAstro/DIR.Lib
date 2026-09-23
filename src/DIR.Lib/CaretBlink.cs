using System;

namespace DIR.Lib;

/// <summary>
/// The text caret's blink, on the clock. A host takes one <see cref="PhaseAt(TimeProvider)"/> per frame,
/// hands it to its widgets (<see cref="PixelWidgetBase{TSurface}.CaretPhase"/>), and asks for a frame
/// only when the phase differs from the one it last painted -- two frames a second while a field has the
/// keyboard, rather than every frame.
/// </summary>
/// <remarks>
/// The blink used to be counted in FRAMES (on for 30, off for 30). That made a focused field a reason to
/// render continuously, since a frame nobody drew could not advance the count, and it made the blink rate
/// the frame rate's: with a swapchain that never waits for vblank, a GUI with the search box open drew
/// flat out and blinked several times a second. On the clock, the blink is the same at any frame rate and
/// costs exactly the frames that show it.
/// </remarks>
public static class CaretBlink
{
    /// <summary>
    /// How long the caret stays in one state, on or off. 530 ms is the Windows default caret blink time
    /// (GetCaretBlinkTime), which is also what the other desktop toolkits settled on.
    /// </summary>
    public const int PhaseMilliseconds = 530;

    /// <summary>The blink phase at <paramref name="milliseconds"/> on any monotonic clock.</summary>
    public static long PhaseAt(long milliseconds) => milliseconds / PhaseMilliseconds;

    /// <summary>The blink phase now, on <paramref name="clock"/>'s monotonic timestamp.</summary>
    public static long PhaseAt(TimeProvider clock) => PhaseAt(clock.GetTimestamp(), clock.TimestampFrequency);

    /// <summary>
    /// The blink phase at a raw monotonic <paramref name="timestamp"/> ticking at
    /// <paramref name="ticksPerSecond"/>, for a host whose clock is not a <see cref="TimeProvider"/>.
    /// </summary>
    public static long PhaseAt(long timestamp, long ticksPerSecond)
        => PhaseAt((long)(timestamp * (1000.0 / ticksPerSecond)));

    /// <summary>Whether the caret is drawn in <paramref name="phase"/>: on in even phases, off in odd.</summary>
    public static bool IsVisible(long phase) => (phase & 1) == 0;
}
