using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

/// <summary>Which mark a <see cref="Builder.SettingRow"/> ends in.</summary>
public enum SettingTrailingKind
{
    /// <summary>A right-pointing caret: the row opens a page.</summary>
    Chevron,
    /// <summary>A down caret that turns up while the row is open: the row expands in place.</summary>
    Expand,
    /// <summary>A north-east arrow: the row leaves the app.</summary>
    External,
    /// <summary>The caller's own node (a switch, a button) with its own hit and press.</summary>
    Node,
}

/// <summary>What ends a setting row. Build one with <see cref="Chevron"/>, <see cref="Expand"/>, <see cref="External"/> or <see cref="Of"/>.</summary>
public sealed record SettingTrailing
{
    /// <summary>Which mark this is.</summary>
    public SettingTrailingKind Kind { get; private init; }

    /// <summary>For <see cref="SettingTrailingKind.Expand"/>: the row is open, so the caret points up.</summary>
    public bool Open { get; private init; }

    /// <summary>For <see cref="SettingTrailingKind.Node"/>: the caller's node.</summary>
    public Node? Content { get; private init; }

    /// <summary>A chevron (<see cref="IconKind.CaretRight"/>).</summary>
    public static SettingTrailing Chevron { get; } = new() { Kind = SettingTrailingKind.Chevron };

    /// <summary>The north-east arrow of a row that leaves the app.</summary>
    public static SettingTrailing External { get; } = new() { Kind = SettingTrailingKind.External };

    /// <summary>The expand caret: <see cref="IconKind.CaretDown"/>, or <see cref="IconKind.CaretUp"/> when <paramref name="open"/>.</summary>
    public static SettingTrailing Expand(bool open) => new() { Kind = SettingTrailingKind.Expand, Open = open };

    /// <summary>A caller's node at the row's end. It keeps its own hit and press.</summary>
    public static SettingTrailing Of(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new() { Kind = SettingTrailingKind.Node, Content = node };
    }
}

public static partial class Builder
{
    /// <summary>
    /// A themed setting row, drawn from <see cref="ControlStyles.SettingRow"/> alone: a whole card row on the panel with the
    /// control radius, a hover fill, a title, an optional subtitle and a <paramref name="trailing"/> mark. The whole row is
    /// one hit (<c>ButtonHit(title)</c> unless <paramref name="hit"/> says otherwise) running <paramref name="onClick"/>; a
    /// trailing node keeps its own hit, so pressing a switch or button in it does not run the row's handler. Title and
    /// subtitle end in an ellipsis at the row's width, with the whole text as tooltip. A <paramref name="disabledReason"/>
    /// makes the row flat, takes no press and shows the reason as its tooltip.
    /// </summary>
    public static Node SettingRow(string title, string? subtitle, SettingTrailing trailing, UiTheme theme, Action? onClick = null,
        string? disabledReason = null, HitResult? hit = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(trailing);
        ArgumentNullException.ThrowIfNull(theme);
        var style = theme.Controls.SettingRowOrDerived;
        var fontSize = theme.Metrics.BaseFontSize;
        var enabled = disabledReason is null;

        var texts = new List<Node>(2)
        {
            Text(title, fontSize, style.TitleInk, TextAlign.Near, TextAlign.Center, trim: TextTrim.End).WithTooltip(title),
        };
        if (!string.IsNullOrEmpty(subtitle))
            texts.Add(Text(subtitle, fontSize, style.SubtitleInk, TextAlign.Near, TextAlign.Center, trim: TextTrim.End).WithTooltip(subtitle));
        var block = VStack([.. texts]).WithGap(style.Gap / 2f).WStar();

        var mark = trailing.Kind switch
        {
            SettingTrailingKind.Chevron => Icon(IconKind.CaretRight, fontSize, style.MarkInk),
            SettingTrailingKind.Expand => Icon(trailing.Open ? IconKind.CaretUp : IconKind.CaretDown, fontSize, style.MarkInk),
            SettingTrailingKind.External => Text(ExternalArrow, fontSize, style.MarkInk, TextAlign.Near, TextAlign.Center),
            _ => trailing.Content!,
        };

        var row = HStack(block, mark).WithGap(style.Gap).CrossCenter()
            .Pad(style.PaddingX, style.PaddingY).Radius(style.Radius).Bg(style.RestFill);
        row = row.Clickable(hit ?? new HitResult.ButtonHit(title), enabled && onClick is not null ? _ => onClick() : null);
        // Disabled propagates to every descendant, so a trailing node (a switch, a button) would die with its row. The row goes
        // flat with the reason as its tooltip instead and the node keeps its own state.
        if (!enabled) return trailing.Kind == SettingTrailingKind.Node ? row.WithTooltip(disabledReason!) : row.Disabled(disabledReason!);
        return row.BgHover(style.HoverFill).BgFocus(style.HoverFill).BgPressed(style.PressedFill).WithCursor(CursorKind.Pointer);
    }
}
