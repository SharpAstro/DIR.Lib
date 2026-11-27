using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

/// <summary>Which of the two DSN-1 v4 sizes a sized <c>Builder.Switch</c> takes.</summary>
public enum SwitchSize
{
    /// <summary>40 by 22 design units, knob 18: settings and forms.</summary>
    Regular,
    /// <summary>30 by 16 design units, knob 12: dense rows such as an equipment list.</summary>
    Small,
}

public static partial class Builder
{
    // Track sizes of the two DSN-1 v4 switches; the knob is the track's height less the inset on both sides.
    private const float SwitchRegularWidth = 40f;
    private const float SwitchRegularHeight = 22f;
    private const float SwitchSmallWidth = 30f;
    private const float SwitchSmallHeight = 16f;
    // The knob sits this far inside a sized track on every side.
    private const float SwitchInset = 2f;
    // Gap between the track and its state label or error mark, and the error mark's size.
    private const float SwitchLabelGap = 6f;
    private const float SwitchMarkSize = 14f;
    private const string SwitchChangingReason = "Changing";

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
    /// Since 11.105 it takes the DSN-1 v4 colours a palette-derived theme gives it (the off knob lightens with its track
    /// under the pointer, a light palette outlines the off knob) and the knob slides when the value changes
    /// (<see cref="Node.Knob"/>); the sizes and the locked, changing, refused and asking states are the overload taking a
    /// <see cref="SwitchSize"/>.
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
        if (!on && enabled && style.OffKnobEdge is { } knobEdge) knob = knob.Border(knobEdge, style.EdgeWidth, knobSize / 2f);
        if (!on && enabled && style.OffHoverKnob is { } hoverKnob) knob = knob.BgHoverWithHit(hoverKnob);
        var id = hit ?? new HitResult.ButtonHit(label ?? "switch");
        if (onChange is not null) knob = knob.WithKnob(new KnobSlide(id, on, !enabled));
        // The knob sits at the end when on: a stretching spacer before it pushes it there.
        var track = (on ? HStack(Spacer().Stretch(), knob) : HStack(knob, Spacer().Stretch()))
            .CrossCenter().Pad(style.KnobInset, style.KnobInset)
            .WFixed(width).HFixed(height).Radius(height / 2f)
            .Bg(lit ? style.OnFill : style.OffFill);
        if (!lit && style.OffEdge is { } edge) track = track.Border(edge, style.EdgeWidth, height / 2f);

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

