using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

/// <summary>Which themed look a <see cref="Builder.Button"/> takes from <see cref="UiTheme.Controls"/>.</summary>
public enum ButtonKind
{
    /// <summary>The one primary action: accent fill.</summary>
    Accent,
    /// <summary>The ordinary button: neutral fill and edge.</summary>
    Standard,
    /// <summary>A quiet button: no fill at rest.</summary>
    Subtle,
}

public static partial class Builder
{
    // Fraction of the way from the ink to the ground when a style names no disabled ink.
    private const float DisabledInkMix = 0.5f;

    /// <summary>
    /// A themed text button, drawn from <see cref="UiTheme.Controls"/> alone (fills, edge, radius, padding, ink), at
    /// <see cref="UiMetrics.ButtonHeight"/>. Hover, pressed and keyboard focus are declared fills
    /// (<see cref="Node.BgHover"/>, <see cref="Node.BgPressed"/>, <see cref="Node.BgFocus"/>), so the look follows the
    /// pointer with no state of the caller's. <paramref name="opens"/> adds a trailing <see cref="IconKind.CaretDown"/>
    /// ("this opens something"). A <paramref name="disabledReason"/> makes the button flat (no hover, no pressed), dims
    /// the ink, takes no press and shows the reason as its tooltip with the not-allowed cursor.
    /// </summary>
    /// <remarks>The hit identity is <c>ButtonHit(label)</c> unless <paramref name="hit"/> says otherwise: pass one when two buttons share a label in a frame.</remarks>
    public static Node Button(string label, ButtonKind kind, Action onClick, UiTheme theme,
        IconKind? icon = null, bool opens = false, string? disabledReason = null, string? tooltip = null, HitResult? hit = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(onClick);
        ArgumentNullException.ThrowIfNull(theme);
        var controls = theme.Controls;
        var style = kind switch
        {
            ButtonKind.Accent => controls.Accent,
            ButtonKind.Standard => controls.Standard,
            _ => controls.Subtle,
        };
        var enabled = disabledReason is null;
        var ink = enabled ? style.Ink : style.DisabledInk ?? RGBAColor32.Mix(style.Ink, theme.Palette.PanelBg, DisabledInkMix);
        var fontSize = theme.Metrics.BaseFontSize;

        var parts = new List<Node>(3);
        if (icon is { } kindOfIcon) parts.Add(Icon(kindOfIcon, fontSize, ink));
        parts.Add(Text(label, fontSize, ink, TextAlign.Near, TextAlign.Center));
        if (opens) parts.Add(Icon(IconKind.CaretDown, fontSize, ink));

        var cell = HStack([.. parts]).WithGap(IconWordGap).CrossCenter()
            .Pad(style.PaddingX, 0f).HFixed(theme.Metrics.ButtonHeight).Radius(style.Radius);
        if (style.RestFill is { } rest) cell = cell.Bg(rest);
        if (style.Edge is { } edge) cell = cell.Border(edge, 1f, style.Radius);

        cell = cell.Clickable(hit ?? new HitResult.ButtonHit(label), enabled ? _ => onClick() : null);
        if (!enabled) return cell.Disabled(disabledReason!);

        cell = cell.BgHover(style.HoverFill).BgFocus(style.HoverFill).BgPressed(style.PressedFill)
            .WithCursor(CursorKind.Pointer);
        return tooltip is null ? cell : cell.WithTooltip(tooltip);
    }
}
