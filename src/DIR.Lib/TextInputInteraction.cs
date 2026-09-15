using System;

namespace DIR.Lib;

/// <summary>
/// Host-agnostic text-input key/text machinery: the per-keystroke decision logic, which is identical on
/// every surface, split from the host's own event plumbing, which is not.
///
/// <para>
/// It exists because that logic first lived inside ONE host's event handler, which a second host never
/// routed through -- so clicking and typing into a text field simply did nothing in the browser, with no
/// error anywhere to say why. A shared control whose key handling lives in one host's loop is a control only
/// that host has.
/// </para>
///
/// <para>
/// <b>Focus bookkeeping is <see cref="TextInputFocus"/>'s</b>, reached through
/// <see cref="KeyContext.Focus"/>. It used to be a pair of host callbacks here, on the reasoning that
/// activation has platform side effects and so must stay host-owned -- true about the side effects, and the
/// wrong conclusion: the side effect belongs on one event the host binds once, not on a callback every
/// caller has to be handed and remember to use. See <see cref="TextInputFocus"/> for the bug that shape
/// produced.
/// </para>
/// </summary>
public static class TextInputInteraction
{
    /// <summary>
    /// Host services plus the optional per-app state <see cref="HandleKey"/> needs.
    /// </summary>
    /// <param name="Tracker">Tracks the async commit so a failing <see cref="TextInputState.OnCommit"/> is
    /// reported rather than swallowed by an unobserved task.</param>
    /// <param name="Focus">Who has the keyboard; the one way to move it.</param>
    /// <param name="RequestRedraw">Marks the surface dirty after a keystroke changed something.</param>
    /// <param name="ActiveSearch">The search interaction whose input is currently active, enabling Up/Down
    /// navigation of its results; null when the active field is not a search box.</param>
    /// <param name="TabFields">
    /// The fields Tab cycles through, in the order they were painted -- which is the visual order, so it
    /// needs no maintaining. A pixel host answers <c>() =&gt; tab.GetRegisteredTextInputs()</c>; a cell host
    /// answers from its arranged tree. Null disables cycling.
    /// <para>
    /// A callback rather than a list because it is consulted only on Tab, and a list would be built on every
    /// keystroke to serve the one key in a hundred that uses it. It replaced an <c>IPixelWidget</c>, which
    /// was the one thing keeping this "host-agnostic" class from working on a terminal.
    /// </para>
    /// </param>
    /// <param name="GetClipboardText">Platform paste, or null where there is no clipboard.</param>
    /// <param name="SetClipboardText">Platform copy, or null where there is no clipboard.</param>
    public readonly record struct KeyContext(
        BackgroundTaskTracker Tracker,
        TextInputFocus Focus,
        Action RequestRedraw,
        SearchInteraction? ActiveSearch = null,
        Func<IReadOnlyList<TextInputState>>? TabFields = null,
        Func<string?>? GetClipboardText = null,
        Action<string>? SetClipboardText = null);

    /// <summary>
    /// Inserts typed text into the active field (the TextInput-event path on desktop, the printable-keydown
    /// path on web) and fires <see cref="TextInputState.OnTextChanged"/>.
    /// </summary>
    public static bool HandleText(TextInputState activeInput, string text)
    {
        activeInput.InsertText(text);
        activeInput.OnTextChanged?.Invoke(activeInput.Text);
        return true;
    }

