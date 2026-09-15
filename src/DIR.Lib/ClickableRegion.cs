using System;

namespace DIR.Lib;

/// <summary>
/// A clickable region registered during rendering. The hit test walks these
/// in reverse order (last-registered = on top) to find what was clicked.
/// </summary>
/// <param name="Cursor">What the pointer looks like here, or null to leave it to whatever is
/// underneath. Declared beside the click for the reason given on <see cref="CursorKind"/>: the region
/// list already knows what is under the pointer, so a host that answers it separately is maintaining a
/// second, divergent copy of the same knowledge.</param>
public readonly record struct ClickableRegion(
    float X, float Y, float Width, float Height, HitResult Result,
    Action<InputModifier>? OnClick = null, CursorKind? Cursor = null)
{
    // The 9.2 additions are init-only properties rather than further positional parameters, for the
    // reason ArrangedNode.Depth is: an optional parameter added to a record's primary constructor DELETES
    // the old constructor from the assembly, so every already-compiled caller of the 7-argument form
    // would throw MissingMethodException. Init-only properties add nothing to the constructor's identity.

    /// <summary>
    /// The press handler bound from <see cref="Layout.Node.OnPress"/>: it is handed the position, the
    /// button and the click count, and returns a <see cref="DragCapture"/> to claim the gesture until the
    /// button comes up -- or null to decline, leaving the press to <see cref="OnClick"/>.
    /// </summary>
    public Func<PointerPress, DragCapture?>? OnPress { get; init; }

    /// <summary>
    /// What Enter on this row does when it differs from a click, bound from
    /// <see cref="Layout.Node.OnActivate"/>. Null means the two are the same thing, which is the ordinary
    /// case; <c>PixelWidgetBase.ActivateListCursor</c> prefers this and falls back to
    /// <see cref="OnClick"/>.
    /// </summary>
    /// <remarks>
    /// It rides on the REGION rather than being looked up on the node afterwards, so a list assembled
    /// imperatively and a list declared as a tree answer Enter the same way -- the region list is where
    /// both end up, and it is what the cursor already resolves against.
    /// </remarks>
    public Action<InputModifier>? OnActivate { get; init; }

    /// <summary>
    /// The hover text for this region, from <see cref="Layout.Node.Tooltip"/> -- or, for a disabled one,
    /// the reason it is disabled, since the answer to "why can't I press this" belongs where the press
    /// was refused rather than in a panel the reader only reaches by making the choice that just failed.
    /// </summary>
    public string? Tooltip { get; init; }

    /// <summary>
    /// Whether this region is inert by declaration (<see cref="Layout.Node.DisabledReason"/>). It still
    /// takes part in hit testing, which is the point: the press is SWALLOWED rather than falling through
    /// to whatever is behind it, and the list cursor steps over it.
    /// </summary>
    public bool IsDisabled { get; init; }

    /// <summary>
    /// The scroll controller this region's node declared (<see cref="Layout.Node.Scroll"/>), so a wheel
    /// can be delivered to the innermost list under the pointer instead of every list testing the pointer
    /// against a rect of its own.
    /// </summary>
    public ListScrollController? Scroll { get; init; }
}

/// <summary>
/// What a field's paint knows about its own text that a hit test cannot see: the left edge it drew from
/// and the face it drew with. Enough, with <see cref="TextInputRenderer.CaretIndexAt"/>, to turn a pointer
/// position into a character index through the same measurements that placed the caret.
/// </summary>
/// <param name="X">The FIELD's left edge, not the text origin — the insets are
/// <see cref="TextInputRenderer"/>'s to apply, and applying them here would be the second copy.</param>
/// <param name="FontFamily">The face the glyphs were measured and drawn with; empty where no font was
/// configured, which is the same case the paint declines to draw.</param>
/// <param name="LeadingRoom">Room taken by a leading mark, as handed to the paint.</param>
public readonly record struct TextInputGeometry(int X, string FontFamily, float FontSize, float LeadingRoom);

