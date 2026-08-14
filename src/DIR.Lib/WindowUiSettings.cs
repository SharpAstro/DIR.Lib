namespace DIR.Lib;

/// <summary>
/// Something that owns the keyboard while it is on screen -- an open dropdown, a menu, a modal.
/// </summary>
/// <remarks>
/// Implementers must return <c>false</c> once they are no longer on screen. That is what makes a stale
/// claim harmless and removes any need to clear one: a closed overlay simply declines, and the host falls
/// through to its normal routing.
/// </remarks>
public interface IKeyboardClaimant
{
    /// <summary>Handle a key; return false to let it through (including when no longer displayed).</summary>
    bool HandleKeyDown(InputKey key);
}

/// <summary>
/// The presentation values that are constant for one WINDOW and shared by every widget drawing into it:
/// the DPI scale, the text face, the emoji face and the per-codepoint fallback chain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a second font context -- it is what the font context is BUILT FROM.</b>
/// <see cref="PixelMeasureContext{TSurface}"/> is the font context: constructed per arrange/paint pass,
/// immutable, and contractually the same instance for measure and paint so the two cannot disagree. A
/// widget may hold several at once (<see cref="PixelMeasureContext{TSurface}.CellAuthored"/> gives a
/// different one for a cell-authored tree), so it is per-OPERATION and cannot own anything. These settings
/// are per-WINDOW and are exactly what each of those contexts is derived from.
/// </para>
/// <para>
/// <b>Held by reference, not copied.</b> A composite hands the same instance to the widgets it composes
/// (<see cref="PixelWidgetBase{TSurface}.ShareUiContext"/>), so a host setting the DPI scale on the chrome
/// has set it for every child in the same assignment. Nothing is pushed down and nothing can be missed.
/// </para>
/// <para>
/// This replaced four per-widget copies kept in agreement by four overridden setters, each naming every
/// child. That shape cost twice over: a new child had to be added to all four lists, and a new per-window
/// value had to restate the whole list again -- and both omissions are silent, because the widget simply
/// draws at the wrong scale or with no fallback face. The question that killed it is the obvious one: a
/// widget belongs to exactly one window, so why does it hold its own copy of what the window knows?
/// </para>
/// <para>
/// <b>Not hung off <see cref="Renderer{TSurface}"/></b>, though every widget already holds one and it is
/// per-window, because a renderer is shared by MORE than the widget tree: an embedded viewer that draws
/// into the same renderer and resolves its own face would overwrite the window's font for everyone. Making
/// the sharing explicit keeps that opt-out visible -- such a widget is simply never handed the context.
/// </para>
/// <para>
/// Mutable on purpose, and deliberately NOT the same object as <see cref="PixelMeasureContext{TSurface}"/>:
/// that one is built per arrange and must stay immutable, since its whole contract is that the SAME
/// instance answers measure and paint. A DPI change landing between the two would break exactly what it
/// exists to guarantee. The measure context is derived FROM this, per pass.
/// </para>
/// </remarks>
public sealed class WindowUiSettings
{
    /// <summary>Device pixels per design unit. The host sets it from the window's scale, at startup and on resize.</summary>
    public float DpiScale { get; set; } = 1f;

    /// <summary>The primary text face. Empty means "no font configured", and the text helpers then draw nothing rather than throwing.</summary>
    public string FontPath { get; set; } = string.Empty;

    /// <summary>The emoji face, for callers with a dedicated emoji path rather than coverage-driven fallback.</summary>
    public string? EmojiFontPath { get; set; }

    /// <summary>
    /// The per-codepoint fallback chain. Null draws with <see cref="FontPath"/> alone -- which is exactly
    /// the state in which any script the primary face lacks renders as nothing at all.
    /// </summary>
    public FontFallbackResolver? FontFallback { get; set; }

    /// <summary>
    /// The overlay that owns the keyboard, registered BY BEING PAINTED and consulted by the host before
    /// its normal key routing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The alternative -- every widget that owns an overlay adding a routing case to its own input switch,
    /// in a different file from the overlay -- is a step someone eventually forgets, and forgetting is
    /// invisible: the overlay still opens, still draws, still takes mouse clicks, and only the arrow keys
    /// do nothing. That is precisely what happened to the Live Session mode pill, which was the one
    /// dropdown of four with no routing case.
    /// </para>
    /// <para>
    /// Paint is the right moment to claim because paint order IS z-order, so the topmost overlay claims
    /// last and therefore wins, with no host arbitrating. Never cleared: a claimant that is no longer
    /// displayed returns false, so a stale claim costs one virtual call.
    /// </para>
    /// </remarks>
    public IKeyboardClaimant? KeyboardClaimant { get; set; }
}
