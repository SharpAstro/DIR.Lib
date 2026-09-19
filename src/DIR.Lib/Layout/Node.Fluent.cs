using System;

namespace DIR.Lib.Layout;

/// <summary>
/// The fluent half of the layout DSL: chainable modifiers that set the chrome a hand-written tree would
/// otherwise put in an object-initializer block. These are <i>instance</i> methods (not extensions) because
/// we own <see cref="Node"/> -- so the chain works on any node value with no <c>using</c> beyond the one that
/// already brings the <c>Layout</c> namespace into view. Each is a single polymorphic <c>this with { ... }</c>
/// over a base-declared property, so it preserves the runtime node kind (Stack/Leaf/Dock/...) and returns a
/// <see cref="Node"/> for further chaining. Pure functional transforms -- the tree stays immutable + declarative.
/// </summary>
public abstract partial record Node
{
    // ---- Single-axis sizing ----

    /// <summary>Set the horizontal sizing explicitly.</summary>
    public Node W(Sizing width) => this with { Width = width };

    /// <summary>Set the vertical sizing explicitly.</summary>
    public Node H(Sizing height) => this with { Height = height };

    /// <summary>Fix the width to <paramref name="designUnits"/>.</summary>
    public Node WFixed(float designUnits) => this with { Width = Sizing.Fixed(designUnits) };

    /// <summary>Make the width proportional (star) with the given <paramref name="weight"/>, optionally
    /// clamped to [<paramref name="min"/>, <paramref name="max"/>] design units (0 = unclamped bound).</summary>
    public Node WStar(float weight = 1f, float min = 0f, float max = 0f) => this with { Width = Sizing.Star(weight, min, max) };

    /// <summary>Shrink the width to content.</summary>
    public Node WAuto() => this with { Width = Sizing.Auto };

    /// <summary>Fix the height to <paramref name="designUnits"/>.</summary>
    public Node HFixed(float designUnits) => this with { Height = Sizing.Fixed(designUnits) };

    /// <summary>Make the height proportional (star) with the given <paramref name="weight"/>, optionally
    /// clamped to [<paramref name="min"/>, <paramref name="max"/>] design units (0 = unclamped bound).</summary>
    public Node HStar(float weight = 1f, float min = 0f, float max = 0f) => this with { Height = Sizing.Star(weight, min, max) };

    /// <summary>Shrink the height to content.</summary>
    public Node HAuto() => this with { Height = Sizing.Auto };

    /// <summary>Clamp the resolved width to [<paramref name="min"/>, <paramref name="max"/>] design units
    /// (0 = unclamped bound), preserving the current kind. No-op on a Fixed width (explicit wins).</summary>
    public Node WClamp(float min, float max = 0f) => this with { Width = Width with { Min = min, Max = max } };

    /// <summary>Clamp the resolved height to [<paramref name="min"/>, <paramref name="max"/>] design units
    /// (0 = unclamped bound), preserving the current kind. No-op on a Fixed height (explicit wins).</summary>
    public Node HClamp(float min, float max = 0f) => this with { Height = Height with { Min = min, Max = max } };

    // ---- Common combinations ----

    /// <summary>Star on both axes -- fill the parent cell (value cells, panes).</summary>
    public Node Stretch() => this with { Width = Sizing.Star(), Height = Sizing.Star() };

    /// <summary>A full-width row of fixed height (<c>Width=Star, Height=Fixed</c>) -- the dominant row pattern.</summary>
    public Node RowH(float designUnits) => this with { Width = Sizing.Star(), Height = Sizing.Fixed(designUnits) };

    /// <summary>A fixed-width column that stretches vertically (<c>Width=Fixed, Height=Star</c>) -- pads, buttons.</summary>
    public Node ColW(float designUnits) => this with { Width = Sizing.Fixed(designUnits), Height = Sizing.Star() };

    // ---- Chrome ----

    /// <summary>Paint a background across this node's whole arranged rect (under its children).</summary>
    public Node Bg(RGBAColor32 color) => this with { Background = color };

