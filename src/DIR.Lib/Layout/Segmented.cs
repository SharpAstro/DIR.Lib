using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

public static partial class Builder
{
    /// <summary>Space between the segments of a <see cref="Segmented{T}"/>, and between its edge and them, design units.</summary>
    internal const float SegmentedInset = 2f;

    /// <summary>
    /// A themed segmented control: one of <paramref name="options"/> is <paramref name="selected"/>; each segment shows its
    /// icon AND its word, and a press on another hands its value to <paramref name="onSelect"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Built from the theme alone.</b> The container is <see cref="UiPalette.RaisedBg"/> with a 1-unit
    /// <see cref="UiPalette.Edge"/> and <see cref="UiMetrics.CornerRadius"/>; the chosen segment is
    /// <see cref="UiPalette.Accent"/> with the ink <see cref="UiPalette.InkOn"/> says reads on it; the others are
    /// <see cref="UiPalette.BodyText"/> with a hover fill and no rest fill. Its height is
    /// <see cref="UiMetrics.ButtonHeight"/>, so it shares a row with buttons.
    /// </para>
    /// <para>
    /// <b>The words drop together, by measurement.</b> The control is the full form (icon and word) with an icon-only
    /// <see cref="Node.NarrowForm"/> that replaces it whole when the full form does not fit the room it is given, so
    /// no host guesses a width. In the narrow form each segment's word is its tooltip. By default the control is as
    /// wide as its content; a sizing set on the result applies to the full form only.
    /// </para>
    /// <para>
    /// <b>Keys.</b> The control declares a horizontal list (<see cref="DeclaredList.Horizontal"/>): with the control
    /// focused or last pressed, Left and Right move the choice to the next enabled segment, as a press would. A
    /// segment's <see cref="ButtonGroupOption{T}.Shortcut"/> chooses it. A disabled segment takes no press, is stepped
    /// over, and shows <see cref="ButtonGroupOption{T}.DisabledReason"/> as its tooltip. A null
    /// <paramref name="onSelect"/> makes the control a display that registers nothing, as <see cref="ButtonGroup{T}"/> does.
    /// </para>
    /// <para>
    /// Building it syncs <paramref name="cursor"/> to the chosen segment, so the first arrow steps off what the reader
    /// sees. Each segment is a <see cref="HitResult.ListItemHit"/> of <paramref name="listId"/> unless the option states its
    /// own <see cref="ButtonGroupOption{T}.Hit"/> (then it is not on the arrow walk).
    /// </para>
    /// </remarks>
    /// <param name="listId">The id of the control's declared list; unique on a frame that has other lists.</param>
    /// <param name="cursor">The keyboard's place in the control. Keep one per control.</param>
    /// <param name="fontSize">Word and icon size, design units.</param>
    public static Node Segmented<T>(ReadOnlySpan<ButtonGroupOption<T>> options, T selected, Action<T>? onSelect,
        UiTheme theme, ListCursor cursor, string listId, float fontSize = 14f)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(cursor);
        var palette = theme.Palette;
        var comparer = EqualityComparer<T>.Default;

        var selectedIndex = -1;
        for (var i = 0; i < options.Length; i++)
        {
            if (comparer.Equals(options[i].Value, selected)) { selectedIndex = i; break; }
        }

        if (onSelect is not null)
        {
            cursor.Open(listId, selectedIndex);
        }

        var wide = new Node[options.Length];
        var narrow = new Node[options.Length];
        for (var i = 0; i < options.Length; i++)
        {
            wide[i] = Segment(options[i], i, withWord: true);
            narrow[i] = Segment(options[i], i, withWord: false);
        }

        return Container(wide).OrWhenNarrow(Container(narrow));

        Node Container(Node[] segments)
        {
            var row = HStack(segments).WithGap(SegmentedInset).Pad(SegmentedInset)
                .Bg(palette.RaisedBg)
                .Border(palette.Edge, 1f, theme.Metrics.CornerRadius)
                .Radius(theme.Metrics.CornerRadius)
                .HFixed(theme.Metrics.ButtonHeight);
            return onSelect is null ? row : row.ListOf(cursor, listId, horizontal: true, selectOnStep: true);
        }

        Node Segment(ButtonGroupOption<T> option, int index, bool withWord)
        {
            var isSelected = index == selectedIndex;
            var enabled = option.DisabledReason is null;
            var ink = isSelected ? palette.InkOn(palette.Accent) : palette.BodyText;

            Node face;
            if (option.Icon is { } icon)
            {
                var mark = Icon(icon, fontSize, ink);
                face = withWord && option.Label is { Length: > 0 } word
                    ? HStack(mark, Text(word, fontSize, ink, TextAlign.Near, TextAlign.Center)).WithGap(6f)
                    : mark;
            }
            else
            {
                face = Text(option.Label ?? string.Empty, fontSize, ink, TextAlign.Center, TextAlign.Center);
            }

            var cell = face.Pad(withWord ? 10f : 8f, 0f).HStar().Radius(4f);
            if (isSelected)
            {
                cell = cell.Bg(palette.Accent);
            }
            else if (onSelect is not null && enabled)
            {
                cell = cell.BgHover(palette.Selection);
            }

            if (onSelect is null) return cell;

            var value = option.Value;
            var hit = option.Hit ?? new HitResult.ListItemHit(listId, index);
            Action<InputModifier>? press = enabled && !isSelected ? _ => onSelect(value) : null;
            cell = cell.Clickable(hit, press);

            if (!enabled)
            {
                cell = cell.Disabled(option.DisabledReason ?? string.Empty);
            }
            else
            {
                if (option.Shortcut is { } chord && !isSelected)
                {
                    cell = cell.WithShortcut(chord);
                }

                var tip = option.Tooltip is { Length: > 0 } t ? t : null;
                if (!withWord && option.Label is { Length: > 0 } name)
                {
                    tip = tip is null ? name : name + ": " + tip;
                }

                if (tip is not null)
                {
                    cell = cell.WithTooltip(tip);
                }
            }

            return cell;
        }
    }
}
