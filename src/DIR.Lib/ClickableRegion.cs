using System;

namespace DIR.Lib;

/// <summary>
/// A clickable region registered during rendering. The hit test walks these
/// in reverse order (last-registered = on top) to find what was clicked.
/// </summary>
public readonly record struct ClickableRegion(float X, float Y, float Width, float Height, HitResult Result, Action<InputModifier>? OnClick = null);

/// <summary>
/// Describes what was hit during a click. Open hierarchy — extend with
/// app-specific subclasses (e.g. SlotHit, SliderHit) in downstream projects.
/// </summary>
public record HitResult
{
    /// <summary>A text input field was clicked — activate it and start text input.</summary>
    public sealed record TextInputHit(TextInputState Input) : HitResult;

    /// <summary>A named action button was clicked.</summary>
    public sealed record ButtonHit(string Action) : HitResult;

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