    /// <summary>Paint <paramref name="color"/> instead of <see cref="Node.Bg"/> while the pointer is over
    /// this node. See <see cref="Node.HoverBackground"/> — the host supplies the pointer and repaints on
    /// motion; without one this is inert.</summary>
    public Node BgHover(RGBAColor32 color) => this with { HoverBackground = color };

    /// <summary>Paint <paramref name="color"/> instead of <see cref="Node.Bg"/> while the keyboard
    /// cursor is on this node. See <see cref="Node.FocusBackground"/> — it resolves against this node's
    /// <see cref="Node.Hit"/>, so a row needs no state of its own to be navigable.</summary>
    public Node BgFocus(RGBAColor32 color) => this with { FocusBackground = color };

    /// <summary>Round the corners of this node's <see cref="Bg"/> (and a <see cref="Content.Box"/> leaf's
    /// own fill) by <paramref name="designUnits"/>. Chrome only -- arrange is unchanged, so a rounded node
    /// occupies exactly the rect a square one would. See <see cref="CornerRadius"/> for how each surface
    /// approximates it.</summary>
    public Node Radius(float designUnits) => this with { CornerRadius = designUnits };

    /// <summary>Inset this node's children by <paramref name="designUnits"/> of inner padding.</summary>
    public Node Pad(float designUnits) => this with { Padding = designUnits };

    /// <summary>Padding stated per axis: <paramref name="across"/> left and right, <paramref name="down"/>
    /// above and below. What a fixed-height bar wants — see <see cref="Node.PaddingY"/>.</summary>
    public Node Pad(float across, float down) => this with { Padding = across, PaddingY = down };

    /// <summary>Horizontal padding only, with nothing added above or below.</summary>
    public Node PadX(float designUnits) => this with { Padding = designUnits, PaddingY = 0f };

    /// <summary>Set where a <see cref="Node.Stack"/> places its children across its own axis.</summary>
    public Node Align(CrossAlign align) => this with { CrossAlign = align };

    /// <summary>
    /// Centre this stack's children across its axis: a row's controls vertically, a column's horizontally.
    /// The common case, and the one that otherwise gets re-derived at the call site as padding or a spacer
    /// sandwich.
    /// </summary>
    public Node CrossCenter() => this with { CrossAlign = Layout.CrossAlign.Center };

    /// <summary>Bind a click region (and optional handler) to this node's whole rect -- draw == hit by construction.</summary>
    public Node Clickable(HitResult? hit, Action<InputModifier>? onClick = null, CursorKind? cursor = null)
        => this with { Hit = hit, OnClick = onClick, Cursor = cursor };

    /// <summary>
    /// Bind a PRESS to this node's whole rect: <paramref name="onPress"/> is told where the press landed
    /// and returns a <see cref="DragCapture"/> to own the gesture until the button comes up, or null to
    /// decline. The sibling of <see cref="Clickable"/>, and a node may carry both -- the press wins when
    /// it claims, and otherwise the click fires on release exactly as it does today.
    /// See <see cref="Node.OnPress"/>.
    /// </summary>
    public Node Pressable(HitResult? hit, Func<PointerPress, DragCapture?> onPress, CursorKind? cursor = null)
        => this with { Hit = hit, OnPress = onPress, Cursor = cursor };

    /// <summary>
    /// State what Enter does on this node when that is not what a click does -- Enter pins, a click
    /// selects. Without one, Enter runs the click handler. See <see cref="Node.OnActivate"/>.
    /// </summary>
    public Node Activatable(Action<InputModifier> onActivate) => this with { OnActivate = onActivate };

    /// <summary>Give this node hover text. Named With* like the gap setters, because a bare
    /// <c>Tooltip</c> method cannot shadow the <see cref="Node.Tooltip"/> property it sets.</summary>
    public Node WithTooltip(string text) => this with { Tooltip = text };

