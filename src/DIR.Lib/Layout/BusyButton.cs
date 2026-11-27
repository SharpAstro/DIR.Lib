using System;

namespace DIR.Lib.Layout;

/// <summary>Where a <see cref="Builder.BusyButton"/>'s task is.</summary>
public enum BusyPhase
{
    /// <summary>Nothing running: the button rests and a press starts the task.</summary>
    Rest,
    /// <summary>Running: the spinner and the busy label, and a Stop segment when the task can be stopped.</summary>
    Busy,
    /// <summary>Stop was pressed and the task has not let go yet: the segment is folded away.</summary>
    Stopping,
    /// <summary>Finished: a tick and the done label in Success, with the result beside it.</summary>
    Done,
    /// <summary>Failed: the button rests again and the reason is beside it in Error.</summary>
    Failed,
}

/// <summary>
/// The state a <see cref="Builder.BusyButton"/> is drawn in. The caller owns the task and passes the state it is in on each
/// build; nothing here is a timer, so holding <see cref="Done"/> for three seconds is the caller's (see
/// <see cref="HoldsDone"/>, which takes the clock as an argument).
/// </summary>
public sealed record BusyState
{
    /// <summary>Where the task is.</summary>
    public BusyPhase Phase { get; init; }

    /// <summary>True when a running task has gone past its estimate: the estimate text turns Warn.</summary>
    public bool PastEstimate { get; init; }

    /// <summary>The result beside a done button, or the reason beside a failed one.</summary>
    public string? Text { get; init; }

    /// <summary>The button at rest.</summary>
    public static BusyState Rest { get; } = new();

    /// <summary>The task is running.</summary>
    public static BusyState Busy { get; } = new() { Phase = BusyPhase.Busy };

    /// <summary>The task is running past its estimate.</summary>
    public static BusyState Overdue { get; } = new() { Phase = BusyPhase.Busy, PastEstimate = true };

    /// <summary>Stop was pressed; the task has not let go yet.</summary>
    public static BusyState Stopping { get; } = new() { Phase = BusyPhase.Stopping };

    /// <summary>The task finished, with <paramref name="result"/> shown beside the button.</summary>
    public static BusyState Done(string? result = null) => new() { Phase = BusyPhase.Done, Text = result };

    /// <summary>The task failed for <paramref name="reason"/>, shown beside the button in Error.</summary>
    public static BusyState Failed(string reason) => new() { Phase = BusyPhase.Failed, Text = reason };

    /// <summary>How long a done button is held before it rests, in seconds.</summary>
    public const double DoneHoldSeconds = 3.0;

    /// <summary>
    /// Whether a done state reached at <paramref name="doneAt"/> is still held at <paramref name="now"/> (both on the caller's
    /// monotonic clock, in milliseconds). The caller rests the button when this turns false; there is no timer here.
    /// </summary>
    public static bool HoldsDone(long doneAt, long now) => now - doneAt < (long)(DoneHoldSeconds * 1000.0);
}

public static partial class Builder
{
    // The Stop segment's width, the busy part's default width, the spinner's size and the gaps, in design units.
    private const float BusyStopWidth = 34f;
    private const float BusyStopSquare = 9f;
    private const float BusyDefaultWidth = 140f;
    private const float BusySpinnerSize = 14f;
    private const float BusyGap = 8f;
    private const float BusyEstimateGap = 10f;
    private const float BusyEstimateScale = 12f / 14f;
    private const float BusyStopHover = 0.28f;

