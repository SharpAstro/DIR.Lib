using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

public static partial class Builder
{
    /// <summary>
    /// A themed filter chip, drawn from <see cref="ControlStyles.ChipOrDerived"/> alone, at
    /// <see cref="UiMetrics.ButtonHeight"/> and fully round unless the style names a radius. On is filled with the accent
    /// and has no ring; off is the neutral control fill with the control edge ring. Hover, pressed and keyboard focus are
    /// declared fills. <paramref name="onClick"/> receives the <see cref="InputModifier"/> of the press, so a host can select
    /// one chip on a plain click and extend the selection with Shift or Ctrl. A <paramref name="disabledReason"/> makes the
    /// chip flat and neutral (off fill and ring, even when on, so the dimmed ink keeps 4.5:1), dims the ink, takes no press and shows the reason as its tooltip.
    /// </summary>
    /// <remarks>The hit identity is <c>ButtonHit(label)</c> unless <paramref name="hit"/> says otherwise. Keyboard focus shows
    /// only for a chip in a <see cref="ChipRow"/> whose hit is that list's <see cref="HitResult.ListItemHit"/>.</remarks>
    public static Node Chip(string label, bool on, Action<InputModifier> onClick, UiTheme theme,
        IconKind? icon = null, string? disabledReason = null, string? tooltip = null, HitResult? hit = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(onClick);
        ArgumentNullException.ThrowIfNull(theme);
        var style = theme.Controls.ChipOrDerived;
        var enabled = disabledReason is null;
        var radius = style.Radius ?? theme.Metrics.ButtonHeight / 2f;
        // A disabled chip is flat and neutral whether on or off: the dimmed ink cannot reach 4.5:1 on the accent, so the
        // accent fill is dropped rather than the contrast.
        var lit = on && enabled;
        var ink = lit ? style.OnInk : style.OffInk;
        if (!enabled) ink = style.DisabledInk ?? RGBAColor32.Mix(ink, theme.Palette.PanelBg, DisabledInkMix);
        var fontSize = theme.Metrics.BaseFontSize;

        var parts = new List<Node>(2);
        if (icon is { } kindOfIcon) parts.Add(Icon(kindOfIcon, fontSize, ink));
        parts.Add(Text(label, fontSize, ink, TextAlign.Near, TextAlign.Center));

        var cell = HStack([.. parts]).WithGap(IconWordGap).CrossCenter()
            .Pad(style.PaddingX, 0f).HFixed(theme.Metrics.ButtonHeight).Radius(radius)
            .Bg(lit ? style.OnFill : style.OffFill);
        if (!lit && style.OffEdge is { } edge) cell = cell.Border(edge, style.EdgeWidth, radius);

        cell = cell.Clickable(hit ?? new HitResult.ButtonHit(label), enabled ? onClick : null);
        if (!enabled) return cell.Disabled(disabledReason!);

        var hover = on ? style.OnHoverFill : style.OffHoverFill;
        var pressed = on ? style.OnPressedFill : style.OffPressedFill;
        cell = cell.BgHover(hover).BgFocus(hover).BgPressed(pressed).WithCursor(CursorKind.Pointer);
        return tooltip is null ? cell : cell.WithTooltip(tooltip);
    }

    /// <summary>
    /// Lays <paramref name="chips"/> out in a row and declares them a horizontal list on <paramref name="cursor"/>, so
    /// Left and Right move the keyboard between chips. A step never toggles a chip (<c>selectOnStep</c> stays false); Enter
    /// presses the chip under the cursor. Give each chip <c>new HitResult.ListItemHit(listId, index)</c> as its hit.
    /// </summary>
    public static Node ChipRow(ListCursor cursor, string listId, UiTheme theme, params Node[] chips)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(listId);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(chips);
        return HStack(chips).WithGap(theme.Metrics.Padding).CrossCenter()
            .ListOf(cursor, listId, horizontal: true, selectOnStep: false);
    }
}
