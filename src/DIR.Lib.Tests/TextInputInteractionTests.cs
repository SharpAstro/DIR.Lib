using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="TextInputInteraction"/> -- the per-keystroke routing every surface shares.
/// <para>
/// It arrives here with NO tests, which is worth saying: it spent its life inside one consumer's UI project,
/// where the machinery it needs (a widget with registered fields, a focus owner, a task tracker) was only
/// assembled by the running app. Promoting it is what makes it testable at all, and that is most of the
/// argument for promoting it -- Tab cycling and commit dispatch were previously verified by using the app.
/// </para>
/// <para>
/// The fields are declared with <see cref="Layout.Builder.TextInput"/> and painted through a real widget, so
/// these exercise the actual chain: a tree declares fields, painting registers them, and Tab walks the
/// registrations. A hand-stubbed field list would assert the routing while skipping the half that makes the
/// order right.
/// </para>
/// </summary>
public class TextInputInteractionTests
{
    private sealed class FieldWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        /// <summary>Paints one field per state, top to bottom, so paint order is the declared order.</summary>
        public void Paint(params TextInputState[] fields)
        {
            var rows = new List<Layout.Node>();
            foreach (var f in fields)
            {
                rows.Add(Layout.Builder.TextInput(f, 1f).RowH(10f));
            }

            BeginFrame();
            RenderLayout(Layout.Builder.VStack([.. rows]), new RectF32(0f, 0f, 100f, 10f * fields.Length),
                fontPath: string.Empty, dpiScale: 1f);
        }
    }

    private sealed class Harness
    {
        public TextInputFocus Focus { get; } = new();
        public BackgroundTaskTracker Tracker { get; } = new();
        public FieldWidget Widget { get; }
        public int Redraws { get; private set; }
        public string? Clipboard { get; set; }

        public Harness(params TextInputState[] fields)
        {
            Widget = new FieldWidget(new RgbaImageRenderer(100, 100));
            Widget.Paint(fields);
        }

        public TextInputInteraction.KeyContext Context => new(
            Tracker, Focus, () => Redraws++,
            TabFields: Widget.GetRegisteredTextInputs,
            GetClipboardText: () => Clipboard,
            SetClipboardText: t => Clipboard = t);

        public bool Key(InputKey key, InputModifier modifiers = InputModifier.None)
            => TextInputInteraction.HandleKey(key, modifiers, Context);
    }

    // ---- The focused field is the context's, never a parameter beside it ----

    [Fact]
    public void WithNoFieldFocused_TheKeyIsNotConsumed()
    {
        var harness = new Harness(new TextInputState());

        harness.Key(InputKey.A).ShouldBeFalse("an unfocused app must still get its own shortcuts");
    }

    /// <summary>
    /// While a field is focused every key belongs to it. That is what makes a field behave like a field:
    /// typing "s" into a name box must not also fire whatever "s" is bound to.
    /// </summary>
    [Fact]
    public void WhileAFieldIsFocused_EveryKeyIsSwallowed()
    {
        var field = new TextInputState();
        var harness = new Harness(field);
        harness.Focus.Focus(field);

        harness.Key(InputKey.S).ShouldBeTrue();
        harness.Key(InputKey.F1).ShouldBeTrue();
    }

    // ---- Tab cycling ----

    [Fact]
    public void Tab_MovesFocusToTheNextDeclaredField()
    {
        TextInputState first = new(), second = new(), third = new();
        var harness = new Harness(first, second, third);
        harness.Focus.Focus(first);

        harness.Key(InputKey.Tab);

        harness.Focus.Current.ShouldBeSameAs(second);
        first.IsActive.ShouldBeFalse();
        second.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void ShiftTab_MovesFocusBackwards_AndWrapsToTheLast()
    {
        TextInputState first = new(), second = new(), third = new();
        var harness = new Harness(first, second, third);
        harness.Focus.Focus(first);

        harness.Key(InputKey.Tab, InputModifier.Shift);

        harness.Focus.Current.ShouldBeSameAs(third);
    }

    [Fact]
    public void Tab_WrapsPastTheLastFieldToTheFirst()
    {
        TextInputState first = new(), second = new();
        var harness = new Harness(first, second);
        harness.Focus.Focus(second);

        harness.Key(InputKey.Tab);

        harness.Focus.Current.ShouldBeSameAs(first);
    }

    /// <summary>
    /// Tab order is paint order, so it is the visual order with nothing to maintain -- no per-field index to
    /// declare and to get wrong. Painting the same fields in a different order must reorder Tab with them.
    /// </summary>
    [Fact]
    public void TabOrderFollowsPaintOrder_WithNoDeclaredIndices()
    {
        TextInputState a = new(), b = new();
        var harness = new Harness(a, b);
        harness.Widget.Paint(b, a);          // repainted the other way round
        harness.Focus.Focus(b);

        harness.Key(InputKey.Tab);

        harness.Focus.Current.ShouldBeSameAs(a);
    }

    /// <summary>A lone field has nowhere to go, and must not lose focus pretending otherwise.</summary>
    [Fact]
    public void TabWithOnlyOneField_KeepsFocusWhereItIs()
    {
        var only = new TextInputState();
        var harness = new Harness(only);
        harness.Focus.Focus(only);

        harness.Key(InputKey.Tab);

        harness.Focus.Current.ShouldBeSameAs(only);
        only.IsActive.ShouldBeTrue();
    }

    // ---- Commit and cancel ----

    [Fact]
    public async Task Enter_DispatchesTheCommitWithTheFieldsText()
    {
        string? committed = null;
        var field = new TextInputState { OnCommit = t => { committed = t; return Task.CompletedTask; } };
        var harness = new Harness(field);
        harness.Focus.Focus(field);
        TextInputInteraction.HandleText(field, "42");

        harness.Key(InputKey.Enter);
        await harness.Tracker.DrainAsync();

        committed.ShouldBe("42");
        harness.Focus.Current.ShouldBeSameAs(field, "committing a value does not mean leaving the field");
    }

    /// <summary>
    /// Escape means "I am done with this box", so it releases the field. Leaving it focused would swallow
    /// the NEXT Escape too -- the one the user expects to close the panel around it.
    /// </summary>
    [Fact]
    public void Escape_RunsTheCancelCallbackAndReleasesTheField()
    {
        var cancelled = false;
        var field = new TextInputState { OnCancel = () => cancelled = true };
        var harness = new Harness(field);
        harness.Focus.Focus(field);

        harness.Key(InputKey.Escape);

        cancelled.ShouldBeTrue();
        harness.Focus.Current.ShouldBeNull();
        field.IsActive.ShouldBeFalse();
    }

    /// <summary>
    /// Cancelling my field must not take the keyboard off a field that meanwhile got focus -- the reason the
    /// release goes through <see cref="TextInputFocus.BlurIfFocused"/> rather than a bare blur.
    /// </summary>
    [Fact]
    public void EscapeOnAFieldThatNoLongerHasFocus_LeavesTheCurrentFieldAlone()
    {
        TextInputState first = new(), second = new();
        first.OnCancel = () => { };
        var harness = new Harness(first, second);
        harness.Focus.Focus(first);
        first.IsCancelled = false;

        // Focus moves away as the Escape is handled, which is what a commit handler stealing focus looks like.
        first.OnCancel = () => harness.Focus.Focus(second);
        harness.Key(InputKey.Escape);

        harness.Focus.Current.ShouldBeSameAs(second);
        second.IsActive.ShouldBeTrue();
    }

    // ---- Editing notifications ----

    [Fact]
    public void Backspace_NotifiesTheTextChanged()
    {
        var seen = new List<string>();
        var field = new TextInputState { OnTextChanged = t => seen.Add(t) };
        var harness = new Harness(field);
        harness.Focus.Focus(field);
        TextInputInteraction.HandleText(field, "abc");

        harness.Key(InputKey.Backspace);

        seen[^1].ShouldBe("ab", "a live search that misses a backspace keeps showing results for text that is gone");
    }

    // ---- Clipboard ----

    [Fact]
    public void CtrlV_InsertsTheClipboardAndNotifies()
    {
        var seen = new List<string>();
        var field = new TextInputState { OnTextChanged = t => seen.Add(t) };
        var harness = new Harness(field) { Clipboard = "pasted" };
        harness.Focus.Focus(field);

        harness.Key(InputKey.V, InputModifier.Ctrl);

        field.Text.ShouldBe("pasted");
        seen.ShouldBe(["pasted"]);
    }

    [Fact]
    public void CtrlC_CopiesOnlyTheSelection()
    {
        var field = new TextInputState { Text = "abcdef", SelectionAnchor = 1, CursorPos = 4 };
        var harness = new Harness(field);
        harness.Focus.Focus(field);

        harness.Key(InputKey.C, InputModifier.Ctrl);

        harness.Clipboard.ShouldBe("bcd");
    }

    [Fact]
    public void CtrlCWithNoSelection_LeavesTheClipboardAlone()
    {
        var field = new TextInputState { Text = "abcdef" };
        var harness = new Harness(field) { Clipboard = "previous" };
        harness.Focus.Focus(field);

        harness.Key(InputKey.C, InputModifier.Ctrl);

        harness.Clipboard.ShouldBe("previous");
    }

    // ---- Override ----

    /// <summary>
    /// A field's own override gets the key before the field's editing does, which is how an autocomplete
    /// takes Enter to commit a highlighted suggestion instead of the raw query.
    /// </summary>
    [Fact]
    public void AFieldsKeyOverride_WinsAheadOfItsOwnEditing()
    {
        var overridden = new List<TextInputKey>();
        var field = new TextInputState
        {
            Text = "abc",
            OnKeyOverride = k => { overridden.Add(k); return true; },
            OnCommit = _ => throw new InvalidOperationException("the override consumed the key"),
        };
        var harness = new Harness(field);
        harness.Focus.Focus(field);

        harness.Key(InputKey.Enter);

        overridden.ShouldBe([TextInputKey.Enter]);
    }
}