    /// <summary>
    /// Declare this node unavailable, and say WHY: the subtree paints dim, the region swallows the press
    /// with a <see cref="CursorKind.NotAllowed"/> cursor, the reason serves as the tooltip, and the list
    /// cursor steps over it. See <see cref="Node.DisabledReason"/>.
    /// </summary>
    public Node Disabled(string reason) => this with { DisabledReason = reason };

    /// <summary>
    /// <see cref="Disabled(string)"/> only when <paramref name="when"/>. The common conditional, spelled
    /// so a caller keeps one chain rather than breaking out of it -- every fluent modifier always SETS a
    /// value, so the <c>if</c> has to go around a re-assignment (see <see cref="Bg"/>).
    /// </summary>
    public Node Disabled(bool when, string reason) => when ? this with { DisabledReason = reason } : this;

    /// <summary>
    /// Declare this node's arranged rect to be <paramref name="controller"/>'s viewport, so a wheel can
    /// reach the innermost list under the pointer and a list stops re-deriving where it was drawn.
    /// On a <see cref="Stack"/> it is a scroll CONTAINER: the children are laid out at their full extent
    /// and slid by the controller's offset, the controller is told what it scrolls over (one surface
    /// unit per atom), and the painter clips the subtree to this rect and registers only what shows.
    /// Cap the stack's extent (<see cref="HClamp"/>) or it measures to its content and never scrolls.
    /// Named With* so it does not shadow the <see cref="Node.Scroll"/> property it sets.
    /// </summary>
    public Node WithScroll(ListScrollController controller) => this with { Scroll = controller };

    /// <summary>
    /// Give this node a keyboard binding, matched against the PAINTED tree. Named With* so it does not
    /// shadow the <see cref="Node.Shortcut"/> property it sets.
    /// </summary>
    public Node WithShortcut(InputKey key, InputModifier mods = InputModifier.None)
        => this with { Shortcut = new KeyChord(key, mods) };

    /// <summary>
    /// <inheritdoc cref="WithShortcut(InputKey, InputModifier)" path="/summary"/>
    /// <para>The <see cref="KeyChord"/> form, for a caller that already HAS one -- a tab or a menu item
    /// that carries its binding -- so the chord is not taken apart and rebuilt to be passed on.</para>
    /// </summary>
    public Node WithShortcut(KeyChord chord) => this with { Shortcut = chord };

    /// <summary>
    /// Make this node the trigger of <paramref name="popover"/>: a press toggles it, a shortcut on the
    /// node toggles it, and while it is open a press on its backdrop that lands here reaches here. See
    /// <see cref="Node.OpensPopover"/>. Pair with <see cref="Clickable"/> or <see cref="Pressable"/> for the
    /// hit; a trigger with neither registers no region and opens nothing.
    /// </summary>
    public Node Opens(PopoverState popover) => this with { OpensPopover = popover };

    /// <summary>
    /// Make this node the backdrop of <paramref name="popover"/>. <see cref="Builder.Popover"/> states it
    /// on the scrim it builds; a hand-built backdrop states it here so a trigger beneath it stays
    /// reachable. See <see cref="Node.DismissesPopover"/>.
    /// </summary>
    public Node Dismisses(PopoverState popover) => this with { DismissesPopover = popover };

    /// <summary>States the pointer's appearance over this node without making it a click target — a
    /// panel's card saying "arrow here", so nothing inside it has to repeat the claim. Named apart from
    /// the <see cref="Node.Cursor"/> property it sets, which a same-named method cannot shadow.</summary>
    public Node WithCursor(CursorKind cursor) => this with { Cursor = cursor };

    /// <summary>Drop this node from the arrangement entirely when a parent <see cref="Stack"/> would give
    /// it a main-axis extent below <paramref name="designUnits"/> -- the freed space redistributes to the
    /// surviving siblings. See <see cref="CollapseThreshold"/>.</summary>
    public Node CollapseBelow(float designUnits) => this with { CollapseThreshold = designUnits };

    // ---- Container-specific (no-op on the wrong kind) ----

