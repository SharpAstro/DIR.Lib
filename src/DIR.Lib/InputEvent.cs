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
    public sealed record KeyDown(InputKey Key, InputModifier Modifiers = default) : InputEvent
    {
        /// <summary>
        /// True when this is the OS auto-repeating a key that is still held, rather than a fresh press.
        /// <para>
        /// A TOGGLE has to ignore repeats: held down, it flips at the repeat rate, which reads as the
        /// action starting and stopping several times a second rather than as one press. A STEP wants
        /// every one of them, since repeating a step is what auto-repeat is for. Only the host can tell
        /// the two events apart, which is why the fact travels on the event rather than being inferred
        /// by a consumer holding its own key-down set.
        /// </para>
        /// <para>
        /// An init-only property rather than a third positional parameter, for the reason
        /// <see cref="Pinch"/> states: consumers match this record as <c>KeyDown(var key, var mods)</c> in
        /// dozens of places, and a third element would break every one. A host with no repeat information
        /// (a synthetic press, a console loop) leaves it false, which is the truthful answer for a press
        /// it generated once.
        /// </para>
        /// </summary>
        public bool Repeat { get; init; }
    }

    /// <summary>
    /// A key being RELEASED. The other half of <see cref="KeyDown"/>, for an action that lasts exactly as
    /// long as the key is held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most bindings need only the press: a toggle, a step, a command. This exists for the ones whose
    /// meaning is "while held" rather than "on press": hold to pause a running comparison, hold to
    /// reveal an alternate reading, hold to nudge continuously. Without it a consumer can only guess a
    /// release from a timer or from the absence of a repeat, and both are wrong the moment the window
    /// loses focus mid-hold.
    /// </para>
    /// <para>
    /// It is a separate record rather than a flag on <see cref="KeyDown"/> because every existing consumer
    /// matches <c>KeyDown</c> to mean "a press happened", and a release arriving through that same type
    /// would fire all of them a second time. A host that has no release information simply never sends
    /// one, and every consumer that does not match it is unaffected.
    /// </para>
    /// </remarks>
    public sealed record KeyUp(InputKey Key, InputModifier Modifiers = default) : InputEvent;

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
