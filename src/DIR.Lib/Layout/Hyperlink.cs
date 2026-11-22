using System;

namespace DIR.Lib.Layout;

public static partial class Builder
{
    /// <summary>The north-east arrow that marks a link leaving the app.</summary>
    internal const string ExternalArrow = "↗";

    /// <summary>
    /// A themed inline link: text in <see cref="HyperlinkStyle.Color"/>, no fill, underlined on hover when the style says
    /// so, with the hand cursor. <paramref name="external"/> adds a trailing north-east arrow (the link leaves the app).
    /// The hit is <c>ButtonHit(label)</c> (a link here is an action the caller runs, not a navigation the host
    /// resolves, so <see cref="HitResult.LinkHit"/> is not used); pass <c>hit</c> when two links share a label in a frame.
    /// A link has no fill, so it has no pressed, focus or disabled look: the hand cursor and the underline are its states.
    /// </summary>
    public static Node Hyperlink(string label, Action onClick, UiTheme theme, bool external = false, HitResult? hit = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(onClick);
        ArgumentNullException.ThrowIfNull(theme);
        var style = theme.Controls.Hyperlink;
        var fontSize = theme.Metrics.BaseFontSize;
        var text = Text(label, fontSize, style.Color, TextAlign.Near, TextAlign.Center);
        if (style.UnderlineOnHover) text = text.UnderlineOnHover();
        var content = external
            ? HStack(text, Text(ExternalArrow, fontSize, style.Color, TextAlign.Near, TextAlign.Center)).WithGap(style.ArrowGap).CrossCenter()
            : text;
        return content.Clickable(hit ?? new HitResult.ButtonHit(label), _ => onClick(), CursorKind.Pointer);
    }
}
