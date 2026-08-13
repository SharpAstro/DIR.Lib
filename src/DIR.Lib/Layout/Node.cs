using System;
using System.Collections.Immutable;
using System.Numerics;

namespace DIR.Lib.Layout;

/// <summary>Main axis of a <see cref="Node.Stack"/>.</summary>
public enum Axis { Vertical, Horizontal }

/// <summary>
/// Where a <see cref="Node.Stack"/> places a child ACROSS its axis: for an <see cref="Axis.Horizontal"/>
/// stack, vertically. Only affects a child whose cross-axis sizing is not <c>Star</c>, since a Star child
/// fills the axis and has nowhere to go.
/// </summary>
public enum CrossAlign
{
    /// <summary>Top of a row, left of a column. The long-standing behaviour and the default.</summary>
    Start,

    /// <summary>Centred across the axis: what a row of differently-sized controls almost always wants.</summary>
    Center,

    /// <summary>Bottom of a row, right of a column.</summary>
    End
}

/// <summary>Edge a docked child is pinned to in a <see cref="Node.Dock"/>.</summary>
public enum DockSide { Top, Bottom, Left, Right }

/// <summary>One pinned strip inside a <see cref="Node.Dock"/>.</summary>
public readonly record struct DockChild(DockSide Side, Node Child, Sizing Size);

/// <summary>
/// A declarative layout tree of immutable records. The engine (<see cref="Engine"/>) measures and arranges it
/// into rects; a per-surface painter then walks the arranged tree to draw + bind clicks. Keeping the tree
/// as records (not an imperative <c>cursor += h</c> API) is the load-bearing decision: the data-driven OTA
/// panel becomes "build a tree from the content model", and the <see cref="Builder"/> DSL is just another
/// front-end that emits these same records. The fluent modifiers (<c>.RowH()</c>, <c>.Bg()</c>, ...) that set
/// the chrome are instance methods on this record -- see the partial in <c>Node.Fluent.cs</c>.
/// </summary>
public abstract partial record Node
{
    /// <summary>How this node is sized along the horizontal axis within its parent. Default <see cref="Sizing.Auto"/>.</summary>
    public Sizing Width { get; init; } = Sizing.Auto;

    /// <summary>How this node is sized along the vertical axis within its parent. Default <see cref="Sizing.Auto"/>.</summary>
    public Sizing Height { get; init; } = Sizing.Auto;

    /// <summary>Inner padding (design units) inset from this node's rect before its children are laid out.
    /// Applies to BOTH axes; <see cref="PaddingY"/> overrides it down the vertical.</summary>
    public float Padding { get; init; }

    /// <summary>
    /// Vertical inner padding, when it differs from <see cref="Padding"/>. Null means "the same as
    /// Padding", which is what a symmetric inset wants and what every existing tree gets.
    /// <para>
    /// A fixed-height bar is the case that needs the two apart: a chip inside a 33-unit bar wants ten
    /// units of breathing room either side of its label and nothing above or below it, because there is
    /// nothing above or below to give. Padded symmetrically it gets a three-unit content box, and
    /// anything in there that sizes off its own box — an icon, which is square by the smaller side —
    /// collapses to a stub while the text, which overflows its rect, goes on looking correct. That
    /// asymmetry in the symptom is what makes it worth an axis rather than a caller's spacer sandwich.
    /// </para>
    /// </summary>
    public float? PaddingY { get; init; }

    /// <summary>The vertical inset actually applied: <see cref="PaddingY"/> if stated, else <see cref="Padding"/>.</summary>
    public float PadDown => PaddingY ?? Padding;

