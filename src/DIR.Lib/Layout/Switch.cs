using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

public static partial class Builder
{
    /// <summary>
    /// A themed on/off switch, drawn from <see cref="ControlStyles.SwitchOrDerived"/> alone: a rounded track and a round
    /// knob, built from ordinary nodes so a cell surface draws it too. On is the accent track with the knob at the end;
    /// off is the neutral control fill with the control edge ring and the knob at the start. The track is
    /// <see cref="SwitchStyle.HeightScale"/> times <see cref="UiMetrics.ButtonHeight"/> high, centred in a row of that
    /// height so it lines up with a button beside it. An optional <paramref name="label"/> sits after the track, or before it
    /// when <paramref name="labelBefore"/>, and is part of the same hit.
    /// </summary>
    /// <remarks>
    /// <paramref name="on"/> is the caller's value. A click, or Space or Enter, calls <paramref name="onChange"/> with
    /// <c>!on</c> and changes nothing itself, so the look follows the value the caller passes next. The hit identity is
    /// <c>ButtonHit(label ?? "switch")</c> unless <paramref name="hit"/> says otherwise. A <paramref name="disabledReason"/>
    /// makes the switch flat (the track neutral even when on, the knob still at the end), dims its ink, takes no press and
    /// shows the reason as its tooltip. Keyboard focus shows only where the hit is a list item the cursor is on.
    /// </remarks>
    public static Node Switch(bool on, Action<bool>? onChange, UiTheme theme, string? label = null, bool labelBefore = false,
        string? disabledReason = null, string? tooltip = null, HitResult? hit = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var style = theme.Controls.SwitchOrDerived;
        var enabled = disabledReason is null;
        var lit = on && enabled;
        var height = theme.Metrics.ButtonHeight * style.HeightScale;
        var width = height * style.WidthScale;
        var knobSize = height - 2f * style.KnobInset;
        var knobInk = lit ? style.OnKnob : style.OffKnob;
        var labelInk = style.LabelInk;
        if (!enabled)
        {
            knobInk = style.DisabledInk ?? RGBAColor32.Mix(knobInk, theme.Palette.PanelBg, DisabledInkMix);
            labelInk = knobInk;
        }

        var knob = Box(knobSize, knobSize).Bg(knobInk).Radius(knobSize / 2f);
        // The knob sits at the end when on: a stretching spacer before it pushes it there.
        var track = (on ? HStack(Spacer().Stretch(), knob) : HStack(knob, Spacer().Stretch()))
            .CrossCenter().Pad(style.KnobInset, style.KnobInset)
            .WFixed(width).HFixed(height).Radius(height / 2f)
            .Bg(lit ? style.OnFill : style.OffFill);
        if (!lit && style.OffEdge is { } edge) track = track.Border(edge, style.EdgeWidth, height / 2f);

        var id = hit ?? new HitResult.ButtonHit(label ?? "switch");
        Action<InputModifier>? press = enabled ? _ => onChange?.Invoke(!on) : null;
        track = track.Clickable(id, press);
        if (enabled) track = track.Activatable(press!);

        Node? caption = null;
        if (label is not null)
        {
            caption = Text(label, theme.Metrics.BaseFontSize, labelInk, TextAlign.Near, TextAlign.Center).Clickable(id, press);
            if (enabled) caption = caption.Activatable(press!);
        }

        if (!enabled)
        {
            track = track.Disabled(disabledReason!);
            caption = caption?.Disabled(disabledReason!);
        }
        else
        {
            var hover = on ? style.OnHoverFill : style.OffHoverFill;
            var pressed = on ? style.OnPressedFill : style.OffPressedFill;
            track = track.BgHover(hover).WithFocusRing(theme.Palette.Focus, theme.Metrics.FocusRingWidth, theme.Metrics.FocusRingGap)
                .BgPressed(pressed).WithCursor(CursorKind.Pointer);
            caption = caption?.WithCursor(CursorKind.Pointer);
            if (tooltip is not null)
            {
                track = track.WithTooltip(tooltip);
                caption = caption?.WithTooltip(tooltip);
            }
        }

        var parts = new List<Node>(2);
        if (caption is not null && labelBefore) parts.Add(caption);
        parts.Add(track);
        if (caption is not null && !labelBefore) parts.Add(caption);
        return HStack([.. parts]).WithGap(IconWordGap).CrossCenter().HFixed(theme.Metrics.ButtonHeight);
    }
}
