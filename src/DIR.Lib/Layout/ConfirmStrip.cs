using System;

namespace DIR.Lib.Layout;

/// <summary>What asking is about, which picks the colour of the strip: a destroying action is <see cref="UiPalette.Error"/>, a hazard is <see cref="UiPalette.Warn"/>.</summary>
public enum Consequence
{
    /// <summary>The action destroys something (remove, abort): the strip is in Error and shows the error icon.</summary>
    Destroys,
    /// <summary>The action is a hazard (goto the unfiltered Sun): the strip is in Warn and shows the warning icon.</summary>
    Hazard,
}

/// <summary>How one choice of <see cref="Builder.ConfirmChoices"/> is drawn.</summary>
public enum ConfirmChoiceLook
{
    /// <summary>Filled in the consequence colour, with <see cref="UiPalette.InkOn"/> ink.</summary>
    Consequence,
    /// <summary>Filled in the accent: the safe way through ("Warm, then off").</summary>
    Accent,
    /// <summary>No fill, an edge and a label in the consequence colour ("Off now").</summary>
    Outlined,
}

/// <summary>One choice of a <see cref="Builder.ConfirmChoices"/> strip: its label, its look and what choosing it runs.</summary>
public readonly record struct ConfirmChoice(string Label, ConfirmChoiceLook Look, Action OnChoose);

/// <summary>
/// Whether a <see cref="Builder.ConfirmStrip"/> (or <see cref="Builder.ConfirmChoices"/>) is asking, and the keyboard's place in it.
/// Caller-held and mutable, one per action, like <see cref="PopoverState"/>: the tree is rebuilt every frame and cannot carry it.
/// </summary>
public sealed class ConfirmState
{
    /// <summary>Whether the question is showing. Moved only by <see cref="Ask"/> and <see cref="Cancel"/>.</summary>
    public bool IsAsking { get; private set; }

    /// <summary>The keyboard's place in the strip: opened on the first choice when asking begins.</summary>
    public ListCursor Cursor { get; } = new();

    /// <summary>The id of the strip's declared list; set a distinct one where two strips can ask in one frame.</summary>
    public string ListId { get; init; } = "confirm-strip";

    /// <summary>Raised when the strip goes from asking to closed, by Cancel, Escape, a press elsewhere or a choice.</summary>
    public event Action? Closed;

    /// <summary>Starts asking and puts the keyboard on the first choice. Idempotent.</summary>
    public void Ask()
    {
        if (IsAsking) return;
        IsAsking = true;
        Cursor.Open(ListId, 0);
    }

    /// <summary>Stops asking, runs nothing and gives the keyboard back. Idempotent.</summary>
    public void Cancel()
    {
        if (!IsAsking) return;
        IsAsking = false;
        Cursor.Close();
        Closed?.Invoke();
    }
}

public static partial class Builder
{
    private const float StripPad = 3f;
    private const float StripGap = 6f;
    private const float StripButtonRadius = 6f;
    private const float StripButtonPad = 10f;
    private const float StripIconSize = 14f;
    private const float StripTint = 0.18f;
    private const float ChoicesPad = 8f;
    private const float ChoicesGap = 8f;
    private const float ChoicesHeightScale = 52f / 28f;

