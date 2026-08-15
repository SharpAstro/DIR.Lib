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

    /// <summary>Set the row/column gaps on a <see cref="Grid"/>; no-op on any other node.</summary>
    public Node WithGaps(float rowGap, float columnGap) => this is Grid g ? g with { RowGap = rowGap, ColumnGap = columnGap } : this;

    /// <summary>
    /// Size a <see cref="Grid"/>'s rows to their own content instead of splitting the height evenly; no-op on
    /// any other node. Named With* like the gap setters, and because a bare AutoRows would shadow the
    /// record property it sets. See <see cref="Grid.AutoRows"/> -- this is what makes cells push rows rather than
    /// every row shrinking as cells are added.
    /// </summary>
    public Node WithAutoRows(bool autoRows = true) => this is Grid g ? g with { AutoRows = autoRows } : this;
}
