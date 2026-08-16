using System.Collections.Immutable;
using DIR.Lib.Layout;

namespace DIR.Lib;

/// <summary>
/// Options a tab strip is described with, other than the tabs themselves. Grouped rather than passed as
/// eight parameters, so a surface states its configuration once and reuses it per frame.
/// </summary>
public sealed record TabStripOptions
{
    /// <inheritdoc cref="TabBar{TSurface}.Side"/>
    public TabStripSide Side { get; init; } = TabStripSide.Top;

    /// <inheritdoc cref="TabBar{TSurface}.Sizing"/>
    public TabSizing Sizing { get; init; } = TabSizing.Content;

    /// <summary>Sizes in the painting surface's design units.</summary>
    public required TabStripMetrics Metrics { get; init; }

    /// <inheritdoc cref="TabBar{TSurface}.Colors"/>
    public TabBarColors Colors { get; init; } = new();

    /// <inheritdoc cref="TabStripOverflow"/>
    public TabStripOverflow Overflow { get; init; } = TabStripOverflow.Clip;

    /// <inheritdoc cref="TabLabelDecoration"/>
    public TabLabelDecoration Decoration { get; init; } = TabLabelDecoration.None;

    /// <inheritdoc cref="TabBar{TSurface}.CanCloseTabs"/>
    public bool CanCloseTabs { get; init; } = true;

    /// <summary>
    /// Whether the strip takes the whole extent it was given along the flow axis (default), or is sized
    /// to its tabs.
    /// </summary>
    /// <remarks>
    /// True when the strip IS the bar, so its background runs to the end rather than leaving the tabs
    /// floating on whatever is behind them. False when it is one child of a larger bar -- a terminal
    /// putting a profile name and a clock after the tabs -- where a strip that stretched would push them
    /// off the row.
    /// </remarks>
    public bool FillsAvailable { get; init; } = true;
}

/// <summary>
/// Builds a tab strip as a <see cref="Layout.Node"/> tree — ONE description that a pixel surface paints
/// through <c>PaintLayout</c> and a cell surface through Console.Lib's <c>CellLayout</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the fold-into-one the strip existed in triplicate to motivate: a GPU tab bar, a GPU nav rail
/// and a terminal tab bar shared no code, and every hand-maintained mirror in this family has eventually
/// diverged in a way nothing caught. A tree is what makes one description paintable by surfaces that
/// have nothing else in common — and it is what makes a tab's hit region the rect its label was drawn
/// into, on both, by construction rather than by two pieces of arithmetic agreeing.
/// </para>
/// <para>
/// <b>What differs between surfaces is numbers and two policies, not shape.</b>
/// <see cref="TabStripMetrics"/> carries the numbers; <see cref="TabStripOverflow"/> and
/// <see cref="TabLabelDecoration"/> carry the policies. Everything else — where the accent goes, which
/// edge is ruled, how a disabled tab reports, what a uniform cell contains — is the same on both.
/// </para>
/// </remarks>
public static class TabStripTree
{
    /// <summary>
    /// Describes the strip. The caller arranges and paints the result.
    /// </summary>
    /// <param name="items">The tabs, in order.</param>
    /// <param name="activeIndex">Index of the tab wearing the accent, or -1 for none.</param>
    /// <param name="hoveredIndex">Index of the tab under the pointer, or -1. A cell surface passes -1.</param>
    /// <param name="availableFlow">Extent along the axis tabs advance on — what they have to fit in.</param>
    /// <param name="measureLabel">A label's extent along the flow axis, in the same units as
    /// <paramref name="options"/>'s metrics. Pixels measure glyphs; cells count characters.</param>
    /// <param name="options">Side, sizing, metrics, colours and the two policies.</param>
    /// <param name="onSelect">Invoked with a tab's index when its region is dispatched. Null for a
    /// surface that reads back registered regions instead of using click callbacks.</param>
    public static Node Build<T>(
        IReadOnlyList<TabItem<T>> items,
        int activeIndex,
        int hoveredIndex,
        float availableFlow,
        Func<string, float> measureLabel,
        TabStripOptions options,
        Action<int>? onSelect = null)
    {
        var m = options.Metrics;
        var vertical = options.Side is TabStripSide.Left or TabStripSide.Right;
        var outerAtStart = options.Side is TabStripSide.Top or TabStripSide.Left;
        var uniform = options.Sizing == TabSizing.Uniform;

        // Zero when the strip cannot close tabs, so the box is not reserved either -- that is what makes a
        // non-closable tab NARROWER rather than gapped. A uniform cell has no room for one regardless.
        var closeBox = options.CanCloseTabs && !uniform ? m.CloseBox : 0f;

        var children = ImmutableArray.CreateBuilder<Node>(items.Count + 1);
        var used = 0f;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var active = i == activeIndex;
            var enabled = item.IsEnabled;
            var hovered = enabled && i == hoveredIndex;
            var label = options.Decoration.Apply(item.Label, active);

            var iconExtent = item.Icon is null ? 0f : m.IconBox + m.Pad * 0.5f;
            var extent = uniform
                ? m.Thickness
                : Math.Clamp(measureLabel(label) + iconExtent + m.Pad * 2 + closeBox,
                    m.MinTabExtent, m.MaxTabExtent);

            // The gap BEFORE this tab counts against the budget, or a strip that drops on overflow keeps
            // the tab whose gap is what actually did not fit.
            var cost = extent + (children.Count > 0 ? m.Gap : 0f);

            // Drop rather than clip: a clipped tab leaves a region that is hit but not visible, so a press
            // lands on something the reader cannot see. Absent from the tree is absent from both.
            if (options.Overflow == TabStripOverflow.Drop && used + cost > availableFlow)
            {
                break;
            }

            children.Add(Tab(item, label, i, extent, iconExtent, closeBox,
                active, enabled, hovered, uniform, vertical, outerAtStart, m, options, onSelect));

            used += cost;
            if (options.Overflow == TabStripOverflow.Clip && used >= availableFlow)
            {
                break;   // the rest would be laid out under the clip
            }
        }

