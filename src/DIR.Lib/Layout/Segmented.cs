using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

public static partial class Builder
{
    /// <summary>Space between the segments of a <see cref="Builder.Segmented{T}(ReadOnlySpan{ButtonGroupOption{T}}, T, Action{T}, UiTheme, ListCursor, string, float)"/>, and between its edge and them, design units.</summary>
    internal const float SegmentedInset = 2f;

    /// <summary>Corner radius of one segment inside a <see cref="Builder.Segmented{T}(ReadOnlySpan{ButtonGroupOption{T}}, T, Action{T}, UiTheme, ListCursor, string, float)"/>, design units.</summary>
    internal const float SegmentRadius = 7f;

    /// <summary>Gap between an icon and its word in a segment or an icon toggle, design units.</summary>
    internal const float IconWordGap = 6f;

    /// <summary>Corner radius of the track of a <see cref="Builder.Segmented{T}(ReadOnlySpan{ButtonGroupOption{T}}, T, Action{T}, UiTheme, ListCursor, string, float)"/>, design units (the screen's 9 on a 30-unit control).</summary>
    internal const float SegmentTrackRadius = 9f;

    /// <summary>
    /// A themed segmented control: one of <paramref name="options"/> is <paramref name="selected"/>; each segment shows its
    /// icon AND its word, and a press on another hands its value to <paramref name="onSelect"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Built from the theme alone, as DSN-1 v4 draws it.</b> One rounded track (<see cref="UiPalette.HeaderBg"/>, radius
    /// 9) holds the segments; the chosen one is a raised neutral pill (<see cref="UiPalette.SeparatorStrong"/>, radius 7) in
    /// <see cref="UiPalette.BodyText"/>, never the accent; the others are <see cref="UiPalette.DimText"/> with no rest fill
    /// and a faint pill (<see cref="UiPalette.Separator"/>) under the pointer. Its height is
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
    /// focused or last pressed, Left and Right move the choice to the next enabled segment, as a press would, and the
    /// track is ringed with <see cref="UiPalette.Focus"/> while its list holds the keyboard
    /// (<see cref="WindowUiSettings.KeyboardListId"/>). A segment's <see cref="ButtonGroupOption{T}.Shortcut"/> chooses
    /// it. A disabled segment takes no press, is stepped over, and shows <see cref="ButtonGroupOption{T}.DisabledReason"/>
    /// as its tooltip. A null <paramref name="onSelect"/> makes the control a display that registers nothing, as
    /// <see cref="ButtonGroup{T}"/> does.
    /// </para>
    /// <para>
    /// Building it syncs <paramref name="cursor"/> to the chosen segment, so the first arrow steps off what the reader
    /// sees. Each segment is a <see cref="HitResult.ListItemHit"/> of <paramref name="listId"/> unless the option states its
    /// own <see cref="ButtonGroupOption{T}.Hit"/> (then it is not on the arrow walk).
    /// </para>
    /// <para>
    /// A <c>lockedReason</c> dims the whole control (flat track, edge, muted pill), takes no press, key or
    /// shortcut, and shows the reason as every segment's tooltip.
    /// </para>
    /// </remarks>
    /// <param name="listId">The id of the control's declared list; unique on a frame that has other lists.</param>
    /// <param name="cursor">The keyboard's place in the control. Keep one per control.</param>
    /// <param name="fontSize">Word and icon size, design units.</param>
    public static Node Segmented<T>(ReadOnlySpan<ButtonGroupOption<T>> options, T selected, Action<T>? onSelect,
        UiTheme theme, ListCursor cursor, string listId, float fontSize = 14f)
        => Segmented(options, selected, onSelect, theme, cursor, listId, (string?)null, fontSize);