    /// <summary>
    /// Wraps a button so its first press only opens a question in the button's own place: the consequence icon, the question,
    /// the confirming button filled in the consequence colour, then Cancel, the strip tinted and edged in that colour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>At rest</b> the result is the caller's button: <paramref name="rest"/> is handed the action that opens the
    /// question and builds the button around it (<c>open =&gt; Builder.Button("Remove", ButtonKind.Destructive, open, theme)</c>).
    /// <b>Asking</b>, the result is a <see cref="Builder.Modal(Node, Node?, Action?)"/> whose card is the strip, so the
    /// router gives it Escape and the press elsewhere (both cancel and run nothing) and gives nothing beneath it a press.
    /// The strip is as wide as its content and the question follows the rest button's right edge in a row that keeps it
    /// to the right, so Cancel is last and sits where the rest button was; the confirming button is never under the first press.
    /// </para>
    /// <para>
    /// <b>The second press of a double-click confirms nothing.</b> That press lands on Cancel, so the confirming button, Cancel
    /// and the strip declare a double-click that does nothing: the question stays open. The confirming button holds the
    /// keyboard (a horizontal declared list on <see cref="ConfirmState.Cursor"/>, opened on it): Enter confirms once and
    /// closes the strip, Left and Right move to Cancel.
    /// </para>
    /// <para>
    /// The confirming button shows the focus ring while the strip is asking: it holds the keyboard (Enter confirms), and a
    /// ring is how every themed control shows that (<see cref="Node.FocusRing"/>). DSN-1's confirm render draws none, as a
    /// still picture with no keyboard; the ring is kept on purpose.
    /// </para>
    /// </remarks>
    /// <param name="rest">Builds the rest button around the opener it is given.</param>
    /// <param name="question">The question naming the thing, such as "Remove ZWO EAF?".</param>
    /// <param name="confirmLabel">The confirming button's label.</param>
    /// <param name="onConfirm">Runs once when the confirming button is pressed or Enter is pressed on it; the strip is closed first.</param>
    /// <param name="width">
    /// The asking strip's width in design units, or null for as wide as its content. Given a width, the question takes the
    /// room left over, so the gap opens between the question and the confirming button and the buttons keep the right end
    /// (DSN-1 confirm draws the strip across its card this way).
    /// </param>
    public static Node ConfirmStrip(ConfirmState state, UiTheme theme, Func<Action, Node> rest,
        string question, string confirmLabel, Action onConfirm, Consequence consequence = Consequence.Destroys, float? width = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(confirmLabel);
        ArgumentNullException.ThrowIfNull(onConfirm);
        if (!state.IsAsking) return rest(state.Ask);

        var palette = theme.Palette;
        var metrics = theme.Metrics;
        var colour = ConsequenceColour(palette, consequence);
        var fontSize = metrics.BaseFontSize;
        var innerHeight = metrics.ButtonHeight - 2f * StripPad;

        var icon = Icon(consequence == Consequence.Destroys ? IconKind.Error : IconKind.Warning, StripIconSize, colour);
        var text = Text(question, fontSize, palette.BodyText, TextAlign.Near, TextAlign.Center);
        if (width is not null) text = text.WStar();
        var confirm = StripButton(state, theme, 0, confirmLabel, palette.InkOn(colour), colour, null, innerHeight, Confirm(state, onConfirm));
        var cancel = StripButton(state, theme, 1, "Cancel", palette.DimText, null, null, innerHeight, state.Cancel);

        var strip = HStack(icon, text, confirm, cancel).WithGap(StripGap).CrossCenter().Pad(StripPad)
            .HFixed(metrics.ButtonHeight)
            .Bg(RGBAColor32.Mix(palette.PanelBg, colour, StripTint)).Border(colour, 1f, metrics.ControlRadius).Radius(metrics.ControlRadius)
            .ListOf(state.Cursor, state.ListId, horizontal: true, selectOnStep: false)
            .Clickable(new HitResult.ChromeHit(), null, CursorKind.Default)
            .DoubleClickable(static _ => { });
        if (width is { } w) return Modal(Spacer(), strip.WFixed(w), state.Cancel).WFixed(w);
        return Modal(Spacer(), strip, state.Cancel);
    }

