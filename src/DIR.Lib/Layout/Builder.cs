using System;
using System.Collections.Immutable;

namespace DIR.Lib.Layout;

/// <summary>
/// Terse factory front-end for the declarative <see cref="Node"/> tree -- the DSL the engine was built to
/// expect (see <see cref="Node"/>'s remarks). Each factory emits the same records you could hand-write, so
/// it is pure sugar with no engine involvement: <c>Builder.Text("x")</c> is exactly
/// <c>new Node.Leaf(new Content.Text("x"))</c>. Compose with the fluent modifiers on
/// <see cref="Node"/> (<c>.WStar()</c>, <c>.RowH()</c>, <c>.Bg()</c>, <c>.Clickable()</c>, ...)
/// to set the chrome that otherwise lands in an object-initializer block.
/// <para>
/// Consumers outside <c>DIR.Lib.Layout</c> use it qualified -- <c>Layout.Builder.VStack(...)</c> -- keeping
/// only <c>using DIR.Lib;</c> (which brings the <c>Layout</c> namespace into view); the fluent modifiers are
/// instance methods on <see cref="Node"/>, reachable with no further import. The collision-prone barewords
/// (<c>Node</c>, <c>Content</c>, <c>Size&lt;T&gt;</c>) stay out of consumer scope that way.
/// </para>
/// </summary>
public static class Builder
{
    // ---- Leaf content factories (each returns a Node.Leaf wrapping the content) ----

    /// <summary>
    /// A text leaf. Styling (colour/alignment/trim) is intrinsic to the run, so it is set here at creation.
    /// <paramref name="trim"/> picks which end survives when the run does not fit — <c>Start</c> for a path
    /// or a URL, whose distinguishing part is at the end.
    /// </summary>
    public static Node Text(string value, float fontSize = 14f, RGBAColor32? color = null,
        TextAlign hAlign = TextAlign.Near, TextAlign vAlign = TextAlign.Center,
        TextTrim trim = TextTrim.End, string? widthSample = null)
        => new Node.Leaf(new Content.Text(value, fontSize)
        {
            Color = color ?? new RGBAColor32(0xff, 0xff, 0xff, 0xff),
            WidthSample = widthSample,
            HAlign = hAlign,
            VAlign = vAlign,
            Trim = trim,
        });

    /// <summary>A fixed-size box (icon/swatch/separator). Transparent <paramref name="color"/> (the default) is a pure spacer.</summary>
    public static Node Box(float width, float height, RGBAColor32? color = null)
        => new Node.Leaf(new Content.Box(width, height) { Color = color ?? default });

    /// <summary>
    /// An icon leaf, <paramref name="size"/> design units square, named by meaning so each surface draws it
    /// its own way (rectangles on pixels, a block-element glyph on cells). See <see cref="Content.Icon"/>.
    /// <para>
    /// <b>Leave <paramref name="size"/> unstated</b> and the mark takes its size from the text run it sits
    /// beside: put it in the same <see cref="HStack"/> as a <see cref="Text"/> and the container resolves it
    /// (<see cref="Content.Icon.MatchesText"/>). That is the case a caret in a chip is, and stating the size
    /// there is the label's font size written out a second time. A size is worth stating for a mark that has
    /// no text to match -- an icon-only button -- since there is nothing for the search to find.
    /// </para>
    /// </summary>
    public static Node Icon(IconKind kind, float? size = null, RGBAColor32? color = null)
        => new Node.Leaf(new Content.Icon(kind, size ?? Content.Icon.DefaultSize)
        {
            Color = color ?? new RGBAColor32(0xff, 0xff, 0xff, 0xff),
            MatchesText = size is null,
        });

    /// <summary>
    /// An editable text field -- <c>Builder.TextInput(state, fontSize)</c> is the whole declaration, and the
    /// painter takes care of drawing it, registering its click region, its focus and its I-beam. See
    /// <see cref="Content.TextInput"/> for why this is a leaf rather than a keyed <see cref="Fill"/>.
    /// <para>
    /// Size it at the call site like any other node (<c>.Stretch()</c> inside a labelled row is the common
    /// case); <paramref name="widthSample"/> only decides anything under <c>Auto</c>.
    /// </para>
    /// </summary>
    public static Node TextInput(TextInputState state, float fontSize = 14f,
        TextInputColors? colors = null, string? widthSample = null, IconKind? leadingIcon = null)
        => new Node.Leaf(new Content.TextInput(state, fontSize)
        {
            Colors = colors,
            WidthSample = widthSample,
            LeadingIcon = leadingIcon,
        });

    /// <summary>An app-drawn escape-hatch leaf (chart/sky map). Pair with <c>Star</c> sizing to fill; set <paramref name="key"/> to route multiple fills. A text field has its own <see cref="TextInput"/> factory.</summary>
    public static Node Fill(float minWidth = 0f, float minHeight = 0f, string? key = null)
        => new Node.Leaf(new Content.Fill(minWidth, minHeight, key));

