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
/// <param name="FocusOnOpen">This region is a text field that asked for the keyboard when it appeared
/// (<see cref="Layout.Content.TextInput.FocusOnOpen"/>). Carried on the region because the request has to
/// be answered AFTER the frame is painted, when the whole painted set is known, and the regions are what
/// records that set. The request never takes the keyboard off a field being typed in -- the rule, and why
/// it can only be applied after the paint, is on the property.</param>
public readonly record struct ClickableRegion(
    float X, float Y, float Width, float Height, HitResult Result,
    Action<InputModifier>? OnClick = null, CursorKind? Cursor = null, bool FocusOnOpen = false);

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
    public sealed record TextInputHit(TextInputState Input, TextInputGeometry Painted = default) : HitResult;

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
