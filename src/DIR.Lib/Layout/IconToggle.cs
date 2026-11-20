using System;

namespace DIR.Lib.Layout;

public static partial class Builder
{
    /// <summary>
    /// A themed square icon toggle: on or off as the CALLER says. Pressed it fills with <see cref="UiPalette.Accent"/>,
    /// takes the ink <see cref="UiPalette.InkOn"/> says reads on it and the filled form of its icon
    /// (<see cref="IconSet.FilledForm"/>); a press hands <paramref name="onToggle"/> the other state and changes nothing
    /// itself, so the look follows the value the caller passes next, never the click.
    /// </summary>
    /// <remarks>
    /// Square at <see cref="UiMetrics.ButtonHeight"/>, radius <see cref="UiMetrics.CornerRadius"/>. The words are the
    /// tooltip. An optional <paramref name="word"/> sits beside the icon and drops by the narrow-form rule (the control
    /// becomes the square when the word's form does not fit). A <paramref name="disabledReason"/> makes it take no press
    /// and show the reason. A null <paramref name="onToggle"/> is a display that registers nothing.
    /// </remarks>
    public static Node IconToggle(IconKind kind, bool on, Action<bool>? onToggle, UiTheme theme, string words,
        string? word = null, string? disabledReason = null, float iconSize = 16f)
        => Toggle(kind, on, onToggle is null ? null : _ => onToggle(!on), theme, words, word, disabledReason, iconSize);

    /// <summary>The same square button for an action (OpenInPage): never pressed, always the outline.</summary>
    public static Node IconAction(IconKind kind, Action? onPress, UiTheme theme, string words,
        string? word = null, string? disabledReason = null, float iconSize = 16f)
        => Toggle(kind, false, onPress is null ? null : _ => onPress(), theme, words, word, disabledReason, iconSize);

    private static Node Toggle(IconKind kind, bool on, Action<InputModifier>? press, UiTheme theme,
        string words, string? word, string? disabledReason, float iconSize)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var palette = theme.Palette;
        var enabled = disabledReason is null;
        var ink = on ? palette.InkOn(palette.Accent) : palette.BodyText;
        var shown = on ? IconSet.FilledForm(kind) : kind;
        var hasWord = word is { Length: > 0 };

        Node Form(bool withWord)
        {
            var mark = Icon(shown, iconSize, ink);
            var cell = withWord
                ? HStack(mark, Text(word!, iconSize, ink, TextAlign.Near, TextAlign.Center)).WithGap(6f).CrossCenter().Pad(10f, 0f)
                : mark.WFixed(theme.Metrics.ButtonHeight);
            cell = cell.HFixed(theme.Metrics.ButtonHeight).Radius(theme.Metrics.CornerRadius);
            if (on)
            {
                cell = cell.Bg(palette.Accent);
            }
            else
            {
                cell = cell.Border(palette.Edge, 1f, theme.Metrics.CornerRadius);
                if (press is not null && enabled)
                {
                    cell = cell.BgHover(palette.Selection);
                }
            }

            if (press is null) return cell;

            cell = cell.Clickable(new HitResult.ButtonHit("icon-toggle:" + kind), enabled ? press : null);
            return enabled ? cell.WithTooltip(words) : cell.Disabled(disabledReason!);
        }

        var full = Form(withWord: hasWord);
        return hasWord ? full.OrWhenNarrow(Form(withWord: false)) : full;
    }
}
