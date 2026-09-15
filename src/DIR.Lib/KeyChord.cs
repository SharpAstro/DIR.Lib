namespace DIR.Lib;

/// <summary>
/// A key and the modifiers held with it: the whole of a keyboard binding, so a control can DECLARE one
/// (<see cref="Layout.Node.Shortcut"/>) rather than a host writing an arm for it in a switch.
/// </summary>
/// <remarks>
/// <para>
/// A binding stated on the node is matched against the PAINTED tree, which is what makes a shortcut on a
/// hidden panel inert without anyone saying so -- the same rule the keyboard claimant and
/// <c>TextInputFocus.BlurIfUnpainted</c> already follow. A host's hand-written map has no such rule: it
/// fires whatever the window is showing, so every panel that can be closed needs a guard beside its key,
/// and the guard is what goes missing.
/// </para>
/// <para>
/// <b><see cref="BeatsFocusedField"/> is a property of the chord, and that is the point.</b> The question
/// it answers -- may this binding fire while a text field has the keyboard -- otherwise gets answered per
/// key at the one site that noticed: a host special-casing its search key by NAME, with a comment saying
/// it is global, beside a hand-written modifier map that is not. Stated here, the router and a test read
/// the same fact and a new binding inherits the rule instead of restating it.
/// </para>
/// </remarks>
/// <param name="Key">The key itself. <see cref="InputKey.None"/> is a chord that can never fire, which is
/// what a <see langword="default"/> value should mean.</param>
/// <param name="Modifiers">Modifiers that must be held. Matched as a whole value, not as a subset test:
/// Ctrl+Shift+F is a different binding from Ctrl+F, and a host that reports the extra bit means it.</param>
public readonly record struct KeyChord(InputKey Key, InputModifier Modifiers = InputModifier.None)
{
    /// <summary>
    /// Whether this binding reaches its node even while a text field has the keyboard.
    /// </summary>
    /// <remarks>
    /// True for anything carrying Ctrl or Alt, and for the function keys; false for a bare letter or
    /// Shift+letter. That is what lets Ctrl+F reach a search box while you are typing in another field,
    /// and what keeps a bare F a letter rather than a rating filter the moment a field is focused. Note
    /// <see cref="InputKey"/> stops at <see cref="InputKey.F12"/>, so the function-key half is F1..F12 --
    /// there is nothing above it to include.
    /// </remarks>
    public bool BeatsFocusedField
        => (Modifiers & (InputModifier.Ctrl | InputModifier.Alt)) != 0
            || Key is >= InputKey.F1 and <= InputKey.F12;
}
