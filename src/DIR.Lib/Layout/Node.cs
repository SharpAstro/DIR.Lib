using System;
using System.Collections.Immutable;
using System.Numerics;

namespace DIR.Lib.Layout;

/// <summary>Main axis of a <see cref="Node.Stack"/>.</summary>
public enum Axis { Vertical, Horizontal }

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

    /// <summary>Inner padding (design units) inset from this node's rect before its children are laid out.</summary>
    public float Padding { get; init; }

    /// <summary>Optional fill painted across this node's whole rect before its children. Since arrange emits
    /// parent-before-children, a container's background lands under its content (panels, rows, headers).</summary>
    public RGBAColor32? Background { get; init; }

    /// <summary>Optional click region bound to this node's arranged rect (draw == hit by construction).
    /// Lives on the node, not the content, so a whole container (a slot row, a panel) is clickable -- not
    /// just leaves. Inner nodes registered later win the hit (top-most), so a button inside a clickable row
    /// still beats the row.</summary>
    public HitResult? Hit { get; init; }

    /// <summary>Optional direct click handler, registered alongside <see cref="Hit"/> when present.</summary>
    public Action<InputModifier>? OnClick { get; init; }

    /// <summary>Children laid out sequentially along <paramref name="Axis"/>, separated by <paramref name="Gap"/> design units.</summary>
    public sealed record Stack(ImmutableArray<Node> Children, Axis Axis = Axis.Vertical, float Gap = 0f) : Node;

    /// <summary>Strips pinned to edges (consumed in order); <paramref name="Fill"/> takes the remainder.</summary>
    public sealed record Dock(ImmutableArray<DockChild> Docked, Node Fill) : Node;

    /// <summary>A uniform N-column grid; cells fill row-major. Column widths split evenly, rows size to the tallest Auto cell.</summary>
    public sealed record Grid(int Columns, ImmutableArray<Node> Cells, float RowGap = 0f, float ColumnGap = 0f) : Node;

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