    /// <summary>
    /// The DSN-1 v4 switch at one of its two sizes (Regular 40x22 knob 18, Small 30x16 knob 12), drawn from
    /// <see cref="ControlStyles.SwitchOrDerived"/> alone: a rounded track with a round knob at the side its value says (right
    /// on, left off, middle while <paramref name="changing"/>). On and off differ by knob side and fill, never by colour alone.
    /// Only the track is the hit (<paramref name="hit"/>, default <c>ButtonHit("switch")</c>); the optional
    /// <paramref name="stateLabel"/> beside it is plain text and takes no press. Hover and pressed are declared fills (the
    /// off knob lightens with its track) and keyboard focus a <see cref="Node.FocusRing"/>, as on <see cref="Button"/>.
    /// </summary>
    /// <remarks>
    /// A press, or Space or Enter, calls <paramref name="onToggle"/> with the NEW value (<c>!on</c>). The caller flips its value
    /// at once, so the knob moves on the press, and sets it back and passes <paramref name="refusedReason"/> when the far end
    /// says no: the track then takes an Error edge, a mark appears beside it and the reason is its tooltip. A
    /// <paramref name="lockedReason"/> dims track and knob but keeps the knob's side, takes no press and shows the reason as
    /// its tooltip. <paramref name="changing"/> parks the knob in the middle on the neutral track and takes no press.
    /// <paramref name="asking"/> rings the switch in Warn (it stays put until the caller's own question is answered). A
    /// null <paramref name="onToggle"/> is a display that registers nothing. The layout is static; the pixel painter slides
    /// the knob when the side changed since it last painted the switch (<see cref="Node.Knob"/>). The switch reserves room
    /// for a ring (focus ring width plus gap) on every side, so asking never moves the layout.
    /// </remarks>
    public static Node Switch(bool on, Action<bool>? onToggle, UiTheme theme, SwitchSize size,
        string? stateLabel = null, string? lockedReason = null, bool changing = false, string? refusedReason = null,
        bool asking = false, string? tooltip = null, HitResult? hit = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var style = theme.Controls.SwitchOrDerived;
        var palette = theme.Palette;
        var metrics = theme.Metrics;
        var small = size == SwitchSize.Small;
        var width = small ? SwitchSmallWidth : SwitchRegularWidth;
        var height = small ? SwitchSmallHeight : SwitchRegularHeight;
        var knob = height - 2f * SwitchInset;
        var locked = lockedReason is not null;
        var interactive = onToggle is not null && !locked && !changing;
        var lit = on && !changing;

        RGBAColor32 fill, knobInk;
        RGBAColor32? edge = null, knobEdge = null;
        if (locked)
        {
            fill = lit ? style.LockedOnFillOrDerived : style.LockedOffFillOrDerived;
            knobInk = lit ? style.LockedOnKnobOrDerived : style.LockedOffKnobOrDerived;
            if (!lit) edge = style.LockedOffEdgeOrDerived;
        }
        else if (lit)
        {
            fill = style.OnFill;
            knobInk = style.OnKnob;
        }
        else
        {
            fill = style.OffFill;
            knobInk = style.OffKnob;
            edge = style.OffEdge;
            knobEdge = style.OffKnobEdge;
        }
        if (refusedReason is not null) edge = style.RefusedEdgeOrDerived;

        var thumb = Box(knob, knob, knobInk).Radius(knob / 2f);
        if (knobEdge is { } ke) thumb = thumb.Border(ke, style.EdgeWidth, knob / 2f);
        // The off knob lightens with its track under the pointer (DSN-1 home's "Off, hover").
        if (interactive && !lit) thumb = thumb.BgHoverWithHit(style.OffHoverKnobOrDerived);
        var id = hit ?? new HitResult.ButtonHit("switch");
        // The painter slides the knob when its side changed since the switch was last painted; the layout stays at the end.
        if (onToggle is not null && !changing) thumb = thumb.WithKnob(new KnobSlide(id, on, locked));
        var track = (changing
                ? HStack(Spacer().Stretch(), thumb, Spacer().Stretch())
                : lit ? HStack(Spacer().Stretch(), thumb) : HStack(thumb, Spacer().Stretch()))
            .CrossCenter().WFixed(width).HFixed(height).Pad(SwitchInset - (edge is null ? 0f : style.EdgeWidth)).Radius(height / 2f).Bg(fill);
        if (edge is { } trackEdge) track = track.Border(trackEdge, style.EdgeWidth, height / 2f);

        if (onToggle is not null)
        {
            Action<InputModifier>? press = interactive ? _ => onToggle(!on) : null;
            track = track.Clickable(id, press);
            if (locked) track = track.Disabled(lockedReason!);
            else if (changing) track = track.Disabled(SwitchChangingReason);
            else
            {
                track = track.Activatable(press!)
                    .BgHover(lit ? style.OnHoverFill : style.OffHoverFill)
                    .WithFocusRing(palette.Focus, metrics.FocusRingWidth, metrics.FocusRingGap)
                    .WithCursor(CursorKind.Pointer);
            }
        }
        var tip = lockedReason ?? refusedReason ?? tooltip;
        if (tip is not null) track = track.WithTooltip(tip);

        // The ring (focus width plus gap) is reserved on every side, so the asking ring never moves the layout.
        var ring = metrics.FocusRingWidth + metrics.FocusRingGap;
        var outerHeight = height + 2f * ring;
        var outer = HStack(track).CrossCenter().Pad(asking ? ring - metrics.FocusRingWidth : ring).WFixed(width + 2f * ring).HFixed(outerHeight).Radius(outerHeight / 2f);
        if (asking) outer = outer.Border(style.AskingRingOrDerived, metrics.FocusRingWidth, outerHeight / 2f);

        if (stateLabel is null && refusedReason is null) return outer;
        var parts = new List<Node>(3) { outer };
        if (refusedReason is not null) parts.Add(Icon(IconKind.Error, SwitchMarkSize, style.RefusedEdgeOrDerived));
        if (stateLabel is not null) parts.Add(Text(stateLabel, metrics.BaseFontSize, palette.BodyText, TextAlign.Near, TextAlign.Center).WAuto());
        return HStack([.. parts]).WithGap(SwitchLabelGap).CrossCenter();
    }
}
