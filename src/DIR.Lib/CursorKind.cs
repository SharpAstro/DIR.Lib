namespace DIR.Lib;

/// <summary>
/// What the pointer should look like over a region — named by MEANING rather than by any platform's
/// cursor set, so a host maps it to SDL's <c>SystemCursor</c>, a CSS <c>cursor</c> keyword or a
/// terminal's nearest equivalent without DIR.Lib knowing any of them.
///
/// <para><b>Why this is a property of a region.</b> A cursor is a statement about what is under the
/// pointer, and what is under the pointer is exactly what the region list already knows. Hosts that
/// answer it any other way end up maintaining a predicate — "over the page, but not the palette, and
/// not the expand handle, and not any open panel" — which every new overlay silently invalidates: the
/// overlay draws over the page and the predicate keeps saying page, so a drop-up menu goes on showing
/// the text tool's I-beam. Declaring it beside the click removes the class of bug rather than the
/// instance, on the same reasoning that binds a click to the rect its content was painted in.</para>
/// </summary>
public enum CursorKind
{
    /// <summary>The ordinary arrow. What chrome wants: a panel, a bar, anything not the content.</summary>
    Default,

    /// <summary>The hand. Something is followable — a link, a button.</summary>
    Pointer,

    /// <summary>The I-beam. Text can be selected here.</summary>
    Text,

    /// <summary>Crosshair. A region is about to be picked out — a marquee, a sample point.</summary>
    Crosshair,

    /// <summary>Horizontal resize, for a vertical edge such as a sidebar divider.</summary>
    ResizeEW,

    /// <summary>Vertical resize, for a horizontal edge.</summary>
    ResizeNS,

    /// <summary>The four-way move cursor: this is being dragged, or can be.</summary>
    Move,

    /// <summary>Busy. Something is in flight and the region will not answer yet.</summary>
    Wait,

    /// <summary>The gesture in progress cannot end here.</summary>
    NotAllowed,
}
