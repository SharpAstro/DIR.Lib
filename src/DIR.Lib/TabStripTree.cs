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
/// <summary>
/// A described strip: the tree to paint, plus the two things only the description knows.
/// </summary>
/// <param name="Root">The tree. Arrange and paint it.</param>
/// <param name="HoveredIndex">The tab under the pointer, or -1. A host draws the tooltip; the strip
/// cannot, since a tooltip lands outside its own bounds.</param>
/// <param name="TabsEnd">Extent the tabs actually consumed along the flow axis, for a host placing
/// something immediately after them -- a "+" button belongs to a tab BAR, not to every tab strip.</param>
public readonly record struct TabStrip(Node Root, int HoveredIndex, float TabsEnd);

/// <summary>
/// What <see cref="TabStripTree.Build"/> needs of whatever is supplying the tabs.
/// </summary>
/// <remarks>
/// An interface rather than an <c>IReadOnlyList&lt;TabItem&lt;T&gt;&gt;</c> because the builder never
/// reads a tab's VALUE -- only its label, glyph and whether it can be selected. Taking the list would
/// force <see cref="TabBar{TSurface}"/>'s titles overload to materialise one every frame for a strip that
/// is repainted every frame. Implement it on a struct and the JIT specialises the walk.
/// </remarks>
public interface ITabStripSource
{
    /// <summary>How many tabs.</summary>
    int Count { get; }

    /// <summary>The tab's text.</summary>
    string Label(int index);

    /// <summary>The tab's glyph, or null.</summary>
    string? Icon(int index);

    /// <summary>Whether it can be selected.</summary>
    bool Enabled(int index);
}

/// <summary>Tabs from a list of <see cref="TabItem{T}"/>.</summary>
public readonly struct TabItemsSource<T>(IReadOnlyList<TabItem<T>> items) : ITabStripSource
{
    /// <inheritdoc/>
    public int Count => items.Count;

    /// <inheritdoc/>
    public string Label(int index) => items[index].Label;

    /// <inheritdoc/>
    public string? Icon(int index) => items[index].Icon;

    /// <inheritdoc/>
    public bool Enabled(int index) => items[index].IsEnabled;
}

/// <summary>Tabs from plain titles: all selectable, none with a glyph.</summary>
public readonly struct TabTitlesSource(IReadOnlyList<string> titles) : ITabStripSource
{
    /// <inheritdoc/>
    public int Count => titles.Count;

    /// <inheritdoc/>
    public string Label(int index) => titles[index];

    /// <inheritdoc/>
    public string? Icon(int index) => null;

    /// <inheritdoc/>
    public bool Enabled(int index) => true;
}

public static class TabStripTree
{
    /// <summary>
    /// Describes the strip. The caller arranges and paints the result.
    /// </summary>
    /// <param name="source">The tabs, in order.</param>
    /// <param name="activeIndex">Index of the tab wearing the accent, or -1 for none.</param>
    /// <param name="pointerFlow">Where the pointer is along the flow axis, measured from the strip's own
    /// start, or null when it is outside the strip (or the surface has no pointer). The strip resolves
    /// WHICH tab that is itself, and reports it back on <see cref="TabStrip.HoveredIndex"/>.</param>
    /// <param name="pointerCross">Where the pointer is ACROSS the strip, from its cross start, or null.
    /// Only the ✕'s own plate needs it: the ✕ sits at the tab's right edge in SCREEN terms, which is the
    /// flow end on a horizontal strip and the cross end on a vertical one.</param>
    /// <param name="availableFlow">Extent along the axis tabs advance on — what they have to fit in.</param>
    /// <param name="measureLabel">A label's extent along the flow axis, in the same units as
    /// <paramref name="options"/>'s metrics. Pixels measure glyphs; cells count characters.</param>
    /// <param name="options">Side, sizing, metrics, colours and the two policies.</param>
    /// <param name="onSelect">Invoked with a tab's index when its region is dispatched. Null for a
    /// surface that reads back registered regions instead of using click callbacks.</param>
    public static TabStrip Build<T>(
        IReadOnlyList<TabItem<T>> items,
        int activeIndex,
        float? pointerFlow,
        float? pointerCross,
        float availableFlow,
        Func<string, float> measureLabel,
        TabStripOptions options,
        Action<int>? onSelect = null)
        => Build(new TabItemsSource<T>(items), activeIndex, pointerFlow, pointerCross, availableFlow,
            measureLabel, options, onSelect);

