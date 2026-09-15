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

    /// <summary>
    /// Fill painted instead of <see cref="Background"/> while the pointer is inside this node's arranged
    /// rect. Null (default) is inert: a node without it paints as before, whatever the pointer is doing.
    /// <para>
    /// Resolved at PAINT time against the rect the node was actually arranged into, which is the whole
    /// point of it living here: a consumer computing a control's rect a second time to hover-test it has
    /// no way to stay in step with the engine, and drifts the moment padding, a spacer or a row count
    /// changes. It is the same guarantee <see cref="Hit"/> already gets by being bound to that rect.
    /// </para>
    /// <para>
    /// The pointer comes from the widget (<c>PixelWidgetBase.Pointer</c>), which the host sets — a host
    /// that never sets one has no hover, and every node keeps its ordinary background. A host that does
    /// must repaint on pointer motion, since motion is not otherwise a reason to draw a frame and the
    /// highlight would sit lit behind a cursor that has left.
    /// </para>
    /// </summary>
    public RGBAColor32? HoverBackground { get; init; }

    /// <summary>
    /// Background to paint instead of <see cref="Background"/> while the KEYBOARD cursor is on this
    /// node — the same idea as <see cref="HoverBackground"/>, for the other pointing device.
    /// <para>
    /// The cursor is a position in a list the tree has already declared: it is on this node when
    /// <see cref="Hit"/> is a <see cref="HitResult.ListItemHit"/> whose list and index the widget's
    /// <c>PixelWidgetBase.ListCursor</c> names. So a row states where it is once, as the click binding
    /// it already needs, and is navigable by that alone.
    /// </para>
    /// <para>
    /// <b>A row that is not clickable cannot be reached.</b> It registers no region, so the cursor
    /// steps over it without anything saying so — which is the whole of what a caller would otherwise
    /// write as a "can this row be acted on" predicate beside the list, and keep in step by hand.
    /// </para>
    /// </summary>
    public RGBAColor32? FocusBackground { get; init; }

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

    /// <summary>
    /// A press handler that is told WHERE it landed and may claim the gesture that follows, set via
    /// <see cref="Pressable"/>. Returning a <see cref="DragCapture"/> says "mine until the button comes
    /// up"; returning null declines, and the press falls through to <see cref="OnClick"/> as before.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="OnClick"/> for a control whose behaviour depends on where inside
    /// itself it was pressed -- a slider, a scrub bar, a divider. <see cref="OnClick"/> carries no
    /// position, so every one of those instead re-derived its own track rect beside the paint and armed a
    /// flag from the host's dispatcher: three branches of one gesture in three places, over a cached rect
    /// the arranged one is free to disagree with. With the press on the node, "draw == hit" becomes
    /// "draw == drag". See <see cref="PointerPress"/>.
    /// </remarks>
    public Func<PointerPress, DragCapture?>? OnPress { get; init; }

    /// <summary>
    /// What Enter does on this node when it differs from a click, set via <see cref="Activatable"/>.
    /// Null (the default) means the two are the same thing and Enter runs <see cref="OnClick"/>.
    /// </summary>
    /// <remarks>
    /// A list whose Enter is not its click is ordinary rather than exotic -- a planner row where Enter
    /// pins the target and a click merely selects it -- and until a row could declare both,
    /// <c>PixelWidgetBase.ActivateListCursor</c> was all-or-nothing on the click handler, so such a list
    /// kept its Enter by hand and the cursor could not own the whole keyboard contract of a list.
    /// </remarks>
    public Action<InputModifier>? OnActivate { get; init; }

    /// <summary>
    /// Hover text for this node, set via <see cref="WithTooltip"/>. A node with a tooltip and no
    /// <see cref="Hit"/> still registers a region -- inert to presses -- because a statement about what
    /// is under the pointer has nowhere else to live.
    /// </summary>
    /// <remarks>
    /// Declared here rather than painted by each consumer, for the reason <see cref="Cursor"/> is: the
    /// region list already knows what is under the pointer. Three separate tooltip painters were counted
    /// in one consumer, none of them with a hover delay, over two declarations (<c>TabItem.Tooltip</c>,
    /// <c>DropdownItem.Tooltip</c>) this library owned and refused to paint.
    /// </remarks>
    public string? Tooltip { get; init; }

    /// <summary>
    /// Why this node cannot be acted on, set via <see cref="Disabled(string)"/>. Null (the default) is an
    /// ordinary node and paints exactly as before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A REASON rather than a flag, because the two facts are one: something a reader cannot press with
    /// no explanation teaches nothing about how to make it pressable, and the explanation belongs where
    /// the press was refused rather than somewhere they reach by making the choice that just failed. The
    /// reason serves as the node's <see cref="Tooltip"/> when it has no other.
    /// </para>
    /// <para>
    /// The painter dims the subtree's text and icons toward the background they are drawn on
    /// (<c>PixelWidgetBase.DimTowards</c>, the rule <c>RenderDropdownMenu</c> has always used for a
    /// disabled row), and registers the region with no click, no press and a
    /// <see cref="CursorKind.NotAllowed"/> cursor. Registered rather than omitted so the press is
    /// SWALLOWED: a disabled row that let the press through to the backdrop behind it would dismiss the
    /// panel, which is to say it would behave exactly like a working one. The list cursor steps over it.
    /// </para>
    /// </remarks>
    public string? DisabledReason { get; init; }

    /// <summary>Whether this node was declared disabled -- <see cref="DisabledReason"/> is stated.</summary>
    public bool IsDisabled => DisabledReason is not null;

    /// <summary>
    /// The scroll controller whose viewport is this node's arranged rect, set via
    /// <see cref="WithScroll"/>. The painter binds the rect
    /// (<see cref="ListScrollController.BindViewport"/>) and registers it as the controller's region, so
    /// a wheel can be delivered to the innermost scrollable under the pointer.
    /// </summary>
    /// <remarks>
    /// The rows and the row height stay the consumer's, stated through
    /// <see cref="ListScrollController.SetExtent"/>; only the viewport moves here, because only the
    /// engine knows it. What it removes is the "is the pointer over my rect" test every wheel handler was
    /// making for itself -- seventeen of them across eleven files in one consumer, each a second
    /// derivation of a rect the arrange pass already held.
    /// </remarks>
    public ListScrollController? Scroll { get; init; }

    /// <summary>
    /// A keyboard binding that reaches this node, set via <see cref="WithShortcut"/>. Inert in measure
    /// and in paint: the router matches it against the PAINTED tree, so a shortcut on a panel that is not
    /// on screen is inert without anyone saying so.
    /// </summary>
    /// <remarks>
    /// What a match DOES depends on the node -- a text-input leaf takes the keyboard, a clickable node is
    /// clicked, a node with <see cref="OnActivate"/> is activated. Whether it fires at all while a field
    /// has the keyboard is <see cref="KeyChord.BeatsFocusedField"/>, stated once, on the chord.
    /// </remarks>
    public KeyChord? Shortcut { get; init; }

    /// <summary>
    /// Marks this node as the root of a popover, so the painter honours its open state rather than the
    /// consumer doing it. Set by <see cref="Builder.Popover"/>; null on every ordinary node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A closed popover paints NOTHING, itself and its whole subtree, and an open one additionally claims
    /// the keyboard and confines the pointer to its content. That is the entire five-obligation checklist
    /// <see cref="PopoverState"/> describes, moved from every consumer into one place, and it is stated on
    /// the node because the node is the thing that knows whether it was painted.
    /// </para>
    /// <para>
    /// It is INERT in measure and arrange, exactly like <see cref="Shortcut"/>: a closed popover is still
    /// measured and still arranged, and only the paint skips it. That costs a little arithmetic on a closed
    /// popover and buys the property that opening one cannot change the layout of anything around it, which
    /// is what a floating overlay is for.
    /// </para>
    /// </remarks>
    public PopoverState? Popover { get; init; }

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
        bool AutoRows = false) : Node
    {
        /// <summary>
        /// Per-column sizing, set via <see cref="WithColumns"/>. Empty (the default) is the even split
        /// this grid has always done, so an untouched grid arranges byte-identically.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Auto</c> measures a column to its own widest cell, <c>Fixed</c> is fixed, and
        /// <c>Star</c> shares whatever the fixed and auto columns leave. That is what a TABLE is: a
        /// label column as wide as its longest label and a value column taking the rest. Without it a
        /// consumer measures the column stops itself and places the cells by hand, which is the one
        /// container that kept re-deriving the arithmetic the engine already owns.
        /// </para>
        /// <para>
        /// Fewer entries than <see cref="Columns"/> leaves the remaining columns <c>Star(1)</c>; extra
        /// entries are ignored. An init-only property rather than a further positional parameter, for
        /// the reason <see cref="ArrangedNode{T}.Depth"/> is one: an optional parameter added to a
        /// record's primary constructor deletes the old constructor from the assembly.
        /// </para>
        /// </remarks>
        public ImmutableArray<Sizing> ColumnSizing { get; init; } = [];
    }

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

    /// <summary>
    /// One child placed at its own measured size somewhere INSIDE this node's rect, rather than filling it
    /// or taking a share of it — a floating panel over a canvas: a tool palette, a HUD, a chip following a
    /// pointer. Pair it with <see cref="Overlay"/> to put one over content:
    /// <c>Overlay(page, Anchored(palette, DockSide.Right, offsetAlong: y))</c>.
    /// <para>
    /// <b>Why the engine and not the caller.</b> Every consumer that floats a panel writes the same three
    /// things by hand: a switch turning a dock side into a coordinate, the reader's offset along that edge,
    /// and a clamp keeping the thing on screen when the window, the sidebar or the panel itself moves. All
    /// three are arithmetic against a rect the arrange pass already holds, and the switch is where they
    /// drift — a consumer's version pinned the wrong coordinate for one of four sides for as long as that
    /// side existed. Stated here, a floating panel says WHERE it floats and nothing about pixels.
    /// </para>
    /// </summary>
    /// <param name="Side">
    /// The edge the child is pinned to, or null to float free — free meaning both
    /// <paramref name="OffsetAlong"/> and <paramref name="OffsetAcross"/> decide the position, where a
    /// pinned child takes its pinned coordinate from the edge and keeps only the offset along it.
    /// </param>
    /// <param name="OffsetAlong">
    /// Design units along the pinned edge (down a left/right edge, across a top/bottom one), and the
    /// horizontal position when <paramref name="Side"/> is null. <b>Consumer-owned state</b>, exactly as
    /// <see cref="Split.FirstExtent"/> is: the engine only arranges what it is given, so a drag updates
    /// this and the next arrange reflects it.
    /// </param>
    /// <param name="OffsetAcross">The vertical position when <paramref name="Side"/> is null; ignored for a
    /// pinned child, whose across-coordinate IS the edge.</param>
    /// <param name="Margin">Design units kept between the child and the edge it is pinned to, and the
    /// inset its clamp respects on every side.</param>
    /// <param name="Clamp">
    /// Keep the child inside this node's rect (the default). What makes a panel survive a window resize or
    /// a sidebar opening under it, rather than being left stranded off-screen — a consumer doing this by
    /// hand re-clamps every frame for exactly that reason. Set false for a child that is deliberately
    /// allowed to overhang, such as a drag chip tracking a pointer past an edge.
    /// </param>
    /// <param name="Anchor">
    /// A rect to place against INSTEAD of this node's own, in the same surface coordinates the tree is
    /// arranged into, or null for the pre-9.2 behaviour of placing against the parent.
    /// <para>
    /// <b><see cref="Side"/> means the opposite thing when this is set, and that is the point.</b> Against
    /// the parent, a side places the child just INSIDE that edge, which is what pins a panel to the bottom
    /// of a pane. Against an anchor, it places the child just OUTSIDE that edge, because the anchor is a
    /// thing on screen the child must sit beside and not cover: a popover under its button, a tooltip above
    /// its icon. Two different questions that happened to share a word, so the word does what the presence
    /// of an anchor says it does.
    /// </para>
    /// <para>
    /// <see cref="Clamp"/> still clamps into this node's rect, not the anchor's. That combination is the
    /// whole reason this exists in the engine rather than in each consumer: a menu opening under the
    /// rightmost button in a bar has to sit below THAT button and still stay on screen, and the hand-written
    /// version of it was got wrong twice.
    /// </para>
    /// </param>
    public sealed record Anchored(
        Node Child,
        DockSide? Side = null,
        float OffsetAlong = 0f,
        float OffsetAcross = 0f,
        float Margin = 0f,
        bool Clamp = true,
        RectF32? Anchor = null) : Node
    {
        /// <summary>The pre-9.2 shape, kept so an already-compiled consumer keeps binding. A record's
        /// constructor takes its arity from the primary constructor, and a trailing DEFAULT does not help
        /// a caller that was compiled against the shorter one; DIR.Lib 9.1 shipped exactly that mistake on
        /// an input record and broke Console.Lib.</summary>
        public Anchored(Node Child, DockSide? Side, float OffsetAlong, float OffsetAcross, float Margin, bool Clamp)
            : this(Child, Side, OffsetAlong, OffsetAcross, Margin, Clamp, null)
        {
        }

        /// <summary>The six-element deconstruction, for the positional patterns written against it. A
        /// positional pattern resolves by ARITY, so both arities have to exist or every
        /// <c>Anchored(var child, var side, _, _, _, _)</c> stops compiling.</summary>
        public void Deconstruct(out Node child, out DockSide? side, out float offsetAlong,
            out float offsetAcross, out float margin, out bool clamp)
        {
            child = Child;
            side = Side;
            offsetAlong = OffsetAlong;
            offsetAcross = OffsetAcross;
            margin = Margin;
            clamp = Clamp;
        }
    }

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