    /// <summary>
    /// Routes a key press to the focused text field: result navigation, Tab cycling,
    /// <see cref="TextInputState.OnKeyOverride"/>, clipboard, then the field's own key handling with
    /// commit/cancel dispatch. Returns false when no field is focused, so a caller can simply offer every
    /// key and let this decide.
    /// <para>
    /// Swallows every key while a field IS focused (returns true). That is deliberate and worth stating,
    /// because it is what makes a field behave like a field: while you are typing into one, a letter is a
    /// letter and not the application shortcut that letter is bound to.
    /// </para>
    /// <para>
    /// The field comes from <see cref="KeyContext.Focus"/> rather than from a parameter beside it. Two ways
    /// to name the focused field is one way too many: a caller passing an input the owner does not consider
    /// focused would move focus off a DIFFERENT field on the next Tab, and nothing would report it.
    /// </para>
    /// </summary>
    public static bool HandleKey(InputKey key, InputModifier modifiers, in KeyContext ctx)
    {
        if (ctx.Focus.Current is not { } activeInput)
        {
            return false;
        }

        // While an input method is composing, the keyboard belongs to IT. Enter picks a candidate, Escape
        // abandons the composition, Backspace edits the preedit -- all of which the IME handles and reports
        // back as a composition update. Acting on them here as well would commit or cancel the FIELD on a
        // keystroke the user aimed at the candidate list. Platforms differ on whether they even deliver
        // these while composing, so the guard is what makes the behaviour the same everywhere rather than a
        // property of the host. Swallowed, not ignored: they are consumed input, and composition ends on its
        // own when the IME clears the preedit, so this cannot wedge the field.
        if (activeInput.IsComposing)
        {
            return true;
        }

        // Result-list navigation while a search box is the active field. The Up/Down protocol lives ONCE in
        // SearchInteraction.HandleNavKey -- arrows are not TextInputKeys, so they cannot ride OnKeyOverride,
        // and this method swallows all keys (see the final return), so the nav has to happen here before the
        // key is consumed.
        if (ctx.ActiveSearch is { } search && activeInput == search.Input && search.HandleNavKey(key))
        {
            ctx.RequestRedraw();
            return true;
        }

        var textKey = key.ToTextInputKey(modifiers);

        // Tab cycling through the fields the surface painted.
        if (key == InputKey.Tab)
        {
            var shift = (modifiers & InputModifier.Shift) != 0;
            var inputs = ctx.TabFields?.Invoke();
            if (inputs is { Count: > 1 })
            {
                var idx = IndexOf(inputs, activeInput);
                if (idx >= 0)
                {
                    ctx.Focus.Focus(shift
                        ? inputs[(idx - 1 + inputs.Count) % inputs.Count]
                        : inputs[(idx + 1) % inputs.Count]);
                    ctx.RequestRedraw();
                    return true;
                }
            }
        }

        // Let the field's own override have it first (autocomplete commit, and so on).
        if (textKey.HasValue && activeInput.OnKeyOverride?.Invoke(textKey.Value) == true)
        {
            ctx.RequestRedraw();
            return true;
        }

        if (textKey == TextInputKey.Paste)
        {
            var clipboardText = ctx.GetClipboardText?.Invoke();
            if (!string.IsNullOrEmpty(clipboardText))
            {
                activeInput.InsertText(clipboardText);
                activeInput.OnTextChanged?.Invoke(activeInput.Text);
            }
            ctx.RequestRedraw();
            return true;
        }

        if (textKey == TextInputKey.Copy)
        {
            if (activeInput.HasSelection)
            {
                ctx.SetClipboardText?.Invoke(
                    activeInput.Text[activeInput.SelectionStart..activeInput.SelectionEnd]);
            }
            return true;
        }

        // Cut is Copy and then Delete, in that order and in one place. Spelling it as the two halves rather
        // than as its own state operation is what keeps a cut and a copy putting IDENTICAL text on the
        // clipboard: there is one expression for "the selected text", and the delete goes through the
        // field's own Delete case, which already knows that a selection is what Delete removes.
        if (textKey == TextInputKey.Cut)
        {
            if (activeInput.HasSelection)
            {
                ctx.SetClipboardText?.Invoke(
                    activeInput.Text[activeInput.SelectionStart..activeInput.SelectionEnd]);
                activeInput.HandleKey(TextInputKey.Delete);
                activeInput.OnTextChanged?.Invoke(activeInput.Text);
                ctx.RequestRedraw();
            }
            return true;
        }

        // Shift rides beside the key rather than inside it: Shift+Ctrl+Left is Ctrl+Left with the selection
        // following, not a different motion. See TextInputState.HandleKey for which keys honour it.
        var extend = (modifiers & InputModifier.Shift) != 0;

        if (textKey.HasValue && activeInput.HandleKey(textKey.Value, extend))
        {
            if (textKey.Value is TextInputKey.Backspace or TextInputKey.Delete or TextInputKey.WordBackspace)
            {
                activeInput.OnTextChanged?.Invoke(activeInput.Text);
            }

            if (activeInput.IsCommitted)
            {
                if (activeInput.OnCommit is { } onCommit)
                {
                    var text = activeInput.Text;
                    ctx.Tracker.Run(() => onCommit(text), "Text input commit");
                }
                activeInput.IsCommitted = false;
            }
            else if (activeInput.IsCancelled)
            {
                activeInput.OnCancel?.Invoke();
                activeInput.IsCancelled = false;
                // Cancel releases the field. Escape means "I am done with this box", so leaving it focused
                // would swallow the next Escape too -- the one the user expects to close the panel.
                ctx.Focus.BlurIfFocused(activeInput);
            }

            ctx.RequestRedraw();
            return true;
        }

        return true;   // Swallow every key while a field is active.
    }