    /// <inheritdoc cref="Segmented{T}(ReadOnlySpan{ButtonGroupOption{T}}, T, Action{T}?, UiTheme, ListCursor, string, float)"/>
    /// <param name="lockedReason">When not null the control is locked: dimmed, no press, and this is its tooltip.</param>
    public static Node Segmented<T>(ReadOnlySpan<ButtonGroupOption<T>> options, T selected, Action<T>? onSelect,
        UiTheme theme, ListCursor cursor, string listId, string? lockedReason, float fontSize = 14f)
        => Segmented(options, selected, onSelect, theme, cursor, listId, lockedReason, fillTrack: false, fontSize);

    /// <inheritdoc cref="Segmented{T}(ReadOnlySpan{ButtonGroupOption{T}}, T, Action{T}?, UiTheme, ListCursor, string, float)"/>
    /// <param name="lockedReason">When not null the control is locked: dimmed, no press, and this is its tooltip.</param>
    /// <param name="fillTrack">When true the segments share the track equally, so a control given more width than its
    /// content (a star or fixed width set on it) fills it, as the screen draws tianwen's rows; when false each segment is
    /// as wide as its content.</param>
    public static Node Segmented<T>(ReadOnlySpan<ButtonGroupOption<T>> options, T selected, Action<T>? onSelect,
        UiTheme theme, ListCursor cursor, string listId, string? lockedReason, bool fillTrack, float fontSize = 14f)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(cursor);
        var locked = lockedReason is not null;
        var palette = theme.Palette;
        var comparer = EqualityComparer<T>.Default;

        var selectedIndex = -1;
        for (var i = 0; i < options.Length; i++)
        {
            if (comparer.Equals(options[i].Value, selected)) { selectedIndex = i; break; }
        }

        // An option with its own Hit is not on the arrow walk, so the cursor is not synced to its index.
        if (onSelect is not null && !locked && selectedIndex >= 0 && options[selectedIndex].Hit is null)
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
            var metrics = theme.Metrics;
            var row = HStack(segments).WithGap(SegmentedInset)
                .Pad(locked ? SegmentedInset - 1f : SegmentedInset)
                .Bg(locked ? palette.PanelBg : palette.HeaderBg)
                .Radius(SegmentTrackRadius)
                .HFixed(metrics.ButtonHeight);
            if (locked) row = row.Border(palette.Separator, 1f, SegmentTrackRadius);
            if (onSelect is null || locked) return row;
            return row.WithFocusRing(palette.Focus, metrics.FocusRingWidth, metrics.FocusRingGap)
                .ListOf(cursor, listId, horizontal: true, selectOnStep: true);
        }

        Node Segment(ButtonGroupOption<T> option, int index, bool withWord)
        {
            var isSelected = index == selectedIndex;
            var enabled = option.DisabledReason is null;
            var ink = locked ? (isSelected ? palette.DimText : palette.SeparatorStrong)
                : isSelected ? palette.BodyText : palette.DimText;

            Node face;
            if (option.Icon is { } icon)
            {
                var mark = Icon(icon, fontSize, ink);
                face = withWord && option.Label is { Length: > 0 } word
                    ? HStack(mark, Text(word, fontSize, ink, TextAlign.Near, TextAlign.Center)).WithGap(IconWordGap)
                    : mark;
            }
            else
            {
                face = Text(option.Label ?? string.Empty, fontSize, ink, TextAlign.Center, TextAlign.Center);
            }

            var cell = face.Pad(withWord ? 10f : 8f, 0f).HStar().Radius(SegmentRadius);
            if (fillTrack) cell = cell.WStar();
            if (isSelected)
            {
                cell = cell.Bg(locked ? palette.Separator : palette.SeparatorStrong);
            }
            else if (onSelect is not null && enabled && !locked)
            {
                cell = cell.BgHover(palette.Separator);
            }

            if (onSelect is null) return cell;

            var value = option.Value;
            var hit = option.Hit ?? new HitResult.ListItemHit(listId, index);
            Action<InputModifier>? press = enabled && !locked && !isSelected ? _ => onSelect(value) : null;
            cell = cell.Clickable(hit, press);

            if (locked)
            {
                cell = cell.Disabled(lockedReason!);
            }
            else if (!enabled)
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
