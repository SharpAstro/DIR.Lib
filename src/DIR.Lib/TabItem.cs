namespace DIR.Lib;

/// <summary>
/// One tab in a <see cref="TabBar{TSurface}"/>: what it says, what it MEANS, and whether it can be
/// selected right now.
/// </summary>
/// <remarks>
/// <para>
/// <b>The value is carried, not inferred from position.</b> The same argument
/// <see cref="DropdownItem{T}"/> is built on, and a tab strip is where it bites hardest: a bar of bare
/// titles hands back an INDEX, so the host maps it through a switch that has to agree with the title
/// order and nothing checks. Reordering the strip then silently selects the wrong page. Here the tab
/// that was pressed IS the value.
/// </para>
/// <para>
/// <b><see cref="Icon"/> is a string, not a mark named by meaning.</b> That looks like the wrong choice
/// next to <see cref="Layout.Content.Icon"/>, whose whole argument is that a symbol character is
/// unreliable on a pixel surface — but the reason it is unreliable is coverage, and
/// <see cref="PixelWidgetBase{TSurface}.DrawText"/> already resolves that: it splits a run by coverage
/// through the widget's <see cref="PixelWidgetBase{TSurface}.FontFallback"/>, and routes
/// supplementary-plane codepoints to <see cref="PixelWidgetBase{TSurface}.EmojiFontPath"/> even without
/// one. A pictograph tab icon is therefore already drawable, and a mark built from rectangles could not
/// draw one at all — <see cref="Layout.IconKind"/> names a caret or a grid, not a telescope. The cost of
/// the flexible option is that a host picks a glyph its faces do not carry and gets .notdef; the cost of
/// the other is that this whole class of icon is inexpressible.
/// </para>
/// </remarks>
/// <param name="Label">The text drawn for this tab, and what a host puts in its tooltip or window title.</param>
/// <param name="Value">What selecting it means, handed straight back on <see cref="TabClick{T}"/>.</param>
public readonly record struct TabItem<T>(string Label, T Value)
{
    /// <summary>
    /// Optional glyph drawn before <see cref="Label"/> — an emoji or any character the widget's font
    /// chain covers. Null = a text-only tab, which is every tab the strip drew before items existed.
    /// </summary>
    public string? Icon { get; init; }

    // Stored inverted so that `default(TabItem<T>)` — and a `new TabItem<T>()`, which C# lets through
    // while IGNORING a primary-constructor property initialiser — comes out ENABLED. A plain
    // `{ get; init; } = true` reads correctly and produces a silently unselectable tab for anyone who
    // reaches the parameterless form, which is exactly the failure a default should never manufacture.
    private readonly bool _disabled;

    /// <summary>
    /// Whether the tab can be selected. A disabled tab is still DRAWN, greyed
    /// (<see cref="TabBarColors.DisabledText"/>) and inert: it registers no cursor and reports no press.
    /// Hiding it instead would teach nothing about how to make it available, and would renumber the strip
    /// underneath a drag.
    /// </summary>
    public bool IsEnabled
    {
        get => !_disabled;
        init => _disabled = !value;
    }

    /// <summary>
    /// Extra explanation, and the REASON when <see cref="IsEnabled"/> is false. The bar does not paint it:
    /// a tooltip is drawn OUTSIDE the strip, over whatever is adjacent, and a widget that clips to its own
    /// bounds cannot put it there. <see cref="TabBar{TSurface}.HoveredIndex"/> is how a host knows to.
    /// </summary>
    public string? Tooltip { get; init; }

    /// <summary>
    /// The keyboard binding that reaches this tab -- Ctrl+E for Equipment, and so on down a strip. Null
    /// is a tab with no chord, which is every tab the strip drew before this existed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stated HERE for the reason <see cref="KeyChord"/> gives: a binding on the node is matched against
    /// the PAINTED tree, so the chord of a tab the strip dropped on overflow, or of one that is
    /// <see cref="IsEnabled"/> false, is inert with nothing guarding it. A host's own Ctrl+letter map
    /// fires whatever the window is showing, which is why every one of them grows a check beside each key
    /// and why the check is what goes missing.
    /// </para>
    /// <para>
    /// Before this, the chord had to be put back on AFTER the paint: a consumer overrode
    /// <c>CollectPaintedNodes</c>, found the cells by their hit, and rewrote each node with the chord and
    /// a handler -- the host re-stating what the item already knew, one frame at a time.
    /// </para>
    /// </remarks>
    public KeyChord? Shortcut { get; init; }

    /// <summary>
    /// What selecting this tab DOES, handed the tab's own <see cref="Value"/>. Null leaves selection to
    /// whatever reads the strip's registered regions back, which is how a bar with no callbacks works and
    /// stays the default.
    /// </summary>
    /// <remarks>
    /// This is what makes <see cref="Shortcut"/> usable rather than decorative: the router ACTIVATES the
    /// node a chord names, and a node with nothing bound to it has nothing to activate. Click and chord
    /// then run the same handler, so the two routes cannot drift the way a press path and a key path
    /// written separately do -- which is exactly how a tab once switched on click and did nothing on its
    /// own shortcut.
    /// </remarks>
    public Action<T>? OnSelect { get; init; }

    /// <summary>A tab that cannot be selected, and says why.</summary>
    public static TabItem<T> Disabled(string label, T value, string? reason = null)
        => new(label, value) { IsEnabled = false, Tooltip = reason };

    /// <summary>A tab selectable or not by <paramref name="enabled"/>, carrying <paramref name="reason"/>
    /// when it is not — the shape a caller with a precondition check actually has.</summary>
    public static TabItem<T> When(bool enabled, string label, T value, string? reason = null)
        => new(label, value) { IsEnabled = enabled, Tooltip = enabled ? null : reason };
}

/// <summary>
/// A press that landed on a tab: which one, what it means, and whether the ✕ inside it was hit rather
/// than its body.
/// </summary>
/// <remarks>
/// Top-level and generic over the item's value alone, like <see cref="TabBarRegions"/> and for the same
/// reason: a tab's meaning has nothing to do with the surface the strip is painted on, so a consumer
/// writes <c>TabClick&lt;GuiTab&gt;</c> rather than <c>TabBar&lt;VulkanContext&gt;.TabClick&lt;GuiTab&gt;</c>.
/// </remarks>
/// <param name="Index">Position in the list that was rendered — for a host that reorders by position.</param>
/// <param name="Value">The pressed item's <see cref="TabItem{T}.Value"/>.</param>
/// <param name="Close">True if the press landed on the ✕ rather than the tab body.</param>
public readonly record struct TabClick<T>(int Index, T Value, bool Close);
