using System;

namespace DIR.Lib.Layout;

public static partial class Builder
{
    /// <summary>
    /// A dropdown menu: <paramref name="state"/>'s entries as a list floated beside
    /// <paramref name="anchor"/>, on the popover the engine already owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This adds no mechanism.</b> Every behaviour a menu needs is already on the node: the backdrop and
    /// the Escape claim come from <see cref="Popover"/>, a disabled row is <see cref="Node.Disabled(string)"/>
    /// (dimmed, press swallowed with <see cref="CursorKind.NotAllowed"/>, the reason as its tooltip), the
    /// highlight is <see cref="Node.BgHover"/>, the overflow is <see cref="Node.WithScroll"/>. What was
    /// missing was a node to know them ON: <c>PixelWidgetBase.RenderDropdownMenu</c> knows all of it
    /// inside a ten-parameter method carrying the obligation "must be called LAST in the render pass",
    /// and a caller who gets that order wrong gets a menu that paints under the chrome it belongs to and
    /// loses its clicks, with nothing to say so.
    /// </para>
    /// <para>
    /// <b>The anchor is the trigger's arranged rect, not remembered geometry.</b> The rendered path carries
    /// <c>AnchorX</c>/<c>AnchorY</c>/<c>AnchorWidth</c> captured at the moment the menu opened, so a menu
    /// still open across a resize hangs where the button used to be. Here placement is the engine's, and it
    /// is re-measured with everything else.
    /// </para>
    /// <para>
    /// Opening one from a button is <c>.Clickable(hit, _ =&gt; state.Popover.Toggle())</c> on the button node.
    /// There is no dispatcher line to add and none to forget.
    /// </para>
    /// </remarks>
    public static Node Dropdown<T>(RectF32 anchor, DropdownMenuState<T> state,
        float fontSize = 14f, RGBAColor32? textColor = null, RGBAColor32? background = null,
        RGBAColor32? highlight = null, RGBAColor32? backdrop = null,
        float maxHeight = 0f, DockSide side = DockSide.Bottom)
        => DropdownCore(anchor, state, fontSize, textColor, background, highlight, backdrop, maxHeight, side, null);

    private static Node DropdownCore<T>(RectF32 anchor, DropdownMenuState<T> state,
        float fontSize, RGBAColor32? textColor, RGBAColor32? background,
        RGBAColor32? highlight, RGBAColor32? backdrop,
        float maxHeight, DockSide side, Func<Node, Node>? card)
    {
        var rowHeight = fontSize * 1.8f;
        var rowPadding = fontSize * 0.5f;

        var rows = new Node[state.Items.Length];
        for (var i = 0; i < state.Items.Length; i++)
        {
            var item = state.Items[i];
            // Captured per row. Closing over the loop variable itself would hand every row the final index.
            var index = i;

            // EVERY row declares its hit and its handler, disabled or not. .Disabled() strips the
            // handler and marks the region; it does not create one. A disabled row declared without a hit
            // would register nothing, so the press would fall through to the backdrop and CLOSE the menu --
            // which reads as "it took my click and did nothing", the precise dead-end the disabled state
            // exists to remove. TrySelect refuses a disabled index anyway, so the swallow is the point.
            var row = Text(item.Label, fontSize, textColor)
                .RowH(rowHeight)
                .PadX(rowPadding)
                .Clickable(new HitResult.ListItemHit(DropdownMenuState<T>.ListId, index),
                    _ => state.TrySelect(index));

            if (item.IsEnabled)
            {
                if (highlight is { } hoverColour)
                {
                    row = row.BgHover(hoverColour);
                }

                if (item.Tooltip is { Length: > 0 } tooltip)
                {
                    row = row.WithTooltip(tooltip);
                }
            }
            else
            {
                row = row.Disabled(item.Tooltip ?? string.Empty);
            }

            rows[i] = row;
        }

        // A row is the atom, so the keyboard's EnsureVisible and the wheel's step count rows; the
        // engine converts it to surface units when it feeds the controller. Stated on every build for
        // the same reason the rows are: the menu's font may have changed since the last one.
        state.Scroll.AtomDesignUnits = rowHeight;
        // As wide as the anchor, for a menu under its button; an anchor with no width (a point: where a context menu was
        // pressed) gives a menu as wide as its widest entry, which the engine measures within the space the popover has,
        // so a long entry on a small window is trimmed rather than run off the edge.
        var list = VStack(rows).W(anchor.Size.X > 0f ? Sizing.Fixed(anchor.Size.X) : Sizing.Auto).WithScroll(state.Scroll);

        if (maxHeight > 0f)
        {
            list = list.HClamp(0f, maxHeight);
        }

        if (background is { } menuColour)
        {
            list = list.Bg(menuColour);
        }

        if (card is not null)
        {
            list = card(list);
        }

        return Popover(anchor, list, state.Popover, side, backdrop);
    }

