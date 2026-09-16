using System.Collections.Generic;

namespace DIR.Lib;

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

    private readonly List<PopoverState> _paintedPopovers = [];

    /// <summary>
    /// The popovers painted this cycle, in paint order, which is z-order. <see cref="InputRouter"/> offers
    /// a key to the LAST one first.
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
    /// <b>A LIST since 10.0, where 9.x had one <c>IKeyboardClaimant</c> slot.</b> One slot means the last
    /// painter wins and nothing restores, so a modal over a popover -- or a popover over the menu that
    /// opened it -- took the slot, and the thing underneath stopped answering Escape for good: dismissing
    /// the inner overlay left the outer one on screen with nothing claiming it. A stack makes the nesting
    /// behave the way it looks, and hands the keyboard back on the next frame without restoring anything,
    /// because the one underneath is still painting and the closed one is not.
    /// </para>
    /// <para>
    /// <b>Cleared per paint CYCLE, registered by being PAINTED</b> (<see cref="NoteFrameBegin"/>,
    /// <see cref="NotePopoverPainted"/>) -- the same mechanism as <see cref="PointerOwner"/>, and that is
    /// what retired the interface. The slot was never cleared, so every implementer carried the obligation
    /// to answer "I am no longer displayed" for itself, which is a contract nothing could check and which
    /// a consumer's own overlay type got wrong. A popover that closed simply stops painting and is not in
    /// the list.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PopoverState> PaintedPopovers => _paintedPopovers;

    /// <summary>
    /// Records that <paramref name="popover"/> painted, putting it on top of this cycle's stack. Called by
    /// the layout painter for every OPEN popover node it draws.
    /// </summary>
    /// <remarks>
    /// Re-registering one already in the list MOVES it to the top rather than duplicating it, so a popover
    /// drawn in more than one of a widget's paint passes is ordered by its LAST paint, which is the one
    /// actually on top.
    /// </remarks>
    internal void NotePopoverPainted(PopoverState popover)
    {
        _paintedPopovers.Remove(popover);
        _paintedPopovers.Add(popover);
    }

    /// <summary>
    /// The rect that owns the pointer this frame, set by an open popover as it paints, or null when nothing
    /// does. Hover resolution consults it, so what is BEHIND a popover stops lighting up under the cursor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hover, not hit-testing. Clicks already go through the region tracker in paint order, so the backdrop
    /// takes them without help; what a raw pointer position cannot express on its own is that a row a
    /// popover is covering should look inert rather than warm. Confining hover is that rule, stated once.
    /// </para>
    /// <para>
    /// <b>Cleared once per PAINT CYCLE, not once per widget</b> (<see cref="NoteFrameBegin"/>). Per widget
    /// would be wrong in a way that only shows up with more than one: every widget clears in its own
    /// BeginFrame, so a second widget beginning its frame would wipe the owner a first widget had just
    /// painted, and the popover would confine hover only when it happened to belong to the last widget
    /// drawn. <see cref="PaintedPopovers"/> is cleared on the same boundary, for the same reason.
    /// </para>
    /// <para>
    /// <b>A cycle is not <see cref="FrameId"/>, and keying it on that was a bug.</b> The first shipped
    /// version cleared when the id CHANGED, which is correct only for a host that advances it. That property
    /// is documented as optional and defaults to 0, so on a host that leaves it there the first BeginFrame
    /// cleared and every later one returned early: an open popover then owned the pointer for the life of
    /// the process, and every hover in the window died the first time one closed. A default that quietly
    /// disables a rule is worse than no default, so the cycle is now derived from the widgets themselves.
    /// </para>
    /// </remarks>
    public RectF32? PointerOwner { get; set; }

    private readonly HashSet<object> _begunThisCycle = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Notes that <paramref name="widget"/> has begun a frame, and clears <see cref="PointerOwner"/> and
    /// <see cref="PaintedPopovers"/> when that begins a NEW cycle. Called by every widget's BeginFrame.
    /// </summary>
    /// <remarks>
    /// A cycle ends when a widget begins a second time, which needs nothing of the host: one widget or
    /// twenty, in any paint order, the first repeat is the boundary. Within a cycle the owner survives every
    /// other widget's BeginFrame, which is the property the whole mechanism exists for, since a popover in
    /// one widget must confine hover in the widgets painted after it.
    /// </remarks>
    internal void NoteFrameBegin(object widget)
    {
        if (_begunThisCycle.Add(widget))
        {
            return;
        }

        _begunThisCycle.Clear();
        _begunThisCycle.Add(widget);
        PointerOwner = null;
        _paintedPopovers.Clear();
    }

    /// <summary>
    /// Which text field in this window has the keyboard, and the one way to move it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-window for the reason focus is a singleton at all: there is one keyboard, so one owner names the
    /// one field receiving it, and every widget drawing into the window has to agree about which that is.
    /// That makes it the same KIND of fact as the DPI scale and the font -- and it lives here so it is
    /// shared the same way, by <see cref="PixelWidgetBase{TSurface}.ShareUiContext"/>, rather than
    /// hand-threaded through the constructor of every widget that happens to own a field.
    /// </para>
    /// <para>
    /// The threading is not hypothetical work avoided. A window whose fields sit in more than one widget --
    /// a search box on a panel, an editable readout in the chrome -- has to give both the SAME
    /// <see cref="TextInputFocus"/> or they each believe they hold the keyboard, and the symptom is two
    /// caret blinks on screen with one of them dead. Reached through the context, there is no second
    /// instance to create.
    /// </para>
    /// <para>
    /// Created here rather than injected, and get-only: a window always has exactly one, and being able to
    /// replace it is being able to orphan the fields that already registered with the old one.
    /// </para>
    /// </remarks>
    public TextInputFocus Focus { get; } = new();

    /// <summary>
    /// Where the focused field's caret was last drawn, or <c>default</c> if no active field has been
    /// painted. A host passes this to its platform's caret-location call (<c>SDL_SetTextInputArea</c>) so
    /// an input method can put its candidate window beside the caret rather than over the text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set at PAINT time by <see cref="PixelWidgetBase{TSurface}.RenderTextInput"/>, from the same call
    /// that draws the caret, for the same reason a click binds to the arranged rect: anything that
    /// recomputes the position separately can disagree with what the user is looking at, and here that
    /// disagreement puts the candidate window in the wrong place -- invisible in every test that does not
    /// involve a real IME.
    /// </para>
    /// <para>
    /// Per-window rather than per-widget because there is one caret, for the same reason there is one
    /// <see cref="Focus"/>. Held per widget, a host had to know which widget painted the focused field in
    /// order to ask the right one -- a question with no stable answer, since the field that has the
    /// keyboard moves between them.
    /// </para>
    /// <para>
    /// Deliberately NOT cleared per paint: a widget's PaintLayout runs more than once a frame (chrome,
    /// then content), so a reset inside it would let whichever ran last wipe the caret the other just
    /// recorded. Staleness cannot bite in practice, because the only caller that matters asks while a
    /// field is focused and <see cref="TextInputFocus.BlurIfUnpainted"/> guarantees a focused field was
    /// painted this frame -- so if there is a focus, this rect is from that frame.
    /// </para>
    /// </remarks>
    public RectInt CaretRect { get; set; }

    /// <summary>
    /// Which frame the window is on. A host bumps it once per frame, before anything draws; every widget
    /// sharing these settings then agrees on what "this frame" means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What it buys is one rule: <b>a widget can only be hit where it was drawn, on the frame it was
    /// drawn</b>. <see cref="PixelWidgetBase{TSurface}.BeginFrame"/> stamps the regions it is about to
    /// collect with this value, and the hit tests answer only regions carrying the current one — so a
    /// widget the host stopped drawing goes silent by itself, on the frame it stops.
    /// </para>
    /// <para>
    /// Registering as you paint already makes a widget un-hittable where it is not drawn. It does NOT make
    /// it un-hittable WHEN it is not drawn: a host that simply stops calling a widget's render — an early
    /// return for a loading screen, a modal covering the window — leaves the last frame's regions standing,
    /// and they answer clicks for a control that is no longer on screen. There is nothing local to the
    /// widget to notice this, which is why it is the window that counts frames.
    /// </para>
    /// <para>
    /// Left at 0 by a host that does not count frames, and then every region is stamped 0 too and every
    /// comparison matches — the previous behaviour, unchanged.
    /// </para>
    /// </remarks>
    public long FrameId { get; set; }
}
