namespace DIR.Lib;

/// <summary>
/// Platform-agnostic mouse button identifiers.
/// </summary>
public enum MouseButton
{
    Left = 0,
    Middle = 1,
    Right = 2,
}

/// <summary>
/// Input device behind a <see cref="InputEvent.Pinch"/>. The host loop classifies this from the
/// platform touch-device type so consumers can anchor zoom sensibly: a touchscreen pinch carries a
/// real on-screen finger midpoint, while a touchpad pinch's raw touch coordinates are touchpad-relative
/// (meaningless on screen), so the host reports the mouse cursor instead and tags it <see cref="Touchpad"/>.
/// </summary>
public enum PinchSource
{
    /// <summary>Indirect touch device (laptop trackpad). X/Y carry the mouse cursor position.</summary>
    Touchpad = 0,
    /// <summary>Direct touch device (touchscreen). X/Y carry the real finger midpoint on screen.</summary>
    Touchscreen = 1,
}

/// <summary>
/// Platform-agnostic input event. Produced by host input loops (SDL, Console),
/// consumed by widgets via <see cref="IWidget.HandleInput"/>.
/// All mouse events carry pixel coordinates; modifiers are available on all
/// event types that support them (e.g. Shift+click, Ctrl+wheel).
/// </summary>
public abstract record InputEvent
{
    /// <summary>Key press event.</summary>
    public sealed record KeyDown(InputKey Key, InputModifier Modifiers = default) : InputEvent;

    /// <summary>Character input (from IME or text composition).</summary>
    public sealed record TextInput(string Text) : InputEvent;

    /// <summary>Mouse button press at pixel coordinates.</summary>
    public sealed record MouseDown(float X, float Y, MouseButton Button = MouseButton.Left,
        InputModifier Modifiers = default, int ClickCount = 1) : InputEvent;

    /// <summary>Mouse button release at pixel coordinates.</summary>
    public sealed record MouseUp(float X, float Y, MouseButton Button = MouseButton.Left) : InputEvent;

    /// <summary>Mouse cursor movement to pixel coordinates.</summary>
    public sealed record MouseMove(float X, float Y) : InputEvent;

    /// <summary>Mouse wheel scroll at pixel coordinates. Positive delta = scroll up.</summary>
    public sealed record Scroll(float Delta, float X, float Y, InputModifier Modifiers = default) : InputEvent;

    /// <summary>Touch pinch gesture. Scale is absolute from pinch start (1.0 = start, &gt;1 = spread, &lt;1 = squeeze).
    /// X/Y are the anchor point in pixel coordinates: the finger midpoint for a touchscreen, or the mouse
    /// cursor for a touchpad (see <see cref="Source"/>). Kept as an init-only property rather than a
    /// positional parameter so existing <c>(Scale, X, Y)</c> deconstructions keep compiling.</summary>
    public sealed record Pinch(float Scale, float X, float Y) : InputEvent
    {
        /// <summary>Which kind of touch device produced the pinch. Defaults to <see cref="PinchSource.Touchpad"/>.</summary>
        public PinchSource Source { get; init; } = PinchSource.Touchpad;
    }

    /// <summary>Touch pinch gesture ended (fingers lifted).</summary>
    public sealed record PinchEnd() : InputEvent;
}