    /// <summary>A transparent zero-intrinsic box -- a pure spacer; size it with <c>.ColW()</c> / <c>.HFixed()</c> / a <c>Star</c> weight.</summary>
    public static Node Spacer() => new Node.Leaf(new Content.Box(0f, 0f));

    // ---- Containers ----

    /// <summary>Children stacked top-to-bottom. Set the inter-child gap with <c>.Gap(g)</c>.</summary>
    public static Node VStack(params ReadOnlySpan<Node> children)
        => new Node.Stack(MatchIconsToText(children), Axis.Vertical);

    /// <summary>Children laid left-to-right. Set the inter-child gap with <c>.Gap(g)</c>.</summary>
    public static Node HStack(params ReadOnlySpan<Node> children)
        => new Node.Stack(MatchIconsToText(children), Axis.Horizontal);

    /// <summary>A uniform N-column grid; cells fill row-major. Set gaps with <c>.Gaps(rowGap, columnGap)</c>.</summary>
    public static Node Grid(int columns, params ReadOnlySpan<Node> cells)
        => new Node.Grid(columns, MatchIconsToText(cells));

    /// <summary>Children flow left-to-right and wrap to the next line when out of width (toolbars / chip
    /// rows on narrow surfaces). Set gaps with <c>.WithGap(g)</c> / <c>.WithLineGap(g)</c>.</summary>
    public static Node WrapH(params ReadOnlySpan<Node> children)
        => new Node.Wrap(MatchIconsToText(children), Axis.Horizontal);

    /// <summary>Children flow top-to-bottom and wrap to the next column when out of height.</summary>
    public static Node WrapV(params ReadOnlySpan<Node> children)
        => new Node.Wrap(MatchIconsToText(children), Axis.Vertical);

    /// <summary><paramref name="layer"/> drawn first, <paramref name="top"/> on top (modal / dropdown / popup).</summary>
    public static Node Overlay(Node layer, Node top) => new Node.Overlay(layer, top);

    /// <summary>Two resizable panes plus a draggable divider; <paramref name="firstExtent"/> is consumer-owned state. See <see cref="Node.Split"/>.</summary>
    public static Node Split(Node first, Node second, Axis axis = Axis.Horizontal,
        float firstExtent = 0f, float dividerThickness = 6f,
        HitResult? dividerHit = null, RGBAColor32? dividerColor = null)
        => new Node.Split(first, second, axis, firstExtent, dividerThickness, dividerHit, dividerColor);

    /// <summary>Strips pinned to edges (see <see cref="Left"/>/<see cref="Right"/>/<see cref="Top"/>/<see cref="Bottom"/>); <paramref name="fill"/> takes the remainder.</summary>
    public static Node Dock(Node fill, params ReadOnlySpan<DockChild> docked)
        => new Node.Dock(ImmutableArray.Create(docked), fill);

    // ---- Dock-side helpers ----

    /// <summary>A left-pinned dock strip of <paramref name="width"/> design units.</summary>
    public static DockChild Left(Node child, float width) => new(DockSide.Left, child, Sizing.Fixed(width));

    /// <summary>A right-pinned dock strip of <paramref name="width"/> design units.</summary>
    public static DockChild Right(Node child, float width) => new(DockSide.Right, child, Sizing.Fixed(width));

    /// <summary>A top-pinned dock strip of <paramref name="height"/> design units.</summary>
    public static DockChild Top(Node child, float height) => new(DockSide.Top, child, Sizing.Fixed(height));

    /// <summary>A bottom-pinned dock strip of <paramref name="height"/> design units.</summary>
    public static DockChild Bottom(Node child, float height) => new(DockSide.Bottom, child, Sizing.Fixed(height));

    // ---- Composed controls (pure Builder sugar over the primitives above) ----