    /// <summary>
    /// A button for a long-running task, from the theme's roles alone. At rest it is a Standard button that calls
    /// <paramref name="onStart"/>. Busy, the busy part keeps the rest button's place and exact size (<paramref name="width"/> design
    /// units, the same at every state), shows a spinner (the accent arc over a <see cref="UiPalette.SeparatorStrong"/> ring) before
    /// <paramref name="busyLabel"/> in <see cref="UiPalette.DimText"/>, and takes no press. When the task can be stopped
    /// (<paramref name="onStop"/> is not null) a Stop segment grows out of its LEFT edge, joined to it by a hairline inside one
    /// rounded edge, neutral at rest and Error on hover, its press calling <paramref name="onStop"/>. Growing left means the pointer
    /// that pressed the button is still over the busy part, so a double-click cannot stop what it just started.
    /// </summary>
    /// <remarks>
    /// <para><see cref="BusyPhase.Stopping"/> folds the segment away and reads <paramref name="stoppingLabel"/>; a null
    /// <paramref name="onStop"/> never grows one. <paramref name="estimate"/> (words, or "step 4 of 9") sits to the left of the whole
    /// control in DimText, in Warn once <see cref="BusyState.PastEstimate"/>. Done shows a Success tick and edge with
    /// <paramref name="doneLabel"/> and the state's result beside it; failed is the rest button with the state's reason beside it in
    /// Error. Build it in a row that keeps the button on the right, so the segment and the text grow leftwards.</para>
    /// <para>The Stop segment's hit is <paramref name="stopHit"/>, else <paramref name="hit"/>'s action with "/stop" after it
    /// when <paramref name="hit"/> is a <see cref="HitResult.ButtonHit"/>, else the label's; so two buttons with one label and
    /// their own hits never share a Stop.</para>
    /// <para>The spinner moves with <see cref="WindowUiSettings.MotionMilliseconds"/>; the host keeps frames coming while
    /// <see cref="WindowUiSettings.IsMoving"/> is up.</para>
    /// </remarks>
    public static Node BusyButton(string label, string busyLabel, BusyState state, string? estimate, Action? onStop,
        Action onStart, UiTheme theme, string stoppingLabel = "Stopping…", string doneLabel = "Done", string? stopTooltip = null,
        float width = BusyDefaultWidth, HitResult? hit = null, HitResult? stopHit = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(busyLabel);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(onStart);
        ArgumentNullException.ThrowIfNull(theme);
        var palette = theme.Palette;
        var metrics = theme.Metrics;
        var standard = theme.Controls.Standard;
        var height = metrics.ButtonHeight;
        var fontSize = metrics.BaseFontSize;
        var radius = standard.Radius;
        var edgeColour = standard.Edge ?? palette.SeparatorStrong;

        Node Beside(string? text, RGBAColor32 ink) => text is null
            ? Spacer().WStar()
            : Text(text, fontSize * BusyEstimateScale, ink, TextAlign.Far, TextAlign.Center).WStar().HFixed(height);

        Node Row(Node left, Node control) => HStack(left, control).WithGap(BusyEstimateGap).CrossCenter().WStar().HFixed(height);

        switch (state.Phase)
        {
            case BusyPhase.Rest:
                return Row(Spacer().WStar(), ButtonCore(label, ButtonKind.Standard, onStart, theme, null, false, null, null, hit, width));

            case BusyPhase.Failed:
                return Row(Beside(state.Text, palette.Error), ButtonCore(label, ButtonKind.Standard, onStart, theme, null, false, null, null, hit, width));

            case BusyPhase.Done:
            {
                var part = HStack(Icon(IconKind.Check, fontSize, palette.Success), Text(doneLabel, fontSize, palette.Success, TextAlign.Near, TextAlign.Center))
                    .WithGap(BusyGap).CrossCenter().Pad(standard.PaddingX, 0f).WFixed(width).HFixed(height).Radius(radius);
                if (standard.RestFill is { } fill) part = part.Bg(fill);
                part = part.Border(palette.Success, standard.EdgeWidth, radius);
                return Row(Beside(state.Text, palette.BodyText), part);
            }
        }

        var stopping = state.Phase == BusyPhase.Stopping;
        var spinner = Overlay(
            Spacer().WFixed(BusySpinnerSize).HFixed(BusySpinnerSize).Border(palette.SeparatorStrong, 2f, BusySpinnerSize / 2f),
            Icon(IconKind.Spinner, BusySpinnerSize, stopping ? palette.DimText : palette.Accent)).WFixed(BusySpinnerSize).HFixed(BusySpinnerSize);
        var busyPart = HStack(spinner, Text(stopping ? stoppingLabel : busyLabel, fontSize, palette.DimText, TextAlign.Near, TextAlign.Center))
            .WithGap(BusyGap).CrossCenter().Pad(standard.PaddingX, 0f).WFixed(width).HFixed(height)
            // A press on the busy part is swallowed and starts nothing.
            .Clickable(new HitResult.ChromeHit(), null, CursorKind.Default);

        var parts = new System.Collections.Generic.List<Node>(3);
        if (onStop is not null && !stopping)
        {
            // The square itself turns Error while the pointer is over the segment (DSN-1 busy's "Hover on Stop").
            var square = Spacer().WFixed(BusyStopSquare).HFixed(BusyStopSquare).Bg(palette.BodyText).BgHoverWithHit(palette.Error).Radius(2f);
            var stopId = stopHit ?? new HitResult.ButtonHit((hit is HitResult.ButtonHit own ? own.Action : label) + "/stop");
            var stop = HStack(Spacer().WStar(), square, Spacer().WStar()).CrossCenter()
                .WFixed(BusyStopWidth).HFixed(height).Radius(radius)
                .Clickable(stopId, _ => onStop())
                .BgHover(RGBAColor32.Mix(standard.RestFill ?? palette.HeaderBg, palette.Error, BusyStopHover))
                .WithFocusRing(palette.Focus, metrics.FocusRingWidth, metrics.FocusRingGap)
                .WithTooltip(stopTooltip ?? "Stop")
                .WithCursor(CursorKind.Pointer);
            parts.Add(stop);
            parts.Add(Spacer().WFixed(standard.EdgeWidth).HFixed(height).Bg(edgeColour));
        }
        parts.Add(busyPart);

        // One rest fill under every part and one edge over them: the Stop segment and the busy part read as one control.
        var row = HStack([.. parts]).CrossCenter().HFixed(height).Radius(radius);
        if (standard.RestFill is { } rest) row = row.Bg(rest);
        var control = Overlay(row, Spacer().Stretch().Radius(radius).Border(edgeColour, standard.EdgeWidth, radius));
        return Row(Beside(estimate, state.PastEstimate ? palette.Warn : palette.DimText), control);
    }
}