    /// <inheritdoc cref="Build{T}(IReadOnlyList{TabItem{T}}, int, float?, float, Func{string, float}, TabStripOptions, Action{int}?)"/>
    public static TabStrip Build<TSource>(
        TSource source,
        int activeIndex,
        float? pointerFlow,
        float? pointerCross,
        float availableFlow,
        Func<string, float> measureLabel,
        TabStripOptions options,
        Action<int>? onSelect = null)
        where TSource : struct, ITabStripSource
    {
        var m = options.Metrics;
        var vertical = options.Side is TabStripSide.Left or TabStripSide.Right;
        var outerAtStart = options.Side is TabStripSide.Top or TabStripSide.Left;
        var uniform = options.Sizing == TabSizing.Uniform;

        // Zero when the strip cannot close tabs, so the box is not reserved either -- that is what makes a
        // non-closable tab NARROWER rather than gapped. A uniform cell has no room for one regardless.
        var closeBox = options.CanCloseTabs && !uniform ? m.CloseBox : 0f;

        var children = ImmutableArray.CreateBuilder<Node>(source.Count + 1);
        var used = 0f;
        var hoveredIndex = -1;
        var tabsEnd = 0f;

        for (var i = 0; i < source.Count; i++)
        {
            var active = i == activeIndex;
            var enabled = source.Enabled(i);
            var icon = source.Icon(i);
            var label = options.Decoration.Apply(source.Label(i), active);

            var iconExtent = icon is null ? 0f : m.IconBox + m.Pad * 0.5f;
            var extent = uniform
                ? m.Thickness
                : Math.Clamp(measureLabel(label) + iconExtent + m.Pad * 2 + closeBox,
                    m.MinTabExtent, m.MaxTabExtent);

            // The gap BEFORE this tab counts against the budget, or a strip that drops on overflow keeps
            // the tab whose gap is what actually did not fit.
            // Where this tab actually starts, gap included -- so the hover test below and the drop test
            // agree with the arranged position rather than each other.
            var tabStart = used + (children.Count > 0 ? m.Gap : 0f);

            // Drop rather than clip: a clipped tab leaves a region that is hit but not visible, so a press
            // lands on something the reader cannot see. Absent from the tree is absent from both.
            if (options.Overflow == TabStripOverflow.Drop && tabStart + extent > availableFlow)
            {
                break;
            }

            // Resolved HERE rather than taken as a parameter, because the extents it needs are computed in
            // this loop -- and a caller asked for an index would have to recompute them, which is a second
            // copy of the sizing rule. Single pass: a tab's hover only affects that tab. The pattern comes
            // first so `pf` is definitely assigned; a disabled tab is never hovered.
            var hovered = pointerFlow is { } pf && pf >= tabStart && pf < tabStart + extent && enabled;
            if (hovered)
            {
                hoveredIndex = i;
            }

            // The ✕'s own plate, because it is a second target inside the tab and a tab-wide hover says
            // nothing about where its edge is. Resolved in the tab's SCREEN-local x, which is the flow
            // offset on a horizontal strip and the cross offset on a vertical one.
            var closeHovered = false;
            if (hovered && closeBox > 0f)
            {
                var localX = vertical ? pointerCross : pointerFlow - tabStart;
                var tabWidth = vertical ? m.Thickness : extent;
                var closeRight = tabWidth - m.Pad * 0.4f;
                closeHovered = localX is { } lx && lx >= closeRight - closeBox && lx <= closeRight;
            }

            children.Add(Tab(icon, label, i, extent, iconExtent, closeBox,
                active, enabled, hovered, closeHovered, uniform, vertical, outerAtStart, m, options, onSelect));

            used = tabStart + extent;
            tabsEnd = used;
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

        var tabs = FlowStack(vertical, [.. children]);
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
            return new TabStrip(tabs.Bg(options.Colors.BarBackground), hoveredIndex, tabsEnd);
        }

        var edge = Builder.Spacer().Bg(options.Colors.Separator)
            .Across(vertical, m.Border).Along(vertical, null);
        var body = outerAtStart
            ? CrossStack(vertical, tabs.Across(vertical, null), edge)
            : CrossStack(vertical, edge, tabs.Across(vertical, null));

        return new TabStrip(body.Bg(options.Colors.BarBackground), hoveredIndex, tabsEnd);
    }