    /// <summary>
    /// A themed dropdown menu: the same popover as the plain <see cref="Dropdown{T}(RectF32, DropdownMenuState{T}, float, RGBAColor32?, RGBAColor32?, RGBAColor32?, RGBAColor32?, float, DockSide)"/>,
    /// drawn from <see cref="ControlStyles.DropdownOrDerived"/>: a raised surface (<see cref="UiPalette.RaisedBg"/>, the
    /// theme's corner radius and shadow, a surface that takes the pointer from what it covers), rows in body text with a
    /// <see cref="UiPalette.ControlFillHover"/> highlight, in the theme's font size. An explicit colour wins over the theme.
    /// </summary>
    public static Node Dropdown<T>(RectF32 anchor, DropdownMenuState<T> state, UiTheme theme,
        float maxHeight = 0f, DockSide side = DockSide.Bottom,
        RGBAColor32? textColor = null, RGBAColor32? background = null, RGBAColor32? highlight = null, RGBAColor32? backdrop = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theme);
        var style = theme.Controls.DropdownOrDerived;
        return DropdownCore(anchor, state, theme.Metrics.BaseFontSize,
            textColor ?? style.RowInk, background ?? style.MenuBg, highlight ?? style.RowHover, backdrop,
            maxHeight, side, list => list.Radius(style.MenuRadius).Shadow(style.MenuShadow).AsSurface());
    }

    /// <summary>How far an explicit box fill is mixed toward the body text for its hover fill.</summary>
    private const float HoverMix = 0.12f;

    /// <summary>
    /// The closed box of a themed dropdown: <paramref name="label"/> on the control fill with the control edge ring at the
    /// control radius, <see cref="UiMetrics.ButtonHeight"/> tall, and a trailing <see cref="IconKind.CaretDown"/> that
    /// becomes <see cref="IconKind.CaretUp"/> while <paramref name="state"/> is open. A press toggles the menu
    /// (<c>state.Popover.Toggle()</c>), so there is no dispatcher line to add. The hit identity is <c>ButtonHit(label)</c>
    /// unless <paramref name="hit"/> says otherwise. <paramref name="fillColor"/> and <paramref name="textColor"/> win over the theme; an explicit fill gets its own hover, the fill mixed a little toward the body text.
    /// </summary>
    public static Node DropdownBox<T>(string label, DropdownMenuState<T> state, UiTheme theme,
        HitResult? hit = null, RGBAColor32? fillColor = null, RGBAColor32? textColor = null, string? tooltip = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theme);
        var style = theme.Controls.DropdownOrDerived;
        var ink = textColor ?? style.BoxInk;
        var fill = fillColor ?? style.BoxFill;
        var fontSize = theme.Metrics.BaseFontSize;
        var caret = state.Popover.IsOpen ? IconKind.CaretUp : IconKind.CaretDown;

        var box = HStack(Text(label, fontSize, ink, TextAlign.Near, TextAlign.Center).WStar(), Icon(caret, fontSize, ink))
            .WithGap(IconWordGap).CrossCenter()
            .Pad(style.PaddingX, 0f).HFixed(theme.Metrics.ButtonHeight).Radius(style.BoxRadius)
            .Bg(fill).Border(style.BoxEdge, style.EdgeWidth, style.BoxRadius)
            .Clickable(hit ?? new HitResult.ButtonHit(label), _ => state.Popover.Toggle())
            .BgHover(fillColor is { } explicit ? RGBAColor32.Mix(explicit, theme.Palette.BodyText, HoverMix) : style.BoxHoverFill).WithCursor(CursorKind.Pointer);
        return tooltip is null ? box : box.WithTooltip(tooltip);
    }
}