    /// <summary>
    /// A declarative fractional progress bar: a coloured <paramref name="track"/> with a
    /// <paramref name="fill"/> spanning [0, 1] of the width, plus an optional centred
    /// <paramref name="label"/> (e.g. remaining time). Composed purely from Spacer/Overlay/HStack
    /// primitives -- no <c>Fill</c> escape hatch and no bespoke draw closure -- so it is draw==hit,
    /// DPI-scaled by the engine, visible in <c>describe_layout</c>, and renders identically on every
    /// surface (which lets a consumer drop a hand-drawn <c>FillRect</c> gauge). The fractional split is two
    /// <c>Star</c>-weighted spacers, so the fill stays a true fraction of the bar at any width/DPI with no
    /// pixel arithmetic. Size it at the call site with <c>.RowH(barHeight)</c> (or any sizing) -- the
    /// returned node fills whatever rect it is given.
    /// </summary>
    public static Node Progress(
        float fraction, RGBAColor32 track, RGBAColor32 fill,
        string? label = null, float labelFontSize = 14f, RGBAColor32 labelColor = default)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);

        // A full/empty bar is a single coloured box; a partial bar overlays a fractional-width fill (two
        // Star-weighted spacers) on the track. In the partial branch both weights are > 0, so the weight
        // split never divides by a zero total.
        var bar = fraction <= 0f
            ? Spacer().Stretch().Bg(track)
            : fraction >= 1f
                ? Spacer().Stretch().Bg(fill)
                : Overlay(
                    Spacer().Stretch().Bg(track),
                    HStack(
                        Spacer().WStar(fraction).HStar().Bg(fill),
                        Spacer().WStar(1f - fraction).HStar()));

        return label is { Length: > 0 }
            ? Overlay(bar, Text(label, labelFontSize, labelColor, TextAlign.Center, TextAlign.Center))
            : bar;
    }

    // ---- Sizing a mark by the text it sits beside (see Content.Icon.MatchesText) ----

    /// <summary>
    /// Gives every size-less icon among <paramref name="children"/> the size of the text in the same
    /// container, and hands back the array the container is built from. The one place that rule lives.
    /// <para>
    /// Done HERE, while the tree is being built, rather than in the engine or in a painter -- because both
    /// of those see the tree AFTER it is flat. The engine's arrange emits a pre-order list, and a painter
    /// walks that list with no parent and no siblings in reach, so a size resolved during the walk would
    /// have to be resolved a second time by anything else that reads the tree. Resolved at construction it
    /// is simply a number in the node: every surface, every measure pass and a layout dump all read the
    /// same one, and <c>describe_layout</c> prints what will actually be drawn instead of a sentinel.
    /// </para>
    /// <para>
    /// Costs a scan of the direct children per container per frame, and nothing else in the overwhelmingly
    /// common case: no size-less icon among them returns the span verbatim, before any font-size search or
    /// allocation.
    /// </para>
    /// </summary>
    private static ImmutableArray<Node> MatchIconsToText(ReadOnlySpan<Node> children)
    {
        var anyToSize = false;
        foreach (var child in children)
        {
            if (child is Node.Leaf { Content: Content.Icon { MatchesText: true } })
            {
                anyToSize = true;
                break;
            }
        }

        // No mark to size: the container is built from exactly what it was handed.
        if (!anyToSize) return ImmutableArray.Create(children);

        // The run to match. Searched over the WHOLE container, not just the icon's neighbours, because the
        // idioms that wrap a run put it out of sibling reach: a padded label is a one-child stack (padding
        // insets a node's children, so it cannot go on the leaf), and a row of two labels and a caret is
        // just as much "the caret beside that text".
        if (FirstFontSize(children) is not { } fontSize) return ImmutableArray.Create(children);

        var size = fontSize * Content.Icon.TextSizeRatio;
        var resolved = ImmutableArray.CreateBuilder<Node>(children.Length);
        for (var i = 0; i < children.Length; i++)
        {
            resolved.Add(children[i] is Node.Leaf { Content: Content.Icon { MatchesText: true } icon } leaf
                ? leaf with { Content = icon with { Size = size } }
                : children[i]);
        }

        return resolved.MoveToImmutable();
    }

    /// <summary>
    /// The font size of the first text-bearing leaf in <paramref name="nodes"/>, in tree order, or null when
    /// there is none. A field counts: a caret beside one is the same relationship as a caret beside a label.
    /// </summary>
    private static float? FirstFontSize(ReadOnlySpan<Node> nodes)
    {
        foreach (var node in nodes)
        {
            if (FirstFontSize(node) is { } size) return size;
        }

        return null;
    }

    private static float? FirstFontSize(Node node) => node switch
    {
        Node.Leaf { Content: Content.Text text } => text.FontSize,
        Node.Leaf { Content: Content.TextInput field } => field.FontSize,
        Node.Leaf => null,
        Node.Stack stack => FirstFontSize(stack.Children.AsSpan()),
        Node.Wrap wrap => FirstFontSize(wrap.Children.AsSpan()),
        Node.Grid grid => FirstFontSize(grid.Cells.AsSpan()),
        Node.Overlay overlay => FirstFontSize(overlay.Base) ?? FirstFontSize(overlay.Top),
        Node.Split split => FirstFontSize(split.First) ?? FirstFontSize(split.Second),
        Node.Dock dock => FirstDockFontSize(dock),
        _ => null,
    };

    private static float? FirstDockFontSize(Node.Dock dock)
    {
        foreach (var docked in dock.Docked)
        {
            if (FirstFontSize(docked.Child) is { } size) return size;
        }

        return FirstFontSize(dock.Fill);
    }
}
