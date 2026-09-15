using System;

namespace DIR.Lib;

/// <summary>
/// State for a single draggable value in a range: caller-owned and mutable, the same precedent
/// <see cref="TextInputState"/> sets for a field. A tree is rebuilt every frame, so the position a drag
/// left <see cref="Value"/> at has to live somewhere the next frame's tree still points to, rather than in
/// the leaf record itself (which is thrown away and rebuilt with everything else).
/// </summary>
/// <remarks>
/// Read and written directly by the engine's drag handling (<see cref="Layout.Content.Slider"/>): a press
/// or a move computes a new value from where the pointer landed and writes it here before calling
/// <see cref="OnChanged"/>, exactly as a text field's own state is mutated by <see cref="TextInputState.HandleKey"/>
/// rather than by the consumer.
/// </remarks>
public sealed class SliderState
{
    /// <summary>The current position. A drag keeps this within [<see cref="Min"/>, <see cref="Max"/>], but
    /// nothing stops a caller from seeding it outside that range before the first paint.</summary>
    public float Value { get; set; }

    /// <summary>The lower bound of the range.</summary>
    public float Min { get; set; }

    /// <summary>The upper bound of the range. Default 1, so a state constructed with nothing stated is an
    /// ordinary normalised [0, 1] slider.</summary>
    public float Max { get; set; } = 1f;

    /// <summary>
    /// The quantum a drag snaps <see cref="Value"/> to, or 0 (the default) for a continuous slider. A
    /// dragged value is rounded to the nearest whole multiple of this away from <see cref="Min"/> before
    /// being clamped back into range.
    /// </summary>
    public float Step { get; set; }

    /// <summary>
    /// Whether the slider answers a press. A caller sets this false the way a disabled row is declared on
    /// a node: the leaf still paints, dimmed, and its region still registers so a press over it is
    /// swallowed rather than reaching whatever the slider was painted over, but the press moves nothing.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Called with the new value, already clamped to range and rounded to <see cref="Step"/>,
    /// whenever a press or a drag moves it.</summary>
    public Action<float>? OnChanged { get; set; }
}
