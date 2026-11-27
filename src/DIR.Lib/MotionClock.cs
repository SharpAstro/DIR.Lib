using System;

namespace DIR.Lib;

/// <summary>
/// The motion clock, on the same pattern as <see cref="CaretBlink"/>: a host takes one reading per frame, stores it
/// once per window in <see cref="WindowUiSettings.MotionMilliseconds"/>, and keeps frames coming only while
/// <see cref="WindowUiSettings.IsMoving"/> says something it painted is moving. There is no timer and no thread here,
/// and a test sets the time itself or hands in a fake <see cref="TimeProvider"/>.
/// </summary>
public static class MotionClock
{
    /// <summary>One turn of a <see cref="Layout.IconKind.Spinner"/>, in milliseconds.</summary>
    public const int SpinnerTurnMilliseconds = 1000;

    /// <summary>One turn of a spinner under reduced motion: slower, but still showing that something is going on.</summary>
    public const int SpinnerReducedTurnMilliseconds = 4000;

    /// <summary>How long a switch's knob takes to slide from one side to the other, in milliseconds.</summary>
    public const int KnobSlideMilliseconds = 150;

    /// <summary>The motion time at <paramref name="clock"/>'s monotonic timestamp, in milliseconds.</summary>
    public static long MillisecondsAt(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return MillisecondsAt(clock.GetTimestamp(), clock.TimestampFrequency);
    }

    /// <summary>The motion time at a raw monotonic <paramref name="timestamp"/> ticking at <paramref name="ticksPerSecond"/>.</summary>
    public static long MillisecondsAt(long timestamp, long ticksPerSecond)
        => (long)(timestamp * (1000.0 / ticksPerSecond));

    /// <summary>
    /// How far round a spinner's turn is at <paramref name="milliseconds"/>, from 0 up to but not including 1.
    /// </summary>
    public static float SpinnerTurn(long milliseconds, bool reducedMotion)
    {
        var period = reducedMotion ? SpinnerReducedTurnMilliseconds : SpinnerTurnMilliseconds;
        return (float)(((milliseconds % period) + period) % period) / period;
    }
}
