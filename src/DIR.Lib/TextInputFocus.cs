using System;
using System.Collections.Generic;

namespace DIR.Lib;

/// <summary>
/// Owns which field has the keyboard, and is the only thing allowed to change it.
///
/// <para>
/// <b>Focus is global, and it has to be.</b> There is one keyboard, so something must name the one field
/// receiving it -- WinForms has exactly this singleton in <c>Form.ActiveControl</c>. A consumer holding an
/// "active input" pointer is therefore not wrong for being global, and this type does not remove the
/// singleton. It removes the thing that actually was wrong.
/// </para>
///
/// <para>
/// <b>What was wrong is that the pointer and its platform side effects are separable.</b> Activating a
/// field also has to start the platform's text input (SDL <c>StartTextInput</c>, an IME, a phone's on-screen
/// keyboard), and where that lives in one handler while the pointer is a plain settable property, any code
/// that assigns the property directly desynchronises the app from the platform: the field stops taking input
/// while the keyboard stays up. That is not hypothetical. It shipped, and the shape of it is the lesson --
/// a cancel path deactivated its fields by hand FIRST, which (the signal bus being deferred, and the handler
/// being gated on the field still being active) turned the correct call into a no-op, so the direct
/// assignment looked <i>necessary</i>.
/// </para>
///
/// <para>
/// So the transition is expressible exactly one way. A host binds <see cref="FocusChanged"/> once and no
/// other code knows the platform calls exist; the class of bug above stops being reachable rather than being
/// fixed once, which is the only durable answer to it.
/// </para>
/// </summary>
public sealed class TextInputFocus
{
    private TextInputState? _current;

    /// <summary>The field with the keyboard, or null when none has it.</summary>
    public TextInputState? Current => _current;

    /// <summary>
    /// Raised on every REAL transition, as <c>(previous, next)</c> -- either may be null. Bind the platform's
    /// text-input lifecycle here, once: <c>next is null</c> means stop, otherwise start.
    /// <para>
    /// Not raised when focus does not move (see <see cref="Focus"/>), so a handler is free to do work that
    /// would be wasteful or wrong per frame -- a redraw request, an IME reset, showing a soft keyboard.
    /// </para>
    /// </summary>
    public event Action<TextInputState?, TextInputState?>? FocusChanged;

    /// <summary>
    /// Gives <paramref name="input"/> the keyboard, blurring whatever had it.
    /// <para>
    /// Idempotent: focusing the field that already has focus does nothing and raises nothing. That matters
    /// because a UI that asks for focus declaratively asks on EVERY frame, and a naive implementation would
    /// re-activate the field each time -- resetting the caret under the user's fingers and, with
    /// <paramref name="initialText"/>, discarding what they had typed.
    /// </para>
    /// </summary>
    /// <param name="input">The field to focus.</param>
    /// <param name="initialText">Seed text, selecting the whole field's worth of value the way opening an
    /// editor on an existing value should; null leaves the field's current text alone.</param>
    public void Focus(TextInputState input, string? initialText = null)
    {
        if (ReferenceEquals(_current, input))
        {
            return;
        }

        var previous = _current;
        previous?.Deactivate();

        _current = input;
        input.Activate(initialText);

        FocusChanged?.Invoke(previous, input);
    }

    /// <summary>
    /// Takes the keyboard away from whichever field has it.
    /// <para>
    /// Gated on THIS type's own record of focus, deliberately, and not on the field's
    /// <see cref="TextInputState.IsActive"/> flag. Those are the same fact stored twice, and a blur that
    /// consults the copy cannot recover when the copy is stale -- which is exactly the state the hand-cleared
    /// field left behind. Here the owner is the single truth, so a blur always completes.
    /// </para>
    /// </summary>
    public void Blur()
    {
        if (_current is not { } previous)
        {
            return;
        }

        previous.Deactivate();
        _current = null;

        FocusChanged?.Invoke(previous, null);
    }

    /// <summary>
    /// Blurs only if <paramref name="input"/> is the field that currently has focus.
    /// <para>
    /// For the caller that wants to close ITS editor without stealing the keyboard from someone else's --
    /// a panel tearing down, a row leaving a list. A bare <see cref="Blur"/> there defocuses whatever
    /// happens to be focused, which is a bug that only shows up once a second field exists.
    /// </para>
    /// </summary>
    public void BlurIfFocused(TextInputState input)
    {
        if (ReferenceEquals(_current, input))
        {
            Blur();
        }
    }

    /// <summary>
    /// Blurs the focused field if it is not among the ones actually drawn, and reports whether it did.
    /// <para>
    /// A field that stops being painted keeps focus, so typing goes on editing a box nobody can see: scroll a
    /// focused field out of a culled list, switch tabs, close the panel it lives on. WinForms blurs a control
    /// removed from <c>Controls</c>; a per-frame tree has no removal event, so the equivalent is asking each
    /// frame whether the focused field is still on screen.
    /// </para>
    /// <para>
    /// <b>The caller supplies what was painted, and that is the whole design.</b> The tempting version reads
    /// the fields off the active tab, which is wrong for any field that is not on it -- one on the chrome, in
    /// an overlay, in a modal above the tab -- and would blur it every single frame. Only the host knows what
    /// its frame is composed of (the same knowledge that decides paint order), so only the host can answer
    /// this without keeping a second, divergent model of its own composition.
    /// </para>
    /// <para>
    /// Call it AFTER painting, with everything painted. Calling it before, or with one surface's fields when
    /// the frame draws several, blurs a field that is on screen -- which looks exactly like the bug it fixes.
    /// </para>
    /// </summary>
    /// <param name="paintedThisFrame">Every field drawn this frame, from every surface that drew one.</param>
    /// <returns>true if a field lost focus, which a caller may want to treat as a redraw trigger.</returns>
    public bool BlurIfUnpainted(IReadOnlyCollection<TextInputState> paintedThisFrame)
    {
        if (_current is not { } current || paintedThisFrame.Contains(current))
        {
            return false;
        }

        Blur();
        return true;
    }
}