    /// <summary>
    /// Where a <see cref="Stack"/> places its children across its own axis: an HStack's children up or down,
    /// a VStack's left or right. Default <see cref="Layout.CrossAlign.Start"/>, which is what a stack has
    /// always done.
    /// <para>
    /// Set on the CONTAINER, like <see cref="Padding"/> and like every other layout system's align-items:
    /// the common case is "centre this row's controls", and per-child alignment would mean repeating it on
    /// each. A child sized <c>Star</c> across the axis fills it and is unaffected.
    /// </para>
    /// <para>
    /// Without this, a Fixed-height button in a taller bar hugs the bar's top, and centring it means either
    /// padding the bar or wrapping every child in a spacer sandwich -- both of which re-derive, at the call
    /// site, a position the engine already knows.
    /// </para>
    /// </summary>
    public CrossAlign CrossAlign { get; init; } = CrossAlign.Start;

    /// <summary>Optional fill painted across this node's whole rect before its children. Since arrange emits
    /// parent-before-children, a container's background lands under its content (panels, rows, headers).</summary>
    public RGBAColor32? Background { get; init; }

    /// <summary>Corner radius in design units for this node's <see cref="Background"/> (and a
    /// <see cref="Content.Box"/> leaf's own fill). 0 (default) is a square corner and paints exactly as
    /// before, so this is inert until asked for.
    /// <para>
    /// Purely a <b>chrome</b> property: arrange does not know about it, so a rounded node occupies and
    /// insets precisely the rect a square one would. Each surface honours it as far as it can -- a pixel
    /// painter through <c>Renderer.FillRoundedRectangle</c>, a cell painter by drawing arc corners
    /// (U+256D..U+2570) since a character grid cannot round by fractions of a cell. A surface that cannot
    /// express it at all just fills square, which is why this is a hint rather than a guarantee.
    /// </para>
    /// Set via <see cref="Radius"/>.</summary>
    public float CornerRadius { get; init; }

    /// <summary>Optional click region bound to this node's arranged rect (draw == hit by construction).
    /// Lives on the node, not the content, so a whole container (a slot row, a panel) is clickable -- not
    /// just leaves. Inner nodes registered later win the hit (top-most), so a button inside a clickable row
    /// still beats the row.</summary>
    public HitResult? Hit { get; init; }

    /// <summary>Optional direct click handler, registered alongside <see cref="Hit"/> when present.</summary>
    public Action<InputModifier>? OnClick { get; init; }

    /// <summary>What the pointer looks like over this node's arranged rect, or null to inherit from
    /// whatever encloses it. Bound to the rect the content was painted in, like <see cref="Hit"/>.</summary>
    public CursorKind? Cursor { get; init; }

    /// <summary>Collapse threshold in design units, honoured by a parent <see cref="Stack"/>: when this
    /// node's resolved main-axis extent lands below the threshold, the node drops out of the arrangement
    /// entirely (not painted, no hit region, no gap) and its space redistributes to the surviving
    /// siblings. The declarative form of "show the strip only when it is at least N tall" -- a squeezed
    /// remnant is unreadable noise, so it collapses instead. 0 (default) = never collapse. Set via
    /// <see cref="CollapseBelow"/>; only a Stack parent honours it (Dock/Grid strips are explicit).</summary>
    public float CollapseThreshold { get; init; }

    /// <summary>Children laid out sequentially along <paramref name="Axis"/>, separated by <paramref name="Gap"/> design units.</summary>
    public sealed record Stack(ImmutableArray<Node> Children, Axis Axis = Axis.Vertical, float Gap = 0f) : Node;

    /// <summary>Strips pinned to edges (consumed in order); <paramref name="Fill"/> takes the remainder.</summary>
    public sealed record Dock(ImmutableArray<DockChild> Docked, Node Fill) : Node;