        // Trailing empty space carries the bar's own background, so the strip reads as a strip rather than
        // a row of plates floating on whatever is behind it -- but only when the strip IS the bar.
        if (options.FillsAvailable)
        {
            children.Add(Builder.Spacer().Along(vertical, null).Across(vertical, null));
        }

        var tabs = vertical ? Builder.VStack([.. children]) : Builder.HStack([.. children]);
        if (m.Gap > 0f)
        {
            tabs = tabs.WithGap(m.Gap);
        }

        // Along the FLOW axis: fill when the strip is the bar, hug its tabs when it is one child of a
        // larger one. Getting this wrong is not subtle but it is silent -- a Star strip beside any other
        // Star sibling splits the row with it and every fixed-width tab is compressed to fit the half it
        // got, so the strip still draws, still hit-tests, and every tab is simply the wrong size.
        tabs = options.FillsAvailable
            ? tabs.Along(vertical, null)
            : (vertical ? tabs.HAuto() : tabs.WAuto());
        tabs = tabs.Across(vertical, null);

        // The bar's rule against the content it heads: the edge OPPOSITE the accent, so one flag places
        // both and they can never land on the same side.
        if (m.Border <= 0f)
        {
            return tabs.Bg(options.Colors.BarBackground);
        }

        var edge = Builder.Spacer().Bg(options.Colors.Separator).Across(vertical, m.Border);
        var body = outerAtStart
            ? Stack(!vertical, tabs.Across(vertical, null), edge)
            : Stack(!vertical, edge, tabs.Across(vertical, null));