    private static Node Tab(
        string? icon, string label, int index, float extent, float iconExtent, float closeBox,
        bool active, bool enabled, bool hovered, bool closeHovered, bool uniform, bool vertical, bool outerAtStart,
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
            ? Builder.Text(icon ?? label, icon is not null ? m.IconSize ?? m.FontSize : m.FontSize,
                    ink, TextAlign.Center, TextAlign.Center)
                .WStar().HStar()
            : Row(icon, label, iconExtent, closeBox, ink, m, colors, index, enabled, closeHovered);

        // Accent on the OUTER cross edge, content filling the rest. Zero border means no accent at all,
        // which is a cell surface: it cannot rule a fraction of a cell.
        var body = m.Border > 0f && active
            ? (outerAtStart
                ? CrossStack(vertical, Accent(vertical, m, colors), content.Across(vertical, null))
                : CrossStack(vertical, content.Across(vertical, null), Accent(vertical, m, colors)))
            : content;

        // Separator along the tab's TRAILING flow edge, then the whole thing fixed to its extent.
        var tab = m.Border > 0f
            ? FlowStack(vertical, body.Along(vertical, null).Across(vertical, null),
                    Builder.Spacer().Bg(colors.Separator)
                        .Along(vertical, m.Border).Across(vertical, null))
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
    private static Node Row(
        string? icon, string label, float iconExtent, float closeBox, RGBAColor32 ink,
        TabStripMetrics m, TabBarColors colors, int index, bool enabled, bool closeHovered)
    {
        var parts = ImmutableArray.CreateBuilder<Node>(6);
        parts.Add(Builder.Spacer().WFixed(m.Pad).HStar());

        if (icon is not null)
        {
            parts.Add(Builder.Text(icon, m.IconSize ?? m.FontSize, ink, TextAlign.Center, TextAlign.Center)
                .WFixed(m.IconBox).HStar());
            parts.Add(Builder.Spacer().WFixed(m.Pad * 0.5f).HStar());
        }

        // Trim.End: the engine owns the rect, the run owns which half of itself carries the meaning.
        parts.Add(Builder.Text(label, m.FontSize, ink, TextAlign.Near, TextAlign.Center, TextTrim.End)
            .WStar().HStar());

        if (closeBox > 0f && enabled)
        {
            parts.Add(Builder.Spacer().WFixed(m.Pad * 0.1f).HStar());
            var close = Builder.Text("×", m.FontSize, colors.CloseMark, TextAlign.Center, TextAlign.Center)
                .WFixed(closeBox).HStar();
            if (closeHovered)
            {
                // Separator is the plate: the one role guaranteed to read against both the panel and the
                // header surface, so this needs no colour of its own in either theme.
                close = close.Bg(colors.Separator).Radius(closeBox * 0.25f);
            }

            parts.Add(close.Clickable(
                new HitResult.ListItemHit(TabBarRegions.CloseButtons, index), null, CursorKind.Pointer));
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

    /// <summary>
    /// The active accent: a band of <see cref="TabStripMetrics.Border"/>*2 across, spanning the tab along
    /// the flow axis.
    /// </summary>
    /// <remarks>
    /// BOTH axes are stated, and that is not belt-and-braces. A Spacer's intrinsic size is zero, so an
    /// unstated axis resolves to Auto and the band paints nothing at all -- it is still in the tree, still
    /// arranged, and simply has no width. Every rule in this file was written that way first.
    /// </remarks>
    private static Node Accent(bool vertical, TabStripMetrics m, TabBarColors colors)
        => Builder.Spacer().Bg(colors.ActiveAccent).Across(vertical, m.Border * 2).Along(vertical, null);

    /// <summary>Stacks children along the axis tabs advance on.</summary>
    /// <remarks>
    /// Named for its ROLE in the strip rather than for a screen axis, and that is the whole point: a
    /// <c>Stack(bool horizontal)</c> helper had every one of its three call sites inverted, because the
    /// author is thinking "flow" and "cross" while the parameter asks about x and y. The same mistake
    /// killed a <c>Stretch(bool)</c> helper earlier in this file. Pair with <see cref="Along"/>.
    /// </remarks>
    private static Node FlowStack(bool vertical, params ReadOnlySpan<Node> children)
        => vertical ? Builder.VStack(children) : Builder.HStack(children);

    /// <summary>Stacks children across the strip's thickness. Pair with <see cref="Across"/>.</summary>
    private static Node CrossStack(bool vertical, params ReadOnlySpan<Node> children)
        => vertical ? Builder.HStack(children) : Builder.VStack(children);

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
