using System;

namespace DIR.Lib.Layout;

public static partial class Builder
{
    /// <summary>
    /// A themed toggle tile: a square icon tile with its label UNDER it, drawn from
    /// <see cref="ControlStyles.TileOrDerived"/> alone. On is filled with the accent (icon in the ink that reads on it, no
    /// ring); off is the neutral control fill with the control edge ring. Hover and pressed are declared fills on the tile.
    /// The tile keeps its side (<see cref="ToggleTileStyle.SideScale"/> times <see cref="UiMetrics.ButtonHeight"/>) whatever
    /// the label. The label ends in an ellipsis at that width (<see cref="TextTrim.End"/>, declared on the caption), and the whole label is the
    /// caption's tooltip (a caller's <paramref name="tooltip"/> describes the tile and never replaces it), so a long label never squeezes the icon. <paramref name="opens"/> adds <see cref="IconKind.CaretRight"/> inside the tile.
    /// </summary>
    /// <remarks>
    /// The tile and its label are one hit: both register <paramref name="hit"/> (default <c>ButtonHit(label)</c>) and call
    /// <paramref name="onToggle"/> with the <see cref="InputModifier"/> of the press, which changes nothing itself, so the
    /// look follows the value the caller passes next. A <paramref name="disabledReason"/> makes the tile flat and neutral
    /// (even when on, so the dimmed ink keeps 4.5:1), takes no press and shows the reason as its tooltip.
    /// </remarks>
    public static Node ToggleTile(IconKind icon, string label, bool on, Action<InputModifier> onToggle, UiTheme theme,
        bool opens = false, string? disabledReason = null, string? tooltip = null, HitResult? hit = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(onToggle);
        ArgumentNullException.ThrowIfNull(theme);
        var style = theme.Controls.TileOrDerived;
        var enabled = disabledReason is null;
        var lit = on && enabled;
        var iconInk = lit ? style.OnInk : style.OffInk;
        var labelInk = style.LabelInk;
        if (!enabled)
        {
            iconInk = style.DisabledInk ?? RGBAColor32.Mix(iconInk, theme.Palette.PanelBg, DisabledInkMix);
            labelInk = iconInk;
        }
        var side = theme.Metrics.ButtonHeight * style.SideScale;
        var fontSize = theme.Metrics.BaseFontSize;
        var id = hit ?? new HitResult.ButtonHit(label);
        // The caption's tooltip is always the whole label (it may be cut); a caller's tooltip describes the tile.
        var tileTip = tooltip ?? label;

        // Spacers on both sides of the mark centre it on the main axis.
        var mark = opens
            ? HStack(Spacer().Stretch(), Icon(icon, fontSize, iconInk), Icon(IconKind.CaretRight, fontSize, iconInk), Spacer().Stretch())
            : HStack(Spacer().Stretch(), Icon(icon, fontSize, iconInk), Spacer().Stretch());
        var tile = mark.CrossCenter().WFixed(side).HFixed(side).Radius(style.Radius)
            .Bg(lit ? style.OnFill : style.OffFill);
        if (!lit && style.OffEdge is { } edge) tile = tile.Border(edge, style.EdgeWidth, style.Radius);

        var caption = Text(label, fontSize, labelInk, TextAlign.Center, TextAlign.Center, trim: TextTrim.End).WFixed(side);

        tile = tile.Clickable(id, enabled ? onToggle : null);
        caption = caption.Clickable(id, enabled ? onToggle : null);
        if (enabled)
        {
            var hover = on ? style.OnHoverFill : style.OffHoverFill;
            var pressed = on ? style.OnPressedFill : style.OffPressedFill;
            tile = tile.BgHover(hover).BgFocus(hover).BgPressed(pressed).WithCursor(CursorKind.Pointer).WithTooltip(tileTip);
            caption = caption.WithCursor(CursorKind.Pointer).WithTooltip(label);
        }
        else
        {
            tile = tile.Disabled(disabledReason!);
            caption = caption.Disabled(disabledReason!);
        }
        return VStack(tile, caption).WithGap(style.LabelGap).WFixed(side);
    }
}