        return body.Bg(options.Colors.BarBackground);
    }

    private static Node Tab<T>(
        TabItem<T> item, string label, int index, float extent, float iconExtent, float closeBox,
        bool active, bool enabled, bool hovered, bool uniform, bool vertical, bool outerAtStart,
        TabStripMetrics m, TabStripOptions options, Action<int>? onSelect)
    {
        var colors = options.Colors;

        // A hovered idle tab takes the ACTIVE plate unless the palette names a hover tone -- see
        // TabBarColors.HoverBackground for why that is the default and when it stops being right.
        var plate = active ? colors.ActiveBackground
                  : hovered ? colors.HoverBackground ?? colors.ActiveBackground
                            : colors.InactiveBackground;

        // One ink for everything the tab says, so a disabled tab greys as a whole rather than greying its
        // label beside a fully-lit glyph.
        var ink = !enabled ? colors.DisabledText
                : active || hovered ? colors.ActiveText
                                    : colors.InactiveText;

        var content = uniform
            ? Builder.Text(item.Icon ?? label, item.Icon is not null ? m.IconSize ?? m.FontSize : m.FontSize,
                    ink, TextAlign.Center, TextAlign.Center)
                .WStar().HStar()
            : Row(item, label, iconExtent, closeBox, ink, m, colors, index, enabled, onSelect);

        // Accent on the OUTER cross edge, content filling the rest. Zero border means no accent at all,
        // which is a cell surface: it cannot rule a fraction of a cell.
        var body = m.Border > 0f && active
            ? (outerAtStart
                ? Stack(!vertical, Accent(vertical, m, colors), content.Across(vertical, null))
                : Stack(!vertical, content.Across(vertical, null), Accent(vertical, m, colors)))
            : content;

        // Separator along the tab's TRAILING flow edge, then the whole thing fixed to its extent.
        var tab = m.Border > 0f
            ? Stack(vertical, body.Along(vertical, null),
                    Builder.Spacer().Bg(colors.Separator).Along(vertical, m.Border))
            : body;

        return tab
            .Along(vertical, extent)
            .Across(vertical, null)
            .Bg(plate)
            .Clickable(
                new HitResult.ListItemHit(enabled ? TabBarRegions.Tabs : TabBarRegions.DisabledTabs, index),
                onSelect is null ? null : _ => onSelect(index),
                enabled ? CursorKind.Pointer : null);
    }

    /// <summary>Icon, label and ✕ laid out ACROSS the tab — horizontally in screen terms on every side,
    /// which is what "upright content" means for a vertical strip.</summary>
    private static Node Row<T>(
        TabItem<T> item, string label, float iconExtent, float closeBox, RGBAColor32 ink,
        TabStripMetrics m, TabBarColors colors, int index, bool enabled, Action<int>? onSelect)
    {
        var parts = ImmutableArray.CreateBuilder<Node>(6);
        parts.Add(Builder.Spacer().WFixed(m.Pad).HStar());

        if (item.Icon is not null)
        {
            parts.Add(Builder.Text(item.Icon, m.IconSize ?? m.FontSize, ink, TextAlign.Center, TextAlign.Center)
                .WFixed(m.IconBox).HStar());
            parts.Add(Builder.Spacer().WFixed(m.Pad * 0.5f).HStar());
        }

        // Trim.End: the engine owns the rect, the run owns which half of itself carries the meaning.
        parts.Add(Builder.Text(label, m.FontSize, ink, TextAlign.Near, TextAlign.Center, TextTrim.End)
            .WStar().HStar());

        if (closeBox > 0f && enabled)
        {
            parts.Add(Builder.Spacer().WFixed(m.Pad * 0.1f).HStar());
            parts.Add(Builder.Text("×", m.FontSize, colors.CloseMark, TextAlign.Center, TextAlign.Center)
                .WFixed(closeBox).HStar()
                .Clickable(new HitResult.ListItemHit(TabBarRegions.CloseButtons, index), null, CursorKind.Pointer));
            parts.Add(Builder.Spacer().WFixed(m.Pad * 0.4f).HStar());
        }
        else
        {
            // The width the ✕ would have taken stays reserved when the strip closes tabs but THIS tab is
            // disabled, so disabling a tab does not resize it.
            parts.Add(Builder.Spacer().WFixed(closeBox > 0f ? closeBox + m.Pad * 0.5f : m.Pad).HStar());
        }

        return Builder.HStack([.. parts]).WStar().HStar();
    }

    private static Node Accent(bool vertical, TabStripMetrics m, TabBarColors colors)
        => Builder.Spacer().Bg(colors.ActiveAccent).Across(vertical, m.Border * 2);

    private static Node Stack(bool horizontal, params ReadOnlySpan<Node> children)
        => horizontal ? Builder.HStack(children) : Builder.VStack(children);

    /// <summary>Sizes a node along the flow axis — width on a horizontal strip, height on a vertical one.
    /// Null means Star (take what is left).</summary>
    private static Node Along(this Node node, bool vertical, float? extent)
        => vertical
            ? (extent is { } h ? node.HFixed(h) : node.HStar())
            : (extent is { } w ? node.WFixed(w) : node.WStar());

    /// <summary>Sizes a node across the flow axis. Null means Star.</summary>
    private static Node Across(this Node node, bool vertical, float? extent)
        => vertical
            ? (extent is { } w ? node.WFixed(w) : node.WStar())
            : (extent is { } h ? node.HFixed(h) : node.HStar());
}