    /// <summary>A uniform N-column grid; cells fill row-major. Column widths split evenly, rows size to the tallest Auto cell.</summary>
    /// <param name="AutoRows">
    /// When <see langword="false"/> (the default) the grid divides its rect evenly: every row gets an equal
    /// share of the height, so cells stretch to fill and a row cannot be taller than its neighbours.
    /// <para>
    /// When <see langword="true"/> each row instead takes the height its OWN tallest cell needs, and the
    /// grid's intrinsic height is the sum of those rows. That is what makes cards "push" the rows: adding
    /// one adds height rather than shrinking every existing row, and an Auto-height grid inside a stack
    /// reports exactly the height its content needs, so a trailing spacer can absorb the slack. Columns are
    /// still an even split -- only the cross axis becomes content-driven.
    /// </para>
    /// </param>
    public sealed record Grid(
        int Columns,
        ImmutableArray<Node> Cells,
        float RowGap = 0f,
        float ColumnGap = 0f,
        bool AutoRows = false) : Node;

    /// <summary>Children flow along <paramref name="Axis"/> and wrap into a new line when the next child
    /// would overflow the available extent -- the flexbox <c>wrap</c> for toolbars / chip rows on narrow
    /// surfaces (a canvas has no CSS to reflow for it). Each child takes its Fixed/measured main extent
    /// (a <c>Star</c> main is meaningless in a flow and measures as Auto); a line's cross extent is its
    /// tallest child's, and a child with <c>Star</c> cross sizing stretches to that line extent.
    /// <paramref name="Gap"/> separates children within a line, <paramref name="LineGap"/> separates
    /// lines. Intrinsic (Auto) size reflows against the available extent, so an Auto-height wrap grows
    /// taller as its container narrows.</summary>
    public sealed record Wrap(ImmutableArray<Node> Children, Axis Axis = Axis.Horizontal, float Gap = 0f, float LineGap = 0f) : Node;

    /// <summary><paramref name="Base"/> drawn first, <paramref name="Top"/> on top (modal / dropdown / popup). Both fill the same rect.</summary>
    public sealed record Overlay(Node Base, Node Top) : Node;

    /// <summary>
    /// Two resizable panes laid out along <paramref name="Axis"/> with a draggable divider of
    /// <paramref name="DividerThickness"/> design units between them. <paramref name="FirstExtent"/>
    /// (design units) is the first pane's size along the axis and is <b>consumer-owned state</b>: the engine
    /// only arranges given it, so the host updates it from the divider's drag delta and the engine re-arranges
    /// next frame. The divider is emitted as its own node carrying <paramref name="DividerHit"/> (a host hit,
    /// e.g. a resize-handle marker its MouseDown logic recognises) filled with <paramref name="DividerColor"/>,
    /// so the grab region <i>is</i> the drawn bar -- no separate widened-rect arithmetic that can drift.
    /// The leftover space (after the first pane + divider) goes to <paramref name="Second"/>; like
    /// <see cref="Dock"/> a Split expects explicit bounds (pair it with <c>Star</c> sizing to fill).
    /// </summary>
    public sealed record Split(
        Node First,
        Node Second,
        Axis Axis = Axis.Horizontal,
        float FirstExtent = 0f,
        float DividerThickness = 6f,
        HitResult? DividerHit = null,
        RGBAColor32? DividerColor = null) : Node;

    /// <summary>A terminal paintable piece.</summary>
    public sealed record Leaf(Content Content) : Node;
}

/// <summary>One node placed at an absolute rect by <see cref="Engine.Arrange{T}"/>. Emitted in
/// pre-order (parent before children, <see cref="Node.Overlay"/> base-subtree before top-subtree)
/// so a painter that draws in list order gets correct z-stacking.</summary>
public readonly record struct ArrangedNode<T>(Node Node, Rect<T> Bounds) where T : INumber<T>
{
    /// <summary>Nesting depth in the arranged pre-order list (root = 0, each child one deeper). The
    /// list is flat, so this lets a consumer reconstruct the tree -- used by the DEBUG inspector's
    /// describe_layout to print the structure. Painters ignore it; it does not affect arrangement,
    /// and the 2-arg ctor / Deconstruct are unchanged (it is an extra init-only property).</summary>
    public int Depth { get; init; }
}
