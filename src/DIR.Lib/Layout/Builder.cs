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

    /// <summary>A text leaf. Styling (colour/alignment) is intrinsic to the run, so it is set here at creation.</summary>
    public static Node Text(string value, float fontSize = 14f, RGBAColor32? color = null,
        TextAlign hAlign = TextAlign.Near, TextAlign vAlign = TextAlign.Center)
        => new Node.Leaf(new Content.Text(value, fontSize)
        {
            Color = color ?? new RGBAColor32(0xff, 0xff, 0xff, 0xff),
            HAlign = hAlign,
            VAlign = vAlign,
        });

    /// <summary>A fixed-size box (icon/swatch/separator). Transparent <paramref name="color"/> (the default) is a pure spacer.</summary>
    public static Node Box(float width, float height, RGBAColor32? color = null)
        => new Node.Leaf(new Content.Box(width, height) { Color = color ?? default });

    /// <summary>An app-drawn escape-hatch leaf (chart/sky map/text input). Pair with <c>Star</c> sizing to fill; set <paramref name="key"/> to route multiple fills.</summary>
    public static Node Fill(float minWidth = 0f, float minHeight = 0f, string? key = null)
        => new Node.Leaf(new Content.Fill(minWidth, minHeight, key));

    /// <summary>A transparent zero-intrinsic box -- a pure spacer; size it with <c>.ColW()</c> / <c>.HFixed()</c> / a <c>Star</c> weight.</summary>
    public static Node Spacer() => new Node.Leaf(new Content.Box(0f, 0f));

    // ---- Containers ----

    /// <summary>Children stacked top-to-bottom. Set the inter-child gap with <c>.Gap(g)</c>.</summary>
    public static Node VStack(params ReadOnlySpan<Node> children)
        => new Node.Stack(ImmutableArray.Create(children), Axis.Vertical);

    /// <summary>Children laid left-to-right. Set the inter-child gap with <c>.Gap(g)</c>.</summary>
    public static Node HStack(params ReadOnlySpan<Node> children)
        => new Node.Stack(ImmutableArray.Create(children), Axis.Horizontal);

    /// <summary>A uniform N-column grid; cells fill row-major. Set gaps with <c>.Gaps(rowGap, columnGap)</c>.</summary>
    public static Node Grid(int columns, params ReadOnlySpan<Node> cells)
        => new Node.Grid(columns, ImmutableArray.Create(cells));

    /// <summary>Children flow left-to-right and wrap to the next line when out of width (toolbars / chip
    /// rows on narrow surfaces). Set gaps with <c>.WithGap(g)</c> / <c>.WithLineGap(g)</c>.</summary>
    public static Node WrapH(params ReadOnlySpan<Node> children)
        => new Node.Wrap(ImmutableArray.Create(children), Axis.Horizontal);

    /// <summary>Children flow top-to-bottom and wrap to the next column when out of height.</summary>
    public static Node WrapV(params ReadOnlySpan<Node> children)
        => new Node.Wrap(ImmutableArray.Create(children), Axis.Vertical);

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
}