    /// <summary>Set the inter-child gap on a <see cref="Stack"/> or <see cref="Wrap"/>; no-op on any other
    /// node. (Named <c>WithGap</c> rather than <c>Gap</c> because both already expose a <c>Gap</c> property.)</summary>
    public Node WithGap(float gap) => this switch
    {
        Stack s => s with { Gap = gap },
        Wrap w => w with { Gap = gap },
        _ => this,
    };

    /// <summary>Set the between-lines gap on a <see cref="Wrap"/>; no-op on any other node.</summary>
    public Node WithLineGap(float lineGap) => this is Wrap w ? w with { LineGap = lineGap } : this;

    /// <summary>
    /// Main-axis extent the FIRST line of a <see cref="Wrap"/> must leave free; later lines run the full
    /// extent. No-op on any other node.
    /// </summary>
    /// <remarks>
    /// Flow around a floated corner item. A <see cref="Dock"/> reserves its strip on every line, which
    /// narrows the wrapped rows too; this reserves it on the first line only. See
    /// <see cref="Wrap.FirstLineReserve"/>.
    /// </remarks>
    public Node WithFirstLineReserve(float reserve)
        => this is Wrap wr ? wr with { FirstLineReserve = reserve } : this;

    /// <summary>
    /// Extra main-axis space before this child in a flow, suppressed when it starts a line. See
    /// <see cref="LeadingGap"/>.
    /// </summary>
    public Node WithLeadingGap(float gap) => this with { LeadingGap = gap };

    /// <summary>
    /// Most lines a <see cref="Wrap"/> may use; children beyond them are dropped entirely. No-op on any
    /// other node. See <see cref="Wrap.MaxLines"/>.
    /// </summary>
    public Node WithMaxLines(int maxLines)
        => this is Wrap wml ? wml with { MaxLines = maxLines } : this;

    /// <summary>Set the row/column gaps on a <see cref="Grid"/>; no-op on any other node.</summary>
    public Node WithGaps(float rowGap, float columnGap) => this is Grid g ? g with { RowGap = rowGap, ColumnGap = columnGap } : this;

    /// <summary>
    /// Size a <see cref="Grid"/>'s rows to their own content instead of splitting the height evenly; no-op on
    /// any other node. Named With* like the gap setters, and because a bare AutoRows would shadow the
    /// record property it sets. See <see cref="Grid.AutoRows"/> -- this is what makes cells push rows rather than
    /// every row shrinking as cells are added.
    /// </summary>
    public Node WithAutoRows(bool autoRows = true) => this is Grid g ? g with { AutoRows = autoRows } : this;

    // ---- Leaf-specific (no-op on the wrong kind) ----

    /// <summary>
    /// Mark a <see cref="Content.Text"/> leaf's run as selectable, so a host that can offer selection does
    /// -- a real span on the web, a native drag-select on a terminal; no-op on any other node. See
    /// <see cref="Content.Text.Selectable"/> for why the run declares this rather than the host deciding.
    /// <para>
    /// A modifier rather than an argument on <see cref="Builder.Text"/> because it is the one piece of a
    /// run's styling that is about the READER rather than about the ink, and because a whole row of
    /// readouts is marked in one pass at the call site that builds them.
    /// </para>
    /// </summary>
    public Node Selectable(bool selectable = true) => this is Leaf { Content: Content.Text run } leaf
        ? leaf with { Content = run with { Selectable = selectable } }
        : this;

    /// <summary>
    /// Size a <see cref="Grid"/>'s columns individually instead of splitting the width evenly; no-op on
    /// any other node. <c>Auto</c> takes the column's own widest cell, <c>Fixed</c> is fixed, <c>Star</c>
    /// shares what is left -- which is what a table is, and what a consumer measuring its own column
    /// stops was writing out. See <see cref="Grid.ColumnSizing"/>.
    /// </summary>
    public Node WithColumns(params ReadOnlySpan<Sizing> columns)
        => this is Grid g ? g with { ColumnSizing = [.. columns] } : this;
}