    /// <summary>
    /// Routes a pointer press on a field to the caret: place, extend, select a word, select the lot.
    /// Focuses <paramref name="field"/> first, so a click into an unfocused box both gives it the keyboard
    /// and lands the caret where it was aimed, in one gesture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Takes an index rather than a coordinate, which is the seam between what every surface shares and
    /// what none of them do. WHICH character a point falls on is a property of how that surface laid the
    /// text out -- glyph measurement through a fallback chain on a pixel host
    /// (<see cref="TextInputRenderer.CaretIndexAt"/>), a column subtraction on a cell one -- while what a
    /// second click MEANS is the same everywhere, and is the half that was missing on every surface at once.
    /// </para>
    /// <para>
    /// A press while an input method is composing is swallowed without moving anything, for the reason
    /// <see cref="HandleKey"/> swallows keys: those characters belong to the IME until it commits them, and
    /// the field's own indices do not address what is on screen.
    /// </para>
    /// </remarks>
    /// <param name="clicks">
    /// How many clicks this press is part of, as the platform counts them: 1 places the caret, 2 selects the
    /// word under it, 3 or more the whole field. The run continues past three rather than cycling, so a
    /// fourth click on a field still reads as "all of it" instead of silently collapsing the selection.
    /// </param>
    /// <param name="extend">Shift was held, or this is the continuation of a drag.</param>
    /// <returns>True when the press was consumed, so a caller can offer every press and let this decide.</returns>
    public static bool HandlePointer(
        TextInputState field, int caretIndex, int clicks, bool extend, in PointerContext ctx)
    {
        if (field.IsComposing)
        {
            return true;
        }

        // Before the caret moves, or the focus change would seed the field and overwrite what we just set.
        ctx.Focus.Focus(field);

        switch (clicks)
        {
            case >= 3:
                field.SelectAll();
                break;
            case 2:
                field.SelectWordAt(caretIndex);
                break;
            default:
                field.MoveCaretTo(caretIndex, extend);
                break;
        }

        ctx.RequestRedraw();
        return true;
    }

    /// <summary>Host services <see cref="HandlePointer"/> needs -- the pointer half of <see cref="KeyContext"/>.</summary>
    /// <param name="Focus">Who has the keyboard; the one way to move it.</param>
    /// <param name="RequestRedraw">Marks the surface dirty after the caret or selection moved.</param>
    public readonly record struct PointerContext(TextInputFocus Focus, Action RequestRedraw);

    /// <summary>Reference position of <paramref name="input"/>, since <c>IReadOnlyList</c> has no IndexOf.</summary>
    private static int IndexOf(IReadOnlyList<TextInputState> inputs, TextInputState input)
    {
        for (var i = 0; i < inputs.Count; i++)
        {
            if (ReferenceEquals(inputs[i], input))
            {
                return i;
            }
        }

        return -1;
    }
}
