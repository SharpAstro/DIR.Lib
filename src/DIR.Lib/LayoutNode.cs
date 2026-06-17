using System;
using System.Collections.Immutable;
using System.Numerics;

namespace DIR.Lib;

/// <summary>Main axis of a <see cref="LayoutNode.Stack"/>.</summary>
public enum LayoutAxis { Vertical, Horizontal }

/// <summary>Edge a docked child is pinned to in a <see cref="LayoutNode.Dock"/>.</summary>
public enum DockSide { Top, Bottom, Left, Right }

/// <summary>How a node (or a docked strip) is sized along an axis.</summary>
public enum SizeKind
{
    /// <summary>An explicit extent in <i>design units</i> (mapped to surface units by the measure context).</summary>
    Fixed,

    /// <summary>Shrink-to-content: the node's intrinsic measured size.</summary>
    Auto,

    /// <summary>Proportional: split the leftover space after Fixed/Auto siblings by <see cref="Sizing.Value"/> weight.</summary>
    Star,
}

/// <summary>
/// The flex story for one axis: <c>Fixed(n)</c> | <c>Auto</c> | <c>Star(weight)</c>. Surface-neutral —
/// <c>Fixed</c>/min values are <i>design units</i> that <see cref="IMeasureContext{T}.ToSurface"/> maps to
/// pixels (x DPI) or character cells. The default is <see cref="Auto"/>.
/// </summary>
public readonly record struct Sizing(SizeKind Kind, float Value)
{
    /// <summary>An explicit extent in design units.</summary>
    public static Sizing Fixed(float designUnits) => new(SizeKind.Fixed, designUnits);

    /// <summary>Shrink-to-content.</summary>
    public static readonly Sizing Auto = new(SizeKind.Auto, 0f);

    /// <summary>Proportional split of leftover space (default weight 1).</summary>
    public static Sizing Star(float weight = 1f) => new(SizeKind.Star, weight);

    public bool IsFixed => Kind == SizeKind.Fixed;
    public bool IsAuto => Kind == SizeKind.Auto;
    public bool IsStar => Kind == SizeKind.Star;
}

/// <summary>A width/height pair in surface coordinate units.</summary>
public readonly record struct Size<T>(T Width, T Height) where T : INumber<T>
{
    public static Size<T> Zero => new(T.Zero, T.Zero);
}

/// <summary>
/// The paintable + hit-testable payload of a <see cref="LayoutNode.Leaf"/>. Surface-neutral: it says
/// <i>what</i> to measure/draw, not <i>how</i> — a per-surface painter interprets the concrete record.
/// The engine only needs <see cref="Text"/>/<see cref="Box"/>/<see cref="Fill"/> to compute intrinsic
/// (Auto) sizes; <see cref="Hit"/> is the click region the painter auto-binds to the arranged rect.
/// </summary>
public abstract record LayoutContent
{
    /// <summary>A text run. Intrinsic size = the measure context's glyph metrics (px) / char count (cells).</summary>
    public sealed record Text(string Value, float FontSize = 14f) : LayoutContent
    {
        /// <summary>Glyph colour (default white). Surface-neutral — Vulkan uses it directly, the TUI maps it to the nearest SGR.</summary>
        public RGBAColor32 Color { get; init; } = new(0xff, 0xff, 0xff, 0xff);

        /// <summary>Horizontal alignment of the text within the leaf's arranged rect.</summary>
        public TextAlign HAlign { get; init; } = TextAlign.Near;

        /// <summary>Vertical alignment of the text within the leaf's arranged rect.</summary>
        public TextAlign VAlign { get; init; } = TextAlign.Center;
    }

    /// <summary>A fixed-size piece (icon, swatch, separator, spacer) — intrinsic size is <paramref name="Width"/> x <paramref name="Height"/> design units. The painter fills it only when <see cref="Color"/> is non-transparent, so a transparent Box is a pure spacer.</summary>
    public sealed record Box(float Width, float Height) : LayoutContent
    {
        /// <summary>Fill colour. Default transparent => the painter draws nothing (spacer).</summary>
        public RGBAColor32 Color { get; init; }
    }

    /// <summary>
    /// An app-drawn escape hatch (chart, sky map, custom widget, text input). Carries only a minimum intrinsic
    /// size in design units; pair with <c>Star</c> sizing to fill available space. The painter draws it via an
    /// app <c>drawFill</c> callback, which receives this instance back -- so when one tree contains several
    /// <see cref="Fill"/> leaves (e.g. a panel with multiple inputs), set <see cref="Key"/> to route each to its
    /// own draw closure (e.g. <c>map[fill.Key]?.Invoke(rect)</c>) without a central switch.
    /// </summary>
    public sealed record Fill(float MinWidth = 0f, float MinHeight = 0f, string? Key = null) : LayoutContent;
}

/// <summary>One pinned strip inside a <see cref="LayoutNode.Dock"/>.</summary>
public readonly record struct DockChild(DockSide Side, LayoutNode Child, Sizing Size);

/// <summary>
/// A data-only declarative layout tree. The engine (<see cref="LayoutEngine"/>) measures and arranges it
/// into rects; a per-surface painter then walks the arranged tree to draw + bind clicks. Keeping the tree
/// as records (not an imperative <c>cursor += h</c> API) is the load-bearing decision: the data-driven OTA
/// panel becomes "build a tree from the content model", and an optional DSL is just another front-end that
/// emits these same records.
/// </summary>
public abstract record LayoutNode
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
    public sealed record Stack(ImmutableArray<LayoutNode> Children, LayoutAxis Axis = LayoutAxis.Vertical, float Gap = 0f) : LayoutNode;

    /// <summary>Strips pinned to edges (consumed in order); <paramref name="Fill"/> takes the remainder.</summary>
    public sealed record Dock(ImmutableArray<DockChild> Docked, LayoutNode Fill) : LayoutNode;

    /// <summary>A uniform N-column grid; cells fill row-major. Column widths split evenly, rows size to the tallest Auto cell.</summary>
    public sealed record Grid(int Columns, ImmutableArray<LayoutNode> Cells, float RowGap = 0f, float ColumnGap = 0f) : LayoutNode;

    /// <summary><paramref name="Base"/> drawn first, <paramref name="Top"/> on top (modal / dropdown / popup). Both fill the same rect.</summary>
    public sealed record Overlay(LayoutNode Base, LayoutNode Top) : LayoutNode;

    /// <summary>A terminal paintable piece.</summary>
    public sealed record Leaf(LayoutContent Content) : LayoutNode;
}

/// <summary>One node placed at an absolute rect by <see cref="LayoutEngine.Arrange{T}"/>. Emitted in
/// pre-order (parent before children, <see cref="LayoutNode.Overlay"/> base-subtree before top-subtree)
/// so a painter that draws in list order gets correct z-stacking.</summary>
public readonly record struct ArrangedNode<T>(LayoutNode Node, Rect<T> Bounds) where T : INumber<T>;