    /// <summary>
    /// The choices form of <see cref="ConfirmStrip"/>, for a strip placed UNDER a row (the cooled camera's "Warm, then off" /
    /// "Off now" / "Cancel"): a question in the consequence colour, two or more <paramref name="choices"/>, then Cancel last, in
    /// a form that is as wide as the room it is given. While <paramref name="state"/> is not asking the result is empty and takes no room.
    /// </summary>
    /// <remarks>
    /// Open it from the row's own control with <see cref="ConfirmState.Ask"/> (the switch stays as it was until a choice is
    /// made). Escape, a press elsewhere and Cancel close it and run nothing; the first choice holds the keyboard; the
    /// second press of a double-click runs no choice. Each choice closes the strip first, then runs.
    /// </remarks>
    public static Node ConfirmChoices(ConfirmState state, UiTheme theme, string question,
        ReadOnlySpan<ConfirmChoice> choices, Consequence consequence = Consequence.Hazard)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(question);
        if (choices.Length < 2) throw new ArgumentException("A choices strip needs two or more choices.", nameof(choices));
        if (!state.IsAsking) return Spacer();

        var palette = theme.Palette;
        var metrics = theme.Metrics;
        var colour = ConsequenceColour(palette, consequence);
        var fontSize = metrics.BaseFontSize;
        var height = metrics.ButtonHeight * ChoicesHeightScale;
        var buttonHeight = metrics.ButtonHeight;

        var parts = new Node[choices.Length + 2];
        parts[0] = Text(question, fontSize, colour, TextAlign.Near, TextAlign.Center).WStar().HStar();
        for (var i = 0; i < choices.Length; i++)
        {
            var choice = choices[i];
            var (ink, fill, edge) = choice.Look switch
            {
                ConfirmChoiceLook.Accent => (palette.InkOn(palette.Accent), (RGBAColor32?)palette.Accent, (RGBAColor32?)null),
                ConfirmChoiceLook.Outlined => (colour, palette.HeaderBg, colour),
                _ => (palette.InkOn(colour), colour, (RGBAColor32?)null),
            };
            parts[i + 1] = StripButton(state, theme, i, choice.Label, ink, fill, edge, buttonHeight, Confirm(state, choice.OnChoose));
        }

        parts[^1] = StripButton(state, theme, choices.Length, "Cancel", palette.BodyText, palette.PanelBg, palette.SeparatorStrong, buttonHeight, state.Cancel);

        var strip = HStack(parts).WithGap(ChoicesGap).CrossCenter().Pad(ChoicesPad).WStar().HFixed(height)
            .Bg(palette.HeaderBg).Border(colour, 1f, metrics.ControlRadius).Radius(metrics.ControlRadius)
            .ListOf(state.Cursor, state.ListId, horizontal: true, selectOnStep: false)
            .Clickable(new HitResult.ChromeHit(), null, CursorKind.Default)
            .DoubleClickable(static _ => { });
        return Modal(Spacer(), strip, state.Cancel).WStar();
    }

    private static RGBAColor32 ConsequenceColour(UiPalette palette, Consequence consequence)
        => consequence == Consequence.Destroys ? palette.Error : palette.Warn;

    // Closes the strip BEFORE the action runs, so an action that rebuilds the tree sees it closed and runs once.
    private static Action Confirm(ConfirmState state, Action action) => () =>
    {
        state.Cancel();
        action();
    };

    private static Node StripButton(ConfirmState state, UiTheme theme, int index, string label, RGBAColor32 ink,
        RGBAColor32? fill, RGBAColor32? edge, float height, Action onPress)
    {
        var palette = theme.Palette;
        var cell = Text(label, theme.Metrics.BaseFontSize, ink, TextAlign.Center, TextAlign.Center)
            .Pad(StripButtonPad, 0f).HFixed(height).Radius(StripButtonRadius);
        if (fill is { } f) cell = cell.Bg(f);
        else cell = cell.BgHover(palette.Separator);
        if (edge is { } e) cell = cell.Border(e, 1f, StripButtonRadius);
        return cell.Clickable(new HitResult.ListItemHit(state.ListId, index), _ => onPress())
            .DoubleClickable(static _ => { })
            .WithFocusRing(palette.Focus, theme.Metrics.FocusRingWidth, theme.Metrics.FocusRingGap)
            .WithCursor(CursorKind.Pointer);
    }
}
