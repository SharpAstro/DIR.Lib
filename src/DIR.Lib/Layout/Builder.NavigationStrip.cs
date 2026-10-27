using System;

namespace DIR.Lib.Layout;

/// <summary>
/// The colours of a <see cref="Builder.NavigationStrip"/>, which the host fills from its own palette so the strip is
/// never a second look.
/// </summary>
/// <param name="Fill">The strip's ground.</param>
/// <param name="HoverFill">A cell's ground under the pointer, when a press would go somewhere.</param>
/// <param name="Ink">An arrow that can be followed.</param>
/// <param name="DisabledInk">An arrow with nowhere to go to: dimmed, and its press swallowed.</param>
public readonly record struct NavigationStripColors(
    RGBAColor32 Fill,
    RGBAColor32 HoverFill,
    RGBAColor32 Ink,
    RGBAColor32 DisabledInk);

public static partial class Builder
{
    /// <summary>The hit's action id for the strip's Back cell.</summary>
    public const string NavigationBackAction = "nav:back";

    /// <summary>The hit's action id for the strip's Forward cell.</summary>
    public const string NavigationForwardAction = "nav:forward";

    /// <summary>The tooltip of Back: it names the key, as <see cref="NavigationInput"/> binds it.</summary>
    public const string NavigationBackTooltip = "Back (Alt+Left)";

    /// <summary>The tooltip of Forward.</summary>
    public const string NavigationForwardTooltip = "Forward (Alt+Right)";

    /// <summary>
    /// A window's Back and Forward as a strip of two cells sharing its width, each a button (<see cref="CursorKind.Pointer"/>,
    /// a <see cref="HitResult.ButtonHit"/> of <see cref="NavigationBackAction"/> / <see cref="NavigationForwardAction"/>) with
    /// a tooltip that names its key. A cell with nowhere to go is <see cref="Node.Disabled(string)"/>: dimmed, its press
    /// swallowed, and its tooltip kept so it still says what the key is. The strip fills the room it is given in both axes,
    /// so the host sizes it (<c>.RowH(h)</c> or a fixed-height slot).
    /// </summary>
    /// <remarks>
    /// The marks are <see cref="IconKind.CaretLeft"/> and <see cref="IconKind.CaretRight"/>, the family's "step back" and
    /// "step forward", never an arrow rune in a text run, which draws whatever the host's face has for it. DIR.Lib has no
    /// arrow icon with a tail.
    /// </remarks>
    /// <param name="canGoBack">Whether Back has a place to go to, e.g. <see cref="NavigationHistory{TPlace}.CanGoBack"/>.</param>
    /// <param name="canGoForward">Whether Forward has one.</param>
    /// <param name="onBack">Runs the host's Back; null leaves the cell a button that does nothing.</param>
    /// <param name="onForward">Runs the host's Forward.</param>
    /// <param name="colors">The host's palette for the strip.</param>
    /// <param name="iconSize">The mark's size in design units.</param>
    public static Node NavigationStrip(bool canGoBack, bool canGoForward, Action? onBack, Action? onForward,
        in NavigationStripColors colors, float iconSize = 12f)
        => HStack(
                NavigationCell(IconKind.CaretLeft, NavigationBackAction, NavigationBackTooltip, canGoBack, onBack, colors, iconSize),
                NavigationCell(IconKind.CaretRight, NavigationForwardAction, NavigationForwardTooltip, canGoForward, onForward, colors, iconSize))
            .Bg(colors.Fill).Stretch();

    private static Node NavigationCell(IconKind mark, string action, string tooltip, bool enabled, Action? go,
        in NavigationStripColors colors, float iconSize)
    {
        // Centred in the cell both ways: the icon draws in a square of its own, so the cell's two Star axes place it.
        var face = Icon(mark, iconSize, enabled ? colors.Ink : colors.DisabledInk).WAuto().HAuto();
        var cell = VStack(
                Spacer().HStar(),
                HStack(Spacer().WStar(), face, Spacer().WStar()).HAuto(),
                Spacer().HStar())
            .WStar().HStar();
        if (enabled)
        {
            cell = cell.BgHover(colors.HoverFill);
        }
        cell = cell.Clickable(new HitResult.ButtonHit(action), go is null ? null : _ => go(), CursorKind.Pointer)
            .WithTooltip(tooltip);
        return enabled ? cell : cell.Disabled(tooltip);
    }
}
