using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

public static partial class Builder
{
    // Suffix that keeps the default hit of the details part distinct from the primary part's.
    private const string SplitDetailsHitSuffix = "/details";

    /// <summary>
    /// A themed split button: one control of two separate hits with a thin divider between them. The left part toggles or
    /// acts (<see cref="UiTheme.Controls"/> Accent when <paramref name="on"/>, else Standard) and the right part is a
    /// <see cref="IconKind.CaretRight"/> that opens details. Each part has its own hover, pressed fill, cursor and tooltip,
    /// so one lights alone. Drawn from <see cref="UiTheme.Controls"/> alone at <see cref="UiMetrics.ButtonHeight"/>: the radius and
    /// ring are the Standard style's (the ring only when off and the style has an edge, drawn once round the whole
    /// control), and the divider is the Standard edge (the Accent ink when on) at the style's edge width.
    /// </summary>
    /// <remarks>
    /// The rest fill is one rounded layer under both parts, so the corners where the parts meet show no notch at rest (a
    /// lit part's own rounded fill leaves a tiny rest-coloured corner beside the divider). <paramref name="onPrimary"/> receives the <see cref="InputModifier"/> of the press. A
    /// <paramref name="disabledReason"/> makes both parts flat, dims the ink, takes no press and shows the reason as the tooltip.
    /// The default hits are <c>ButtonHit(label)</c> and <c>ButtonHit(label + "/details")</c>, so the two never collide. The
    /// details part's tooltip defaults to <c>label + " options"</c>, so the caret is never a mute target.
    /// </remarks>
    public static Node SplitButton(string label, bool on, Action<InputModifier> onPrimary, Action onDetails, UiTheme theme,
        IconKind? icon = null, string? tooltip = null, string? detailsTooltip = null, string? disabledReason = null,
        HitResult? hit = null, HitResult? detailsHit = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(onPrimary);
        ArgumentNullException.ThrowIfNull(onDetails);
        ArgumentNullException.ThrowIfNull(theme);
        var controls = theme.Controls;
        var enabled = disabledReason is null;
        // A disabled split button is flat and neutral even when on, so the dimmed ink keeps 4.5:1.
        var lit = on && enabled;
        var style = lit ? controls.Accent : controls.Standard;
        var radius = controls.Standard.Radius;
        var ink = enabled ? style.Ink : style.DisabledInk ?? RGBAColor32.Mix(style.Ink, theme.Palette.PanelBg, DisabledInkMix);
        var fontSize = theme.Metrics.BaseFontSize;
        var height = theme.Metrics.ButtonHeight;

        var parts = new List<Node>(2);
        if (icon is { } kindOfIcon) parts.Add(Icon(kindOfIcon, fontSize, ink));
        parts.Add(Text(label, fontSize, ink, TextAlign.Near, TextAlign.Center));

        Node Face(Node cell, HitResult id, Action<InputModifier>? click, string? tip)
        {
            cell = cell.Clickable(id, enabled ? click : null);
            if (!enabled) return cell.Disabled(disabledReason!);
            cell = cell.BgHover(style.HoverFill).WithFocusRing(theme.Palette.Focus, theme.Metrics.FocusRingWidth).BgPressed(style.PressedFill)
                .WithCursor(CursorKind.Pointer);
            return tip is null ? cell : cell.WithTooltip(tip);
        }

        var left = Face(HStack([.. parts]).WithGap(IconWordGap).CrossCenter()
                .Pad(style.PaddingX, 0f).HFixed(height).Radius(radius),
            hit ?? new HitResult.ButtonHit(label), onPrimary, tooltip);
        var right = Face(HStack(Icon(IconKind.CaretRight, fontSize, ink)).CrossCenter()
                .Pad(style.PaddingX, 0f).HFixed(height).Radius(radius),
            detailsHit ?? new HitResult.ButtonHit(label + SplitDetailsHitSuffix), _ => onDetails(), detailsTooltip ?? label + " options");

        var dividerColour = lit ? controls.Accent.Ink : controls.Standard.Edge ?? controls.Standard.Ink;
        var divider = Spacer().WFixed(controls.Standard.EdgeWidth).HFixed(height).Bg(dividerColour);
        var row = HStack(left, divider, right).CrossCenter().HFixed(height).Radius(radius);
        // One rest fill under both parts: the parts' rounded inner corners then show it, not the page, beside the divider.
        if (style.RestFill is { } rest) row = row.Bg(rest);

        if (lit || controls.Standard.Edge is not { } edge) return row;
        // The ring is one layer over both parts, so neither part's fill paints over it.
        return Overlay(row, Spacer().Stretch().Radius(radius).Border(edge, controls.Standard.EdgeWidth, radius));
    }
}
