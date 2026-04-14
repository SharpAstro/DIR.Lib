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
    /// X/Y are the midpoint between fingers in pixel coordinates.</summary>
    public sealed record Pinch(float Scale, float X, float Y) : InputEvent;

    /// <summary>Touch pinch gesture ended (fingers lifted).</summary>
    public sealed record PinchEnd() : InputEvent;
}
