using System;
using System.Collections.Generic;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="TextInputFocus"/> -- the owner that makes "which field has the keyboard" answerable in exactly
/// one way.
/// <para>
/// The invariant every test here serves: <b>no path may change the focused field without
/// <see cref="TextInputFocus.FocusChanged"/> firing</b>. A host binds the platform's text-input lifecycle to
/// that event and nothing else, so an unannounced transition is a field that stops taking input while the
/// IME or on-screen keyboard stays up -- which is the bug that motivated the type, and which a settable
/// pointer could not prevent.
/// </para>
/// </summary>
public class TextInputFocusTests
{
    /// <summary>Records every announced transition, standing in for the host's Start/StopTextInput binding.</summary>
    private sealed class Log
    {
        public List<(TextInputState? From, TextInputState? To)> Transitions { get; } = [];

        public static Log Watching(TextInputFocus focus)
        {
            var log = new Log();
            focus.FocusChanged += (from, to) => log.Transitions.Add((from, to));
            return log;
        }
    }

    [Fact]
    public void FocusingAField_MakesItCurrentAndActive_AndAnnouncesTheTransition()
    {
        var focus = new TextInputFocus();
        var log = Log.Watching(focus);
        var field = new TextInputState();

        focus.Focus(field);

        focus.Current.ShouldBeSameAs(field);
        field.IsActive.ShouldBeTrue();
        log.Transitions.ShouldBe([(null, field)]);
    }

    [Fact]
    public void FocusingASecondField_BlursTheFirst_InOneAnnouncedTransition()
    {
        var focus = new TextInputFocus();
        TextInputState first = new(), second = new();
        focus.Focus(first);
        var log = Log.Watching(focus);

        focus.Focus(second);

        first.IsActive.ShouldBeFalse();
        second.IsActive.ShouldBeTrue();
        focus.Current.ShouldBeSameAs(second);
        log.Transitions.ShouldBe([(first, second)],
            "one move is one transition -- a blur then a focus would stop and restart the platform's text input");
    }

    /// <summary>
    /// A declarative UI asks for what it wants on EVERY frame, so re-focusing the focused field has to be
    /// free. A naive implementation re-activates it each time, which resets the caret under the user's
    /// fingers and, with seed text, throws away what they had typed.
    /// </summary>
    [Fact]
    public void RefocusingTheSameField_ChangesNothingAndAnnouncesNothing()
    {
        var focus = new TextInputFocus();
        var field = new TextInputState();
        focus.Focus(field, "seed");
        field.CursorPos = 2;
        var log = Log.Watching(focus);

        focus.Focus(field, "seed");

        field.Text.ShouldBe("seed");
        field.CursorPos.ShouldBe(2, "the caret must not jump back under the user");
        log.Transitions.ShouldBeEmpty();
    }

    [Fact]
    public void Blurring_ClearsCurrentAndAnnouncesTheRelease()
    {
        var focus = new TextInputFocus();
        var field = new TextInputState();
        focus.Focus(field);
        var log = Log.Watching(focus);

        focus.Blur();

        focus.Current.ShouldBeNull();
        field.IsActive.ShouldBeFalse();
        log.Transitions.ShouldBe([(field, null)]);
    }

    [Fact]
    public void BlurringWithNothingFocused_AnnouncesNothing()
    {
        var focus = new TextInputFocus();
        var log = Log.Watching(focus);

        focus.Blur();

        log.Transitions.ShouldBeEmpty();
    }

    /// <summary>
    /// THE regression. A caller that clears the field's own <see cref="TextInputState.IsActive"/> flag by
    /// hand and then asks for a blur used to be answered with a no-op, because the blur was gated on that
    /// flag -- so the app kept the platform's text input running with nothing to receive it. Gating on the
    /// owner's own record means a blur always completes, whatever a caller did to the field first.
    /// </summary>
    [Fact]
    public void AHandClearedActiveFlag_DoesNotPreventTheBlurFromCompleting()
    {
        var focus = new TextInputFocus();
        var field = new TextInputState();
        focus.Focus(field);
        var log = Log.Watching(focus);

        field.Deactivate();   // what the buggy cancel path did before asking for the blur
        focus.Blur();

        focus.Current.ShouldBeNull();
        log.Transitions.ShouldBe([(field, null)],
            "the platform must still be told, or the on-screen keyboard outlives the field");
    }

    [Fact]
    public void BlurIfFocused_LeavesADifferentFieldsFocusAlone()
    {
        var focus = new TextInputFocus();
        TextInputState mine = new(), theirs = new();
        focus.Focus(theirs);

        focus.BlurIfFocused(mine);

        focus.Current.ShouldBeSameAs(theirs, "closing my editor must not take the keyboard off someone else's");
        theirs.IsActive.ShouldBeTrue();
    }

    // ---- Focus must not survive the field leaving the screen ----

    [Fact]
    public void AFieldStillOnScreen_KeepsFocus()
    {
        var focus = new TextInputFocus();
        var field = new TextInputState();
        focus.Focus(field);

        focus.BlurIfUnpainted(new[] { field }).ShouldBeFalse();
        focus.Current.ShouldBeSameAs(field);
    }

    /// <summary>
    /// Scrolled out of a culled list, tab switched, panel closed: the field is gone but the keyboard still
    /// points at it, so typing edits a box nobody can see.
    /// </summary>
    [Fact]
    public void AFieldThatStoppedBeingPainted_LosesFocus()
    {
        var focus = new TextInputFocus();
        var offScreen = new TextInputState();
        var elsewhere = new TextInputState();
        focus.Focus(offScreen);
        var log = Log.Watching(focus);

        focus.BlurIfUnpainted(new[] { elsewhere }).ShouldBeTrue();

        focus.Current.ShouldBeNull();
        log.Transitions.ShouldBe([(offScreen, null)]);
    }

    [Fact]
    public void WithNothingFocused_TheUnpaintedCheckIsANoOp()
    {
        var focus = new TextInputFocus();
        var log = Log.Watching(focus);

        focus.BlurIfUnpainted(Array.Empty<TextInputState>()).ShouldBeFalse();

        log.Transitions.ShouldBeEmpty();
    }
}