/// <summary>
/// Describes what was hit during a click. Open hierarchy — extend with
/// app-specific subclasses (e.g. SlotHit, SliderHit) in downstream projects.
/// </summary>
public record HitResult
{
    /// <summary>A text input field was clicked — activate it and start text input.</summary>
    /// <param name="Painted">
    /// Where this field's text was laid down, so the click can be resolved to a character.
    /// <para>
    /// Stated as part of the HIT for the reason <see cref="LinkHit"/> is: it keeps the drawn region and the
    /// clickable region the same arranged rect. A host that re-derived the text origin from the region's own
    /// x would be keeping a second copy of the field's insets, and the copy that drifts is the one nobody
    /// looks at — a caret landing a padding-width off the pointer is not obviously a bug, it just feels
    /// wrong. Left at <c>default</c> by a caller that only wants focus-on-click, which is what every caller
    /// did before the caret could be placed at all.
    /// </para>
    /// </param>
    public sealed record TextInputHit(TextInputState Input, TextInputGeometry Painted = default) : HitResult
    {
        /// <summary>
        /// The pre-9.1 shape, kept so an already-compiled host keeps binding.
        /// </summary>
        /// <remarks>
        /// This is the constructor 9.1 deleted. Adding <c>Painted</c> as an optional parameter on the
        /// primary constructor was source-compatible and binary-fatal: the published Console.Lib, compiled
        /// against 9.0, calls <c>new HitResult.TextInputHit(field.State)</c> in <c>CellLayout.HitOf</c>, so
        /// against 9.1 every terminal hit test threw <c>MissingMethodException</c>. Invisible on a dev box,
        /// where the sibling compiles from source and the package path CI takes is never taken; it surfaced
        /// only because an agent happened to be working in a checkout where the sibling probe failed.
        /// Restoring it here is what lets Console.Lib 4.33 run against 9.2 with no rebuild.
        /// </remarks>
        public TextInputHit(TextInputState Input) : this(Input, default) { }
    }

    /// <summary>A named action button was clicked.</summary>
    public sealed record ButtonHit(string Action) : HitResult;

    /// <summary>
    /// A chrome surface with no action of its own — a panel's card, a bar's background. Registered so
    /// the surface can state its cursor and so a host can tell "the pointer is over my overlay" from
    /// "the pointer is over the content", which is otherwise a geometry predicate the host has to keep
    /// in step with every overlay by hand.
    /// </summary>
    public sealed record ChromeHit : HitResult;

    /// <summary>
    /// A hyperlink was hit. Carries the target <see cref="Url"/> so a host can open it (desktop:
    /// the OS browser) and drive a pointer/hand cursor on hover. A web host that renders links as real
    /// DOM elements handles the navigation itself and can leave this inert.
    /// <para>
    /// <b>This is also how a layout tree DECLARES a link, not merely how a click reports one</b> (7.7+).
    /// A painter that can express a hyperlink does so for text under a node carrying this hit:
    /// <c>PixelWidgetBase.PaintLayout</c> emits the run with <see cref="SelectableTextRegion.Href"/> set, so
    /// a DOM host renders a real <c>&lt;a href&gt;</c>, and Console.Lib's <c>CellLayout</c> wraps the glyphs
    /// in an OSC 8 pair. Both resolve it through the same nearest-enclosing walk, so the link may sit on a
    /// row wrapper rather than on the text itself.
    /// </para>
    /// <para>
    /// Stating the link as the HIT is what keeps the drawn region and the clickable region the same arranged
    /// rect — there is deliberately no <c>Layout.Node.Link</c> property to disagree with it. A raster host
    /// has no navigation model and just paints the text, so the affordance is a progressive enhancement
    /// rather than something a tree has to ask for once per surface.
    /// </para>
    /// </summary>
    public sealed record LinkHit(string Url) : HitResult;

    /// <summary>A list item was clicked at the given index.</summary>
    public sealed record ListItemHit(string ListId, int Index) : HitResult;

    /// <summary>A slot was clicked for assignment. Payload is app-specific.</summary>
    public sealed record SlotHit<T>(T Slot) : HitResult;

    /// <summary>A slider was clicked/dragged at the given index.</summary>
    public sealed record SliderHit(int SliderIndex) : HitResult;
}
