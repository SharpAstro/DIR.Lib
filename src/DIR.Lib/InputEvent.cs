namespace DIR.Lib;

/// <summary>
/// Platform-agnostic mouse button identifiers.
/// </summary>
public enum MouseButton
{
    /// <summary>
    /// No button -- a pointer that is merely moving, which is what
    /// <see cref="InputEvent.MouseMove.Button"/> reports when nothing is held.
    /// </summary>
    /// <remarks>
    /// Numbered -1 rather than taking 0, which is <see cref="Left"/> and has been since this enum was
    /// written. Renumbering would change what every already-compiled host means by a left press, and what
    /// every value already stored means, to buy nothing -- this is not a button, so a number outside the
    /// buttons is the honest one anyway.
    /// </remarks>
    None = -1,
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
    /// <param name="Modifiers">
    /// Modifiers held at RELEASE. <see cref="MouseDown"/> has carried them since it was written and this
    /// did not, so every consumer that needed the pair remembered the press's own modifiers in a field --
    /// which is the right answer for a gesture (a reader who lets go of Shift mid-drag has not changed
    /// what the drag is, so <see cref="PointerMove"/> carries the press's), and the wrong answer for a
    /// plain Shift+click, where the release is simply the other half of one event.
    /// </param>
    public sealed record MouseUp(float X, float Y, MouseButton Button = MouseButton.Left,
        InputModifier Modifiers = default) : InputEvent
    {
        /// <summary>
        /// The pre-9.2 shape, kept so an already-compiled host keeps binding.
        /// </summary>
        /// <remarks>
        /// <b>An optional parameter added to a record's primary constructor DELETES the old constructor
        /// from the assembly.</b> Source-compatible, binary-fatal: 9.1 added one to
        /// <c>HitResult.TextInputHit</c> and the published Console.Lib, which calls the two-argument form,
        /// threw <c>MissingMethodException</c> on every terminal hit test -- invisible on a dev box, where
        /// the sibling compiles from source and the package path is never taken. Console.Lib and
        /// SdlVulkan.Renderer both construct a <c>MouseUp</c> with three arguments today, so this is that
        /// constructor, kept by hand.
        /// </remarks>
        public MouseUp(float X, float Y, MouseButton Button) : this(X, Y, Button, default) { }

        /// <summary>
        /// The three-element deconstruction, for the positional patterns written against it.
        /// </summary>
        /// <remarks>
        /// A record's synthesized <c>Deconstruct</c> takes its arity from the primary constructor, so
        /// <c>MouseUp(var x, var y, _)</c> would stop compiling the moment a fourth parameter appeared --
        /// a positional pattern resolves by arity, and a trailing DEFAULT does not help it. Both arities
        /// exist, so both patterns bind.
        /// </remarks>
        public void Deconstruct(out float x, out float y, out MouseButton button)
        {
            x = X;
            y = Y;
            button = Button;
        }
    }

    /// <summary>Mouse cursor movement to pixel coordinates.</summary>
    /// <param name="Button">
    /// The button held while moving, or <see cref="MouseButton.None"/> for a bare hover. Every drag in
    /// every consumer used to answer this from a flag it set on the press and cleared on the release --
    /// three branches of one gesture in three places, which is how a divider drag reached one dispatcher
    /// and not the other. A host that does not track the held button leaves it <c>None</c>, which is the
    /// truthful answer for a move it cannot describe.
    /// </param>
    public sealed record MouseMove(float X, float Y, MouseButton Button = MouseButton.None) : InputEvent
    {
        /// <summary>The pre-9.2 shape, kept so an already-compiled host keeps binding -- see
        /// <see cref="MouseUp(float,float,MouseButton)"/> for why a defaulted parameter is not enough.
        /// Console.Lib and SdlVulkan.Renderer both construct a <c>MouseMove</c> with two arguments.</summary>
        public MouseMove(float X, float Y) : this(X, Y, MouseButton.None) { }

        /// <summary>The two-element deconstruction, for the <c>MouseMove(var x, var y)</c> patterns
        /// written against it -- a positional pattern resolves by arity, so both arities must exist.</summary>
        public void Deconstruct(out float x, out float y)
        {
            x = X;
            y = Y;
        }
    }

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
